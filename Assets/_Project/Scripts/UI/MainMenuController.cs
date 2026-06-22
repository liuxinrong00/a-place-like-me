using APlaceLikeMe.Gameplay;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace APlaceLikeMe.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private string gameSceneName = "Store";
        [SerializeField] private PrototypeGameConfig prototypeConfig;
        [SerializeField] private string startButtonName = "StartButton";
        [SerializeField] private string loadButtonName = "LoadButton";
        [SerializeField] private string settingsButtonName = "SettingsButton";
        [SerializeField] private string exitButtonName = "ExitButton";

        private void Awake()
        {
            BindButton(startButtonName, StartGame);
            BindButton(loadButtonName, LoadGame);
            BindButton(settingsButtonName, OpenSettings);
            BindButton(exitButtonName, ExitGame);
        }

        public void StartGame()
        {
            if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                Debug.LogError($"Cannot start game because scene is not in Build Settings: {gameSceneName}");
                return;
            }

            PrototypeGameController.PrepareNewGame(prototypeConfig);
            SceneManager.LoadScene(gameSceneName);
        }

        public void LoadGame()
        {
            Debug.Log("Load save is reserved for future implementation.");
        }

        public void OpenSettings()
        {
            Debug.Log("Settings menu is reserved for future implementation.");
        }

        public void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BindButton(string buttonName, UnityAction action)
        {
            var button = FindButton(buttonName);
            if (button == null)
            {
                Debug.LogWarning($"Main menu button was not found: {buttonName}");
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private Button FindButton(string buttonName)
        {
            if (string.IsNullOrWhiteSpace(buttonName))
            {
                return null;
            }

            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                if (button.name == buttonName)
                {
                    return button;
                }
            }

            var target = GameObject.Find(buttonName);
            return target == null ? null : target.GetComponentInChildren<Button>(true);
        }
    }
}
