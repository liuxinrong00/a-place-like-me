using System.Collections;
using System.Collections.Generic;
using System.Linq;
using APlaceLikeMe.Core;
using APlaceLikeMe.Data;
using APlaceLikeMe.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace APlaceLikeMe.UI
{
    public sealed class PrototypeGameController : MonoBehaviour
    {
        private enum RoomMode
        {
            Store,
            Bedroom
        }

        private const string StoreSceneName = "Store";
        private const string BedroomSceneName = "BedRoom";
        private const string SampleSceneName = "SampleScene";
        private const string InteractionSceneName = "S_OrderBoardUI";
        private const string OrderSceneName = "Order";
        private const string FixSceneName = "Fix";
        private const string BuySceneName = "Buy";
        private const string ChooseUiSceneName = "ChooseUIScene";
        private const string FixTableMarkerName = "FixTable";
        private const string PhoneMarkerName = "Phone";
        private const string EnterBedroomMarkerName = "EnterBedRoom";
        private const string EnterStoreMarkerName = "EnterStoreDoor";
        private const string EnterStoreRoomMarkerName = "EnterStoreRoom";
        private const string UiCanvasName = "UICanvas";
        private const string MoneyHudName = "Money";
        private const string EnergyHudName = "Energy";
        private const string TimeHudName = "Time";
        private const string ChooseYesButtonName = "Yes";
        private const string ChooseNoButtonName = "No";
        private const float InteractionPadding = 0.65f;
        private const string RuntimeRootName = "PrototypeRuntime";
        private const string PlayerObjectName = "LXR";
        private const string LegacyPlayerObjectName = "Player";
        private const string InteractionMarkerRootName = "Interaction";
        private const string LegacyInteractionMarkerRootName = "Intraction";
        private const float CameraOrthographicSize = 5f;
        private const float InteractionRadius = 0.55f;
#if UNITY_EDITOR
        private const string PrototypeConfigAssetPath = "Assets/_Project/ScriptableObjects/GameConfig/PrototypeGameConfig.asset";
#endif

        private static readonly Vector3Int[] TilemapRegionDirections =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.up,
            Vector3Int.down
        };
        private static readonly Vector2 StoreCarpetSpawn = new(-16.5f, 4.5f);
        private static readonly Vector2 StoreSpawn = StoreCarpetSpawn;
        private static readonly Vector2 StoreFromBedroomSpawn = StoreCarpetSpawn;
        private static readonly Vector2 BedroomFromStoreSpawn = new(-3.0f, -6.4f);
        private static readonly Vector2 BedroomWakeSpawn = new(-6.0f, 0.8f);
        private static readonly Rect StoreMovementBounds = Rect.MinMaxRect(-21.5f, -5.4f, 9.5f, 7.2f);
        private static readonly Rect BedroomMovementBounds = Rect.MinMaxRect(-14.5f, -7.6f, 9.5f, 6.5f);
        private static readonly Rect StoreCameraBounds = Rect.MinMaxRect(-22.0f, -6.0f, 10.0f, 7.2f);
        private static readonly Rect BedroomCameraBounds = Rect.MinMaxRect(-15.0f, -8.0f, 10.0f, 6.5f);

        private static Font cachedDefaultFont;
        private readonly GameSessionState state = new();
        private readonly OrderService orderService = new();

        [SerializeField] private PrototypeGameConfig config;

        private RoomMode currentRoom = RoomMode.Store;
        private string loadedRoomSceneName;
        private Coroutine roomSwitchRoutine;
        private string[] roomSpawnMarkerNames;
        private Transform currentPlayer;
        private Camera roomCamera;
        private Rect currentMovementBounds = StoreMovementBounds;
        private Rect currentCameraBounds = StoreCameraBounds;
        private PrototypeInteractionPanelMode pendingPanelMode = PrototypeInteractionPanelMode.Orders;
        private string loadedInteractionSceneName;
        private bool suppressNextInteractInput;
        private bool isRoomSwitching;
        private bool hasStartupRoomOverride;
        private RoomMode startupRoomOverride = RoomMode.Store;
        private Vector2 startupSpawnOverride = StoreSpawn;
        private Text interactionHintText;
        private Text feedbackText;
        private RectTransform confirmationOverlay;
        private Text confirmationText;
        private System.Action confirmationYesAction;
        private System.Action confirmationNoAction;
        private Component moneyHudText;
        private Component energyHudText;
        private Component timeHudText;

        public static PrototypeGameController Active { get; private set; }
        public GameSessionState State => state;
        public OrderService OrderService => orderService;
        public PrototypeGameConfig Config => config;
        public PrototypeInteractionPanelMode PendingPanelMode => pendingPanelMode;
        public bool AreWorldControlsLocked => isRoomSwitching || IsInteractionSceneLoaded() || IsConfirmationOpen();

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }

            Active = this;
        }

        private void Start()
        {
            if (Active != this)
            {
                return;
            }

            Initialize(config);
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void Update()
        {
            if (isRoomSwitching || IsConfirmationOpen())
            {
                return;
            }

            if (IsInteractionSceneLoaded())
            {
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseInteractionSceneFromPanel();
                }

                return;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                if (suppressNextInteractInput)
                {
                    suppressNextInteractInput = false;
                    return;
                }

                TryInteract();
            }
            else if (Input.GetKeyUp(KeyCode.E))
            {
                suppressNextInteractInput = false;
            }

            UpdateInteractionHint();
        }

        private void LateUpdate()
        {
            if (currentPlayer != null)
            {
                UpdateCameraForPlayer(currentPlayer.position);
            }
        }

        public void Initialize(PrototypeGameConfig prototypeConfig)
        {
            config = prototypeConfig;
            EnsureEventSystem();
            BuildUi();

            if (config == null || config.InitialConfig == null || config.OrderPool.Count == 0 || config.RepairMethods.Count == 0)
            {
                feedbackText.text = "Prototype config is missing. Assign PrototypeGameConfig and repair methods.";
                return;
            }

            state.Initialize(config.InitialConfig);
            StartDay();
        }

        public string BuildNightSummaryText()
        {
            var todayCompletedCount = state.CompletedOrders.Count(order => order.DayCompleted == state.CurrentDay);
            var feedback = state.FeedbackLog.Count == 0 ? "今天还没有顾客反馈。" : string.Join("\n", state.FeedbackLog);
            return $"材料购买\n完成订单：{todayCompletedCount}\n当日收入：{state.TodayIncome}\n真实度变化：{FormatSigned(state.TodayAuthenticityDelta)}\n顾客反馈：\n{feedback}\n\n当前材料：\n{FormatMaterials()}\n\n选择材料后可以用金币购买补充。";
        }

        public OrderResult TryCompleteOrderFromPanel(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            var result = orderService.TryCompleteOrder(state, order, repairMethod);
            Render();
            return result;
        }

        public OrderResult TryBuySupplyFromPanel(MaterialDefinition material, int purchaseCount)
        {
            var clampedCount = Mathf.Clamp(purchaseCount, 1, 10);
            var costPerBatch = config == null ? 1 : Mathf.Max(1, config.NightSupplyCost);
            var amountPerBatch = config == null ? 1 : Mathf.Max(1, config.NightSupplyAmount);
            var result = orderService.TryBuyNightSupply(state, material, costPerBatch * clampedCount, amountPerBatch * clampedCount);
            Render();
            return result;
        }

        public void GoNextDayFromPanel()
        {
            GoNextDay();
        }

        public void CloseInteractionSceneFromPanel()
        {
            suppressNextInteractInput = true;
            UnloadInteractionSceneIfLoaded();
            Render();
        }

        internal void UpdateCameraForPlayer(Vector3 playerPosition)
        {
            if (roomCamera == null)
            {
                return;
            }

            var cameraBounds = currentCameraBounds;
            var halfHeight = roomCamera.orthographicSize;
            var halfWidth = halfHeight * Mathf.Max(1.0f, roomCamera.aspect);
            var cameraX = ClampCameraAxis(playerPosition.x, cameraBounds.xMin, cameraBounds.xMax, halfWidth);
            var cameraY = ClampCameraAxis(playerPosition.y, cameraBounds.yMin, cameraBounds.yMax, halfHeight);
            roomCamera.transform.position = new Vector3(cameraX, cameraY, -10f);
        }

        private void StartDay()
        {
            state.SetPhase(GamePhase.OrderSelection);
            var orders = orderService.GetOrdersForDay(config.OrderPool, state.CurrentDay, config.OrdersPerDay);
            state.SetTodaysOrders(orders);
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = $"第 {state.CurrentDay} 天开店。到蓝色工作台接订单，门可以进入卧室。";
            var startupRoom = hasStartupRoomOverride ? startupRoomOverride : RoomMode.Store;
            var startupSpawn = hasStartupRoomOverride ? startupSpawnOverride : StoreSpawn;
            SwitchRoom(startupRoom, startupSpawn);
            Render();
        }

        private void TryInteract()
        {
            var interactable = GetCurrentInteractable();
            if (interactable == null)
            {
                feedbackText.text = currentRoom == RoomMode.Store
                    ? "靠近双门或蓝色工作台后按 E 互动。"
                    : "靠近床、材料桌或门后按 E 互动。";
                Render();
                return;
            }

            switch (interactable.Kind)
            {
                case PrototypeSceneInteractionKind.StoreBedroomDoor:
                    feedbackText.text = "你进入了卧室。床可以结束当天，桌子可以买材料。";
                    SwitchRoom(RoomMode.Bedroom, EnterStoreMarkerName, EnterStoreRoomMarkerName);
                    break;
                case PrototypeSceneInteractionKind.BedroomStoreDoor:
                    feedbackText.text = "你回到了店铺。";
                    SwitchRoom(RoomMode.Store, EnterBedroomMarkerName);
                    break;
                case PrototypeSceneInteractionKind.Bed:
                    TryUseBed();
                    break;
                case PrototypeSceneInteractionKind.SupplyTable:
                    if (state.Phase == GamePhase.DayEnd)
                    {
                        feedbackText.text = "今天的原型流程已经结束。";
                        Render();
                        return;
                    }

                    OpenInteractionScene(BuySceneName, PrototypeInteractionPanelMode.MaterialPurchase);
                    break;
                case PrototypeSceneInteractionKind.OrderBoard:
                case PrototypeSceneInteractionKind.Phone:
                    if (state.Phase == GamePhase.DayEnd)
                    {
                        feedbackText.text = "今天的原型流程已经结束。";
                        Render();
                        return;
                    }

                    OpenInteractionScene(OrderSceneName, PrototypeInteractionPanelMode.Orders);
                    break;
                case PrototypeSceneInteractionKind.RepairTable:
                    if (state.Phase == GamePhase.DayEnd)
                    {
                        feedbackText.text = "今天的原型流程已经结束。";
                        Render();
                        return;
                    }

                    OpenInteractionScene(FixSceneName, PrototypeInteractionPanelMode.Orders);
                    break;
            }
        }

        private void TryUseBed()
        {
            if (state.Phase == GamePhase.DayEnd)
            {
                feedbackText.text = "原型流程已经结束。";
                Render();
                return;
            }

            OpenInteractionScene(ChooseUiSceneName, PrototypeInteractionPanelMode.NightSummary);
        }

        private void GoNextDay()
        {
            if (state.CurrentDay >= config.PrototypeDays)
            {
                state.SetPhase(GamePhase.DayEnd);
                UnloadInteractionSceneIfLoaded();
                feedbackText.text = $"原型流程完成：你已经经营了 {config.PrototypeDays} 天。";
                Render();
                return;
            }

            var orders = orderService.GetOrdersForDay(config.OrderPool, state.CurrentDay + 1, config.OrdersPerDay);
            state.StartNextDay(orders);
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = $"第 {state.CurrentDay} 天早上。能量已恢复，从卧室门回店铺继续接订单。";
            var wakeSpawn = currentPlayer == null ? BedroomWakeSpawn : (Vector2)currentPlayer.position;
            SwitchRoom(RoomMode.Bedroom, wakeSpawn);
            Render();
        }

        private void SwitchRoom(RoomMode room, Vector2 spawnPosition)
        {
            SwitchRoom(room, spawnPosition, null);
        }

        private void SwitchRoom(RoomMode room, params string[] spawnMarkerNames)
        {
            var fallbackSpawn = room == RoomMode.Store ? StoreFromBedroomSpawn : BedroomFromStoreSpawn;
            SwitchRoom(room, fallbackSpawn, spawnMarkerNames);
        }

        private void SwitchRoom(RoomMode room, Vector2 spawnPosition, string[] spawnMarkerNames)
        {
            currentRoom = room;
            if (roomSwitchRoutine != null)
            {
                StopCoroutine(roomSwitchRoutine);
            }

            roomSpawnMarkerNames = spawnMarkerNames;
            roomSwitchRoutine = StartCoroutine(SwitchRoomRoutine(room, spawnPosition));
            Render();
        }

        private IEnumerator SwitchRoomRoutine(RoomMode room, Vector2 spawnPosition)
        {
            isRoomSwitching = true;
            currentPlayer = null;
            roomCamera = null;

            var targetSceneName = GetSceneName(room);
            var targetScene = SceneManager.GetSceneByName(targetSceneName);
            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                AsyncOperation loadOperation = null;
                try
                {
                    loadOperation = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
                }
                catch (System.Exception exception)
                {
                    feedbackText.text = $"无法加载场景 {targetSceneName}：{exception.Message}";
                    isRoomSwitching = false;
                    Render();
                    yield break;
                }

                while (loadOperation != null && !loadOperation.isDone)
                {
                    yield return null;
                }
            }

            yield return UnloadOtherGameplayScenes(targetSceneName);

            targetScene = SceneManager.GetSceneByName(targetSceneName);
            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                feedbackText.text = $"无法找到已加载场景：{targetSceneName}";
                isRoomSwitching = false;
                Render();
                yield break;
            }

            spawnPosition = ResolveRoomSpawnPosition(targetScene, spawnPosition);
            loadedRoomSceneName = targetSceneName;
            SceneManager.SetActiveScene(targetScene);
            SetupRoomScene(targetScene, room, spawnPosition);
            isRoomSwitching = false;
            roomSwitchRoutine = null;
            Render();
        }

        private Vector2 ResolveRoomSpawnPosition(Scene targetScene, Vector2 fallbackSpawn)
        {
            if (roomSpawnMarkerNames == null || roomSpawnMarkerNames.Length == 0)
            {
                return fallbackSpawn;
            }

            var markerRegion = FindPreferredRegion(FindInteractionMarkerRegions(targetScene), roomSpawnMarkerNames);
            roomSpawnMarkerNames = null;
            return markerRegion?.Center ?? fallbackSpawn;
        }

        private void SetupRoomScene(Scene scene, RoomMode room, Vector2 spawnPosition)
        {
            ConfigureRuntimeBounds(scene, room);
            SetRoomCamera(scene);
            var runtimeRoot = CreateRuntimeRoot(scene);
            SetupPlayer(scene, room, spawnPosition);

            if (room == RoomMode.Store)
            {
                CreateStoreInteractables(scene, runtimeRoot.transform);
            }
            else
            {
                CreateBedroomInteractables(scene, runtimeRoot.transform);
            }

            if (currentPlayer != null)
            {
                UpdateCameraForPlayer(currentPlayer.position);
            }

            BindSceneHud(scene);
        }

        private static IEnumerator UnloadOtherGameplayScenes(string targetSceneName)
        {
            foreach (var sceneName in new[] { StoreSceneName, BedroomSceneName, SampleSceneName })
            {
                if (sceneName == targetSceneName)
                {
                    continue;
                }

                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var unloadOperation = SceneManager.UnloadSceneAsync(scene);
                while (unloadOperation != null && !unloadOperation.isDone)
                {
                    yield return null;
                }
            }
        }

        private void SetupPlayer(Scene scene, RoomMode room, Vector2 spawnPosition)
        {
            var playerObject = FindSceneObject(scene, PlayerObjectName);
            if (playerObject == null)
            {
                playerObject = new GameObject(PlayerObjectName);
                SceneManager.MoveGameObjectToScene(playerObject, scene);
            }

            RemoveLegacyPlayer(scene, playerObject);

            playerObject.transform.position = new Vector3(spawnPosition.x, spawnPosition.y, playerObject.transform.position.z);
            ConfigurePlayerMovement(playerObject, currentMovementBounds);
            currentPlayer = playerObject.transform;
        }

        private void ConfigureRuntimeBounds(Scene scene, RoomMode room)
        {
            var sceneBounds = CalculateSceneTileBounds(scene);
            if (!sceneBounds.HasValue)
            {
                currentMovementBounds = room == RoomMode.Store ? StoreMovementBounds : BedroomMovementBounds;
                currentCameraBounds = room == RoomMode.Store ? StoreCameraBounds : BedroomCameraBounds;
                return;
            }

            currentMovementBounds = sceneBounds.Value;
            currentCameraBounds = ExpandRect(sceneBounds.Value, 0.5f, 0.5f);
        }

        private static Rect? CalculateSceneTileBounds(Scene scene)
        {
            var hasBounds = false;
            var min = Vector2.zero;
            var max = Vector2.zero;
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                foreach (var tilemap in rootObject.GetComponentsInChildren<Tilemap>(true))
                {
                    if (IsInteractionMarkerTilemap(tilemap))
                    {
                        continue;
                    }

                    var localBounds = CalculateTilemapBounds(tilemap);
                    if (!localBounds.HasValue)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        min = localBounds.Value.min;
                        max = localBounds.Value.max;
                        hasBounds = true;
                        continue;
                    }

                    min = Vector2.Min(min, localBounds.Value.min);
                    max = Vector2.Max(max, localBounds.Value.max);
                }
            }

            return hasBounds ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : null;
        }

        private static void CreateBedroomInteractables(Scene scene, Transform runtimeRoot)
        {
            var markerRegions = FindInteractionMarkerRegions(scene);
            if (markerRegions.Count < 3)
            {
                CreateFallbackBedroomInteractables(runtimeRoot);
                return;
            }

            var exitRegion = FindPreferredRegion(markerRegions, EnterStoreMarkerName, EnterStoreRoomMarkerName)
                ?? markerRegions
                    .OrderBy(region => region.Min.y)
                    .ThenBy(region => Mathf.Abs(region.Center.x))
                    .First();
            var chooseUiRegion = markerRegions
                .Where(region => !region.Equals(exitRegion))
                .OrderBy(region => Mathf.Abs(region.Center.x + 2.5f))
                .ThenByDescending(region => region.Center.y)
                .First();
            var supplyRegion = markerRegions
                .Where(region => !region.Equals(exitRegion) && !region.Equals(chooseUiRegion))
                .OrderByDescending(region => region.Max.x)
                .ThenByDescending(region => region.Center.y)
                .First();

            CreateInteractable(runtimeRoot, "StoreDoorTrigger", PrototypeSceneInteractionKind.BedroomStoreDoor, exitRegion.Center, ExpandInteractionSize(exitRegion.Size), "按 E：回到店铺");
            CreateInteractable(runtimeRoot, "BedTrigger", PrototypeSceneInteractionKind.Bed, chooseUiRegion.Center, ExpandInteractionSize(chooseUiRegion.Size), "按 E：结束今天");
            CreateInteractable(runtimeRoot, "SupplyTableTrigger", PrototypeSceneInteractionKind.SupplyTable, supplyRegion.Center, ExpandInteractionSize(supplyRegion.Size), "按 E：购买材料");
        }

        private static void CreateStoreInteractables(Scene scene, Transform runtimeRoot)
        {
            var markerRegions = FindInteractionMarkerRegions(scene);
            if (markerRegions.Count < 2)
            {
                CreateFallbackStoreInteractables(runtimeRoot);
                return;
            }

            var doorRegion = FindPreferredRegion(markerRegions, EnterBedroomMarkerName)
                ?? markerRegions
                    .OrderByDescending(region => region.Max.y)
                    .ThenBy(region => Mathf.Abs(region.Center.x))
                    .First();
            var fixTableRegions = markerRegions
                .Where(region => region.SourceName == FixTableMarkerName)
                .OrderBy(region => region.Min.y)
                .ThenBy(region => region.Min.x)
                .ToList();
            var phoneRegions = markerRegions
                .Where(region => region.SourceName == PhoneMarkerName)
                .OrderBy(region => region.Min.y)
                .ThenBy(region => region.Min.x)
                .ToList();
            var interactionRegions = markerRegions
                .Where(region => !region.Equals(doorRegion))
                .Where(region => region.SourceName != FixTableMarkerName)
                .Where(region => region.SourceName != PhoneMarkerName)
                .OrderBy(region => region.Min.y)
                .ThenBy(region => region.Min.x)
                .ToList();

            CreateInteractable(runtimeRoot, "BedroomDoorTrigger", PrototypeSceneInteractionKind.StoreBedroomDoor, doorRegion.Center, ExpandInteractionSize(doorRegion.Size), "按 E：进入卧室");
            for (var index = 0; index < fixTableRegions.Count; index++)
            {
                var fixTableRegion = fixTableRegions[index];
                CreateInteractable(runtimeRoot, $"RepairTableTrigger_{index + 1}", PrototypeSceneInteractionKind.RepairTable, fixTableRegion.Center, ExpandInteractionSize(fixTableRegion.Size), "按 E：打开修补台");
            }

            for (var index = 0; index < phoneRegions.Count; index++)
            {
                var phoneRegion = phoneRegions[index];
                CreateInteractable(runtimeRoot, $"PhoneTrigger_{index + 1}", PrototypeSceneInteractionKind.Phone, phoneRegion.Center, ExpandInteractionSize(phoneRegion.Size), "按 E：打开订单");
            }

            for (var index = 0; index < interactionRegions.Count; index++)
            {
                var interactionRegion = interactionRegions[index];
                if (interactionRegion.Center.y < 3f && interactionRegion.Center.x > -2f)
                {
                    CreateInteractable(runtimeRoot, $"RepairTableTrigger_{fixTableRegions.Count + index + 1}", PrototypeSceneInteractionKind.RepairTable, interactionRegion.Center, ExpandInteractionSize(interactionRegion.Size), "按 E：打开修补台");
                    continue;
                }

                CreateInteractable(runtimeRoot, $"PhoneTrigger_{phoneRegions.Count + index + 1}", PrototypeSceneInteractionKind.Phone, interactionRegion.Center, ExpandInteractionSize(interactionRegion.Size), "按 E：打开订单");
            }
        }

        private static void CreateFallbackStoreInteractables(Transform runtimeRoot)
        {
            CreateInteractable(runtimeRoot, "BedroomDoorTrigger", PrototypeSceneInteractionKind.StoreBedroomDoor, new Vector2(-2.0f, 1.0f), new Vector2(3.4f, 2.2f), "按 E：进入卧室");
            CreateInteractable(runtimeRoot, "PhoneTrigger", PrototypeSceneInteractionKind.Phone, new Vector2(-6.5f, 2.5f), new Vector2(2.0f, 2.0f), "按 E：打开订单");
            CreateInteractable(runtimeRoot, "RepairTableTrigger", PrototypeSceneInteractionKind.RepairTable, new Vector2(4.0f, -2.5f), new Vector2(6.0f, 2.0f), "按 E：打开修补台");
        }

        private static void CreateFallbackBedroomInteractables(Transform runtimeRoot)
        {
            CreateInteractable(runtimeRoot, "StoreDoorTrigger", PrototypeSceneInteractionKind.BedroomStoreDoor, new Vector2(-3.0f, -7.0f), new Vector2(4.5f, 1.6f), "按 E：回到店铺");
            CreateInteractable(runtimeRoot, "BedTrigger", PrototypeSceneInteractionKind.Bed, new Vector2(-6.0f, 1.9f), new Vector2(3.0f, 2.4f), "按 E：结束今天");
            CreateInteractable(runtimeRoot, "SupplyTableTrigger", PrototypeSceneInteractionKind.SupplyTable, new Vector2(2.5f, -3.0f), new Vector2(8.8f, 3.2f), "按 E：购买材料");
        }

        private static List<TilemapInteractionRegion> FindInteractionMarkerRegions(Scene scene)
        {
            var markerRoot = FindSceneObject(scene, InteractionMarkerRootName);
            if (markerRoot == null)
            {
                markerRoot = FindSceneObject(scene, LegacyInteractionMarkerRootName);
            }

            var regions = new List<TilemapInteractionRegion>();
            if (markerRoot == null)
            {
                return regions;
            }

            foreach (var tilemap in markerRoot.GetComponentsInChildren<Tilemap>(true))
            {
                regions.AddRange(FindConnectedTileRegions(tilemap));
            }

            return regions;
        }

        private static TilemapInteractionRegion? FindPreferredRegion(IEnumerable<TilemapInteractionRegion> regions, params string[] sourceNames)
        {
            var sourceNameSet = new HashSet<string>(sourceNames);
            var matchingRegions = regions
                .Where(region => sourceNameSet.Contains(region.SourceName))
                .OrderByDescending(region => region.Size.x * region.Size.y)
                .ToList();
            return matchingRegions.Count == 0 ? null : matchingRegions[0];
        }

        private static Vector2 ExpandInteractionSize(Vector2 size)
        {
            return new Vector2(size.x + InteractionPadding * 2f, size.y + InteractionPadding * 2f);
        }

        private static List<TilemapInteractionRegion> FindConnectedTileRegions(Tilemap tilemap)
        {
            var remainingCells = new HashSet<Vector3Int>();
            foreach (var cellPosition in tilemap.cellBounds.allPositionsWithin)
            {
                if (tilemap.HasTile(cellPosition))
                {
                    remainingCells.Add(cellPosition);
                }
            }

            var regions = new List<TilemapInteractionRegion>();
            while (remainingCells.Count > 0)
            {
                var startCell = remainingCells.First();
                var regionCells = FloodFillTileRegion(startCell, remainingCells);
                regions.Add(CreateTilemapInteractionRegion(tilemap, regionCells));
            }

            return regions;
        }

        private static List<Vector3Int> FloodFillTileRegion(Vector3Int startCell, HashSet<Vector3Int> remainingCells)
        {
            var regionCells = new List<Vector3Int>();
            var pendingCells = new Queue<Vector3Int>();
            pendingCells.Enqueue(startCell);
            remainingCells.Remove(startCell);

            while (pendingCells.Count > 0)
            {
                var currentCell = pendingCells.Dequeue();
                regionCells.Add(currentCell);

                foreach (var direction in TilemapRegionDirections)
                {
                    var neighborCell = currentCell + direction;
                    if (!remainingCells.Remove(neighborCell))
                    {
                        continue;
                    }

                    pendingCells.Enqueue(neighborCell);
                }
            }

            return regionCells;
        }

        private static TilemapInteractionRegion CreateTilemapInteractionRegion(Tilemap tilemap, List<Vector3Int> cells)
        {
            var minX = cells.Min(cell => cell.x);
            var minY = cells.Min(cell => cell.y);
            var maxX = cells.Max(cell => cell.x);
            var maxY = cells.Max(cell => cell.y);
            return CreateTilemapRegionFromCellBounds(tilemap, minX, minY, maxX, maxY, tilemap.name);
        }

        private static Rect? CalculateTilemapBounds(Tilemap tilemap)
        {
            var hasTile = false;
            var minX = 0;
            var minY = 0;
            var maxX = 0;
            var maxY = 0;
            foreach (var cellPosition in tilemap.cellBounds.allPositionsWithin)
            {
                if (!tilemap.HasTile(cellPosition))
                {
                    continue;
                }

                if (!hasTile)
                {
                    minX = maxX = cellPosition.x;
                    minY = maxY = cellPosition.y;
                    hasTile = true;
                    continue;
                }

                minX = Mathf.Min(minX, cellPosition.x);
                minY = Mathf.Min(minY, cellPosition.y);
                maxX = Mathf.Max(maxX, cellPosition.x);
                maxY = Mathf.Max(maxY, cellPosition.y);
            }

            if (!hasTile)
            {
                return null;
            }

            var region = CreateTilemapRegionFromCellBounds(tilemap, minX, minY, maxX, maxY, tilemap.name);
            return Rect.MinMaxRect(region.Min.x, region.Min.y, region.Max.x, region.Max.y);
        }

        private static TilemapInteractionRegion CreateTilemapRegionFromCellBounds(Tilemap tilemap, int minX, int minY, int maxX, int maxY, string sourceName)
        {
            var minWorld = tilemap.CellToWorld(new Vector3Int(minX, minY, 0));
            var maxWorld = tilemap.CellToWorld(new Vector3Int(maxX + 1, maxY + 1, 0));
            var min = Vector2.Min(minWorld, maxWorld);
            var max = Vector2.Max(minWorld, maxWorld);
            var size = max - min;
            size.x = Mathf.Max(1.0f, size.x);
            size.y = Mathf.Max(1.0f, size.y);

            return new TilemapInteractionRegion(min, max, size, sourceName);
        }

        private static bool IsInteractionMarkerTilemap(Tilemap tilemap)
        {
            var parent = tilemap.transform.parent;
            while (parent != null)
            {
                if (parent.name == InteractionMarkerRootName || parent.name == LegacyInteractionMarkerRootName)
                {
                    return true;
                }

                parent = parent.parent;
            }

            return false;
        }

        private static Rect ExpandRect(Rect rect, float horizontalPadding, float verticalPadding)
        {
            return Rect.MinMaxRect(
                rect.xMin - horizontalPadding,
                rect.yMin - verticalPadding,
                rect.xMax + horizontalPadding,
                rect.yMax + verticalPadding);
        }

        private void ConfigurePlayerMovement(GameObject playerObject, Rect bounds)
        {
            playerObject.SendMessage("ConfigureBounds", bounds, SendMessageOptions.DontRequireReceiver);

            if (playerObject.GetComponent("PlayerMove") != null)
            {
                return;
            }

            var fallbackController = playerObject.GetComponent<PrototypeScenePlayerController>();
            if (fallbackController == null)
            {
                fallbackController = playerObject.AddComponent<PrototypeScenePlayerController>();
            }

            fallbackController.Configure(this, bounds);
        }

        private static void RemoveLegacyPlayer(Scene scene, GameObject activePlayer)
        {
            var legacyPlayer = FindSceneObject(scene, LegacyPlayerObjectName);
            if (legacyPlayer != null && legacyPlayer != activePlayer)
            {
                Destroy(legacyPlayer);
            }
        }

        private static GameObject CreateRuntimeRoot(Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == RuntimeRootName)
                {
                    Destroy(rootObject);
                }
            }

            var runtimeRoot = new GameObject(RuntimeRootName);
            SceneManager.MoveGameObjectToScene(runtimeRoot, scene);
            return runtimeRoot;
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                var match = FindInChildren(rootObject.transform, objectName);
                if (match != null)
                {
                    return match.gameObject;
                }
            }

            return null;
        }

        private static Transform FindInChildren(Transform parent, string objectName)
        {
            if (parent.name == objectName)
            {
                return parent;
            }

            for (var index = 0; index < parent.childCount; index++)
            {
                var match = FindInChildren(parent.GetChild(index), objectName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static void CreateInteractable(Transform parent, string name, PrototypeSceneInteractionKind kind, Vector2 center, Vector2 size, string prompt)
        {
            var interactableObject = new GameObject(name, typeof(BoxCollider2D), typeof(PrototypeSceneInteractable));
            interactableObject.transform.SetParent(parent, false);
            interactableObject.transform.position = new Vector3(center.x, center.y, -0.1f);

            var collider = interactableObject.GetComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = size;

            var interactable = interactableObject.GetComponent<PrototypeSceneInteractable>();
            interactable.Configure(kind, prompt);
        }

        private readonly struct TilemapInteractionRegion
        {
            public TilemapInteractionRegion(Vector2 min, Vector2 max, Vector2 size, string sourceName)
            {
                Min = min;
                Max = max;
                Center = (min + max) * 0.5f;
                Size = size;
                SourceName = sourceName;
            }

            public Vector2 Min { get; }
            public Vector2 Max { get; }
            public Vector2 Center { get; }
            public Vector2 Size { get; }
            public string SourceName { get; }
        }

        private void SetRoomCamera(Scene roomScene)
        {
            roomCamera = null;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var isRoomCamera = camera.gameObject.scene == roomScene;
                camera.enabled = isRoomCamera;
                if (isRoomCamera && roomCamera == null)
                {
                    roomCamera = camera;
                }
            }

            foreach (var audioListener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                audioListener.enabled = audioListener.gameObject.scene == roomScene;
            }

            if (roomCamera == null)
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                SceneManager.MoveGameObjectToScene(cameraObject, roomScene);
                roomCamera = cameraObject.GetComponent<Camera>();
            }

            roomCamera.orthographic = true;
            roomCamera.orthographicSize = CameraOrthographicSize;
            roomCamera.clearFlags = CameraClearFlags.SolidColor;
            roomCamera.backgroundColor = new Color32(49, 77, 121, 255);
        }

        private PrototypeSceneInteractable GetCurrentInteractable()
        {
            if (currentPlayer == null)
            {
                return null;
            }

            var playerPosition = (Vector2)currentPlayer.position;
            PrototypeSceneInteractable nearest = null;
            var nearestDistance = float.MaxValue;
            var maxDistance = InteractionRadius * InteractionRadius;
            foreach (var interactable in FindObjectsByType<PrototypeSceneInteractable>(FindObjectsSortMode.None))
            {
                if (interactable.gameObject.scene != currentPlayer.gameObject.scene)
                {
                    continue;
                }

                var collider = interactable.GetComponent<Collider2D>();
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                var closestPoint = collider.ClosestPoint(playerPosition);
                var distance = Vector2.SqrMagnitude(closestPoint - playerPosition);
                if (distance > maxDistance)
                {
                    continue;
                }

                if (distance < nearestDistance)
                {
                    nearest = interactable;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private void OpenInteractionScene(PrototypeInteractionPanelMode mode)
        {
            OpenInteractionScene(InteractionSceneName, mode);
        }

        private void OpenInteractionScene(string sceneName, PrototypeInteractionPanelMode mode)
        {
            pendingPanelMode = mode;
            if (IsInteractionSceneLoaded())
            {
                return;
            }

            loadedInteractionSceneName = sceneName;
            StartCoroutine(LoadInteractionSceneRoutine(sceneName));
        }

        private void UnloadInteractionSceneIfLoaded()
        {
            if (!IsInteractionSceneLoaded())
            {
                return;
            }

            var sceneName = loadedInteractionSceneName;
            loadedInteractionSceneName = null;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        private bool IsInteractionSceneLoaded()
        {
            return !string.IsNullOrEmpty(loadedInteractionSceneName);
        }

        private IEnumerator LoadInteractionSceneRoutine(string sceneName)
        {
            var loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (loadOperation != null && !loadOperation.isDone)
            {
                yield return null;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (loadedInteractionSceneName == sceneName)
                {
                    DisableSceneCameras(scene);
                    BindInteractionScene(scene, sceneName);
                }
                else
                {
                    SceneManager.UnloadSceneAsync(scene);
                }
            }
        }

        private static void DisableSceneCameras(Scene scene)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                foreach (var camera in rootObject.GetComponentsInChildren<Camera>(true))
                {
                    camera.enabled = false;
                }

                foreach (var audioListener in rootObject.GetComponentsInChildren<AudioListener>(true))
                {
                    audioListener.enabled = false;
                }
            }
        }

        private void BindInteractionScene(Scene scene, string sceneName)
        {
            if (sceneName != ChooseUiSceneName)
            {
                return;
            }

            BindButton(scene, ChooseYesButtonName, GoNextDayFromPanel);
            BindButton(scene, ChooseNoButtonName, CloseInteractionSceneFromPanel);
        }

        private static void BindButton(Scene scene, string objectName, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = FindSceneObject(scene, objectName);
            var button = buttonObject == null ? null : buttonObject.GetComponentInChildren<Button>(true);
            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(onClick);
        }

        private void BindSceneHud(Scene scene)
        {
            moneyHudText = null;
            energyHudText = null;
            timeHudText = null;

            var canvasObject = FindSceneObject(scene, UiCanvasName);
            if (canvasObject == null)
            {
                return;
            }

            moneyHudText = FindHudTextComponent(canvasObject, MoneyHudName);
            energyHudText = FindHudTextComponent(canvasObject, EnergyHudName);
            timeHudText = FindHudTextComponent(canvasObject, TimeHudName);
            UpdateSceneHud();
        }

        private static Component FindHudTextComponent(GameObject canvasObject, string hudObjectName)
        {
            var hudObject = FindInChildren(canvasObject.transform, hudObjectName);
            return hudObject == null ? null : FindWritableTextComponent(hudObject.gameObject);
        }

        private static Component FindWritableTextComponent(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform)
                {
                    continue;
                }

                var textProperty = component.GetType().GetProperty("text");
                if (textProperty != null && textProperty.PropertyType == typeof(string) && textProperty.CanWrite)
                {
                    return component;
                }
            }

            return null;
        }

        private void UpdateSceneHud()
        {
            SetTextValue(moneyHudText, state.Coins.ToString());
            SetTextValue(energyHudText, $"{state.Energy}/{Mathf.Max(1, state.DailyEnergyRecovery)}");
            SetTextValue(timeHudText, GetCurrentTimeLabel());
        }

        private string GetCurrentTimeLabel()
        {
            if (state.Phase == GamePhase.DayEnd || state.Phase == GamePhase.NightSummary || state.Phase == GamePhase.MaterialPurchase)
            {
                return "晚上";
            }

            var todayCompletedCount = state.CompletedOrders.Count(order => order.DayCompleted == state.CurrentDay);
            if (todayCompletedCount <= 0)
            {
                return "上午";
            }

            if (todayCompletedCount == 1)
            {
                return "午后";
            }

            return "快收店了";
        }

        private static void SetTextValue(Component component, string value)
        {
            if (component == null)
            {
                return;
            }

            var textProperty = component.GetType().GetProperty("text");
            if (textProperty == null || textProperty.PropertyType != typeof(string) || !textProperty.CanWrite)
            {
                return;
            }

            textProperty.SetValue(component, value, null);
        }

        private void ShowConfirmation(string message, string yesLabel, string noLabel, System.Action onYes, System.Action onNo)
        {
            confirmationYesAction = onYes;
            confirmationNoAction = onNo;
            confirmationText.text = message;

            var buttonsRoot = confirmationOverlay.Find("Dialog/Buttons");
            for (var index = buttonsRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(buttonsRoot.GetChild(index).gameObject);
            }

            CreateButton(buttonsRoot, yesLabel, ConfirmYes, true);
            CreateButton(buttonsRoot, noLabel, ConfirmNo);
            confirmationOverlay.gameObject.SetActive(true);
        }

        private bool IsConfirmationOpen()
        {
            return confirmationOverlay != null && confirmationOverlay.gameObject.activeSelf;
        }

        private void ConfirmYes()
        {
            var action = confirmationYesAction;
            HideConfirmation();
            action?.Invoke();
        }

        private void ConfirmNo()
        {
            var action = confirmationNoAction;
            HideConfirmation();
            action?.Invoke();
        }

        private void HideConfirmation()
        {
            confirmationOverlay.gameObject.SetActive(false);
            confirmationYesAction = null;
            confirmationNoAction = null;
            suppressNextInteractInput = true;
        }

        private void Render()
        {
            UpdateSceneHud();
            UpdateInteractionHint();
        }

        private void UpdateInteractionHint()
        {
            if (interactionHintText == null)
            {
                return;
            }

            if (isRoomSwitching)
            {
                interactionHintText.text = "正在切换场景...";
                return;
            }

            var interactable = GetCurrentInteractable();
            if (interactable != null)
            {
                interactionHintText.text = interactable.Prompt;
                return;
            }

            interactionHintText.text = currentRoom == RoomMode.Store
                ? "WASD / 方向键移动，靠近双门或蓝色工作台按 E"
                : "WASD / 方向键移动，靠近床、材料桌或门按 E";
        }

        private string FormatMaterials()
        {
            if (state.MaterialStock.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", state.MaterialStock.Select(pair => $"{pair.Key.DisplayName} x{pair.Value}"));
        }

        private static string GetSceneName(RoomMode room)
        {
            return room == RoomMode.Store ? StoreSceneName : BedroomSceneName;
        }

        private static float ClampCameraAxis(float value, float min, float max, float halfSize)
        {
            if (max - min <= halfSize * 2f)
            {
                return (min + max) * 0.5f;
            }

            return Mathf.Clamp(value, min + halfSize, max - halfSize);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureControllerForDirectRoomPlay()
        {
            if (Active != null || FindFirstObjectByType<PrototypeGameController>() != null)
            {
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            if (!TryGetRoomMode(activeScene.name, out var room))
            {
                return;
            }

            var prototypeConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<PrototypeGameConfig>(PrototypeConfigAssetPath);
            if (prototypeConfig == null)
            {
                Debug.LogWarning($"Cannot start prototype interactions without config at {PrototypeConfigAssetPath}.");
                return;
            }

            var controllerObject = new GameObject("PrototypeGameController");
            DontDestroyOnLoad(controllerObject);

            var controller = controllerObject.AddComponent<PrototypeGameController>();
            controller.config = prototypeConfig;
            controller.hasStartupRoomOverride = true;
            controller.startupRoomOverride = room;
            controller.startupSpawnOverride = GetExistingPlayerPosition(activeScene, room);
        }

        private static bool TryGetRoomMode(string sceneName, out RoomMode room)
        {
            if (sceneName == StoreSceneName)
            {
                room = RoomMode.Store;
                return true;
            }

            if (sceneName == BedroomSceneName)
            {
                room = RoomMode.Bedroom;
                return true;
            }

            if (sceneName == SampleSceneName)
            {
                room = RoomMode.Store;
                return true;
            }

            room = RoomMode.Store;
            return false;
        }

        private static Vector2 GetExistingPlayerPosition(Scene scene, RoomMode room)
        {
            var playerObject = FindSceneObject(scene, PlayerObjectName);
            if (playerObject != null)
            {
                return playerObject.transform.position;
            }

            return room == RoomMode.Store ? StoreSpawn : BedroomFromStoreSpawn;
        }
#endif

        private void BuildUi()
        {
            var canvas = CreateCanvas();
            if (hasStartupRoomOverride)
            {
                DontDestroyOnLoad(canvas.gameObject);
            }

            var root = CreatePanel(canvas.transform, "HudRoot", new Vector2(0, 0), new Vector2(1, 1));

            interactionHintText = CreateHiddenText(root, "HiddenInteractionHint");
            feedbackText = CreateHiddenText(root, "HiddenFeedback");

            BuildConfirmationOverlay(canvas);
        }

        private void BuildConfirmationOverlay(Transform parent)
        {
            confirmationOverlay = CreatePanel(parent, "ConfirmationOverlay", new Vector2(0, 0), new Vector2(1, 1));
            var overlayImage = confirmationOverlay.gameObject.AddComponent<Image>();
            overlayImage.color = new Color32(252, 251, 247, 210);

            var dialog = CreateCard(confirmationOverlay, "Dialog", PrototypeUiTheme.Paper);
            dialog.anchorMin = new Vector2(0.32f, 0.34f);
            dialog.anchorMax = new Vector2(0.68f, 0.66f);
            dialog.offsetMin = Vector2.zero;
            dialog.offsetMax = Vector2.zero;

            var dialogLayout = dialog.gameObject.AddComponent<VerticalLayoutGroup>();
            dialogLayout.padding = new RectOffset(28, 28, 24, 24);
            dialogLayout.spacing = PrototypeUiTheme.SpaceLarge;
            dialogLayout.childControlWidth = true;
            dialogLayout.childControlHeight = true;

            confirmationText = CreateText(dialog, "Message", 24, FontStyle.Bold, PrototypeUiTheme.Ink);
            confirmationText.alignment = TextAnchor.MiddleCenter;
            var messageElement = confirmationText.gameObject.AddComponent<LayoutElement>();
            messageElement.flexibleHeight = 1;

            var buttons = CreatePanel(dialog, "Buttons", new Vector2(0, 0), new Vector2(1, 0));
            var buttonsElement = buttons.gameObject.AddComponent<LayoutElement>();
            buttonsElement.minHeight = 68;
            var buttonsLayout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = PrototypeUiTheme.SpaceMedium;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childForceExpandWidth = true;

            confirmationOverlay.gameObject.SetActive(false);
        }

        private static RectTransform CreateCanvas()
        {
            var canvasObject = new GameObject("PrototypeHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var rectTransform = canvasObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return rectTransform;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            return rectTransform;
        }

        private static RectTransform CreateOverlayCard(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var card = CreatePanel(parent, name, anchorMin, anchorMax);
            var image = card.gameObject.AddComponent<Image>();
            image.color = new Color32(255, 255, 252, 230);
            AddOutline(card.gameObject, 2);
            return card;
        }

        private static RectTransform CreateCard(Transform parent, string name, Color color)
        {
            var card = CreatePanel(parent, name, new Vector2(0, 0), new Vector2(1, 1));
            var image = card.gameObject.AddComponent<Image>();
            image.color = color;
            AddOutline(card.gameObject, 2);
            return card;
        }

        private static void PlaceFull(RectTransform rectTransform, RectOffset padding)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(padding.left, padding.bottom);
            rectTransform.offsetMax = new Vector2(-padding.right, -padding.top);
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, bool primary = false)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = primary ? PrototypeUiTheme.Primary : PrototypeUiTheme.Card;
            AddOutline(buttonObject, 2);

            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            var colors = button.colors;
            colors.normalColor = primary ? PrototypeUiTheme.Primary : PrototypeUiTheme.Card;
            colors.highlightedColor = PrototypeUiTheme.PrimaryHover;
            colors.pressedColor = PrototypeUiTheme.PaperMuted;
            colors.disabledColor = PrototypeUiTheme.CardUnavailable;
            button.colors = colors;

            var labelText = CreateText(buttonObject.transform, "Label", 20, FontStyle.Bold, PrototypeUiTheme.Ink);
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleCenter;
            var labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8, 4);
            labelRect.offsetMax = new Vector2(-8, -4);

            var layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 60;
            layout.preferredHeight = 60;
            return button;
        }

        private static void AddOutline(GameObject target, int distance)
        {
            var outline = target.AddComponent<Outline>();
            outline.effectColor = PrototypeUiTheme.Line;
            outline.effectDistance = new Vector2(distance, -distance);
        }

        private static Text CreateText(Transform parent, string name, int fontSize, FontStyle style, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = string.Empty;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.font = GetDefaultFont();
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Text CreateHiddenText(Transform parent, string name)
        {
            var text = CreateText(parent, name, 1, FontStyle.Normal, Color.clear);
            text.gameObject.SetActive(false);
            return text;
        }

        private static Font GetDefaultFont()
        {
            if (cachedDefaultFont == null)
            {
                cachedDefaultFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            }

            return cachedDefaultFont;
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? $"+{value}" : value.ToString();
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }
    }

}
