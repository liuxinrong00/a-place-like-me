using APlaceLikeMe.Core;
using UnityEngine;

namespace APlaceLikeMe.Data
{
    [CreateAssetMenu(menuName = "A Place Like Me/Data/Repair Method", fileName = "RepairMethodDefinition")]
    public sealed class RepairMethodDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private RepairMethodType repairMethodType;
        [SerializeField] private int energyModifier;
        [SerializeField] private int coinRewardModifier;
        [SerializeField] private int authenticityModifier;
        [SerializeField, TextArea] private string description;

        public string Id => id;
        public string DisplayName => displayName;
        public RepairMethodType RepairMethodType => repairMethodType;
        public int EnergyModifier => energyModifier;
        public int CoinRewardModifier => coinRewardModifier;
        public int AuthenticityModifier => authenticityModifier;
        public string Description => description;
    }
}
