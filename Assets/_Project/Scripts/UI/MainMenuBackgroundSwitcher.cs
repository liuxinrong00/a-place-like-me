using UnityEngine;
using UnityEngine.UI;

namespace APlaceLikeMe.UI
{
    public sealed class MainMenuBackgroundSwitcher : MonoBehaviour
    {
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Sprite[] backgrounds;
        [SerializeField] private Image startButtonImage;
        [SerializeField] private Sprite[] startButtonSprites;
        [SerializeField] private Image loadButtonImage;
        [SerializeField] private Sprite[] loadButtonSprites;
        [SerializeField] private Image settingsButtonImage;
        [SerializeField] private Sprite[] settingsButtonSprites;
        [SerializeField] private Image exitButtonImage;
        [SerializeField] private Sprite[] exitButtonSprites;
        [SerializeField] private Image weatherButtonImage;
        [SerializeField] private Sprite[] weatherButtonSprites;

        private int currentIndex;

        private void Awake()
        {
            ApplyRandomBackground();
        }

        private void ApplyRandomBackground()
        {
            if (backgrounds == null || backgrounds.Length == 0)
            {
                return;
            }

            Apply(Random.Range(0, backgrounds.Length));
        }

        public void ShowNextBackground()
        {
            if (backgrounds == null || backgrounds.Length == 0)
            {
                return;
            }

            Apply((currentIndex + 1) % backgrounds.Length);
        }

        private void Apply(int index)
        {
            if (backgroundImage == null || backgrounds == null || backgrounds.Length == 0)
            {
                return;
            }

            currentIndex = Mathf.Clamp(index, 0, backgrounds.Length - 1);
            backgroundImage.sprite = backgrounds[currentIndex];
            ApplySprite(startButtonImage, startButtonSprites, currentIndex);
            ApplySprite(loadButtonImage, loadButtonSprites, currentIndex);
            ApplySprite(settingsButtonImage, settingsButtonSprites, currentIndex);
            ApplySprite(exitButtonImage, exitButtonSprites, currentIndex);
            ApplySprite(weatherButtonImage, weatherButtonSprites, currentIndex);
        }

        private static void ApplySprite(Image image, Sprite[] sprites, int index)
        {
            if (image == null || sprites == null || sprites.Length == 0)
            {
                return;
            }

            image.sprite = sprites[Mathf.Clamp(index, 0, sprites.Length - 1)];
        }
    }
}
