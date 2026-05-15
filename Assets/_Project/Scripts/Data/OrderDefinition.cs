using System.Collections.Generic;
using UnityEngine;

namespace APlaceLikeMe.Data
{
    [CreateAssetMenu(menuName = "A Place Like Me/Data/Order", fileName = "OrderDefinition")]
    public sealed class OrderDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string itemType;
        [SerializeField] private string damageLevel;
        [SerializeField] private CustomerDefinition customer;
        [SerializeField] private List<MaterialAmount> requiredMaterials = new();
        [SerializeField] private int energyCost = 1;
        [SerializeField] private int rewardCoins = 5;
        [SerializeField, TextArea] private string customerNote;
        [SerializeField, TextArea] private string feedbackText;

        public string Id => id;
        public string DisplayName => displayName;
        public string ItemType => itemType;
        public string DamageLevel => damageLevel;
        public CustomerDefinition Customer => customer;
        public IReadOnlyList<MaterialAmount> RequiredMaterials => requiredMaterials;
        public int EnergyCost => energyCost;
        public int RewardCoins => rewardCoins;
        public string CustomerNote => customerNote;
        public string FeedbackText => feedbackText;
    }
}
