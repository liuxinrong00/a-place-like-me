using System.Collections.Generic;
using UnityEngine;

namespace APlaceLikeMe.Data
{
    [CreateAssetMenu(menuName = "A Place Like Me/Data/Game Initial Config", fileName = "GameInitialConfig")]
    public sealed class GameInitialConfig : ScriptableObject
    {
        [SerializeField] private int initialCoins = 20;
        [SerializeField] private int initialEnergy = 6;
        [SerializeField] private int initialAuthenticity = 50;
        [SerializeField] private int dailyEnergyRecovery = 6;
        [SerializeField] private List<InitialMaterialStock> initialMaterials = new();

        public int InitialCoins => initialCoins;
        public int InitialEnergy => initialEnergy;
        public int InitialAuthenticity => initialAuthenticity;
        public int DailyEnergyRecovery => dailyEnergyRecovery;
        public IReadOnlyList<InitialMaterialStock> InitialMaterials => initialMaterials;
    }
}
