using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace APlaceLikeMe.UI
{
    public sealed class SettingSceneController : MonoBehaviour
    {
        private const string MusicVolumeKey = "Settings.MusicVolume";
        private const string SoundEffectVolumeKey = "Settings.SoundEffectVolume";

        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider soundEffectSlider;
        [SerializeField] private Text musicValueText;
        [SerializeField] private Text soundEffectValueText;
        [SerializeField] private Button applyButton;
        [SerializeField] private Button saveGameButton;
        [SerializeField] private Button returnButton;
        [SerializeField] private string returnSceneName = "MainScene";

        private void Awake()
        {
            if (musicSlider != null)
            {
                musicSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(MusicVolumeKey, 0.7f));
                musicSlider.onValueChanged.AddListener(_ => UpdateLabels());
            }

            if (soundEffectSlider != null)
            {
                soundEffectSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(SoundEffectVolumeKey, 0.8f));
                soundEffectSlider.onValueChanged.AddListener(_ => UpdateLabels());
            }

            if (applyButton != null)
            {
                applyButton.onClick.AddListener(ApplySettings);
            }

            if (saveGameButton != null)
            {
                saveGameButton.onClick.AddListener(SaveGame);
            }

            if (returnButton != null)
            {
                returnButton.onClick.AddListener(ReturnToMainScene);
            }

            UpdateLabels();
        }

        public void ApplySettings()
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, GetSliderValue(musicSlider, 0.7f));
            PlayerPrefs.SetFloat(SoundEffectVolumeKey, GetSliderValue(soundEffectSlider, 0.8f));
            PlayerPrefs.Save();
        }

        private void SaveGame()
        {
            ApplySettings();
            Debug.Log("Save game button pressed from Setting scene.");
        }

        private void ReturnToMainScene()
        {
            ApplySettings();

            if (!string.IsNullOrWhiteSpace(returnSceneName) && Application.CanStreamedLevelBeLoaded(returnSceneName))
            {
                SceneManager.LoadScene(returnSceneName);
            }
        }

        private void UpdateLabels()
        {
            SetPercentText(musicValueText, GetSliderValue(musicSlider, 0.7f));
            SetPercentText(soundEffectValueText, GetSliderValue(soundEffectSlider, 0.8f));
        }

        private static float GetSliderValue(Slider slider, float fallback)
        {
            return slider == null ? fallback : Mathf.Clamp01(slider.value);
        }

        private static void SetPercentText(Text label, float value)
        {
            if (label != null)
            {
                label.text = $"{Mathf.RoundToInt(value * 100f)}%";
            }
        }
    }
}
