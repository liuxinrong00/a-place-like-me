using System.Collections.Generic;
using System.Linq;
using APlaceLikeMe.Core;
using APlaceLikeMe.Data;
using APlaceLikeMe.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private PrototypeInteractionPanelMode panelMode;
        private GameObject canvasRoot;
        private Text orderDetailText;
        private Text repairMethodText;
        private Text nightSummaryText;
        private Text purchaseCountText;
        private Text feedbackText;
        private Transform orderListRoot;
        private Transform repairMethodRoot;
        private Transform supplyRoot;
        private Button repairButton;
        private Button buySupplyButton;
        private Button decreaseSupplyCountButton;
        private Button increaseSupplyCountButton;
        private Button closeButton;
        private string feedbackMessage;
        private int selectedSupplyPurchaseCount = 1;
        private bool isClosing;

        private void Start()
        {
            host = PrototypeGameController.Active;
            panelMode = host == null ? PrototypeInteractionPanelMode.Orders : host.PendingPanelMode;
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
            feedbackMessage = IsMaterialPurchaseMode()
                ? "材料购买：选择材料和数量后点击购买。"
                : "接单台：选择一个订单，再挑一种修补方式。";
            Render();
        }

        private void OnDestroy()
        {
            if (canvasRoot != null)
            {
                Destroy(canvasRoot);
                canvasRoot = null;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
            {
                ClosePanel();
            }
        }

        private void TryRepairSelectedOrder()
        {
            var result = host.TryCompleteOrderFromPanel(selectedOrder, selectedRepairMethod);
            feedbackMessage = result.Message;
            if (result.Succeeded)
            {
                selectedOrder = host.State.TodaysOrders.FirstOrDefault();
            }

            Render();
        }

        private void TryBuySupply()
        {
            var result = host.TryBuySupplyFromPanel(selectedSupplyMaterial, selectedSupplyPurchaseCount);
            feedbackMessage = result.Message;
            Render();
        }

        private void DecreaseSupplyPurchaseCount()
        {
            selectedSupplyPurchaseCount = Mathf.Max(1, selectedSupplyPurchaseCount - 1);
            Render();
        }

        private void IncreaseSupplyPurchaseCount()
        {
            selectedSupplyPurchaseCount = Mathf.Min(10, selectedSupplyPurchaseCount + 1);
            Render();
        }

        private void ClosePanel()
        {
            if (isClosing)
            {
                return;
            }

            isClosing = true;
            if (canvasRoot != null)
            {
                canvasRoot.SetActive(false);
                Destroy(canvasRoot);
                canvasRoot = null;
            }

            if (host != null)
            {
                host.CloseInteractionSceneFromPanel();
                return;
            }

            Destroy(gameObject);
        }

        private void Render()
        {
            if (IsMaterialPurchaseMode())
            {
                RenderMaterialPurchase();
                return;
            }

            RenderOrderBoard();
        }

        private void RenderOrderBoard()
        {
            RenderOrders();
            RenderRepairMethods();
            orderDetailText.text = selectedOrder == null ? "没有可处理订单。" : FormatOrderDetail(selectedOrder, host.State);
            repairMethodText.text = selectedOrder == null || selectedRepairMethod == null
                ? "请选择订单和修补方式。"
                : FormatRepairPreview(selectedOrder, selectedRepairMethod);

            feedbackText.text = feedbackMessage;
            repairButton.interactable = selectedOrder != null && selectedRepairMethod != null;
            repairButton.GetComponentInChildren<Text>().text = selectedRepairMethod == null ? "开始修补" : $"开始修补：{selectedRepairMethod.DisplayName}";
        }

        private void RenderMaterialPurchase()
        {
            RenderSupplies();
            nightSummaryText.text = host.BuildNightSummaryText();
            feedbackText.text = feedbackMessage;
            buySupplyButton.interactable = selectedSupplyMaterial != null;
            decreaseSupplyCountButton.interactable = selectedSupplyPurchaseCount > 1;
            increaseSupplyCountButton.interactable = selectedSupplyPurchaseCount < 10;
            purchaseCountText.text = $"{selectedSupplyPurchaseCount}/10";
            var purchaseAmount = selectedSupplyPurchaseCount * Mathf.Max(1, host.Config.NightSupplyAmount);
            var purchaseCost = selectedSupplyPurchaseCount * Mathf.Max(1, host.Config.NightSupplyCost);
            buySupplyButton.GetComponentInChildren<Text>().text = selectedSupplyMaterial == null
                ? "购买"
                : $"购买：{selectedSupplyMaterial.DisplayName} +{purchaseAmount} / {purchaseCost} 金币";
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
                }, false, 18, 92, 0, false);
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
                }, false, 17, 92, 0, false);
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
                button.interactable = host.State.Phase != GamePhase.DayEnd;
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

        private static string FormatOrderDetail(OrderDefinition order, GameSessionState state)
        {
            var materialText = order.RequiredMaterials.Count == 0
                ? "无"
                : string.Join(" / ", order.RequiredMaterials.Select(material =>
                {
                    var label = $"{material.material.DisplayName} x{material.amount}";
                    if (material.material != null && !state.HasMaterial(material.material, material.amount))
                    {
                        return $"<color=#803020>{label}</color>";
                    }

                    return label;
                }));
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
            canvasRoot = canvas.gameObject;
            SceneManager.MoveGameObjectToScene(canvasRoot, gameObject.scene);

            var backdrop = CreateCard(canvas.transform, "OrderBoardBackdrop", new Color32(252, 251, 247, 220));
            var root = CreateCard(backdrop, "OrderBoardRoot", PrototypeUiTheme.Paper);
            root.anchorMin = new Vector2(0.05f, 0.06f);
            root.anchorMax = new Vector2(0.95f, 0.94f);

            var vertical = root.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.padding = new RectOffset(24, 24, 20, 20);
            vertical.spacing = PrototypeUiTheme.SpaceMedium;
            vertical.childControlHeight = true;
            vertical.childControlWidth = true;
            vertical.childForceExpandHeight = false;

            var title = CreateText(root, "Title", 28, FontStyle.Bold, PrototypeUiTheme.Ink);
            title.text = IsMaterialPurchaseMode() ? "材料购买" : "接单台订单";
            title.alignment = TextAnchor.MiddleCenter;
            AddLayout(title.gameObject, 48, 48);

            if (IsMaterialPurchaseMode())
            {
                BuildMaterialPurchaseUi(root);
            }
            else
            {
                BuildOrderBoardUi(root);
            }
        }

        private void BuildOrderBoardUi(Transform root)
        {
            var content = CreatePanel(root, "Content", new Vector2(0, 0), new Vector2(1, 0));
            AddLayout(content.gameObject, 0, 0, 1);
            var contentLayout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = PrototypeUiTheme.SpaceMedium;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;

            var ordersSection = CreateSection(content, "OrdersSection", "订单列表", 0, 244);
            orderListRoot = CreateScrollStack(ordersSection, "OrdersScroll", "Orders", PrototypeUiTheme.SpaceSmall);

            var detailSection = CreateSection(content, "DetailSection", "订单详情", 1);
            orderDetailText = CreateText(detailSection, "OrderDetail", 17, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            AddLayout(orderDetailText.gameObject, 170, 210);

            var previewTitle = CreateText(detailSection, "PreviewTitle", 22, FontStyle.Bold, PrototypeUiTheme.Ink);
            previewTitle.text = "修补预览";
            AddLayout(previewTitle.gameObject, 32, 32);

            repairMethodText = CreateText(detailSection, "RepairPreview", 17, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            AddLayout(repairMethodText.gameObject, 0, 0, 1);

            var toolsSection = CreateSection(content, "ToolsSection", "修补方式", 0, 244);
            repairMethodRoot = CreateScrollStack(toolsSection, "RepairMethodsScroll", "RepairMethods", PrototypeUiTheme.SpaceSmall);

            feedbackText = CreateText(root, "Feedback", 18, FontStyle.Bold, PrototypeUiTheme.Ink);
            feedbackText.alignment = TextAnchor.MiddleLeft;
            AddLayout(feedbackText.gameObject, 76, 76);

            var actions = CreateActions(root);
            repairButton = CreateButton(actions, "开始修补", TryRepairSelectedOrder, true);
            closeButton = CreateButton(actions, "返回场景", ClosePanel);
        }

        private void BuildMaterialPurchaseUi(Transform root)
        {
            var content = CreatePanel(root, "NightContent", new Vector2(0, 0), new Vector2(1, 0));
            AddLayout(content.gameObject, 0, 0, 1);
            var contentLayout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = PrototypeUiTheme.SpaceMedium;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;

            var summarySection = CreateSection(content, "SummarySection", "今日状态", 0.58f);
            nightSummaryText = CreateText(summarySection, "NightSummary", 19, FontStyle.Normal, PrototypeUiTheme.InkMuted);
            AddLayout(nightSummaryText.gameObject, 0, 0, 1);

            var supplySection = CreateSection(content, "SupplySection", "材料", 0.42f);
            supplyRoot = CreateScrollStack(supplySection, "SupplyScroll", "Supplies", PrototypeUiTheme.SpaceSmall);

            var countRow = CreatePanel(root, "SupplyCount", new Vector2(0, 0), new Vector2(1, 0));
            AddLayout(countRow.gameObject, 62, 62);
            var countLayout = countRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            countLayout.spacing = PrototypeUiTheme.SpaceMedium;
            countLayout.childControlWidth = true;
            countLayout.childControlHeight = true;
            countLayout.childForceExpandWidth = true;

            decreaseSupplyCountButton = CreateButton(countRow, "←", DecreaseSupplyPurchaseCount);
            var purchaseCountFrame = CreateCard(countRow, "PurchaseCountFrame", PrototypeUiTheme.Card);
            AddLayout(purchaseCountFrame.gameObject, 56, 56);
            purchaseCountText = CreateText(purchaseCountFrame, "PurchaseCount", 22, FontStyle.Bold, PrototypeUiTheme.Ink);
            purchaseCountText.alignment = TextAnchor.MiddleCenter;
            var purchaseCountRect = purchaseCountText.GetComponent<RectTransform>();
            purchaseCountRect.anchorMin = Vector2.zero;
            purchaseCountRect.anchorMax = Vector2.one;
            purchaseCountRect.offsetMin = Vector2.zero;
            purchaseCountRect.offsetMax = Vector2.zero;
            increaseSupplyCountButton = CreateButton(countRow, "→", IncreaseSupplyPurchaseCount);

            feedbackText = CreateText(root, "Feedback", 18, FontStyle.Bold, PrototypeUiTheme.Ink);
            feedbackText.alignment = TextAnchor.MiddleLeft;
            AddLayout(feedbackText.gameObject, 76, 76);

            var actions = CreateActions(root);
            buySupplyButton = CreateButton(actions, "购买", TryBuySupply, true);
            closeButton = CreateButton(actions, "返回场景", ClosePanel);
        }

        private bool IsMaterialPurchaseMode()
        {
            return panelMode == PrototypeInteractionPanelMode.MaterialPurchase || panelMode == PrototypeInteractionPanelMode.NightSummary;
        }

        private static RectTransform CreateActions(Transform root)
        {
            var actions = CreatePanel(root, "Actions", new Vector2(0, 0), new Vector2(1, 0));
            AddLayout(actions.gameObject, 76, 76);
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = PrototypeUiTheme.SpaceMedium;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = true;
            return actions;
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
            AddOutline(card.gameObject, 2);
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

        private static Transform CreateScrollStack(Transform parent, string scrollName, string contentName, int spacing)
        {
            var scroll = CreateCard(parent, scrollName, PrototypeUiTheme.Paper);
            AddLayout(scroll.gameObject, 0, 0, 1);

            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 34f;

            var viewport = CreatePanel(scroll, "Viewport", new Vector2(0, 0), new Vector2(1, 1));
            viewport.offsetMin = new Vector2(8, 8);
            viewport.offsetMax = new Vector2(-26, -8);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color32(255, 255, 252, 1);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = CreatePanel(viewport, contentName, new Vector2(0, 1), new Vector2(1, 1));
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbar = CreateScrollbar(scroll, "Scrollbar");
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return content;
        }

        private static Scrollbar CreateScrollbar(Transform parent, string name)
        {
            var scrollbarObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(parent, false);
            var rect = scrollbarObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(10, 0);
            rect.offsetMin = new Vector2(-14, 8);
            rect.offsetMax = new Vector2(-4, -8);
            scrollbarObject.GetComponent<Image>().color = PrototypeUiTheme.PaperMuted;

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(scrollbarObject.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handle.GetComponent<Image>().color = PrototypeUiTheme.Line;

            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            return scrollbar;
        }

        private static RectTransform CreateSection(Transform parent, string name, string title, float flexibleWidth, float fixedWidth = 0)
        {
            var section = CreateCard(parent, name, PrototypeUiTheme.Card);
            var sectionLayout = AddLayout(section.gameObject, 0, 0, 1, flexibleWidth);
            if (fixedWidth > 0)
            {
                sectionLayout.minWidth = fixedWidth;
                sectionLayout.preferredWidth = fixedWidth;
                sectionLayout.flexibleWidth = 0;
            }

            var layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = PrototypeUiTheme.SpaceSmall;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var titleText = CreateText(section, $"{name}Title", 22, FontStyle.Bold, PrototypeUiTheme.Ink);
            titleText.text = title;
            titleText.alignment = TextAnchor.MiddleCenter;
            AddLayout(titleText.gameObject, 36, 36);
            return section;
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
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static LayoutElement AddLayout(GameObject target, float minHeight = 0, float preferredHeight = 0, float flexibleHeight = 0, float flexibleWidth = 0)
        {
            var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            layout.minHeight = minHeight;
            layout.preferredHeight = preferredHeight;
            layout.flexibleHeight = flexibleHeight;
            layout.flexibleWidth = flexibleWidth;
            return layout;
        }

        private static Button CreateButton(
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction onClick,
            bool primary = false,
            int fontSize = 19,
            float preferredHeight = 0,
            float preferredWidth = 0,
            bool allowBestFit = true)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = primary ? PrototypeUiTheme.Primary : PrototypeUiTheme.Card;
            AddOutline(buttonObject, 2);
            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            var colors = button.colors;
            colors.normalColor = primary ? PrototypeUiTheme.Primary : PrototypeUiTheme.Card;
            colors.highlightedColor = primary ? PrototypeUiTheme.PrimaryHover : PrototypeUiTheme.CardSelected;
            colors.pressedColor = PrototypeUiTheme.PaperMuted;
            colors.disabledColor = new Color32(132, 116, 91, 160);
            button.colors = colors;

            var labelText = CreateText(buttonObject.transform, "Label", fontSize, FontStyle.Bold, PrototypeUiTheme.Ink);
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.lineSpacing = 0.92f;
            labelText.resizeTextForBestFit = allowBestFit;
            labelText.resizeTextMinSize = 13;
            labelText.resizeTextMaxSize = fontSize;
            var labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 6);
            labelRect.offsetMax = new Vector2(-10, -6);

            var buttonHeight = preferredHeight > 0 ? preferredHeight : label.Contains("\n") ? 92 : 56;
            var layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = buttonHeight;
            layout.preferredHeight = buttonHeight;
            layout.flexibleWidth = 1;
            if (preferredWidth > 0)
            {
                layout.minWidth = preferredWidth;
                layout.preferredWidth = preferredWidth;
                layout.flexibleWidth = 0;
            }

            if (!allowBestFit)
            {
                layout.minWidth = 0;
                layout.preferredWidth = 0;
            }

            return button;
        }

        private static void AddOutline(GameObject target, int distance)
        {
            var outline = target.AddComponent<Outline>();
            outline.effectColor = PrototypeUiTheme.Line;
            outline.effectDistance = new Vector2(distance, -distance);
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

            if (decreaseSupplyCountButton != null)
            {
                decreaseSupplyCountButton.interactable = interactable;
            }

            if (increaseSupplyCountButton != null)
            {
                increaseSupplyCountButton.interactable = interactable;
            }

            if (closeButton != null)
            {
                closeButton.interactable = interactable;
            }
        }
    }
}
