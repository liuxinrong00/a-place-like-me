using System.Collections.Generic;
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
        private enum InteractionTarget
        {
            None,
            Door,
            BulletinBoard,
            Bed,
            Table
        }

        private enum RoomMode
        {
            Shop,
            Bedroom
        }

        private static Font cachedDefaultFont;
        private const float PlayerMoveSpeed = 0.55f;
        private const string InteractionSceneName = "S_OrderBoardUI";

        private readonly GameSessionState state = new();
        private readonly OrderService orderService = new();
        private readonly Rect shopDoorInteractionZone = new(0.78f, 0.82f, 0.2f, 0.16f);
        private readonly Rect bulletinInteractionZone = new(0.0f, 0.0f, 0.16f, 0.16f);
        private readonly Rect bedroomDoorInteractionZone = new(0.48f, 0.0f, 0.2f, 0.16f);
        private readonly Rect bedInteractionZone = new(0.0f, 0.42f, 0.22f, 0.48f);
        private readonly Rect tableInteractionZone = new(0.40f, 0.74f, 0.28f, 0.22f);

        [SerializeField] private PrototypeGameConfig config;

        private RoomMode currentRoom = RoomMode.Shop;
        private Vector2 playerPosition = new(0.48f, 0.24f);
        private PrototypeInteractionPanelMode pendingPanelMode = PrototypeInteractionPanelMode.Orders;
        private bool isInteractionSceneLoaded;
        private bool suppressNextInteractInput;
        private bool roomVisualsDirty = true;
        private Text titleText;
        private Text resourceText;
        private Text shopStatusText;
        private Text interactionHintText;
        private Text feedbackText;
        private RectTransform shopFloorRoot;
        private RectTransform roomObjectsRoot;
        private RectTransform playerMarker;
        private RectTransform confirmationOverlay;
        private Text confirmationText;
        private System.Action confirmationYesAction;
        private System.Action confirmationNoAction;

        public static PrototypeGameController Active { get; private set; }
        public GameSessionState State => state;
        public OrderService OrderService => orderService;
        public PrototypeGameConfig Config => config;
        public PrototypeInteractionPanelMode PendingPanelMode => pendingPanelMode;

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
            if (playerMarker == null)
            {
                return;
            }

            if (isInteractionSceneLoaded)
            {
                return;
            }

            if (IsConfirmationOpen())
            {
                return;
            }

            HandlePlayerMovement();
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
            return $"夜晚结算\n完成订单：{todayCompletedCount}\n当日收入：{state.TodayIncome}\n真实度变化：{FormatSigned(state.TodayAuthenticityDelta)}\n顾客反馈：\n{feedback}\n\n当前材料：\n{FormatMaterials()}\n\n可以用金币补一点材料。";
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
            var result = orderService.TryBuyNightSupply(state, material, clampedCount, clampedCount);
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

        private void StartDay()
        {
            state.SetPhase(GamePhase.OrderSelection);
            var orders = orderService.GetOrdersForDay(config.OrderPool, state.CurrentDay, config.OrdersPerDay);
            state.SetTodaysOrders(orders);
            currentRoom = RoomMode.Shop;
            playerPosition = new Vector2(0.48f, 0.24f);
            roomVisualsDirty = true;
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = $"第 {state.CurrentDay} 天开店。去公告栏查看订单，晚上可以在卧室桌子补货。";
            Render();
        }

        private void TryInteract()
        {
            switch (GetCurrentInteractionTarget())
            {
                case InteractionTarget.Door:
                    TryUseDoor();
                    break;
                case InteractionTarget.BulletinBoard:
                    OpenInteractionScene(PrototypeInteractionPanelMode.Orders);
                    break;
                case InteractionTarget.Bed:
                    if (state.Phase == GamePhase.NightSummary)
                    {
                        ShowConfirmation(
                            "是否睡到明天？",
                            "是",
                            "否",
                            GoNextDay,
                            () =>
                            {
                                feedbackText.text = "你还留在卧室。可以先去桌子补货。";
                                Render();
                            });
                    }
                    else
                    {
                        feedbackText.text = "现在还没到夜晚。先从店铺的门进入黑夜。";
                        Render();
                    }

                    break;
                case InteractionTarget.Table:
                    if (state.Phase == GamePhase.NightSummary)
                    {
                        OpenInteractionScene(PrototypeInteractionPanelMode.NightSummary);
                    }
                    else
                    {
                        feedbackText.text = "桌子留给夜晚补货。现在是早上，先从门回店铺。";
                        Render();
                    }

                    break;
                default:
                    feedbackText.text = currentRoom == RoomMode.Shop ? "靠近门或公告栏后按 E 互动。" : "靠近床、桌子或门后按 E 互动。";
                    Render();
                    break;
            }
        }

        private void TryUseDoor()
        {
            if (currentRoom == RoomMode.Shop)
            {
                ShowConfirmation(
                    "是否跳到黑夜并进入卧室？",
                    "是",
                    "否",
                    EnterNightBedroom,
                    () =>
                    {
                        feedbackText.text = "你留在店里。还可以继续处理订单。";
                        Render();
                    });
                return;
            }

            if (state.Phase == GamePhase.NightSummary)
            {
                feedbackText.text = "已经是夜晚了。等睡到第二天早上再回店铺。";
                Render();
                return;
            }

            ToggleRoom();
        }

        private void EnterNightBedroom()
        {
            state.SetPhase(GamePhase.NightSummary);
            currentRoom = RoomMode.Bedroom;
            playerPosition = new Vector2(0.58f, 0.16f);
            roomVisualsDirty = true;
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = "夜晚到了。桌子可以补货，床可以睡到明天。";
            Render();
        }

        private void ToggleRoom()
        {
            currentRoom = currentRoom == RoomMode.Shop ? RoomMode.Bedroom : RoomMode.Shop;
            playerPosition = currentRoom == RoomMode.Shop ? new Vector2(0.84f, 0.82f) : new Vector2(0.58f, 0.16f);
            roomVisualsDirty = true;
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = currentRoom == RoomMode.Shop ? "你回到了店铺。" : "你进入了卧室。床用于睡到明天，桌子用于夜晚补货。";
            Render();
        }

        private void GoNextDay()
        {
            if (state.CurrentDay >= config.PrototypeDays)
            {
                state.SetPhase(GamePhase.DayEnd);
                UnloadInteractionSceneIfLoaded();
                feedbackText.text = "原型流程完成：你已经经营了 3 天。后续可以扩展口碑、手机与店铺升级。";
                Render();
                return;
            }

            var orders = orderService.GetOrdersForDay(config.OrderPool, state.CurrentDay + 1, config.OrdersPerDay);
            state.StartNextDay(orders);
            currentRoom = RoomMode.Bedroom;
            playerPosition = new Vector2(0.13f, 0.48f);
            roomVisualsDirty = true;
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = $"第 {state.CurrentDay} 天早上。能量已恢复，走到门边按 E 回店铺。";
            Render();
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
            titleText.text = $"《不完美的小店》{(currentRoom == RoomMode.Shop ? "店铺" : "卧室")}原型  Day {state.CurrentDay} / {config.PrototypeDays}";
            resourceText.text = $"金币 {state.Coins}    能量 {state.Energy}    真实度 {state.Authenticity} ({FormatSigned(state.TodayAuthenticityDelta)})\n材料：{FormatMaterials()}";
            shopStatusText.text = FormatShopStatus();

            if (roomVisualsDirty)
            {
                RebuildRoomObjects();
                roomVisualsDirty = false;
            }

            UpdateInteractionHint();
            UpdatePlayerMarker();
        }

        private void HandlePlayerMovement()
        {
            var move = Vector2.zero;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                move.x -= 1f;
            }

            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                move.x += 1f;
            }

            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                move.y -= 1f;
            }

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                move.y += 1f;
            }

            if (move == Vector2.zero)
            {
                return;
            }

            playerPosition += move.normalized * PlayerMoveSpeed * Time.deltaTime;
            playerPosition.x = Mathf.Clamp01(playerPosition.x);
            playerPosition.y = Mathf.Clamp01(playerPosition.y);
            UpdatePlayerMarker();
            UpdateInteractionHint();
        }

        private InteractionTarget GetCurrentInteractionTarget()
        {
            if (currentRoom == RoomMode.Shop)
            {
                if (shopDoorInteractionZone.Contains(playerPosition))
                {
                    return InteractionTarget.Door;
                }

                if (bulletinInteractionZone.Contains(playerPosition))
                {
                    return InteractionTarget.BulletinBoard;
                }
            }
            else
            {
                if (bedroomDoorInteractionZone.Contains(playerPosition))
                {
                    return InteractionTarget.Door;
                }

                if (bedInteractionZone.Contains(playerPosition))
                {
                    return InteractionTarget.Bed;
                }

                if (tableInteractionZone.Contains(playerPosition))
                {
                    return InteractionTarget.Table;
                }
            }

            return InteractionTarget.None;
        }

        private void UpdateInteractionHint()
        {
            if (interactionHintText == null)
            {
                return;
            }

            interactionHintText.text = GetCurrentInteractionTarget() switch
            {
                InteractionTarget.Door when currentRoom == RoomMode.Shop => "按 E：询问是否进入黑夜",
                InteractionTarget.Door when currentRoom == RoomMode.Bedroom && state.Phase == GamePhase.NightSummary => "夜晚不能回店铺，先睡到明天",
                InteractionTarget.Door when currentRoom == RoomMode.Bedroom => "按 E：回到店铺",
                InteractionTarget.Bed when state.Phase == GamePhase.NightSummary => "按 E：询问是否睡到明天",
                InteractionTarget.Bed => "床：夜晚才能睡到明天",
                InteractionTarget.Table when state.Phase == GamePhase.NightSummary => "按 E：在桌子补货",
                InteractionTarget.Table => "桌子：夜晚可补货",
                InteractionTarget.BulletinBoard => "按 E：打开订单场景",
                _ => currentRoom == RoomMode.Shop ? "WASD / 方向键移动，靠近门或公告栏按 E 互动" : "WASD / 方向键移动，靠近床、桌子或门按 E 互动"
            };
        }

        private void UpdatePlayerMarker()
        {
            if (playerMarker == null)
            {
                return;
            }

            playerMarker.anchorMin = playerPosition;
            playerMarker.anchorMax = playerPosition;
            playerMarker.anchoredPosition = Vector2.zero;
        }

        private void RebuildRoomObjects()
        {
            if (roomObjectsRoot == null)
            {
                return;
            }

            for (var index = roomObjectsRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(roomObjectsRoot.GetChild(index).gameObject);
            }

            if (currentRoom == RoomMode.Shop)
            {
                BuildShopRoomObjects(roomObjectsRoot);
            }
            else
            {
                BuildBedroomRoomObjects(roomObjectsRoot);
            }
        }

        private static void BuildShopRoomObjects(Transform parent)
        {
            CreateRoomObject(parent, "收银台", new Vector2(0.04f, 0.62f), new Vector2(0.24f, 0.82f), PrototypeUiTheme.Card);
            CreateRoomObject(parent, "货架", new Vector2(0.36f, 0.52f), new Vector2(0.58f, 0.86f), PrototypeUiTheme.Card);
            CreateRoomObject(parent, "货架", new Vector2(0.70f, 0.52f), new Vector2(0.92f, 0.86f), PrototypeUiTheme.Card);
            CreateRoomObject(parent, "工作台", new Vector2(0.42f, 0.16f), new Vector2(0.76f, 0.32f), PrototypeUiTheme.Card);
            CreateRoomObject(parent, "公告栏\n订单", new Vector2(0.01f, 0.00f), new Vector2(0.15f, 0.15f), PrototypeUiTheme.CardSelected, 17);
            CreateRoomObject(parent, "门\n卧室", new Vector2(0.78f, 0.93f), new Vector2(0.98f, 1.00f), PrototypeUiTheme.Card, 16);
            CreateRoomObject(parent, "小窗", new Vector2(0.08f, 0.88f), new Vector2(0.26f, 0.98f), PrototypeUiTheme.Card, 16);
        }

        private static void BuildBedroomRoomObjects(Transform parent)
        {
            CreateRoomObject(parent, "床\n睡到明天", new Vector2(0.02f, 0.42f), new Vector2(0.20f, 0.92f), PrototypeUiTheme.Card, 21);
            CreateRoomObject(parent, "桌子\n补货", new Vector2(0.40f, 0.74f), new Vector2(0.68f, 0.96f), PrototypeUiTheme.CardSelected, 21);
            CreateRoomObject(parent, "椅子", new Vector2(0.47f, 0.56f), new Vector2(0.58f, 0.70f), PrototypeUiTheme.Card, 20);
            CreateRoomObject(parent, "盆栽", new Vector2(0.76f, 0.76f), new Vector2(0.88f, 0.94f), PrototypeUiTheme.Card, 20);
            CreateRoomObject(parent, "地毯", new Vector2(0.36f, 0.18f), new Vector2(0.78f, 0.44f), PrototypeUiTheme.Card, 22);
            CreateRoomObject(parent, "门\n店铺", new Vector2(0.50f, 0.00f), new Vector2(0.66f, 0.12f), PrototypeUiTheme.Card, 16);
        }

        private string FormatMaterials()
        {
            if (state.MaterialStock.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", state.MaterialStock.Select(pair => $"{pair.Key.DisplayName} x{pair.Value}"));
        }

        private string FormatShopStatus()
        {
            var phaseText = state.Phase switch
            {
                GamePhase.OrderSelection => currentRoom == RoomMode.Shop ? "白天营业：公告栏会打开订单；门会询问是否进入黑夜。" : "早上醒来：从卧室门回到店铺。",
                GamePhase.NightSummary => currentRoom == RoomMode.Bedroom ? "夜晚：桌子补货，床边按 E 询问是否睡到明天。" : "夜晚：需要留在卧室，桌子补货或床边睡到明天。",
                GamePhase.DayEnd => "原型完成：这间小店暂时打烊。",
                _ => "小店准备中。"
            };

            return $"{phaseText}\n移动：WASD / 方向键    互动：E";
        }

        private void BuildUi()
        {
            var canvas = CreateCanvas();
            var root = CreatePanel(canvas.transform, "Root", new Vector2(0, 0), new Vector2(1, 1));
            var rootImage = root.gameObject.AddComponent<Image>();
            rootImage.color = PrototypeUiTheme.Background;
            var vertical = root.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.padding = new RectOffset(28, 28, 22, 22);
            vertical.spacing = PrototypeUiTheme.SpaceMedium;
            vertical.childControlHeight = true;
            vertical.childControlWidth = true;
            vertical.childForceExpandHeight = false;

            var header = CreateCard(root, "Header", PrototypeUiTheme.Paper);
            var headerElement = header.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = 132;
            var headerLayout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            headerLayout.padding = new RectOffset(22, 22, 14, 14);
            headerLayout.spacing = 6;
            headerLayout.childControlWidth = true;
            titleText = CreateText(header, "Title", 30, FontStyle.Bold, PrototypeUiTheme.Ink);
            resourceText = CreateText(header, "Resources", 19, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            shopStatusText = CreateText(header, "ShopStatus", 17, FontStyle.Normal, PrototypeUiTheme.InkMuted);

            var shopPanel = CreateCard(root, "Room", PrototypeUiTheme.Paper);
            var shopElement = shopPanel.gameObject.AddComponent<LayoutElement>();
            shopElement.minHeight = 720;
            shopElement.flexibleHeight = 1;
            BuildRoomFloor(shopPanel);

            feedbackText = CreateText(root, "Feedback", 18, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            var feedbackElement = feedbackText.gameObject.AddComponent<LayoutElement>();
            feedbackElement.minHeight = 36;

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

        private void BuildRoomFloor(RectTransform parent)
        {
            shopFloorRoot = CreatePanel(parent, "FloorRoot", new Vector2(0, 0), new Vector2(1, 1));
            shopFloorRoot.offsetMin = new Vector2(18, 18);
            shopFloorRoot.offsetMax = new Vector2(-18, -18);
            roomObjectsRoot = CreatePanel(shopFloorRoot, "RoomObjects", new Vector2(0, 0), new Vector2(1, 1));

            interactionHintText = CreateText(shopFloorRoot, "InteractionHint", 18, FontStyle.Bold, PrototypeUiTheme.Ink);
            var hintRect = interactionHintText.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.16f, 0.01f);
            hintRect.anchorMax = new Vector2(0.76f, 0.08f);
            hintRect.offsetMin = Vector2.zero;
            hintRect.offsetMax = Vector2.zero;
            interactionHintText.alignment = TextAnchor.MiddleCenter;

            playerMarker = CreatePanel(shopFloorRoot, "Player", playerPosition, playerPosition);
            playerMarker.sizeDelta = new Vector2(46, 46);
            var playerImage = playerMarker.gameObject.AddComponent<Image>();
            playerImage.color = PrototypeUiTheme.Primary;
            AddOutline(playerMarker.gameObject, 3);
            var playerLabel = CreateText(playerMarker, "PlayerLabel", 20, FontStyle.Bold, PrototypeUiTheme.Ink);
            playerLabel.text = "我";
            playerLabel.alignment = TextAnchor.MiddleCenter;
            var playerLabelRect = playerLabel.GetComponent<RectTransform>();
            playerLabelRect.anchorMin = Vector2.zero;
            playerLabelRect.anchorMax = Vector2.one;
            playerLabelRect.offsetMin = Vector2.zero;
            playerLabelRect.offsetMax = Vector2.zero;
        }

        private static RectTransform CreateCanvas()
        {
            var canvasObject = new GameObject("PrototypeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var rectTransform = canvasObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

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

        private static RectTransform CreateCard(Transform parent, string name, Color color)
        {
            var card = CreatePanel(parent, name, new Vector2(0, 0), new Vector2(1, 1));
            var image = card.gameObject.AddComponent<Image>();
            image.color = color;
            AddOutline(card.gameObject, 2);
            return card;
        }

        private static RectTransform CreateRoomObject(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Color color, int fontSize = 25)
        {
            var roomObject = CreateCard(parent, label, color);
            roomObject.anchorMin = anchorMin;
            roomObject.anchorMax = anchorMax;
            roomObject.offsetMin = Vector2.zero;
            roomObject.offsetMax = Vector2.zero;

            var labelText = CreateText(roomObject, "Label", fontSize, FontStyle.Bold, PrototypeUiTheme.Ink);
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleCenter;
            var labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4, 4);
            labelRect.offsetMax = new Vector2(-4, -4);
            return roomObject;
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
