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
    public sealed class PrototypeStaticInteractionSceneController : MonoBehaviour
    {
        private const string OrderSceneName = "Order";
        private const string FixSceneName = "Fix";
        private const string BuySceneName = "Buy";
        private const string OrderListName = "OrderList";
        private const string OrderInformationName = "OrderInformation";
        private const string AcceptButtonName = "AcceptButton";
        private const string ReturnButtonName = "ReturnButton";
        private const string LastButtonName = "LastButton";
        private const string NextButtonName = "NextButton";
        private const string ScrollbarName = "Scrollbar";
        private const int VisibleRows = 3;
        private const int MaxPurchaseCount = 10;

        private static Font cachedDefaultFont;

        private readonly List<Button> listButtons = new();
        private readonly List<Button> repairMethodButtons = new();

        private PrototypeGameController host;
        private string sceneName;
        private bool initialized;
        private bool settingScrollbarValue;
        private RectTransform listContentRoot;
        private RectTransform infoContentRoot;
        private RectTransform repairMethodRoot;
        private Text detailText;
        private Text statusText;
        private Text purchaseCountText;
        private Button acceptButton;
        private Button returnButton;
        private Button lastButton;
        private Button nextButton;
        private Scrollbar materialScrollbar;
        private OrderDefinition selectedOrder;
        private RepairMethodDefinition selectedRepairMethod;
        private MaterialDefinition selectedMaterial;
        private int orderPageIndex;
        private int materialStartIndex;
        private int purchaseCount = 1;
        private string statusMessage;

        public void Configure(string loadedSceneName, PrototypeGameController prototypeHost)
        {
            sceneName = loadedSceneName;
            host = prototypeHost;
            InitializeOnce();
        }

        private void Start()
        {
            InitializeOnce();
        }

        private void InitializeOnce()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            host ??= PrototypeGameController.Active;
            sceneName = string.IsNullOrWhiteSpace(sceneName) ? gameObject.scene.name : sceneName;

            BindSharedButtons();
            if (host == null || host.Config == null)
            {
                BuildOrderInfoLayout();
                detailText.text = "找不到主原型控制器。请从 S_Bootstrap 场景进入。";
                SetButtonsInteractable(false);
                return;
            }

            if (sceneName == BuySceneName)
            {
                InitializeBuyScene();
                return;
            }

            if (sceneName == FixSceneName)
            {
                InitializeFixScene();
                return;
            }

            InitializeOrderScene();
        }

        private void BindSharedButtons()
        {
            acceptButton = FindButton(AcceptButtonName);
            returnButton = FindButton(ReturnButtonName);
            lastButton = FindButton(LastButtonName);
            nextButton = FindButton(NextButtonName);

            if (returnButton != null)
            {
                returnButton.onClick.AddListener(CloseScene);
            }
        }

        private void InitializeOrderScene()
        {
            BuildListLayout();
            BuildOrderInfoLayout();
            selectedOrder = host.State.TodaysOrders.FirstOrDefault();
            statusMessage = "选择订单后点击接受订单。";

            if (acceptButton != null)
            {
                acceptButton.onClick.AddListener(AcceptSelectedOrder);
            }

            BindPageButtons();
            RenderOrderScene();
        }

        private void InitializeFixScene()
        {
            BuildListLayout();
            BuildFixInfoLayout();
            selectedOrder = host.State.AcceptedOrders.FirstOrDefault();
            selectedRepairMethod = host.Config.RepairMethods.FirstOrDefault();
            statusMessage = "选择已接订单和修补方式后开始修补。";

            if (acceptButton != null)
            {
                acceptButton.onClick.AddListener(RepairSelectedOrder);
            }

            BindPageButtons();
            RenderFixScene();
        }

        private void InitializeBuyScene()
        {
            BuildMaterialListLayout();
            BuildBuyInfoLayout();
            selectedMaterial = GetAvailableMaterials().FirstOrDefault();
            statusMessage = "选择材料和数量后点击购买。";
            purchaseCountText = FindTextByContent("10/10") ?? CreateCountText();
            materialScrollbar = FindSceneObject(ScrollbarName)?.GetComponent<Scrollbar>();

            if (acceptButton != null)
            {
                acceptButton.onClick.AddListener(BuySelectedMaterial);
            }

            if (lastButton != null)
            {
                lastButton.onClick.AddListener(DecreasePurchaseCount);
            }

            if (nextButton != null)
            {
                nextButton.onClick.AddListener(IncreasePurchaseCount);
            }

            if (materialScrollbar != null)
            {
                materialScrollbar.onValueChanged.AddListener(OnMaterialScrollbarChanged);
            }

            RenderBuyScene();
        }

        private void BindPageButtons()
        {
            if (lastButton != null)
            {
                lastButton.onClick.AddListener(PreviousOrderPage);
            }

            if (nextButton != null)
            {
                nextButton.onClick.AddListener(NextOrderPage);
            }
        }

        private void AcceptSelectedOrder()
        {
            var result = host.TryAcceptOrderFromPanel(selectedOrder);
            statusMessage = result.Message;
            var orders = host.State.TodaysOrders;
            orderPageIndex = Mathf.Clamp(orderPageIndex, 0, GetMaxPage(orders.Count));
            selectedOrder = orders.Skip(orderPageIndex * VisibleRows).FirstOrDefault() ?? orders.FirstOrDefault();
            RenderOrderScene();
        }

        private void RepairSelectedOrder()
        {
            var result = host.TryCompleteOrderFromPanel(selectedOrder, selectedRepairMethod);
            statusMessage = result.Message;
            var orders = host.State.AcceptedOrders;
            orderPageIndex = Mathf.Clamp(orderPageIndex, 0, GetMaxPage(orders.Count));
            selectedOrder = orders.Skip(orderPageIndex * VisibleRows).FirstOrDefault() ?? orders.FirstOrDefault();
            RenderFixScene();
        }

        private void BuySelectedMaterial()
        {
            var result = host.TryBuySupplyFromPanel(selectedMaterial, purchaseCount);
            statusMessage = result.Message;
            RenderBuyScene();
        }

        private void PreviousOrderPage()
        {
            orderPageIndex = Mathf.Max(0, orderPageIndex - 1);
            selectedOrder = GetCurrentOrders().Skip(orderPageIndex * VisibleRows).FirstOrDefault() ?? GetCurrentOrders().FirstOrDefault();
            RenderCurrentOrderMode();
        }

        private void NextOrderPage()
        {
            var orders = GetCurrentOrders();
            orderPageIndex = Mathf.Min(GetMaxPage(orders.Count), orderPageIndex + 1);
            selectedOrder = orders.Skip(orderPageIndex * VisibleRows).FirstOrDefault() ?? orders.FirstOrDefault();
            RenderCurrentOrderMode();
        }

        private void DecreasePurchaseCount()
        {
            purchaseCount = Mathf.Max(1, purchaseCount - 1);
            RenderBuyScene();
        }

        private void IncreasePurchaseCount()
        {
            purchaseCount = Mathf.Min(MaxPurchaseCount, purchaseCount + 1);
            RenderBuyScene();
        }

        private void OnMaterialScrollbarChanged(float value)
        {
            if (settingScrollbarValue)
            {
                return;
            }

            var materials = GetAvailableMaterials();
            var maxStart = Mathf.Max(0, materials.Count - VisibleRows);
            materialStartIndex = Mathf.Clamp(Mathf.RoundToInt((1f - value) * maxStart), 0, maxStart);
            selectedMaterial = materials.Skip(materialStartIndex).FirstOrDefault() ?? materials.FirstOrDefault();
            RenderBuyScene();
        }

        private void RenderCurrentOrderMode()
        {
            if (sceneName == FixSceneName)
            {
                RenderFixScene();
                return;
            }

            RenderOrderScene();
        }

        private void RenderOrderScene()
        {
            var orders = host.State.TodaysOrders;
            orderPageIndex = Mathf.Clamp(orderPageIndex, 0, GetMaxPage(orders.Count));
            if (selectedOrder != null && !orders.Contains(selectedOrder))
            {
                selectedOrder = orders.Skip(orderPageIndex * VisibleRows).FirstOrDefault() ?? orders.FirstOrDefault();
            }

            RenderOrderList(orders);
            detailText.text = selectedOrder == null
                ? "今天没有可接订单。"
                : FormatOrderDetail(selectedOrder);
            statusText.text = statusMessage;

            if (acceptButton != null)
            {
                acceptButton.interactable = selectedOrder != null;
            }

            SetPageButtonState(orders.Count);
        }

        private void RenderFixScene()
        {
            var orders = host.State.AcceptedOrders;
            orderPageIndex = Mathf.Clamp(orderPageIndex, 0, GetMaxPage(orders.Count));
            if (selectedOrder != null && !orders.Contains(selectedOrder))
            {
                selectedOrder = orders.Skip(orderPageIndex * VisibleRows).FirstOrDefault() ?? orders.FirstOrDefault();
            }

            RenderOrderList(orders);
            RenderRepairMethods();
            detailText.text = selectedOrder == null
                ? "还没有已接受订单。先到接单界面接受订单。"
                : FormatFixDetail(selectedOrder, selectedRepairMethod);
            statusText.text = statusMessage;

            if (acceptButton != null)
            {
                acceptButton.interactable = selectedOrder != null && selectedRepairMethod != null;
            }

            SetPageButtonState(orders.Count);
        }

        private void RenderBuyScene()
        {
            var materials = GetAvailableMaterials();
            var maxStart = Mathf.Max(0, materials.Count - VisibleRows);
            materialStartIndex = Mathf.Clamp(materialStartIndex, 0, maxStart);
            if (selectedMaterial != null && !materials.Contains(selectedMaterial))
            {
                selectedMaterial = materials.Skip(materialStartIndex).FirstOrDefault() ?? materials.FirstOrDefault();
            }

            RenderMaterials(materials);
            UpdateMaterialScrollbar(materials.Count);
            UpdatePurchaseControls();

            detailText.text = FormatBuySummary();
            statusText.text = statusMessage;

            if (acceptButton != null)
            {
                acceptButton.interactable = selectedMaterial != null;
            }
        }

        private void RenderOrderList(IReadOnlyList<OrderDefinition> orders)
        {
            ClearButtons(listButtons);
            var visibleOrders = orders.Skip(orderPageIndex * VisibleRows).Take(VisibleRows);
            foreach (var order in visibleOrders)
            {
                var button = CreateRuntimeButton(listContentRoot, FormatOrderButton(order), () =>
                {
                    selectedOrder = order;
                    statusMessage = sceneName == FixSceneName ? "已选择修补订单。" : "已选择订单。";
                    RenderCurrentOrderMode();
                });
                SetButtonSelected(button, order == selectedOrder);
                listButtons.Add(button);
            }
        }

        private void RenderRepairMethods()
        {
            ClearButtons(repairMethodButtons);
            foreach (var repairMethod in host.Config.RepairMethods.Take(VisibleRows))
            {
                var button = CreateRuntimeButton(repairMethodRoot, FormatRepairMethodButton(repairMethod), () =>
                {
                    selectedRepairMethod = repairMethod;
                    statusMessage = "已选择修补方式。";
                    RenderFixScene();
                }, 56);
                SetButtonSelected(button, repairMethod == selectedRepairMethod);
                repairMethodButtons.Add(button);
            }
        }

        private void RenderMaterials(IReadOnlyList<MaterialDefinition> materials)
        {
            ClearButtons(listButtons);
            foreach (var material in materials.Skip(materialStartIndex).Take(VisibleRows))
            {
                var button = CreateRuntimeButton(listContentRoot, FormatMaterialButton(material), () =>
                {
                    selectedMaterial = material;
                    statusMessage = "已选择材料。";
                    RenderBuyScene();
                });
                SetButtonSelected(button, material == selectedMaterial);
                listButtons.Add(button);
            }
        }

        private void UpdateMaterialScrollbar(int materialCount)
        {
            if (materialScrollbar == null)
            {
                return;
            }

            var maxStart = Mathf.Max(0, materialCount - VisibleRows);
            settingScrollbarValue = true;
            materialScrollbar.interactable = maxStart > 0;
            materialScrollbar.size = materialCount <= VisibleRows ? 1f : Mathf.Clamp01((float)VisibleRows / materialCount);
            materialScrollbar.value = maxStart <= 0 ? 1f : 1f - (float)materialStartIndex / maxStart;
            settingScrollbarValue = false;
        }

        private void UpdatePurchaseControls()
        {
            if (purchaseCountText != null)
            {
                purchaseCountText.text = $"{purchaseCount}/{MaxPurchaseCount}";
            }

            if (lastButton != null)
            {
                lastButton.interactable = purchaseCount > 1;
            }

            if (nextButton != null)
            {
                nextButton.interactable = purchaseCount < MaxPurchaseCount;
            }
        }

        private void SetPageButtonState(int itemCount)
        {
            var maxPage = GetMaxPage(itemCount);
            if (lastButton != null)
            {
                lastButton.interactable = orderPageIndex > 0;
            }

            if (nextButton != null)
            {
                nextButton.interactable = orderPageIndex < maxPage;
            }
        }

        private IReadOnlyList<OrderDefinition> GetCurrentOrders()
        {
            return sceneName == FixSceneName ? host.State.AcceptedOrders : host.State.TodaysOrders;
        }

        private List<MaterialDefinition> GetAvailableMaterials()
        {
            var materials = new List<MaterialDefinition>();
            AddMaterials(materials, host.State.MaterialStock.Keys);
            if (host.Config != null)
            {
                AddMaterials(materials, host.Config.OrderPool
                    .Where(order => order != null)
                    .SelectMany(order => order.RequiredMaterials)
                    .Where(material => material.material != null)
                    .Select(material => material.material));
            }

            return materials;
        }

        private static void AddMaterials(List<MaterialDefinition> target, IEnumerable<MaterialDefinition> materials)
        {
            foreach (var material in materials)
            {
                if (material != null && !target.Contains(material))
                {
                    target.Add(material);
                }
            }
        }

        private void BuildListLayout()
        {
            var listObject = FindSceneObject(OrderListName);
            listContentRoot = CreateRuntimeRoot(listObject, "RuntimeOrderRows", new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.76f));
            AddVerticalLayout(listContentRoot, 14);
        }

        private void BuildMaterialListLayout()
        {
            var listObject = FindSceneObject(OrderListName);
            listContentRoot = CreateRuntimeRoot(listObject, "RuntimeMaterialRows", new Vector2(0.08f, 0.21f), new Vector2(0.84f, 0.72f));
            AddVerticalLayout(listContentRoot, 14);
        }

        private void BuildOrderInfoLayout()
        {
            var infoObject = FindSceneObject(OrderInformationName);
            infoContentRoot = CreateRuntimeRoot(infoObject, "RuntimeOrderInfo", new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.78f));
            AddVerticalLayout(infoContentRoot, 12);
            detailText = CreateRuntimeText(infoContentRoot, "Detail", 22, FontStyle.Normal);
            AddLayout(detailText.gameObject, 0, 1f);
            statusText = CreateRuntimeText(infoContentRoot, "Status", 20, FontStyle.Bold);
            AddLayout(statusText.gameObject, 58, 0f);
        }

        private void BuildFixInfoLayout()
        {
            var infoObject = FindSceneObject(OrderInformationName);
            infoContentRoot = CreateRuntimeRoot(infoObject, "RuntimeFixInfo", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.82f));
            AddVerticalLayout(infoContentRoot, 12);
            detailText = CreateRuntimeText(infoContentRoot, "Detail", 18, FontStyle.Normal);
            AddLayout(detailText.gameObject, 160, 1f);
            repairMethodRoot = CreateChildPanel(infoContentRoot, "RepairMethods");
            AddLayout(repairMethodRoot.gameObject, 196, 0f);
            AddVerticalLayout(repairMethodRoot, 10);
            statusText = CreateRuntimeText(infoContentRoot, "Status", 19, FontStyle.Bold);
            AddLayout(statusText.gameObject, 52, 0f);
        }

        private void BuildBuyInfoLayout()
        {
            var infoObject = FindSceneObject(OrderInformationName);
            infoContentRoot = CreateRuntimeRoot(infoObject, "RuntimeBuyInfo", new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.78f));
            AddVerticalLayout(infoContentRoot, 12);
            detailText = CreateRuntimeText(infoContentRoot, "Detail", 22, FontStyle.Normal);
            AddLayout(detailText.gameObject, 0, 1f);
            statusText = CreateRuntimeText(infoContentRoot, "Status", 20, FontStyle.Bold);
            AddLayout(statusText.gameObject, 58, 0f);
        }

        private RectTransform CreateRuntimeRoot(GameObject parentObject, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (parentObject == null)
            {
                parentObject = gameObject;
            }

            var existing = parentObject.transform.Find(name);
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parentObject.transform, false);
            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            return rectTransform;
        }

        private static RectTransform CreateChildPanel(Transform parent, string name)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            return rectTransform;
        }

        private Button CreateRuntimeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float minHeight = 84)
        {
            var buttonObject = new GameObject("RuntimeButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = PrototypeUiTheme.Card;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var colors = button.colors;
            colors.normalColor = PrototypeUiTheme.Card;
            colors.highlightedColor = PrototypeUiTheme.PrimaryHover;
            colors.pressedColor = PrototypeUiTheme.PaperMuted;
            colors.selectedColor = PrototypeUiTheme.CardSelected;
            colors.disabledColor = PrototypeUiTheme.CardUnavailable;
            button.colors = colors;

            var text = CreateRuntimeText(buttonObject.transform, "Label", 18, FontStyle.Bold);
            text.text = label;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18, 8);
            textRect.offsetMax = new Vector2(-18, -8);

            AddLayout(buttonObject, minHeight, 1f);
            return button;
        }

        private Text CreateCountText()
        {
            var listObject = FindSceneObject(OrderListName);
            var parent = listObject == null ? transform : listObject.transform;
            var text = CreateRuntimeText(parent, "RuntimePurchaseCount", 22, FontStyle.Bold);
            text.alignment = TextAnchor.MiddleCenter;
            var rectTransform = text.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.35f, 0.06f);
            rectTransform.anchorMax = new Vector2(0.65f, 0.16f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            return text;
        }

        private Text CreateRuntimeText(Transform parent, string name, int fontSize, FontStyle style)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = GetDefaultFont();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = PrototypeUiTheme.Ink;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void AddVerticalLayout(RectTransform root, float spacing)
        {
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void AddLayout(GameObject target, float minHeight, float flexibleHeight)
        {
            var layout = target.AddComponent<LayoutElement>();
            layout.minHeight = minHeight;
            layout.flexibleHeight = flexibleHeight;
        }

        private static void SetButtonSelected(Button button, bool selected)
        {
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected ? PrototypeUiTheme.CardSelected : PrototypeUiTheme.Card;
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (acceptButton != null)
            {
                acceptButton.interactable = interactable;
            }

            if (lastButton != null)
            {
                lastButton.interactable = interactable;
            }

            if (nextButton != null)
            {
                nextButton.interactable = interactable;
            }
        }

        private static void ClearButtons(List<Button> buttons)
        {
            foreach (var button in buttons)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }

            buttons.Clear();
        }

        private void CloseScene()
        {
            host?.CloseInteractionSceneFromPanel();
        }

        private Button FindButton(string objectName)
        {
            var target = FindSceneObject(objectName);
            return target == null ? null : target.GetComponentInChildren<Button>(true);
        }

        private GameObject FindSceneObject(string objectName)
        {
            var scene = gameObject.scene;
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

        private Text FindTextByContent(string content)
        {
            var scene = gameObject.scene;
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                foreach (var text in rootObject.GetComponentsInChildren<Text>(true))
                {
                    if (text.text == content)
                    {
                        return text;
                    }
                }
            }

            return null;
        }

        private string FormatOrderButton(OrderDefinition order)
        {
            var customerName = order.Customer == null ? "未知顾客" : order.Customer.DisplayName;
            return $"{order.DisplayName}\n{customerName} / {order.RewardCoins} 金币 / 能量 {order.EnergyCost}";
        }

        private string FormatOrderDetail(OrderDefinition order)
        {
            var customerName = order.Customer == null ? "未知顾客" : order.Customer.DisplayName;
            var customerType = order.Customer == null ? "未知" : FormatCustomerType(order.Customer.CustomerType);
            var requiredMaterials = FormatRequiredMaterials(order);
            return $"订单：{order.DisplayName}\n物品：{order.ItemType}\n损坏：{order.DamageLevel}\n顾客：{customerName} / {customerType}\n需要材料：{requiredMaterials}\n基础能量：{order.EnergyCost}\n基础报酬：{order.RewardCoins}\n备注：{order.CustomerNote}";
        }

        private string FormatRequiredMaterials(OrderDefinition order)
        {
            if (order.RequiredMaterials.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", order.RequiredMaterials.Select(material =>
            {
                if (material.material == null)
                {
                    return string.Empty;
                }

                var label = $"{material.material.DisplayName} x{material.amount}";
                return host.State.HasMaterial(material.material, material.amount) ? label : $"{label}（不足）";
            }).Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        private string FormatRepairPreview(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (repairMethod == null)
            {
                return "请选择修补方式。";
            }

            var energyCost = host.OrderService.GetFinalEnergyCost(order, repairMethod);
            var rewardCoins = host.OrderService.GetFinalRewardCoins(order, repairMethod);
            var authenticityDelta = host.OrderService.GetFinalAuthenticityDelta(order, repairMethod);
            return $"修补方式：{repairMethod.DisplayName}\n{repairMethod.Description}\n预计消耗：{energyCost} 能量\n预计收入：{rewardCoins} 金币\n真实度：{FormatSigned(authenticityDelta)}";
        }

        private string FormatFixDetail(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            var methodName = repairMethod == null ? "未选择" : repairMethod.DisplayName;
            var energyCost = repairMethod == null ? order.EnergyCost : host.OrderService.GetFinalEnergyCost(order, repairMethod);
            var rewardCoins = repairMethod == null ? order.RewardCoins : host.OrderService.GetFinalRewardCoins(order, repairMethod);
            var authenticityDelta = repairMethod == null ? 0 : host.OrderService.GetFinalAuthenticityDelta(order, repairMethod);
            return $"订单：{order.DisplayName}\n损坏：{order.DamageLevel}\n需要材料：{FormatRequiredMaterials(order)}\n当前方式：{methodName}\n预计消耗：{energyCost} 能量\n预计收入：{rewardCoins} 金币\n真实度：{FormatSigned(authenticityDelta)}";
        }

        private static string FormatRepairMethodButton(RepairMethodDefinition repairMethod)
        {
            return $"{repairMethod.DisplayName}\n能量 {FormatSigned(repairMethod.EnergyModifier)} / 金币 {FormatSigned(repairMethod.CoinRewardModifier)} / 真实 {FormatSigned(repairMethod.AuthenticityModifier)}";
        }

        private string FormatMaterialButton(MaterialDefinition material)
        {
            host.State.MaterialStock.TryGetValue(material, out var currentAmount);
            var price = GetPurchaseCost(1);
            var amount = GetPurchaseAmount(1);
            return $"{material.DisplayName}\n库存 {currentAmount} / +{amount} 需要 {price} 金币";
        }

        private string FormatBuySummary()
        {
            var completedCount = host.State.CompletedOrders.Count(order => order.DayCompleted == host.State.CurrentDay);
            var purchaseAmount = GetPurchaseAmount(purchaseCount);
            var purchaseCost = GetPurchaseCost(purchaseCount);
            var materialText = selectedMaterial == null
                ? "未选择"
                : $"{selectedMaterial.DisplayName} +{purchaseAmount} / {purchaseCost} 金币";
            return $"今日状态\n第 {host.State.CurrentDay} 天\n金币：{host.State.Coins}\n能量：{host.State.Energy}/{host.State.DailyEnergyRecovery}\n完成订单：{completedCount}\n当日收入：{host.State.TodayIncome}\n真实度变化：{FormatSigned(host.State.TodayAuthenticityDelta)}\n\n购买内容\n{materialText}\n\n当前材料\n{FormatCurrentMaterials()}";
        }

        private string FormatCurrentMaterials()
        {
            if (host.State.MaterialStock.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", host.State.MaterialStock.Select(pair => $"{pair.Key.DisplayName} x{pair.Value}"));
        }

        private int GetPurchaseAmount(int count)
        {
            return Mathf.Max(1, host.Config.NightSupplyAmount) * Mathf.Clamp(count, 1, MaxPurchaseCount);
        }

        private int GetPurchaseCost(int count)
        {
            return Mathf.Max(1, host.Config.NightSupplyCost) * Mathf.Clamp(count, 1, MaxPurchaseCount);
        }

        private static int GetMaxPage(int itemCount)
        {
            return itemCount <= 0 ? 0 : (itemCount - 1) / VisibleRows;
        }

        private static string FormatCustomerType(CustomerType customerType)
        {
            return customerType switch
            {
                CustomerType.Gentle => "温柔型",
                CustomerType.Demanding => "挑剔型",
                CustomerType.Makeover => "改造型",
                _ => "普通"
            };
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? $"+{value}" : value.ToString();
        }

        private static Font GetDefaultFont()
        {
            if (cachedDefaultFont == null)
            {
                cachedDefaultFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            }

            return cachedDefaultFont;
        }
    }
}
