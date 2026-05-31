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
        private const string DangerTextColor = "#803020";
        private const int VisibleRows = 3;
        private const int MaxPurchaseCount = 10;

        private static Font cachedDefaultFont;

        private readonly List<Button> listButtons = new();
        private readonly List<Button> repairMethodButtons = new();
        private readonly List<PrototypeButtonBinding> buttonBindings = new();

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
        private PrototypeButtonBinding pendingMouseButtonBinding;
        private PrototypeButtonBinding pendingTouchButtonBinding;
        private int pendingTouchFingerId = -1;
        private int lastButtonActionFrame = -1;
        private Button lastButtonActionTarget;

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

        private void Update()
        {
            if (host != null && host.IsOverlayOpen)
            {
                return;
            }

            TryHandleButtonPointer();
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
                BindButtonAction(returnButton, CloseScene);
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
                BindButtonAction(acceptButton, AcceptSelectedOrder);
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
                BindButtonAction(acceptButton, RepairSelectedOrder);
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
                BindButtonAction(acceptButton, BuySelectedMaterial);
            }

            if (lastButton != null)
            {
                BindButtonAction(lastButton, DecreasePurchaseCount);
            }

            if (nextButton != null)
            {
                BindButtonAction(nextButton, IncreasePurchaseCount);
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
                BindButtonAction(lastButton, PreviousOrderPage);
            }

            if (nextButton != null)
            {
                BindButtonAction(nextButton, NextOrderPage);
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
            statusMessage = result.Succeeded ? string.Empty : FormatStatusMessage(result.Message);
            var orders = host.State.AcceptedOrders;
            orderPageIndex = Mathf.Clamp(orderPageIndex, 0, GetMaxPage(orders.Count));
            selectedOrder = orders.Skip(orderPageIndex * VisibleRows).FirstOrDefault() ?? orders.FirstOrDefault();
            RenderFixScene();
            if (result.Succeeded)
            {
                host.ShowRepairResultFromPanel(result.Message);
            }
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
            if (selectedOrder == null)
            {
                detailText.text = "还没有已接受订单。先到接单界面接受订单。";
                detailText.alignment = TextAnchor.MiddleCenter;
                statusText.text = string.Empty;
            }
            else
            {
                detailText.text = FormatFixDetail(selectedOrder, selectedRepairMethod);
                detailText.alignment = TextAnchor.UpperLeft;
                statusText.text = statusMessage;
            }

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
            statusText.text = selectedMaterial == null ? string.Empty : statusMessage;

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
                purchaseCountText.text = $"{GetPurchaseAmount(purchaseCount)}/{MaxPurchaseCount}";
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
                    .SelectMany(order => order.RepairProfiles)
                    .Where(profile => profile != null)
                    .SelectMany(profile => profile.requiredMaterials)
                    .Where(required => required.material != null)
                    .Select(required => required.material));
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
            statusText.alignment = TextAnchor.LowerLeft;
            AddLayout(statusText.gameObject, 64, 0f);
        }

        private void BuildBuyInfoLayout()
        {
            var infoObject = FindSceneObject(OrderInformationName);
            infoContentRoot = CreateRuntimeRoot(infoObject, "RuntimeBuyInfo", new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.78f));
            AddVerticalLayout(infoContentRoot, 12);
            detailText = CreateRuntimeText(infoContentRoot, "Detail", 20, FontStyle.Normal);
            detailText.lineSpacing = 0.92f;
            detailText.verticalOverflow = VerticalWrapMode.Truncate;
            AddLayout(detailText.gameObject, 0, 1f);
            statusText = CreateRuntimeText(infoContentRoot, "Status", 20, FontStyle.Bold);
            statusText.alignment = TextAnchor.LowerLeft;
            AddLayout(statusText.gameObject, 78, 0f);
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
            image.color = PrototypeUiTheme.ListItem;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            BindButtonAction(button, onClick);
            var colors = button.colors;
            colors.normalColor = PrototypeUiTheme.ListItem;
            colors.highlightedColor = PrototypeUiTheme.ListItemHover;
            colors.pressedColor = PrototypeUiTheme.ListItemSelected;
            colors.selectedColor = PrototypeUiTheme.ListItemSelected;
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
                image.color = selected ? PrototypeUiTheme.ListItemSelected : PrototypeUiTheme.ListItem;
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

        private void ClearButtons(List<Button> buttons)
        {
            foreach (var button in buttons)
            {
                if (button != null)
                {
                    RemoveButtonBinding(button);
                    Destroy(button.gameObject);
                }
            }

            buttons.Clear();
        }

        private void CloseScene()
        {
            host?.CloseInteractionSceneFromPanel();
        }

        private void BindButtonAction(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(() => TryInvokeButtonAction(button, action));
            buttonBindings.Add(new PrototypeButtonBinding(button, action));
        }

        private bool TryHandleButtonPointer()
        {
            if (buttonBindings.Count == 0)
            {
                return false;
            }

            var handled = false;
            if (Input.GetMouseButtonDown(0))
            {
                pendingMouseButtonBinding = FindButtonBindingAtScreenPosition(Input.mousePosition);
                handled = pendingMouseButtonBinding.Button != null;
            }

            if (Input.GetMouseButtonUp(0))
            {
                var pressedBinding = pendingMouseButtonBinding;
                pendingMouseButtonBinding = default;
                if (pressedBinding.Button != null &&
                    IsSameButtonBinding(pressedBinding, FindButtonBindingAtScreenPosition(Input.mousePosition)) &&
                    TryInvokeButtonBinding(pressedBinding))
                {
                    return true;
                }
            }

            for (var index = 0; index < Input.touchCount; index++)
            {
                var touch = Input.GetTouch(index);
                if (touch.phase == TouchPhase.Began)
                {
                    var binding = FindButtonBindingAtScreenPosition(touch.position);
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
                        IsSameButtonBinding(pressedBinding, FindButtonBindingAtScreenPosition(touch.position)) &&
                        TryInvokeButtonBinding(pressedBinding))
                    {
                        return true;
                    }
                }
            }

            return handled;
        }

        private PrototypeButtonBinding FindButtonBindingAtScreenPosition(Vector2 screenPosition)
        {
            for (var index = buttonBindings.Count - 1; index >= 0; index--)
            {
                var binding = buttonBindings[index];
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

        private bool TryInvokeButtonBinding(PrototypeButtonBinding binding)
        {
            return TryInvokeButtonAction(binding.Button, binding.Action);
        }

        private bool TryInvokeButtonAction(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                return false;
            }

            if (lastButtonActionFrame == Time.frameCount && lastButtonActionTarget == button)
            {
                return true;
            }

            lastButtonActionFrame = Time.frameCount;
            lastButtonActionTarget = button;
            action?.Invoke();
            return true;
        }

        private void RemoveButtonBinding(Button button)
        {
            if (button == null)
            {
                return;
            }

            for (var index = buttonBindings.Count - 1; index >= 0; index--)
            {
                if (buttonBindings[index].Button == button)
                {
                    buttonBindings.RemoveAt(index);
                }
            }
        }

        private static bool IsSameButtonBinding(PrototypeButtonBinding left, PrototypeButtonBinding right)
        {
            return left.Button != null && left.Button == right.Button;
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
            return $"{FormatDifficulty(order.Difficulty)} · {order.DisplayName}\n{customerName} / {GetDefaultReward(order)} 金币 / 能量 {GetDefaultEnergy(order)}";
        }

        private string FormatOrderDetail(OrderDefinition order)
        {
            var customerName = order.Customer == null ? "未知顾客" : order.Customer.DisplayName;
            var customerType = order.Customer == null ? "未知" : FormatCustomerType(order.Customer.CustomerType);
            var requiredMaterials = FormatRequiredMaterials(order);
            return $"订单：{order.DisplayName}\n难度：{FormatDifficulty(order.Difficulty)}\n物品：{order.ItemType}\n损坏：{order.DamageLevel}\n顾客：{customerName} / {customerType}\n需要材料：{requiredMaterials}\n基础能量：{GetDefaultEnergy(order)}\n基础报酬：{GetDefaultReward(order)}\n备注：{order.CustomerNote}";
        }

        private string FormatRequiredMaterials(OrderDefinition order)
        {
            var requiredMaterials = GetSelectedRequiredMaterials(order);
            if (requiredMaterials.Count == 0)
            {
                return "无";
            }

            return string.Join(" / ", requiredMaterials.Select(material =>
            {
                if (material.Key == null)
                {
                    return string.Empty;
                }

                var label = $"{material.Key.DisplayName} x{material.Value}";
                return host.State.HasMaterial(material.Key, material.Value)
                    ? label
                    : ColorDanger($"{label}（不足）");
            }).Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        private string FormatRepairPreview(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (repairMethod == null)
            {
                return "请选择修补方式。";
            }

            var energyCost = host.OrderService.GetFinalEnergyCost(order, repairMethod);
            var materialCost = host.OrderService.GetFinalMaterialCost(order, repairMethod);
            var rewardCoins = host.OrderService.GetFinalRewardCoins(order, repairMethod);
            var authenticityDelta = host.OrderService.GetFinalAuthenticityDelta(order, repairMethod);
            return $"修补方式：{repairMethod.DisplayName}\n{repairMethod.Description}\n预计消耗：{energyCost} 能量\n材料成本：{materialCost} 金币\n预计收入：{rewardCoins} 金币\n净收益：{rewardCoins - materialCost} 金币\n真实度：{FormatSigned(authenticityDelta)}";
        }

        private string FormatFixDetail(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            var methodName = repairMethod == null ? "未选择" : repairMethod.DisplayName;
            var energyCost = repairMethod == null ? order.EnergyCost : host.OrderService.GetFinalEnergyCost(order, repairMethod);
            var materialCost = repairMethod == null ? 0 : host.OrderService.GetFinalMaterialCost(order, repairMethod);
            var rewardCoins = repairMethod == null ? order.RewardCoins : host.OrderService.GetFinalRewardCoins(order, repairMethod);
            var authenticityDelta = repairMethod == null ? 0 : host.OrderService.GetFinalAuthenticityDelta(order, repairMethod);
            return $"订单：{order.DisplayName}\n损坏：{order.DamageLevel}\n需要材料：{FormatRequiredMaterials(order)}\n当前方式：{methodName}\n预计消耗：{energyCost} 能量\n材料成本：{materialCost} 金币\n预计收入：{rewardCoins} 金币\n净收益：{rewardCoins - materialCost} 金币\n真实度：{FormatSigned(authenticityDelta)}";
        }

        private string FormatRepairMethodButton(RepairMethodDefinition repairMethod)
        {
            if (selectedOrder == null)
            {
                return repairMethod.DisplayName;
            }

            var energyCost = host.OrderService.GetFinalEnergyCost(selectedOrder, repairMethod);
            var rewardCoins = host.OrderService.GetFinalRewardCoins(selectedOrder, repairMethod);
            var materialCost = host.OrderService.GetFinalMaterialCost(selectedOrder, repairMethod);
            return $"{repairMethod.DisplayName}\n能量 {energyCost} / 收入 {rewardCoins} / 净 {rewardCoins - materialCost}";
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
            return $"今日状态\n{FormatDayLabel(host.State.CurrentDay)}\n金币：{host.State.Coins}\n能量：{host.State.Energy}/{host.State.DailyEnergyRecovery}\n完成订单：{completedCount}\n当日收入：{host.State.TodayIncome}\n当日支出：{host.State.TodayExpenses}\n真实度变化：{FormatSigned(host.State.TodayAuthenticityDelta)}\n\n购买内容\n{materialText}\n\n当前材料\n{FormatCurrentMaterials()}";
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
            return Mathf.Clamp(count, 1, MaxPurchaseCount);
        }

        private int GetPurchaseCost(int count)
        {
            return selectedMaterial == null ? 0 : selectedMaterial.DefaultPrice * GetPurchaseAmount(count);
        }

        private IReadOnlyDictionary<MaterialDefinition, int> GetSelectedRequiredMaterials(OrderDefinition order)
        {
            return host.OrderService.GetRequiredMaterials(order, selectedRepairMethod);
        }

        private int GetDefaultEnergy(OrderDefinition order)
        {
            var repairMethod = selectedRepairMethod ?? host.Config.RepairMethods.FirstOrDefault();
            return repairMethod == null ? order.EnergyCost : host.OrderService.GetFinalEnergyCost(order, repairMethod);
        }

        private int GetDefaultReward(OrderDefinition order)
        {
            var repairMethod = selectedRepairMethod ?? host.Config.RepairMethods.FirstOrDefault();
            return repairMethod == null ? order.RewardCoins : host.OrderService.GetFinalRewardCoins(order, repairMethod);
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

        private static string FormatDifficulty(OrderDifficulty difficulty)
        {
            return difficulty switch
            {
                OrderDifficulty.Easy => "简单",
                OrderDifficulty.Normal => "普通",
                OrderDifficulty.Hard => "困难",
                _ => "普通"
            };
        }

        private static string FormatStatusMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message) || !message.StartsWith("材料不足")
                ? message
                : ColorDanger(message);
        }

        private static string ColorDanger(string text)
        {
            return $"<color={DangerTextColor}>{text}</color>";
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

        private readonly struct PrototypeButtonBinding
        {
            public PrototypeButtonBinding(Button button, UnityEngine.Events.UnityAction action)
            {
                Button = button;
                Action = action;
            }

            public Button Button { get; }
            public UnityEngine.Events.UnityAction Action { get; }
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
