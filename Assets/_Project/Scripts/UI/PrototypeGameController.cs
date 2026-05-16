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
            Bed
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

        [SerializeField] private PrototypeGameConfig config;

        private RoomMode currentRoom = RoomMode.Shop;
        private Vector2 playerPosition = new(0.48f, 0.24f);
        private PrototypeInteractionPanelMode pendingPanelMode = PrototypeInteractionPanelMode.Orders;
        private bool isInteractionSceneLoaded;
        private bool roomVisualsDirty = true;
        private Text titleText;
        private Text resourceText;
        private Text shopStatusText;
        private Text interactionHintText;
        private Text feedbackText;
        private RectTransform shopFloorRoot;
        private RectTransform roomObjectsRoot;
        private RectTransform playerMarker;

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

            HandlePlayerMovement();
            if (Input.GetKeyDown(KeyCode.E))
            {
                TryInteract();
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
            return $"夜晚结算\n完成订单：{todayCompletedCount}\n当日收入：{state.TodayIncome}\n真实度变化：{FormatSigned(state.TodayAuthenticityDelta)}\n顾客反馈：\n{feedback}\n\n可以用金币补一点材料，再进入下一天。";
        }

        public OrderResult TryCompleteOrderFromPanel(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            var result = orderService.TryCompleteOrder(state, order, repairMethod);
            Render();
            return result;
        }

        public OrderResult TryBuySupplyFromPanel(MaterialDefinition material)
        {
            var result = orderService.TryBuyNightSupply(state, material, config.NightSupplyCost, config.NightSupplyAmount);
            Render();
            return result;
        }

        public void GoNextDayFromPanel()
        {
            GoNextDay();
        }

        public void CloseInteractionSceneFromPanel()
        {
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
            feedbackText.text = $"第 {state.CurrentDay} 天开店。去公告栏查看订单，或者走到门口进入卧室。";
            Render();
        }

        private void TryInteract()
        {
            switch (GetCurrentInteractionTarget())
            {
                case InteractionTarget.Door:
                    ToggleRoom();
                    break;
                case InteractionTarget.BulletinBoard:
                    OpenInteractionScene(PrototypeInteractionPanelMode.Orders);
                    break;
                case InteractionTarget.Bed:
                    if (state.Phase == GamePhase.OrderSelection)
                    {
                        state.SetPhase(GamePhase.NightSummary);
                        OpenInteractionScene(PrototypeInteractionPanelMode.NightSummary);
                    }
                    else if (state.Phase == GamePhase.NightSummary)
                    {
                        GoNextDay();
                    }
                    else
                    {
                        feedbackText.text = "床铺整理好了，小店今天已经打烊。";
                        Render();
                    }

                    break;
                default:
                    feedbackText.text = currentRoom == RoomMode.Shop ? "靠近门或公告栏后按 E 互动。" : "靠近床或门后按 E 互动。";
                    Render();
                    break;
            }
        }

        private void ToggleRoom()
        {
            currentRoom = currentRoom == RoomMode.Shop ? RoomMode.Bedroom : RoomMode.Shop;
            playerPosition = currentRoom == RoomMode.Shop ? new Vector2(0.84f, 0.82f) : new Vector2(0.58f, 0.16f);
            roomVisualsDirty = true;
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = currentRoom == RoomMode.Shop ? "你回到了店铺。" : "你进入了卧室。床可以用于结束当天。";
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
            currentRoom = RoomMode.Shop;
            playerPosition = new Vector2(0.84f, 0.82f);
            roomVisualsDirty = true;
            UnloadInteractionSceneIfLoaded();
            feedbackText.text = $"第 {state.CurrentDay} 天开店。能量已恢复，小店又亮起一盏旧灯。";
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
                InteractionTarget.Door when currentRoom == RoomMode.Shop => "按 E：进入卧室",
                InteractionTarget.Door when currentRoom == RoomMode.Bedroom => "按 E：回到店铺",
                InteractionTarget.Bed when state.Phase == GamePhase.OrderSelection => "按 E：在床上结束当天并结算",
                InteractionTarget.Bed when state.Phase == GamePhase.NightSummary => "按 E：睡到下一天",
                InteractionTarget.Bed => "床：今天已经结算过了",
                InteractionTarget.BulletinBoard => "按 E：打开订单场景",
                _ => currentRoom == RoomMode.Shop ? "WASD / 方向键移动，靠近门或公告栏按 E 互动" : "WASD / 方向键移动，靠近床或门按 E 互动"
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
            CreateRoomObject(parent, "收银台", new Vector2(0.03f, 0.68f), new Vector2(0.26f, 0.86f), PrototypeUiTheme.Card);
            CreateRoomObject(parent, "货架", new Vector2(0.39f, 0.48f), new Vector2(0.62f, 0.86f), PrototypeUiTheme.PaperMuted);
            CreateRoomObject(parent, "货架", new Vector2(0.75f, 0.48f), new Vector2(0.98f, 0.86f), PrototypeUiTheme.PaperMuted);
            CreateRoomObject(parent, "货架", new Vector2(0.39f, 0.06f), new Vector2(0.62f, 0.38f), PrototypeUiTheme.PaperMuted);
            CreateRoomObject(parent, "货架", new Vector2(0.75f, 0.06f), new Vector2(0.98f, 0.38f), PrototypeUiTheme.PaperMuted);
            CreateRoomObject(parent, "公告栏\n按 E 打开订单场景", new Vector2(0.01f, 0.00f), new Vector2(0.13f, 0.13f), PrototypeUiTheme.CardSelected, 17);
            CreateRoomObject(parent, "门\n进入卧室", new Vector2(0.78f, 0.94f), new Vector2(0.98f, 1.00f), PrototypeUiTheme.Card, 16);
        }

        private static void BuildBedroomRoomObjects(Transform parent)
        {
            CreateRoomObject(parent, "床\n用于结算当天", new Vector2(0.02f, 0.42f), new Vector2(0.20f, 0.92f), PrototypeUiTheme.Card, 21);
            CreateRoomObject(parent, "桌子", new Vector2(0.44f, 0.78f), new Vector2(0.62f, 0.94f), PrototypeUiTheme.PaperMuted, 21);
            CreateRoomObject(parent, "椅子", new Vector2(0.48f, 0.60f), new Vector2(0.58f, 0.70f), PrototypeUiTheme.PaperMuted, 20);
            CreateRoomObject(parent, "盆栽", new Vector2(0.74f, 0.78f), new Vector2(0.86f, 0.94f), PrototypeUiTheme.Card, 20);
            CreateRoomObject(parent, "地毯", new Vector2(0.40f, 0.18f), new Vector2(0.78f, 0.46f), PrototypeUiTheme.PaperMuted, 22);
            CreateRoomObject(parent, "门\n回到店铺", new Vector2(0.50f, 0.00f), new Vector2(0.66f, 0.12f), PrototypeUiTheme.Card, 16);
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
                GamePhase.OrderSelection => currentRoom == RoomMode.Shop ? "白天营业：公告栏会打开独立订单场景；门通向卧室。" : "卧室：靠近床按 E 结束当天并结算。",
                GamePhase.NightSummary => currentRoom == RoomMode.Bedroom ? "夜晚结算：可以补货，然后在床边按 E 进入下一天。" : "夜晚结算：回到卧室床边进入下一天。",
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
            headerLayout.padding = new RectOffset(18, 18, 12, 12);
            headerLayout.spacing = 4;
            headerLayout.childControlWidth = true;
            titleText = CreateText(header, "Title", 30, FontStyle.Bold, PrototypeUiTheme.Ink);
            resourceText = CreateText(header, "Resources", 19, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            shopStatusText = CreateText(header, "ShopStatus", 17, FontStyle.Normal, PrototypeUiTheme.InkMuted);

            var shopPanel = CreateCard(root, "Room", PrototypeUiTheme.Paper);
            var shopElement = shopPanel.gameObject.AddComponent<LayoutElement>();
            shopElement.minHeight = 720;
            shopElement.flexibleHeight = 1;
            BuildRoomFloor(shopPanel);

            feedbackText = CreateText(root, "Feedback", 18, FontStyle.Normal, PrototypeUiTheme.Paper);
            var feedbackElement = feedbackText.gameObject.AddComponent<LayoutElement>();
            feedbackElement.minHeight = 36;
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
