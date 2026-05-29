using UnityEngine;

namespace APlaceLikeMe.UI
{
    public static class PrototypeUiTheme
    {
        public static readonly Color32 Background = new(252, 251, 247, 255);
        public static readonly Color32 Paper = new(255, 255, 252, 255);
        public static readonly Color32 PaperMuted = new(244, 243, 238, 255);
        public static readonly Color32 Card = new(255, 255, 252, 255);
        public static readonly Color32 ListItem = new(200, 178, 150, 255);
        public static readonly Color32 ListItemSelected = new(200, 178, 150, 255);
        public static readonly Color32 ListItemHover = new(218, 202, 171, 255);
        public static readonly Color32 CardSelected = new(235, 239, 229, 255);
        public static readonly Color32 CardUnavailable = new(238, 238, 232, 255);
        public static readonly Color32 Primary = new(238, 242, 231, 255);
        public static readonly Color32 PrimaryHover = new(224, 232, 216, 255);
        public static readonly Color32 Ink = new(28, 28, 26, 255);
        public static readonly Color32 InkMuted = new(84, 82, 76, 255);
        public static readonly Color32 Danger = new(128, 64, 55, 255);
        public static readonly Color32 Success = new(73, 105, 72, 255);
        public static readonly Color32 Line = new(28, 28, 26, 255);

        public const int Radius = 4;
        public const int SpaceSmall = 10;
        public const int SpaceMedium = 16;
        public const int SpaceLarge = 24;
    }
}
