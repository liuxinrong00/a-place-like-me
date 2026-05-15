using APlaceLikeMe.Core;
using UnityEngine;

namespace APlaceLikeMe.Flow
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GamePhase initialPhase = GamePhase.Boot;

        public GamePhase CurrentPhase { get; private set; }

        private void Awake()
        {
            CurrentPhase = initialPhase;
        }

        private void Start()
        {
            EnterPhase(GamePhase.DayStart);
        }

        public void EnterPhase(GamePhase nextPhase)
        {
            CurrentPhase = nextPhase;
            Debug.Log($"Game phase changed: {CurrentPhase}");
        }
    }
}
