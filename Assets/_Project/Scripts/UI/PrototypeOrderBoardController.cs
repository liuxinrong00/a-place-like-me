using System.Collections.Generic;
using System.Linq;
using APlaceLikeMe.Core;
using APlaceLikeMe.Data;
using APlaceLikeMe.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace APlaceLikeMe.UI
{
    public sealed class PrototypeOrderBoardController : MonoBehaviour
    {
        private static Font cachedDefaultFont;
        private readonly List<Button> orderButtons = new();
        private readonly List<Button> repairMethodButtons = new();
        private readonly List<Button> supplyButtons = new();

        private PrototypeGameController host;
        private OrderDefinition selectedOrder;
        private RepairMethodDefinition selectedRepairMethod;
        private MaterialDefinition selectedSupplyMaterial;
        private Text orderDetailText;
        private Text repairMethodText;
        private Text feedbackText;
        private Transform orderListRoot;
        private Transform repairMethodRoot;
        private Transform supplyRoot;
        private Button repairButton;
        private Button buySupplyButton;
        private Button nextDayButton;
        private Button closeButton;

        private void Start()
        {
            host = PrototypeGameController.Active;
            BuildUi();

            if (host == null || host.Config == null)
            {
                feedbackText.text = "找不到主原型控制器。请从 S_Bootstrap 场景进入。";
                SetButtonsInteractable(false);
                return;
            }

            selectedOrder = host.State.TodaysOrders.FirstOrDefault();
            selectedRepairMethod = host.Config.RepairMethods.FirstOrDefault();
            selectedSupplyMaterial = host.State.MaterialStock.Keys.FirstOrDefault();
            feedbackText.text = host.PendingPanelMode == PrototypeInteractionPanelMode.NightSummary
                ? host.BuildNightSummaryText()
                : "公告栏：选择一个订单，再挑一种修补方式。";
            Render();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ClosePanel();
            }
        }

        private void TryRepairSelectedOrder()
        {
            var result = host.TryCompleteOrderFromPanel(selectedOrder, selectedRepairMethod);
            feedbackText.text = result.Message;
            if (result.Succeeded)
            {
                selectedOrder = host.State.TodaysOrders.FirstOrDefault();
            }

            Render();
        }

        private void TryBuySupply()
        {
            var result = host.TryBuySupplyFromPanel(selectedSupplyMaterial);
            feedbackText.text = result.Message;
            Render();
        }

        private void GoNextDay()
        {
            host.GoNextDayFromPanel();
        }

        private void ClosePanel()
        {
            host.CloseInteractionSceneFromPanel();
        }

        private void Render()
        {
            RenderOrders();
            RenderRepairMethods();
            RenderSupplies();
            orderDetailText.text = selectedOrder == null ? "没有可处理订单。" : FormatOrderDetail(selectedOrder);
            repairMethodText.text = selectedOrder == null || selectedRepairMethod == null
                ? "请选择订单和修补方式。"
                : FormatRepairPreview(selectedOrder, selectedRepairMethod);

            var inNightSummary = host.State.Phase == GamePhase.NightSummary;
            repairButton.interactable = !inNightSummary && selectedOrder != null && selectedRepairMethod != null;
            buySupplyButton.interactable = inNightSummary && selectedSupplyMaterial != null;
            nextDayButton.interactable = inNightSummary;
            repairButton.GetComponentInChildren<Text>().text = selectedRepairMethod == null ? "开始修补" : $"开始修补：{selectedRepairMethod.DisplayName}";
            buySupplyButton.GetComponentInChildren<Text>().text = selectedSupplyMaterial == null ? "补货" : $"补货：{selectedSupplyMaterial.DisplayName} +{host.Config.NightSupplyAmount} / -{host.Config.NightSupplyCost}";
        }

        private void RenderOrders()
        {
            foreach (var button in orderButtons)
            {
                Destroy(button.gameObject);
            }

            orderButtons.Clear();
            foreach (var order in host.State.TodaysOrders)
            {
                var button = CreateButton(orderListRoot, FormatOrderButton(order), () =>
                {
                    selectedOrder = order;
                    Render();
                });
                button.interactable = host.State.Phase == GamePhase.OrderSelection;
                SetButtonColor(button, GetOrderButtonColor(order));
                orderButtons.Add(button);
            }
        }

        private void RenderRepairMethods()
        {
            foreach (var button in repairMethodButtons)
            {
                Destroy(button.gameObject);
            }

            repairMethodButtons.Clear();
            foreach (var repairMethod in host.Config.RepairMethods)
            {
                var button = CreateButton(repairMethodRoot, FormatRepairMethodButton(repairMethod), () =>
                {
                    selectedRepairMethod = repairMethod;
                    Render();
                });
                button.interactable = host.State.Phase == GamePhase.OrderSelection;
                SetButtonColor(button, repairMethod == selectedRepairMethod ? PrototypeUiTheme.CardSelected : PrototypeUiTheme.PaperMuted);
                repairMethodButtons.Add(button);
            }
        }

        private void RenderSupplies()
        {
            foreach (var button in supplyButtons)
            {
                Destroy(button.gameObject);
            }

            supplyButtons.Clear();
            foreach (var material in host.State.MaterialStock.Keys)
            {
                var button = CreateButton(supplyRoot, material.DisplayName, () =>
                {
                    selectedSupplyMaterial = material;
                    Render();
                });
                button.interactable = host.State.Phase == GamePhase.NightSummary;
                SetButtonColor(button, material == selectedSupplyMaterial ? PrototypeUiTheme.CardSelected : PrototypeUiTheme.Card);
                supplyButtons.Add(button);
            }
        }

        private Color GetOrderButtonColor(OrderDefinition order)
        {
            if (order == selectedOrder)
            {
                return PrototypeUiTheme.CardSelected;
            }

            return CanRepairOrder(order) ? PrototypeUiTheme.PaperMuted : PrototypeUiTheme.CardUnavailable;
        }

        private bool CanRepairOrder(OrderDefinition order)
        {
            if (order == null || selectedRepairMethod == null)
            {
                return false;
            }

            if (host.State.Energy < host.OrderService.GetFinalEnergyCost(order, selectedRepairMethod))
            {
                return false;
            }

            foreach (var requiredMaterial in order.RequiredMaterials)
            {
                if (requiredMaterial.material != null && !host.State.HasMaterial(requiredMaterial.material, requiredMaterial.amount))
                {
                    return false;
                }
            }

            return true;
        }

        private string FormatOrderButton(OrderDefinition order)
        {
            var customerName = order.Customer == null ? "未知顾客" : order.Customer.DisplayName;
            return $"{order.DisplayName}\n{customerName} · {order.RewardCoins} 金币 · {GetOrderStatusText(order)}";
        }

        private string GetOrderStatusText(OrderDefinition order)
        {
            if (selectedRepairMethod == null)
            {
                return "未选方式";
            }

            if (host.State.Energy < host.OrderService.GetFinalEnergyCost(order, selectedRepairMethod))
            {
                return "能量不足";
            }

            foreach (var requiredMaterial in order.RequiredMaterials)
            {
                if (requiredMaterial.material != null && !host.State.HasMaterial(requiredMaterial.material, requiredMaterial.amount))
                {
                    return $"缺{requiredMaterial.material.DisplayName}";
                }
            }

            return "可修补";
        }

        private static string FormatOrderDetail(OrderDefinition order)
        {
            var materialText = order.RequiredMaterials.Count == 0
                ? "无"
                : string.Join(" / ", order.RequiredMaterials.Select(material => $"{material.material.DisplayName} x{material.amount}"));
            var customerName = order.Customer == null ? "未知顾客" : order.Customer.DisplayName;
            var customerType = order.Customer == null ? "未知" : FormatCustomerType(order.Customer.CustomerType);
            return $"订单：{order.DisplayName}\n物品：{order.ItemType}\n损坏：{order.DamageLevel}\n顾客：{customerName} / {customerType}\n需要：{materialText}\n基础能量：{order.EnergyCost}\n基础报酬：{order.RewardCoins}\n备注：{order.CustomerNote}";
        }

        private string FormatRepairPreview(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            var energyCost = host.OrderService.GetFinalEnergyCost(order, repairMethod);
            var rewardCoins = host.OrderService.GetFinalRewardCoins(order, repairMethod);
            var authenticityDelta = host.OrderService.GetFinalAuthenticityDelta(order, repairMethod);
            return $"修补方式：{repairMethod.DisplayName}\n{repairMethod.Description}\n\n预计消耗能量：{energyCost}\n预计收入：{rewardCoins}\n预计真实度：{FormatSigned(authenticityDelta)}";
        }

        private static string FormatRepairMethodButton(RepairMethodDefinition repairMethod)
        {
            return $"{repairMethod.DisplayName}\n能量 {FormatSigned(repairMethod.EnergyModifier)} / 金币 {FormatSigned(repairMethod.CoinRewardModifier)} / 真实 {FormatSigned(repairMethod.AuthenticityModifier)}";
        }

        private static string FormatCustomerType(CustomerType customerType)
        {
            return customerType switch
            {
                CustomerType.Gentle => "温柔型",
                CustomerType.Demanding => "挑剔型",
                CustomerType.Makeover => "改造型",
                _ => "未知"
            };
        }

        private void BuildUi()
        {
            var canvas = CreateCanvas();
            var root = CreateCard(canvas.transform, "OrderBoardRoot", PrototypeUiTheme.Paper);
            var vertical = root.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.padding = new RectOffset(22, 22, 18, 18);
            vertical.spacing = PrototypeUiTheme.SpaceSmall;
            vertical.childControlHeight = true;
            vertical.childControlWidth = true;

            CreateText(root, "Title", 24, FontStyle.Bold, PrototypeUiTheme.Ink).text = "公告栏订单";
            orderListRoot = CreateStack(root, "Orders", PrototypeUiTheme.SpaceSmall);

            CreateText(root, "DetailTitle", 22, FontStyle.Bold, PrototypeUiTheme.Ink).text = "订单详情";
            orderDetailText = CreateText(root, "OrderDetail", 17, FontStyle.Normal, PrototypeUiTheme.InkMuted);

            CreateText(root, "RepairTitle", 22, FontStyle.Bold, PrototypeUiTheme.Ink).text = "修补方式";
            repairMethodRoot = CreateStack(root, "RepairMethods", PrototypeUiTheme.SpaceSmall);
            repairMethodText = CreateText(root, "RepairPreview", 17, FontStyle.Normal, PrototypeUiTheme.InkMuted);

            CreateText(root, "SupplyTitle", 22, FontStyle.Bold, PrototypeUiTheme.Ink).text = "夜晚补货";
            supplyRoot = CreateStack(root, "Supplies", PrototypeUiTheme.SpaceSmall);
            feedbackText = CreateText(root, "Feedback", 18, FontStyle.Bold, PrototypeUiTheme.Ink);

            var actions = CreatePanel(root, "Actions", new Vector2(0, 0), new Vector2(1, 0));
            var actionsElement = actions.gameObject.AddComponent<LayoutElement>();
            actionsElement.minHeight = 72;
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = PrototypeUiTheme.SpaceMedium;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;

            repairButton = CreateButton(actions, "开始修补", TryRepairSelectedOrder, true);
            buySupplyButton = CreateButton(actions, "补货", TryBuySupply);
            nextDayButton = CreateButton(actions, "进入下一天", GoNextDay, true);
            closeButton = CreateButton(actions, "返回场景", ClosePanel);
        }

        private static RectTransform CreateCanvas()
        {
            var canvasObject = new GameObject("OrderBoardCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var rectTransform = canvasObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 1080);
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

        private static RectTransform CreateStack(Transform parent, string name, int spacing)
        {
            var stack = CreatePanel(parent, name, new Vector2(0, 0), new Vector2(1, 0));
            var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return stack;
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

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, bool primary = false)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = primary ? PrototypeUiTheme.Primary : PrototypeUiTheme.Card;
            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            var colors = button.colors;
            colors.normalColor = primary ? PrototypeUiTheme.Primary : PrototypeUiTheme.Card;
            colors.highlightedColor = primary ? PrototypeUiTheme.PrimaryHover : PrototypeUiTheme.CardSelected;
            colors.pressedColor = PrototypeUiTheme.PaperMuted;
            colors.disabledColor = new Color32(132, 116, 91, 160);
            button.colors = colors;

            var labelText = CreateText(buttonObject.transform, "Label", 19, FontStyle.Bold, PrototypeUiTheme.Ink);
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleCenter;
            var labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8, 4);
            labelRect.offsetMax = new Vector2(-8, -4);

            var layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = label.Contains("\n") ? 74 : 56;
            return button;
        }

        private static void SetButtonColor(Button button, Color color)
        {
            var image = button.GetComponent<Image>();
            image.color = color;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = PrototypeUiTheme.CardSelected;
            button.colors = colors;
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

        private void SetButtonsInteractable(bool interactable)
        {
            if (repairButton != null)
            {
                repairButton.interactable = interactable;
            }

            if (buySupplyButton != null)
            {
                buySupplyButton.interactable = interactable;
            }

            if (nextDayButton != null)
            {
                nextDayButton.interactable = interactable;
            }

            if (closeButton != null)
            {
                closeButton.interactable = interactable;
            }
        }
    }
}
