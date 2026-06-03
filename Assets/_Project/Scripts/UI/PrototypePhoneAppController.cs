using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace APlaceLikeMe.UI
{
    public sealed class PrototypePhoneAppController
    {
        public enum Tab
        {
            Reviews,
            Diary,
            Moments,
            Summary
        }

        private readonly Dictionary<Tab, RectTransform> tabRects = new();
        private readonly Dictionary<Tab, Button> tabButtons = new();
        private readonly Dictionary<Tab, TabSprites> tabSprites = new();
        private readonly Dictionary<Tab, RectTransform> launcherTabRects = new();
        private readonly System.Func<Tab, string> getTitle;
        private readonly System.Func<Tab, string> getBody;
        private readonly System.Action close;

        private RectTransform panel;
        private RectTransform launcher;
        private RectTransform closeRect;
        private ScrollRect scrollRect;
        private Text titleText;
        private Text bodyText;
        private Tab selectedTab = Tab.Reviews;

        private readonly struct TabSprites
        {
            public TabSprites(Sprite active, Sprite nonactive)
            {
                Active = active;
                Nonactive = nonactive;
            }

            public Sprite Active { get; }
            public Sprite Nonactive { get; }
        }

        public PrototypePhoneAppController(
            RectTransform panel,
            RectTransform launcher,
            RectTransform closeRect,
            ScrollRect scrollRect,
            Text titleText,
            Text bodyText,
            System.Func<Tab, string> getTitle,
            System.Func<Tab, string> getBody,
            System.Action close)
        {
            this.panel = panel;
            this.launcher = launcher;
            this.closeRect = closeRect;
            this.scrollRect = scrollRect;
            this.titleText = titleText;
            this.bodyText = bodyText;
            this.getTitle = getTitle;
            this.getBody = getBody;
            this.close = close;
        }

        public Tab SelectedTab => selectedTab;

        public void SetTabTarget(Tab tab, RectTransform rectTransform, Button button)
        {
            tabRects[tab] = rectTransform;
            tabButtons[tab] = button;
        }

        public void SetTabSprites(Tab tab, Sprite active, Sprite nonactive)
        {
            tabSprites[tab] = new TabSprites(active, nonactive);
        }

        public void SetLauncherTabTarget(Tab tab, RectTransform rectTransform)
        {
            launcherTabRects[tab] = rectTransform;
        }

        public bool IsOpen()
        {
            return panel != null && panel.gameObject.activeSelf;
        }

        public void SetLauncherVisible(bool visible)
        {
            if (launcher != null)
            {
                launcher.gameObject.SetActive(visible);
            }
        }

        public bool TryOpenFromScreenPosition(Vector2 screenPosition)
        {
            if (launcher == null || !launcher.gameObject.activeInHierarchy)
            {
                return false;
            }

            foreach (var pair in launcherTabRects)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(pair.Value, screenPosition, null))
                {
                    continue;
                }

                Open(pair.Key);
                return true;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(launcher, screenPosition, null))
            {
                return false;
            }

            Open();
            return true;
        }

        public bool TryHandleOpenPointer(Vector2 screenPosition)
        {
            if (!IsOpen())
            {
                return false;
            }

            if (closeRect != null && RectTransformUtility.RectangleContainsScreenPoint(closeRect, screenPosition, null))
            {
                close?.Invoke();
                return true;
            }

            foreach (var pair in tabRects)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(pair.Value, screenPosition, null))
                {
                    continue;
                }

                SelectTab(pair.Key);
                return true;
            }

            return false;
        }

        public void Open()
        {
            Open(Tab.Reviews);
        }

        public void Open(Tab initialTab)
        {
            if (panel == null)
            {
                return;
            }

            selectedTab = initialTab;
            panel.gameObject.SetActive(true);
            Render();
        }

        public void Close()
        {
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }
        }

        public void SelectTab(Tab tab)
        {
            selectedTab = tab;
            Render();
        }

        public void Render()
        {
            if (titleText == null || bodyText == null)
            {
                return;
            }

            titleText.text = getTitle == null ? string.Empty : getTitle(selectedTab);
            bodyText.text = getBody == null ? string.Empty : getBody(selectedTab);
            foreach (var pair in tabButtons)
            {
                var image = pair.Value.targetGraphic as Image;
                if (image == null)
                {
                    continue;
                }

                var selected = pair.Key == selectedTab;
                if (tabSprites.TryGetValue(pair.Key, out var sprites))
                {
                    var sprite = selected ? sprites.Active : sprites.Nonactive;
                    sprite ??= selected ? sprites.Nonactive : sprites.Active;
                    image.sprite = sprite;
                    image.color = sprite == null ? Color.clear : Color.white;
                    continue;
                }

                image.sprite = null;
                image.color = Color.clear;
            }

            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }
    }
}
