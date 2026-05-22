using System.Collections;
using System.Linq;
using APlaceLikeMe.Core;
using APlaceLikeMe.Data;
using APlaceLikeMe.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
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
        private const string InteractionSceneName = "S_OrderBoardUI";
        private const string RuntimeRootName = "PrototypeRuntime";
        private const string PlayerObjectName = "LXR";
        private const string LegacyPlayerObjectName = "Player";
        private const float CameraOrthographicSize = 5f;
        private const float InteractionRadius = 0.55f;

        private static readonly Vector2 StoreSpawn = new(-3.94f, -0.03f);
        private static readonly Vector2 StoreFromBedroomSpawn = new(-6.6f, 1.1f);
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
        private Transform currentPlayer;
        private Camera roomCamera;
        private PrototypeInteractionPanelMode pendingPanelMode = PrototypeInteractionPanelMode.Orders;
        private bool isInteractionSceneLoaded;
        private bool suppressNextInteractInput;
        private bool isRoomSwitching;
        private Text titleText;
        private Text resourceText;
        private Text statusText;
        private Text interactionHintText;
        private Text feedbackText;
        private RectTransform confirmationOverlay;
        private Text confirmationText;
        private System.Action confirmationYesAction;
        private System.Action confirmationNoAction;

        public static PrototypeGameController Active { get; private set; }
        public GameSessionState State => state;
        public OrderService OrderService => orderService;
        public PrototypeGameConfig Config => config;
        public PrototypeInteractionPanelMode PendingPanelMode => pendingPanelMode;
        public bool AreWorldControlsLocked => isRoomSwitching || isInteractionSceneLoaded || IsConfirmationOpen();

        private void Start()
        {
            Active = this;
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
            if (AreWorldControlsLocked)
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

            var cameraBounds = currentRoom == RoomMode.Store ? StoreCameraBounds : BedroomCameraBounds;
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
            SwitchRoom(RoomMode.Store, StoreSpawn);
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
                    SwitchRoom(RoomMode.Bedroom, BedroomFromStoreSpawn);
                    break;
                case PrototypeSceneInteractionKind.BedroomStoreDoor:
                    feedbackText.text = "你回到了店铺。";
                    SwitchRoom(RoomMode.Store, StoreFromBedroomSpawn);
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

                    OpenInteractionScene(PrototypeInteractionPanelMode.MaterialPurchase);
                    break;
                case PrototypeSceneInteractionKind.OrderBoard:
                    if (state.Phase == GamePhase.DayEnd)
                    {
                        feedbackText.text = "今天的原型流程已经结束。";
                        Render();
                        return;
                    }

                    OpenInteractionScene(PrototypeInteractionPanelMode.Orders);
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

            ShowConfirmation(
                "是否结束今天并睡到明天？",
                "睡到明天",
                "再等等",
                GoNextDay,
                () =>
                {
                    feedbackText.text = "你还留在卧室。可以先去桌子购买材料。";
                    Render();
                });
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
            SwitchRoom(RoomMode.Bedroom, BedroomWakeSpawn);
            Render();
        }

        private void SwitchRoom(RoomMode room, Vector2 spawnPosition)
        {
            currentRoom = room;
            if (roomSwitchRoutine != null)
            {
                StopCoroutine(roomSwitchRoutine);
            }

            roomSwitchRoutine = StartCoroutine(SwitchRoomRoutine(room, spawnPosition));
            Render();
        }

        private IEnumerator SwitchRoomRoutine(RoomMode room, Vector2 spawnPosition)
        {
            isRoomSwitching = true;
            currentPlayer = null;
            roomCamera = null;

            var targetSceneName = GetSceneName(room);
            if (!string.IsNullOrEmpty(loadedRoomSceneName) && loadedRoomSceneName != targetSceneName)
            {
                var oldScene = SceneManager.GetSceneByName(loadedRoomSceneName);
                if (oldScene.IsValid() && oldScene.isLoaded)
                {
                    var unloadOperation = SceneManager.UnloadSceneAsync(oldScene);
                    while (unloadOperation != null && !unloadOperation.isDone)
                    {
                        yield return null;
                    }
                }
            }

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

            targetScene = SceneManager.GetSceneByName(targetSceneName);
            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                feedbackText.text = $"无法找到已加载场景：{targetSceneName}";
                isRoomSwitching = false;
                Render();
                yield break;
            }

            loadedRoomSceneName = targetSceneName;
            SceneManager.SetActiveScene(targetScene);
            SetupRoomScene(targetScene, room, spawnPosition);
            isRoomSwitching = false;
            roomSwitchRoutine = null;
            Render();
        }

        private void SetupRoomScene(Scene scene, RoomMode room, Vector2 spawnPosition)
        {
            SetRoomCamera(scene);
            var runtimeRoot = CreateRuntimeRoot(scene);
            SetupPlayer(scene, room, spawnPosition);

            if (room == RoomMode.Store)
            {
                CreateInteractable(runtimeRoot.transform, "BedroomDoorTrigger", PrototypeSceneInteractionKind.StoreBedroomDoor, new Vector2(-2.0f, 1.0f), new Vector2(3.4f, 2.2f), "按 E：进入卧室");
                CreateInteractable(runtimeRoot.transform, "OrderBoardTrigger", PrototypeSceneInteractionKind.OrderBoard, new Vector2(-4.0f, -2.6f), new Vector2(8.6f, 2.6f), "按 E：接订单");
            }
            else
            {
                CreateInteractable(runtimeRoot.transform, "StoreDoorTrigger", PrototypeSceneInteractionKind.BedroomStoreDoor, new Vector2(-3.0f, -7.0f), new Vector2(4.5f, 1.6f), "按 E：回到店铺");
                CreateInteractable(runtimeRoot.transform, "BedTrigger", PrototypeSceneInteractionKind.Bed, new Vector2(-6.0f, 1.9f), new Vector2(3.0f, 2.4f), "按 E：结束今天");
                CreateInteractable(runtimeRoot.transform, "SupplyTableTrigger", PrototypeSceneInteractionKind.SupplyTable, new Vector2(2.5f, -3.0f), new Vector2(8.8f, 3.2f), "按 E：购买材料");
            }

            if (currentPlayer != null)
            {
                UpdateCameraForPlayer(currentPlayer.position);
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
            ConfigurePlayerMovement(playerObject, room == RoomMode.Store ? StoreMovementBounds : BedroomMovementBounds);
            currentPlayer = playerObject.transform;
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

            var playerPosition = currentPlayer.position;
            var hits = Physics2D.OverlapCircleAll(playerPosition, InteractionRadius);
            PrototypeSceneInteractable nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                var interactable = hit.GetComponent<PrototypeSceneInteractable>();
                if (interactable == null)
                {
                    continue;
                }

                var distance = Vector2.SqrMagnitude((Vector2)interactable.transform.position - (Vector2)playerPosition);
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
            pendingPanelMode = mode;
            if (isInteractionSceneLoaded)
            {
                return;
            }

            isInteractionSceneLoaded = true;
            SceneManager.LoadSceneAsync(InteractionSceneName, LoadSceneMode.Additive);
        }

        private void UnloadInteractionSceneIfLoaded()
        {
            if (!isInteractionSceneLoaded)
            {
                return;
            }

            isInteractionSceneLoaded = false;
            var scene = SceneManager.GetSceneByName(InteractionSceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.UnloadSceneAsync(scene);
            }
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
            if (titleText == null || resourceText == null || statusText == null)
            {
                return;
            }

            titleText.text = $"《不完美的小店》{GetRoomDisplayName()}  Day {state.CurrentDay} / {config.PrototypeDays}";
            resourceText.text = $"金币 {state.Coins}    能量 {state.Energy}    真实度 {state.Authenticity} ({FormatSigned(state.TodayAuthenticityDelta)})\n材料：{FormatMaterials()}";
            statusText.text = FormatStatus();
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

        private string FormatStatus()
        {
            var phaseText = state.Phase switch
            {
                GamePhase.OrderSelection when currentRoom == RoomMode.Store => "白天营业：蓝色工作台接订单，双门进入卧室。",
                GamePhase.OrderSelection => "卧室：床结束当天，桌子购买材料，门回店铺。",
                GamePhase.DayEnd => "原型完成：这间小店暂时打烊。",
                _ => "小店准备中。"
            };

            return $"{phaseText}\n移动：WASD / 方向键    互动：E";
        }

        private string FormatMaterials()
        {
            if (state.MaterialStock.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", state.MaterialStock.Select(pair => $"{pair.Key.DisplayName} x{pair.Value}"));
        }

        private string GetRoomDisplayName()
        {
            return currentRoom == RoomMode.Store ? "店铺" : "卧室";
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

        private void BuildUi()
        {
            var canvas = CreateCanvas();
            var root = CreatePanel(canvas.transform, "HudRoot", new Vector2(0, 0), new Vector2(1, 1));

            var header = CreateOverlayCard(root, "Header", new Vector2(0.02f, 0.83f), new Vector2(0.48f, 0.98f));
            titleText = CreateText(header, "Title", 25, FontStyle.Bold, PrototypeUiTheme.Ink);
            PlaceFull(titleText.GetComponent<RectTransform>(), new RectOffset(16, 16, 10, 64));
            statusText = CreateText(header, "Status", 17, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            PlaceFull(statusText.GetComponent<RectTransform>(), new RectOffset(16, 16, 54, 10));

            var resources = CreateOverlayCard(root, "Resources", new Vector2(0.52f, 0.86f), new Vector2(0.98f, 0.98f));
            resourceText = CreateText(resources, "ResourceText", 18, FontStyle.Bold, PrototypeUiTheme.Ink);
            PlaceFull(resourceText.GetComponent<RectTransform>(), new RectOffset(16, 16, 12, 12));

            var hint = CreateOverlayCard(root, "InteractionHint", new Vector2(0.24f, 0.02f), new Vector2(0.76f, 0.09f));
            interactionHintText = CreateText(hint, "HintText", 19, FontStyle.Bold, PrototypeUiTheme.Ink);
            interactionHintText.alignment = TextAnchor.MiddleCenter;
            PlaceFull(interactionHintText.GetComponent<RectTransform>(), new RectOffset(12, 12, 6, 6));

            var feedback = CreateOverlayCard(root, "Feedback", new Vector2(0.02f, 0.10f), new Vector2(0.58f, 0.19f));
            feedbackText = CreateText(feedback, "FeedbackText", 18, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            feedbackText.alignment = TextAnchor.MiddleLeft;
            PlaceFull(feedbackText.GetComponent<RectTransform>(), new RectOffset(14, 14, 8, 8));

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

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }

}
