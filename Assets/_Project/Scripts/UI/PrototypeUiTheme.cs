using UnityEngine;

namespace APlaceLikeMe.UI
{
    public static class PrototypeUiTheme
    {
        public static readonly Color32 Background = new(55, 43, 31, 255);
        public static readonly Color32 Paper = new(246, 229, 188, 255);
        public static readonly Color32 PaperMuted = new(226, 203, 155, 255);
        public static readonly Color32 Card = new(255, 241, 204, 255);
        public static readonly Color32 CardSelected = new(255, 211, 112, 255);
        public static readonly Color32 CardUnavailable = new(226, 190, 158, 255);
        public static readonly Color32 Primary = new(216, 155, 42, 255);
        public static readonly Color32 PrimaryHover = new(238, 178, 55, 255);
        public static readonly Color32 Ink = new(58, 45, 35, 255);
        public static readonly Color32 InkMuted = new(111, 88, 66, 255);
        public static readonly Color32 Danger = new(162, 72, 45, 255);
        public static readonly Color32 Success = new(86, 120, 64, 255);

        public const int Radius = 14;
        public const int SpaceSmall = 8;
        public const int SpaceMedium = 12;
        public const int SpaceLarge = 20;
    }
}
