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

        private enum PrototypeSceneButtonAction
        {
            CloseInteraction,
            CloseOverlay,
            GoNextDay
        }

        private const string StoreSceneName = "Store";
        private const string BedroomSceneName = "BedRoom";
        private const string SampleSceneName = "SampleScene";
        private const string InteractionSceneName = "S_OrderBoardUI";
        private const string OrderSceneName = "Order";
        private const string FixSceneName = "Fix";
        private const string BuySceneName = "Buy";
        private const string ChooseUiSceneName = "ChooseUIScene";
        private const string SleepChoiceSceneName = "SleepChoice";
        private const string LegacySleepChoiceSceneName = "SleepChioce";
        private const string RepairResultSceneName = "ShoppingInformation";
        private const string LegacyRepairResultSceneName = "ShoopingInformation";
        private const string BedMarkerName = "Bed";
        private const string FixTableMarkerName = "FixTable";
        private const string PhoneMarkerName = "Phone";
        private const string CashierDeskMarkerName = "CashierDesk";
        private const string EnterBedroomMarkerName = "EnterBedRoom";
        private const string EnterStoreMarkerName = "EnterStoreDoor";
        private const string EnterStoreRoomMarkerName = "EnterStoreRoom";
        private const string UiCanvasName = "UICanvas";
        private const string MoneyHudName = "Money";
        private const string EnergyHudName = "Energy";
        private const string TimeHudName = "Time";
        private const string NightOverlayName = "PrototypeNightOverlay";
        private const string ChooseYesButtonName = "Yes";
        private const string ChooseNoButtonName = "No";
        private const string CloseButtonName = "CloseButton";
        private const int DailyLivingCost = 20;
        private const int ThirdDayExtraCost = 100;
        private const float InteractionPadding = 0.65f;
        private const float BedWakeSpawnPadding = 1.25f;
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
        private static readonly Vector2 BedroomFromStoreSpawn = new(5.0f, 4.15f);
        private static readonly Vector2 BedroomWakeSpawn = new(-2.5f, 2.25f);
        private static readonly Rect StoreMovementBounds = Rect.MinMaxRect(-21.5f, -5.4f, 9.5f, 7.2f);
        private static readonly Rect BedroomMovementBounds = Rect.MinMaxRect(-14.5f, -7.6f, 9.5f, 6.5f);
        private static readonly Rect StoreCameraBounds = Rect.MinMaxRect(-22.0f, -6.0f, 10.0f, 7.2f);
        private static readonly Rect BedroomCameraBounds = Rect.MinMaxRect(-15.0f, -8.0f, 10.0f, 6.5f);
        private static readonly PrototypeInteractionSceneDefinition[] InteractionSceneDefinitions =
        {
            new(
                new[] { ChooseUiSceneName },
                "需要休息到明天才能开店哦",
                new[] { new PrototypeSceneButtonDefinition(CloseButtonName, PrototypeSceneButtonAction.CloseInteraction) }),
            new(
                new[] { SleepChoiceSceneName, LegacySleepChoiceSceneName },
                "现在休息到明天吗？",
                new[]
                {
                    new PrototypeSceneButtonDefinition(ChooseYesButtonName, PrototypeSceneButtonAction.GoNextDay),
                    new PrototypeSceneButtonDefinition(ChooseNoButtonName, PrototypeSceneButtonAction.CloseInteraction),
                    new PrototypeSceneButtonDefinition(CloseButtonName, PrototypeSceneButtonAction.CloseInteraction)
                })
        };
        private static readonly PrototypeInteractionSceneDefinition[] OverlaySceneDefinitions =
        {
            new(
                new[] { RepairResultSceneName, LegacyRepairResultSceneName },
                null,
                new[] { new PrototypeSceneButtonDefinition(CloseButtonName, PrototypeSceneButtonAction.CloseOverlay) })
        };

        private static Font cachedDefaultFont;
        private readonly GameSessionState state = new();
        private readonly OrderService orderService = new();
        private readonly Dictionary<PrototypePhoneAppController.Tab, Button> phoneTabButtons = new();
        private readonly List<PrototypeSceneButtonBinding> interactionButtonBindings = new();
        private readonly List<PrototypeSceneButtonBinding> overlayButtonBindings = new();

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
        private string loadedOverlaySceneName;
        private string pendingRepairResultMessage;
        private bool suppressNextInteractInput;
        private bool canReturnToStoreAfterRest;
        private bool bedroomRestedLightOn;
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
        private RectTransform phoneLauncher;
        private RectTransform phonePanel;
        private RectTransform phoneScrollContent;
        private ScrollRect phoneScrollRect;
        private Text phoneTitleText;
        private Text phoneBodyText;
        private PrototypePhoneAppController phoneApp;
        private PrototypeSceneButtonBinding pendingMouseButtonBinding;
        private PrototypeSceneButtonBinding pendingTouchButtonBinding;
        private int pendingTouchFingerId = -1;
        private int lastSceneButtonActionFrame = -1;
        private PrototypeSceneButtonBinding lastSceneButtonBinding;

        public static PrototypeGameController Active { get; private set; }
        public GameSessionState State => state;
        public OrderService OrderService => orderService;
        public PrototypeGameConfig Config => config;
        public PrototypeInteractionPanelMode PendingPanelMode => pendingPanelMode;
        public bool IsOverlayOpen => IsOverlaySceneLoaded();
        public bool AreWorldControlsLocked => isRoomSwitching || IsInteractionSceneLoaded() || IsOverlaySceneLoaded() || IsConfirmationOpen() || IsPhoneOpen();

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

            if (IsPhoneOpen())
            {
                if (TryHandlePhonePointer())
                {
                    return;
                }

                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
                {
                    ClosePhone();
                }

                return;
            }

            if (IsOverlaySceneLoaded())
            {
                if (TryHandleSceneButtonPointer(overlayButtonBindings))
                {
                    return;
                }

                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseOverlaySceneFromPanel();
                }

                return;
            }

            if (IsInteractionSceneLoaded())
            {
                if (TryHandleSceneButtonPointer(interactionButtonBindings))
                {
                    return;
                }

                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseInteractionSceneFromPanel();
                }

                return;
            }

            if (TryOpenPhoneFromPointer())
            {
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
            return $"材料购买\n完成订单：{todayCompletedCount}\n当日收入：{state.TodayIncome}\n当日支出：{state.TodayExpenses}\n今日材料消耗：{FormatTodayMaterialConsumption()}\n今日能量变化：-{state.TodayEnergySpent}\n真实度变化：{FormatSigned(state.TodayAuthenticityDelta)}\n顾客反馈：\n{feedback}\n\n当前材料：\n{FormatMaterials()}\n\n选择材料后可以用金币购买补充。";
        }

        public OrderResult TryAcceptOrderFromPanel(OrderDefinition order)
        {
            var result = orderService.TryAcceptOrder(state, order);
            Render();
            return result;
        }

        public OrderResult TryCompleteOrderFromPanel(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            var result = orderService.TryCompleteOrder(state, order, repairMethod);
            Render();
            return result;
        }

        public void ShowRepairResultFromPanel(string resultMessage)
        {
            pendingRepairResultMessage = resultMessage;
            OpenOverlayScene(RepairResultSceneName);
        }

        public OrderResult TryBuySupplyFromPanel(MaterialDefinition material, int purchaseCount)
        {
            var clampedCount = Mathf.Clamp(purchaseCount, 1, 10);
            var cost = material == null ? 0 : Mathf.Max(1, material.DefaultPrice) * clampedCount;
            var result = orderService.TryBuyNightSupply(state, material, cost, clampedCount);
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

        public void CloseOverlaySceneFromPanel()
        {
            suppressNextInteractInput = true;
            UnloadOverlaySceneIfLoaded();
            Render();
        }

        public void ClosePhoneFromButton()
        {
            ClosePhone();
        }

        private void OpenPhone()
        {
            if (phoneApp == null || currentRoom != RoomMode.Bedroom)
            {
                return;
            }

            phoneApp.Open();
            suppressNextInteractInput = true;
            Render();
        }

        private void ClosePhone()
        {
            if (phoneApp == null || !phoneApp.IsOpen())
            {
                return;
            }

            phoneApp.Close();
            suppressNextInteractInput = true;
            Render();
        }

        private bool IsPhoneOpen()
        {
            return phoneApp != null && phoneApp.IsOpen();
        }

        private bool TryOpenPhoneFromPointer()
        {
            if (currentRoom != RoomMode.Bedroom || phoneApp == null || IsPhoneOpen())
            {
                return false;
            }

            if (Input.GetMouseButtonDown(0) && phoneApp.TryOpenFromScreenPosition(Input.mousePosition))
            {
                return true;
            }

            for (var index = 0; index < Input.touchCount; index++)
            {
                var touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began && phoneApp.TryOpenFromScreenPosition(touch.position))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHandlePhonePointer()
        {
            if (phoneApp == null)
            {
                return false;
            }

            if (Input.GetMouseButtonDown(0) && phoneApp.TryHandleOpenPointer(Input.mousePosition))
            {
                return true;
            }

            for (var index = 0; index < Input.touchCount; index++)
            {
                var touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began && phoneApp.TryHandleOpenPointer(touch.position))
                {
                    return true;
                }
            }

            return false;
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
            canReturnToStoreAfterRest = false;
            bedroomRestedLightOn = false;
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
                    : "靠近床、材料桌、手机或门后按 E 互动。";
                Render();
                return;
            }

            switch (interactable.Kind)
            {
                case PrototypeSceneInteractionKind.StoreBedroomDoor:
                    bedroomRestedLightOn = false;
                    feedbackText.text = "你进入了卧室。床可以结束当天，桌子可以买材料。";
                    SwitchRoom(RoomMode.Bedroom, EnterStoreMarkerName, EnterStoreRoomMarkerName);
                    break;
                case PrototypeSceneInteractionKind.BedroomStoreDoor:
                    if (canReturnToStoreAfterRest)
                    {
                        canReturnToStoreAfterRest = false;
                        feedbackText.text = $"{FormatDayLabel(state.CurrentDay)}开店啦。";
                        SwitchRoom(RoomMode.Store, EnterBedroomMarkerName);
                        break;
                    }

                    feedbackText.text = "需要休息到明天才能开店哦";
                    OpenInteractionScene(ChooseUiSceneName, PrototypeInteractionPanelMode.NightSummary);
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
                    if (state.Phase == GamePhase.DayEnd)
                    {
                        feedbackText.text = "今天的原型流程已经结束。";
                        Render();
                        return;
                    }

                    OpenInteractionScene(OrderSceneName, PrototypeInteractionPanelMode.Orders);
                    break;
                case PrototypeSceneInteractionKind.Phone:
                    if (currentRoom == RoomMode.Bedroom)
                    {
                        OpenPhone();
                        break;
                    }

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
                case PrototypeSceneInteractionKind.CashierDesk:
                    if (ShopManager.Instance != null && ShopManager.Instance.TryCheckoutFrontNPC(out var checkoutIncome))
                    {
                        state.AddCoins(checkoutIncome);
                        feedbackText.text = $"结账完成，收入 +{checkoutIncome} 元，顾客离店。";
                    }
                    else
                    {
                        feedbackText.text = "现在没有顾客在收银台等待。";
                    }

                    Render();
                    break;
            }
        }

        private void TryUseBed()
        {
            if (canReturnToStoreAfterRest)
            {
                feedbackText.text = "已经休息好了，先去开店吧。";
                Render();
                return;
            }

            if (state.Phase == GamePhase.DayEnd)
            {
                feedbackText.text = "原型流程已经结束。";
                Render();
                return;
            }

            OpenInteractionScene(SleepChoiceSceneName, PrototypeInteractionPanelMode.NightSummary);
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
            var livingCost = GetLivingCostForNextDay(state.CurrentDay + 1);
            state.StartNextDay(orders);
            state.SpendCoins(livingCost);
            canReturnToStoreAfterRest = true;
            bedroomRestedLightOn = true;
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = $"{FormatDayLabel(state.CurrentDay)}早上。扣除生活费 {livingCost} 元，能量已恢复。";
            SwitchRoom(RoomMode.Bedroom, BedroomWakeSpawn, new[] { BedMarkerName });
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
            if (!markerRegion.HasValue)
            {
                return fallbackSpawn;
            }

            if (markerRegion.Value.SourceName == BedMarkerName)
            {
                return new Vector2(markerRegion.Value.Center.x, markerRegion.Value.Min.y - BedWakeSpawnPadding);
            }

            if (markerRegion.Value.SourceName == EnterStoreMarkerName || markerRegion.Value.SourceName == EnterStoreRoomMarkerName)
            {
                return new Vector2(markerRegion.Value.Center.x, markerRegion.Value.Min.y - 0.85f);
            }

            return markerRegion.Value.Center;
        }

        private void SetupRoomScene(Scene scene, RoomMode room, Vector2 spawnPosition)
        {
            ConfigureRuntimeBounds(scene, room);
            ConfigureRoomInteractionMarkerColliders(scene, room);
            SetRoomCamera(scene);
            var runtimeRoot = CreateRuntimeRoot(scene);
            ConfigureRoomLighting(scene, runtimeRoot.transform, room);
            SetupPlayer(scene, room, spawnPosition);

            if (room == RoomMode.Store)
            {
                CreateStoreInteractables(scene, runtimeRoot.transform);
            }
            else
            {
                CreateBedroomInteractables(scene, runtimeRoot.transform);
                CreateBedroomPhoneInteractable(runtimeRoot.transform);
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

            spawnPosition = ClampSpawnPosition(spawnPosition, currentMovementBounds);
            spawnPosition = ResolveSafePlayerSpawnPosition(scene, playerObject, spawnPosition, currentMovementBounds);
            playerObject.transform.position = new Vector3(spawnPosition.x, spawnPosition.y, playerObject.transform.position.z);
            var playerRigidbody = playerObject.GetComponent<Rigidbody2D>();
            if (playerRigidbody != null)
            {
                playerRigidbody.simulated = true;
                playerRigidbody.gravityScale = 0f;
                playerRigidbody.freezeRotation = true;
                playerRigidbody.position = spawnPosition;
                playerRigidbody.velocity = Vector2.zero;
                playerRigidbody.angularVelocity = 0f;
                playerRigidbody.WakeUp();
            }

            ConfigurePlayerMovement(playerObject, currentMovementBounds);
            currentPlayer = playerObject.transform;
        }

        private static Vector2 ClampSpawnPosition(Vector2 spawnPosition, Rect bounds)
        {
            const float margin = 0.35f;
            spawnPosition.x = Mathf.Clamp(spawnPosition.x, bounds.xMin + margin, bounds.xMax - margin);
            spawnPosition.y = Mathf.Clamp(spawnPosition.y, bounds.yMin + margin, bounds.yMax - margin);
            return spawnPosition;
        }

        private static Vector2 ResolveSafePlayerSpawnPosition(Scene scene, GameObject playerObject, Vector2 desiredPosition, Rect bounds)
        {
            if (playerObject == null)
            {
                return desiredPosition;
            }

            var playerCollider = playerObject.GetComponent<Collider2D>();
            if (playerCollider == null || IsPlayerSpawnClear(scene, playerObject, playerCollider, desiredPosition))
            {
                return desiredPosition;
            }

            var step = 0.25f;
            var directions = new[]
            {
                Vector2.down,
                Vector2.left,
                Vector2.right,
                Vector2.up,
                new Vector2(-1f, -1f).normalized,
                new Vector2(1f, -1f).normalized,
                new Vector2(-1f, 1f).normalized,
                new Vector2(1f, 1f).normalized
            };

            for (var radiusStep = 1; radiusStep <= 12; radiusStep++)
            {
                var distance = step * radiusStep;
                for (var index = 0; index < directions.Length; index++)
                {
                    var candidate = ClampSpawnPosition(desiredPosition + directions[index] * distance, bounds);
                    if (IsPlayerSpawnClear(scene, playerObject, playerCollider, candidate))
                    {
                        return candidate;
                    }
                }
            }

            return desiredPosition;
        }

        private static bool IsPlayerSpawnClear(Scene scene, GameObject playerObject, Collider2D playerCollider, Vector2 position)
        {
            var center = position;
            var size = new Vector2(0.8f, 1.0f);
            var direction = CapsuleDirection2D.Vertical;

            if (playerCollider is CapsuleCollider2D capsuleCollider)
            {
                center += RotateVector(capsuleCollider.offset, playerObject.transform.eulerAngles.z);
                size = Vector2.Scale(capsuleCollider.size, AbsVector(playerObject.transform.lossyScale));
                direction = capsuleCollider.direction;
            }
            else if (playerCollider is BoxCollider2D boxCollider)
            {
                center += RotateVector(boxCollider.offset, playerObject.transform.eulerAngles.z);
                size = Vector2.Scale(boxCollider.size, AbsVector(playerObject.transform.lossyScale));
            }
            else
            {
                center = position + (Vector2)(playerCollider.bounds.center - playerObject.transform.position);
                size = playerCollider.bounds.size;
            }

            var overlaps = Physics2D.OverlapCapsuleAll(center, size, direction, playerObject.transform.eulerAngles.z);
            for (var index = 0; index < overlaps.Length; index++)
            {
                var overlap = overlaps[index];
                if (overlap == null || overlap.isTrigger || overlap.gameObject.scene != scene)
                {
                    continue;
                }

                if (overlap.transform == playerObject.transform || overlap.transform.IsChildOf(playerObject.transform))
                {
                    continue;
                }

                if (IsInteractionMarker(overlap.transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static Vector2 AbsVector(Vector3 vector)
        {
            return new Vector2(Mathf.Abs(vector.x), Mathf.Abs(vector.y));
        }

        private static Vector2 RotateVector(Vector2 vector, float zDegrees)
        {
            if (Mathf.Approximately(zDegrees, 0f))
            {
                return vector;
            }

            var radians = zDegrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
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

            CreateInteractable(runtimeRoot, "StoreDoorTrigger", PrototypeSceneInteractionKind.BedroomStoreDoor, exitRegion.Center, ExpandInteractionSize(exitRegion.Size), "需要休息到明天才能开店哦");
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
            var cashierRegions = markerRegions
                .Where(region => region.SourceName == CashierDeskMarkerName)
                .OrderBy(region => region.Min.y)
                .ThenBy(region => region.Min.x)
                .ToList();
            var interactionRegions = markerRegions
                .Where(region => !region.Equals(doorRegion))
                .Where(region => region.SourceName != FixTableMarkerName)
                .Where(region => region.SourceName != PhoneMarkerName)
                .Where(region => region.SourceName != CashierDeskMarkerName)
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

            for (var index = 0; index < cashierRegions.Count; index++)
            {
                var cashierRegion = cashierRegions[index];
                CreateInteractable(runtimeRoot, $"CashierDeskTrigger_{index + 1}", PrototypeSceneInteractionKind.CashierDesk, cashierRegion.Center, ExpandInteractionSize(cashierRegion.Size), "按 E：结账");
            }

            for (var index = 0; index < interactionRegions.Count; index++)
            {
                var interactionRegion = interactionRegions[index];
                if (IsCashierDeskRegion(scene, interactionRegion))
                {
                    CreateInteractable(runtimeRoot, $"CashierDeskTrigger_{cashierRegions.Count + index + 1}", PrototypeSceneInteractionKind.CashierDesk, interactionRegion.Center, ExpandInteractionSize(interactionRegion.Size), "按 E：结账");
                    continue;
                }

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
            CreateInteractable(runtimeRoot, "StoreDoorTrigger", PrototypeSceneInteractionKind.BedroomStoreDoor, new Vector2(-3.0f, -7.0f), new Vector2(4.5f, 1.6f), "需要休息到明天才能开店哦");
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

        private static bool IsCashierDeskRegion(Scene scene, TilemapInteractionRegion region)
        {
            var cashierDesk = FindSceneObject(scene, CashierDeskMarkerName);
            if (cashierDesk == null)
            {
                return false;
            }

            var bounds = GetObjectBounds(cashierDesk);
            if (!bounds.HasValue)
            {
                return Vector2.Distance(region.Center, cashierDesk.transform.position) <= 2f;
            }

            var cashierCenter = (Vector2)bounds.Value.center;
            var cashierSize = (Vector2)bounds.Value.size;
            return Mathf.Abs(region.Center.x - cashierCenter.x) <= Mathf.Max(1f, cashierSize.x * 0.75f + 0.75f)
                && Mathf.Abs(region.Center.y - cashierCenter.y) <= Mathf.Max(1f, cashierSize.y * 0.75f + 0.75f);
        }

        private static Bounds? GetObjectBounds(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
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
            return tilemap != null && IsInteractionMarker(tilemap.transform);
        }

        private static bool IsInteractionMarker(Transform target)
        {
            var parent = target;
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

        private static void ConfigureRoomInteractionMarkerColliders(Scene scene, RoomMode room)
        {
            if (room != RoomMode.Bedroom)
            {
                return;
            }

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                foreach (var tilemap in rootObject.GetComponentsInChildren<Tilemap>(true))
                {
                    if (!IsInteractionMarkerTilemap(tilemap))
                    {
                        continue;
                    }

                    foreach (var collider in tilemap.GetComponents<Collider2D>())
                    {
                        collider.isTrigger = true;
                    }
                }
            }
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

        private static void CreateBedroomPhoneInteractable(Transform runtimeRoot)
        {
            CreateInteractable(runtimeRoot, "BedroomPhoneTrigger", PrototypeSceneInteractionKind.Phone, new Vector2(7.6f, -5.7f), new Vector2(3.2f, 2.2f), "按 E：查看手机");
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

        private readonly struct PrototypeSceneButtonBinding
        {
            public PrototypeSceneButtonBinding(Button button, UnityEngine.Events.UnityAction action)
            {
                Button = button;
                Action = action;
            }

            public Button Button { get; }
            public UnityEngine.Events.UnityAction Action { get; }
        }

        private readonly struct PrototypeSceneButtonDefinition
        {
            public PrototypeSceneButtonDefinition(string objectName, PrototypeSceneButtonAction action)
            {
                ObjectName = objectName;
                Action = action;
            }

            public string ObjectName { get; }
            public PrototypeSceneButtonAction Action { get; }
        }

        private readonly struct PrototypeInteractionSceneDefinition
        {
            public PrototypeInteractionSceneDefinition(
                IReadOnlyList<string> sceneNames,
                string message,
                IReadOnlyList<PrototypeSceneButtonDefinition> buttons)
            {
                SceneNames = sceneNames;
                Message = message;
                Buttons = buttons;
            }

            public IReadOnlyList<string> SceneNames { get; }
            public string Message { get; }
            public IReadOnlyList<PrototypeSceneButtonDefinition> Buttons { get; }

            public bool Matches(string sceneName)
            {
                return SceneNames != null && SceneNames.Contains(sceneName);
            }
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
            roomCamera.backgroundColor = roomScene.name == BedroomSceneName
                ? new Color32(8, 12, 22, 255)
                : new Color32(49, 77, 121, 255);
        }

        private void ConfigureRoomLighting(Scene scene, Transform runtimeRoot, RoomMode room)
        {
            if (room != RoomMode.Bedroom || roomCamera == null || runtimeRoot == null || bedroomRestedLightOn)
            {
                return;
            }

            var overlayObject = new GameObject(NightOverlayName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(overlayObject, scene);
            overlayObject.transform.SetParent(runtimeRoot, false);

            var canvas = overlayObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10;

            var scaler = overlayObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var raycaster = overlayObject.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            var shadeObject = new GameObject("Shade", typeof(RectTransform), typeof(Image));
            shadeObject.transform.SetParent(overlayObject.transform, false);
            var shadeRect = shadeObject.GetComponent<RectTransform>();
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = Vector2.zero;
            shadeRect.offsetMax = Vector2.zero;

            var shadeImage = shadeObject.GetComponent<Image>();
            shadeImage.color = new Color32(5, 8, 18, 88);
            shadeImage.raycastTarget = false;
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
            ClosePhone();
            pendingPanelMode = mode;
            if (IsInteractionSceneLoaded())
            {
                return;
            }

            sceneName = ResolveInteractionSceneName(sceneName);
            loadedInteractionSceneName = sceneName;
            ClearSceneButtonBindings(interactionButtonBindings);
            StartCoroutine(LoadInteractionSceneRoutine(sceneName));
        }

        private void OpenOverlayScene(string sceneName)
        {
            ClosePhone();
            if (IsOverlaySceneLoaded())
            {
                return;
            }

            sceneName = ResolveOverlaySceneName(sceneName);
            loadedOverlaySceneName = sceneName;
            ClearSceneButtonBindings(overlayButtonBindings);
            StartCoroutine(LoadOverlaySceneRoutine(sceneName));
        }

        private void UnloadInteractionSceneIfLoaded()
        {
            if (!IsInteractionSceneLoaded())
            {
                return;
            }

            UnloadOverlaySceneIfLoaded();

            var sceneName = loadedInteractionSceneName;
            loadedInteractionSceneName = null;
            ClearSceneButtonBindings(interactionButtonBindings);
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

        private void UnloadOverlaySceneIfLoaded()
        {
            if (!IsOverlaySceneLoaded())
            {
                return;
            }

            var sceneName = loadedOverlaySceneName;
            loadedOverlaySceneName = null;
            ClearSceneButtonBindings(overlayButtonBindings);
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        private bool IsOverlaySceneLoaded()
        {
            return !string.IsNullOrEmpty(loadedOverlaySceneName);
        }

        private static string ResolveInteractionSceneName(string sceneName)
        {
            if (sceneName != SleepChoiceSceneName)
            {
                return sceneName;
            }

            if (Application.CanStreamedLevelBeLoaded(SleepChoiceSceneName))
            {
                return SleepChoiceSceneName;
            }

            return Application.CanStreamedLevelBeLoaded(LegacySleepChoiceSceneName)
                ? LegacySleepChoiceSceneName
                : SleepChoiceSceneName;
        }

        private static string ResolveOverlaySceneName(string sceneName)
        {
            if (sceneName != RepairResultSceneName)
            {
                return sceneName;
            }

            if (Application.CanStreamedLevelBeLoaded(RepairResultSceneName))
            {
                return RepairResultSceneName;
            }

            return Application.CanStreamedLevelBeLoaded(LegacyRepairResultSceneName)
                ? LegacyRepairResultSceneName
                : RepairResultSceneName;
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
                    EnsureInteractionUiInput(scene);
                    BindInteractionScene(scene, sceneName);
                }
                else
                {
                    SceneManager.UnloadSceneAsync(scene);
                }
            }
        }

        private IEnumerator LoadOverlaySceneRoutine(string sceneName)
        {
            var loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (loadOperation != null && !loadOperation.isDone)
            {
                yield return null;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (loadedOverlaySceneName == sceneName)
                {
                    DisableSceneCameras(scene);
                    EnsureInteractionUiInput(scene, 60);
                    BindOverlayScene(scene, sceneName);
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

        private void EnsureInteractionUiInput(Scene scene, int sortingOrder = 40)
        {
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                foreach (var eventSystem in rootObject.GetComponentsInChildren<EventSystem>(true))
                {
                    eventSystem.enabled = false;
                }

                foreach (var inputModule in rootObject.GetComponentsInChildren<BaseInputModule>(true))
                {
                    inputModule.enabled = false;
                }

                foreach (var canvas in rootObject.GetComponentsInChildren<Canvas>(true))
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = sortingOrder;

                    var rectTransform = canvas.GetComponent<RectTransform>();
                    if (rectTransform != null && rectTransform.localScale == Vector3.zero)
                    {
                        rectTransform.localScale = Vector3.one;
                    }

                    if (canvas.GetComponent<GraphicRaycaster>() == null)
                    {
                        canvas.gameObject.AddComponent<GraphicRaycaster>();
                    }
                }

                foreach (var image in rootObject.GetComponentsInChildren<Image>(true))
                {
                    if (image.color.a <= 0.01f && image.GetComponent<Selectable>() == null)
                    {
                        image.raycastTarget = false;
                    }
                }
            }

            EnsureEventSystem();
        }

        private void BindInteractionScene(Scene scene, string sceneName)
        {
            if (sceneName == OrderSceneName || sceneName == FixSceneName || sceneName == BuySceneName)
            {
                var rootObject = scene.GetRootGameObjects().FirstOrDefault();
                if (rootObject != null && rootObject.GetComponent<PrototypeStaticInteractionSceneController>() == null)
                {
                    rootObject.AddComponent<PrototypeStaticInteractionSceneController>().Configure(sceneName, this);
                }

                return;
            }

            if (TryFindSceneDefinition(sceneName, InteractionSceneDefinitions, out var definition))
            {
                BindConfiguredScene(scene, definition, interactionButtonBindings);
            }
        }

        private void BindOverlayScene(Scene scene, string sceneName)
        {
            if (!TryFindSceneDefinition(sceneName, OverlaySceneDefinitions, out var definition))
            {
                return;
            }

            BindConfiguredScene(scene, definition, overlayButtonBindings);
            BindRepairResultText(scene);
        }

        private void BindRepairResultText(Scene scene)
        {
            var resultText = scene.GetRootGameObjects()
                .SelectMany(rootObject => rootObject.GetComponentsInChildren<Text>(true))
                .FirstOrDefault(text => text.GetComponentInParent<Button>(true) == null);

            if (resultText == null)
            {
                return;
            }

            resultText.text = string.IsNullOrWhiteSpace(pendingRepairResultMessage)
                ? "修补完成。"
                : pendingRepairResultMessage;
            resultText.font = GetDefaultFont();
            resultText.fontSize = 26;
            resultText.fontStyle = FontStyle.Bold;
            resultText.color = PrototypeUiTheme.Ink;
            resultText.alignment = TextAnchor.MiddleLeft;
            resultText.horizontalOverflow = HorizontalWrapMode.Wrap;
            resultText.verticalOverflow = VerticalWrapMode.Overflow;

            var rectTransform = resultText.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.offsetMin = new Vector2(56, 48);
                rectTransform.offsetMax = new Vector2(-160, -48);
            }
        }

        private void BindConfiguredScene(
            Scene scene,
            PrototypeInteractionSceneDefinition definition,
            List<PrototypeSceneButtonBinding> bindings)
        {
            BindSceneMessageText(scene, definition.Message);
            foreach (var buttonDefinition in definition.Buttons)
            {
                BindSceneButton(
                    scene,
                    buttonDefinition.ObjectName,
                    GetSceneButtonAction(buttonDefinition.Action),
                    bindings);
            }
        }

        private static void BindSceneMessageText(Scene scene, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var messageText = scene.GetRootGameObjects()
                .SelectMany(rootObject => rootObject.GetComponentsInChildren<Text>(true))
                .FirstOrDefault(text => text.GetComponentInParent<Button>(true) == null);

            if (messageText == null)
            {
                return;
            }

            messageText.text = message;
        }

        private UnityEngine.Events.UnityAction GetSceneButtonAction(PrototypeSceneButtonAction action)
        {
            return action switch
            {
                PrototypeSceneButtonAction.GoNextDay => GoNextDayFromPanel,
                PrototypeSceneButtonAction.CloseOverlay => CloseOverlaySceneFromPanel,
                _ => CloseInteractionSceneFromPanel
            };
        }

        private static bool TryFindSceneDefinition(
            string sceneName,
            IReadOnlyList<PrototypeInteractionSceneDefinition> definitions,
            out PrototypeInteractionSceneDefinition definition)
        {
            foreach (var candidate in definitions)
            {
                if (candidate.Matches(sceneName))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = default;
            return false;
        }

        private void BindSceneButton(
            Scene scene,
            string objectName,
            UnityEngine.Events.UnityAction onClick,
            List<PrototypeSceneButtonBinding> bindings)
        {
            var buttonObject = FindSceneObject(scene, objectName);
            var button = buttonObject == null ? null : buttonObject.GetComponentInChildren<Button>(true);
            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(() => TryInvokeSceneButton(button, onClick));
            bindings.Add(new PrototypeSceneButtonBinding(button, onClick));
        }

        private bool TryHandleSceneButtonPointer(List<PrototypeSceneButtonBinding> bindings)
        {
            if (bindings.Count == 0)
            {
                return false;
            }

            var handled = false;
            if (Input.GetMouseButtonDown(0))
            {
                pendingMouseButtonBinding = FindSceneButtonBindingAtScreenPosition(bindings, Input.mousePosition);
                handled = pendingMouseButtonBinding.Button != null;
            }

            if (Input.GetMouseButtonUp(0))
            {
                var pressedBinding = pendingMouseButtonBinding;
                pendingMouseButtonBinding = default;
                if (pressedBinding.Button != null &&
                    IsSameSceneButtonBinding(pressedBinding, FindSceneButtonBindingAtScreenPosition(bindings, Input.mousePosition)) &&
                    TryInvokeSceneButtonBinding(pressedBinding))
                {
                    return true;
                }
            }

            for (var index = 0; index < Input.touchCount; index++)
            {
                var touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began)
                {
                    var binding = FindSceneButtonBindingAtScreenPosition(bindings, touch.position);
                    if (binding.Button == null)
                    {
                        continue;
                    }

                    pendingTouchFingerId = touch.fingerId;
                    pendingTouchButtonBinding = binding;
                    handled = true;
                }
                else if (touch.fingerId == pendingTouchFingerId &&
                    (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled))
                {
                    var pressedBinding = pendingTouchButtonBinding;
                    pendingTouchFingerId = -1;
                    pendingTouchButtonBinding = default;
                    if (touch.phase == TouchPhase.Ended &&
                        pressedBinding.Button != null &&
                        IsSameSceneButtonBinding(pressedBinding, FindSceneButtonBindingAtScreenPosition(bindings, touch.position)) &&
                        TryInvokeSceneButtonBinding(pressedBinding))
                    {
                        return true;
                    }
                }
            }

            return handled;
        }

        private PrototypeSceneButtonBinding FindSceneButtonBindingAtScreenPosition(
            List<PrototypeSceneButtonBinding> bindings,
            Vector2 screenPosition)
        {
            for (var index = bindings.Count - 1; index >= 0; index--)
            {
                var binding = bindings[index];
                if (binding.Button == null)
                {
                    continue;
                }

                if (!binding.Button.gameObject.activeInHierarchy || !binding.Button.interactable)
                {
                    continue;
                }

                var rectTransform = binding.Button.GetComponent<RectTransform>();
                var canvas = binding.Button.GetComponentInParent<Canvas>();
                var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;
                if (rectTransform != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition, camera))
                {
                    return binding;
                }
            }

            return default;
        }

        private bool TryInvokeSceneButtonBinding(PrototypeSceneButtonBinding binding)
        {
            return TryInvokeSceneButton(binding.Button, binding.Action);
        }

        private bool TryInvokeSceneButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                return false;
            }

            if (lastSceneButtonActionFrame == Time.frameCount &&
                lastSceneButtonBinding.Button == button)
            {
                return true;
            }

            lastSceneButtonActionFrame = Time.frameCount;
            lastSceneButtonBinding = new PrototypeSceneButtonBinding(button, action);
            action?.Invoke();
            return true;
        }

        private static bool IsSameSceneButtonBinding(PrototypeSceneButtonBinding left, PrototypeSceneButtonBinding right)
        {
            return left.Button != null && left.Button == right.Button;
        }

        private void ClearSceneButtonBindings(List<PrototypeSceneButtonBinding> bindings)
        {
            bindings.Clear();
            pendingMouseButtonBinding = default;
            pendingTouchButtonBinding = default;
            pendingTouchFingerId = -1;
            lastSceneButtonActionFrame = -1;
            lastSceneButtonBinding = default;
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
            return hudObject == null ? null : EnsureHudText(hudObject);
        }

        private static Component EnsureHudText(Transform hudObject)
        {
            var existingText = hudObject.GetComponentInChildren<Text>(true);
            if (existingText != null)
            {
                return existingText;
            }

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(hudObject, false);
            var rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.font = GetDefaultFont();
            text.fontSize = 18;
            text.fontStyle = FontStyle.Bold;
            text.color = PrototypeUiTheme.Ink;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private void UpdateSceneHud()
        {
            SetTextValue(moneyHudText, state.Coins.ToString());
            SetTextValue(energyHudText, $"{state.Energy}/{Mathf.Max(1, state.DailyEnergyRecovery)}");
            SetTextValue(timeHudText, GetCurrentTimeLabel());
        }

        private string GetCurrentTimeLabel()
        {
            return FormatDayLabel(state.CurrentDay);
        }

        private static string FormatDayLabel(int day)
        {
            return day switch
            {
                1 => "第一天",
                2 => "第二天",
                3 => "第三天",
                _ => $"第{day}天"
            };
        }

        private static int GetLivingCostForNextDay(int day)
        {
            return day == 3 ? DailyLivingCost + ThirdDayExtraCost : DailyLivingCost;
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
            UpdatePhoneVisibility();
            if (IsPhoneOpen())
            {
                phoneApp.Render();
            }

            UpdateInteractionHint();
        }

        private void UpdatePhoneVisibility()
        {
            if (phoneApp == null)
            {
                return;
            }

            phoneApp.SetLauncherVisible(currentRoom == RoomMode.Bedroom && !IsPhoneOpen());
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
                interactionHintText.text = GetInteractionPrompt(interactable);
                return;
            }

            interactionHintText.text = currentRoom == RoomMode.Store
                ? "WASD / 方向键移动，靠近双门或蓝色工作台按 E"
                : "WASD / 方向键移动，靠近床、材料桌、手机或门按 E";
        }

        private string GetInteractionPrompt(PrototypeSceneInteractable interactable)
        {
            if (interactable.Kind == PrototypeSceneInteractionKind.BedroomStoreDoor && canReturnToStoreAfterRest)
            {
                return "按 E：回店铺";
            }

            return interactable.Prompt;
        }

        private string FormatMaterials()
        {
            if (state.MaterialStock.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", state.MaterialStock.Select(pair => $"{pair.Key.DisplayName} x{pair.Value}"));
        }

        private string FormatTodayMaterialConsumption()
        {
            if (state.TodayMaterialConsumption.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", state.TodayMaterialConsumption.Select(pair => $"{pair.Key.DisplayName} x{pair.Value}"));
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

            BuildPhoneUi(canvas);
            BuildConfirmationOverlay(canvas);
        }

        private void BuildPhoneUi(Transform parent)
        {
            phoneLauncher = CreatePanel(parent, "PhoneLauncher", new Vector2(0.72f, 0.02f), new Vector2(0.98f, 0.34f));
            var launcherButton = phoneLauncher.gameObject.AddComponent<Button>();
            var launcherImage = phoneLauncher.gameObject.AddComponent<Image>();
            launcherImage.color = new Color32(35, 39, 45, 245);
            AddOutline(phoneLauncher.gameObject, 2);
            launcherButton.targetGraphic = launcherImage;
            launcherButton.onClick.AddListener(OpenPhone);

            var launcherLayout = phoneLauncher.gameObject.AddComponent<VerticalLayoutGroup>();
            launcherLayout.padding = new RectOffset(20, 20, 18, 18);
            launcherLayout.spacing = 8;
            launcherLayout.childControlWidth = true;
            launcherLayout.childControlHeight = true;
            launcherLayout.childForceExpandWidth = true;
            launcherLayout.childForceExpandHeight = false;

            var launcherTop = CreatePanel(phoneLauncher, "Speaker", new Vector2(0, 0), new Vector2(1, 0));
            var speakerElement = launcherTop.gameObject.AddComponent<LayoutElement>();
            speakerElement.minHeight = 12;
            speakerElement.preferredHeight = 12;
            var speakerImage = launcherTop.gameObject.AddComponent<Image>();
            speakerImage.color = new Color32(13, 17, 22, 255);

            var launcherText = CreateText(phoneLauncher, "Label", 22, FontStyle.Bold, PrototypeUiTheme.Paper);
            launcherText.text = "手机\n查看今日反馈";
            launcherText.alignment = TextAnchor.MiddleCenter;
            launcherText.verticalOverflow = VerticalWrapMode.Truncate;
            var launcherTextElement = launcherText.gameObject.AddComponent<LayoutElement>();
            launcherTextElement.flexibleHeight = 1;

            phonePanel = CreatePanel(parent, "PhonePanel", new Vector2(0.66f, 0.02f), new Vector2(0.98f, 0.98f));
            var phoneFrame = phonePanel.gameObject.AddComponent<Image>();
            phoneFrame.color = new Color32(28, 31, 36, 250);
            AddOutline(phonePanel.gameObject, 2);

            var phoneSafeArea = CreatePanel(phonePanel, "Screen", new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
            var screenImage = phoneSafeArea.gameObject.AddComponent<Image>();
            screenImage.color = new Color32(255, 255, 252, 248);

            var screenLayout = phoneSafeArea.gameObject.AddComponent<VerticalLayoutGroup>();
            screenLayout.padding = new RectOffset(20, 20, 20, 20);
            screenLayout.spacing = 12;
            screenLayout.childControlWidth = true;
            screenLayout.childControlHeight = true;
            screenLayout.childForceExpandWidth = true;
            screenLayout.childForceExpandHeight = false;

            var header = CreatePanel(phoneSafeArea, "Header", new Vector2(0, 0), new Vector2(1, 0));
            var headerElement = header.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = 58;
            headerElement.preferredHeight = 58;
            var headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 12;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;

            phoneTitleText = CreateText(header, "Title", 24, FontStyle.Bold, PrototypeUiTheme.Ink);
            phoneTitleText.alignment = TextAnchor.MiddleLeft;
            var titleElement = phoneTitleText.gameObject.AddComponent<LayoutElement>();
            titleElement.flexibleWidth = 1;

            var closeButton = CreateButton(header, "×", ClosePhoneFromButton);
            var closeRect = closeButton.GetComponent<RectTransform>();
            var closeLayout = closeButton.gameObject.GetComponent<LayoutElement>();
            closeLayout.minWidth = 56;
            closeLayout.preferredWidth = 56;

            var tabs = CreatePanel(phoneSafeArea, "Tabs", new Vector2(0, 0), new Vector2(1, 0));
            var tabsElement = tabs.gameObject.AddComponent<LayoutElement>();
            tabsElement.minHeight = 124;
            tabsElement.preferredHeight = 124;
            var tabsLayout = tabs.gameObject.AddComponent<GridLayoutGroup>();
            tabsLayout.cellSize = new Vector2(248, 54);
            tabsLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            tabsLayout.constraintCount = 2;
            tabsLayout.spacing = new Vector2(10, 10);
            tabsLayout.childAlignment = TextAnchor.UpperCenter;

            AddPhoneTab(tabs, PrototypePhoneAppController.Tab.Reviews, "评价");
            AddPhoneTab(tabs, PrototypePhoneAppController.Tab.Diary, "日记");
            AddPhoneTab(tabs, PrototypePhoneAppController.Tab.Moments, "朋友圈");
            AddPhoneTab(tabs, PrototypePhoneAppController.Tab.Summary, "结算");

            var viewport = CreatePanel(phoneSafeArea, "Viewport", new Vector2(0, 0), new Vector2(1, 1));
            var viewportElement = viewport.gameObject.AddComponent<LayoutElement>();
            viewportElement.flexibleHeight = 1;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color32(244, 243, 238, 255);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            phoneScrollContent = CreatePanel(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1));
            phoneScrollContent.pivot = new Vector2(0.5f, 1f);
            phoneScrollContent.offsetMin = new Vector2(14, 0);
            phoneScrollContent.offsetMax = new Vector2(-14, 0);
            var contentLayout = phoneScrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(0, 0, 14, 14);
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            var contentSize = phoneScrollContent.gameObject.AddComponent<ContentSizeFitter>();
            contentSize.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            phoneBodyText = CreateText(phoneScrollContent, "Body", 20, FontStyle.Normal, PrototypeUiTheme.Ink);
            phoneBodyText.alignment = TextAnchor.UpperLeft;
            phoneBodyText.lineSpacing = 1.05f;
            var bodyRect = phoneBodyText.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0, 1);
            bodyRect.anchorMax = new Vector2(1, 1);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.offsetMin = new Vector2(0, 0);
            bodyRect.offsetMax = new Vector2(0, -16);
            var bodySize = phoneBodyText.gameObject.AddComponent<ContentSizeFitter>();
            bodySize.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            phoneScrollRect = viewport.gameObject.AddComponent<ScrollRect>();
            phoneScrollRect.viewport = viewport;
            phoneScrollRect.content = phoneScrollContent;
            phoneScrollRect.horizontal = false;
            phoneScrollRect.vertical = true;
            phoneScrollRect.movementType = ScrollRect.MovementType.Clamped;

            phoneApp = new PrototypePhoneAppController(
                phonePanel,
                phoneLauncher,
                closeRect,
                phoneScrollRect,
                phoneTitleText,
                phoneBodyText,
                GetPhoneTabTitle,
                GetPhoneTabText,
                ClosePhoneFromButton);
            foreach (var pair in phoneTabButtons)
            {
                phoneApp.SetTabTarget(pair.Key, pair.Value.GetComponent<RectTransform>(), pair.Value);
            }

            phonePanel.gameObject.SetActive(false);
            phoneLauncher.gameObject.SetActive(false);
        }

        private void AddPhoneTab(Transform parent, PrototypePhoneAppController.Tab tab, string label)
        {
            var button = CreateButton(parent, label, () =>
            {
                phoneApp?.SelectTab(tab);
            });
            phoneTabButtons[tab] = button;
        }

        private static string GetPhoneTabTitle(PrototypePhoneAppController.Tab tab)
        {
            return tab switch
            {
                PrototypePhoneAppController.Tab.Reviews => "店铺评价",
                PrototypePhoneAppController.Tab.Diary => "夜晚日记",
                PrototypePhoneAppController.Tab.Moments => "朋友圈",
                PrototypePhoneAppController.Tab.Summary => "今日结算",
                _ => "手机"
            };
        }

        private string GetPhoneTabText(PrototypePhoneAppController.Tab tab)
        {
            return tab switch
            {
                PrototypePhoneAppController.Tab.Reviews => BuildPhoneReviewsText(),
                PrototypePhoneAppController.Tab.Diary => BuildPhoneDiaryText(),
                PrototypePhoneAppController.Tab.Moments => BuildPhoneMomentsText(),
                PrototypePhoneAppController.Tab.Summary => BuildPhoneSummaryText(),
                _ => string.Empty
            };
        }

        private string BuildPhoneReviewsText()
        {
            var todayFeedback = state.FeedbackLog;
            if (todayFeedback.Count == 0)
            {
                return "消息提醒\n\n“今天有人在街角说，你的店又开了。”\n\n“也许明天会有人来看看。”\n\n完成修补后，顾客会在这里留下新的店铺评价。";
            }

            var lines = new List<string>
            {
                $"第 {state.CurrentDay} 天收到 {todayFeedback.Count} 条反馈：",
                string.Empty
            };
            for (var index = 0; index < todayFeedback.Count; index++)
            {
                lines.Add($"{index + 1}. {todayFeedback[index]}");
            }

            return string.Join("\n", lines);
        }

        private string BuildPhoneDiaryText()
        {
            var completedCount = state.CompletedOrders.Count(order => order.DayCompleted == state.CurrentDay);
            var energyText = state.Energy <= 20
                ? "身体已经有点发沉，明天要记得少勉强一点。"
                : "今天还留了一点力气，晚上可以慢慢把事情整理清楚。";
            var authenticityText = state.TodayAuthenticityDelta switch
            {
                > 0 => "我好像更敢按照自己的判断修东西了。",
                < 0 => "我又忍不住去迎合别人的期待。也许这不是唯一的做法。",
                _ => "今天没有明显偏离，也没有特别靠近自己。"
            };
            var completedText = completedCount == 0
                ? "今天没有完成修补，店里安静得能听见木地板的声音。"
                : $"今天完成了 {completedCount} 件旧物。每一件都有自己的裂纹和重量。";

            return $"{FormatDayLabel(state.CurrentDay)} 夜\n\n{completedText}\n\n{authenticityText}\n\n{energyText}\n\n当前真实度：{state.Authenticity}";
        }

        private string BuildPhoneMomentsText()
        {
            var completedToday = state.CompletedOrders
                .Where(record => record.DayCompleted == state.CurrentDay && record.Order != null)
                .Select(record => record.Order)
                .ToList();
            if (completedToday.Count == 0)
            {
                return "小城动态\n\n今天没有新的顾客后续。\n\n也许有人还在犹豫，要不要把那件旧东西拿来修。";
            }

            var lines = new List<string>
            {
                "小城动态",
                string.Empty
            };
            foreach (var order in completedToday)
            {
                var customerName = order.Customer == null ? "一位顾客" : order.Customer.DisplayName;
                var itemName = string.IsNullOrWhiteSpace(order.DisplayName) ? "旧物" : order.DisplayName;
                lines.Add($"{customerName}：把「{itemName}」带回家了。原来修补不是把过去藏起来。");
                lines.Add(string.Empty);
            }

            lines.Add("这些零散的消息，会慢慢变成这家小店被记住的方式。");
            return string.Join("\n", lines);
        }

        private string BuildPhoneSummaryText()
        {
            var todayCompletedCount = state.CompletedOrders.Count(order => order.DayCompleted == state.CurrentDay);
            return $"第 {state.CurrentDay} 天\n\n完成订单：{todayCompletedCount}\n当日收入：{state.TodayIncome}\n当日支出：{state.TodayExpenses}\n今日材料消耗：{FormatTodayMaterialConsumption()}\n今日能量变化：-{state.TodayEnergySpent}\n今日口碑变化：收到 {state.FeedbackLog.Count} 条评价\n金币余额：{state.Coins}\n能量：{state.Energy}/{Mathf.Max(1, state.DailyEnergyRecovery)}\n真实度变化：{FormatSigned(state.TodayAuthenticityDelta)}\n当前真实度：{state.Authenticity}\n\n当前材料：\n{FormatMaterials()}\n\n需要补材料时，去卧室材料桌购买。准备好了再到床边休息。";
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
            if (FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Any(eventSystem => eventSystem.isActiveAndEnabled))
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }
    }

}
