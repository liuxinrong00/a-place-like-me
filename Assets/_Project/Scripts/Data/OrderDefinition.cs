using System.Collections.Generic;
using APlaceLikeMe.Core;
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
        [SerializeField] private OrderDifficulty difficulty;
        [SerializeField] private CustomerDefinition customer;
        [SerializeField] private List<MaterialAmount> requiredMaterials = new();
        [SerializeField] private List<RepairMethodMaterialProfile> repairProfiles = new();
        [SerializeField] private int energyCost = 1;
        [SerializeField] private int rewardCoins = 5;
        [SerializeField] private bool isSpecialOrder;
        [SerializeField] private string specialStoryId;
        [SerializeField] private int specialStorySequence;
        [SerializeField] private bool overridePreferredRepairMethod;
        [SerializeField] private RepairMethodType preferredRepairMethodType;
        [SerializeField, TextArea] private string customerNote;
        [SerializeField, TextArea] private string feedbackText;
        [SerializeField, TextArea] private string momentText;
        [SerializeField, TextArea] private string todayStoryText;
        [SerializeField, TextArea] private string diaryText;

        public string Id => id;
        public string DisplayName => displayName;
        public string ItemType => itemType;
        public string DamageLevel => damageLevel;
        public OrderDifficulty Difficulty => difficulty;
        public CustomerDefinition Customer => customer;
        public IReadOnlyList<MaterialAmount> RequiredMaterials => requiredMaterials;
        public IReadOnlyList<RepairMethodMaterialProfile> RepairProfiles => repairProfiles;
        public int EnergyCost => energyCost;
        public int RewardCoins => rewardCoins;
        public bool IsSpecialOrder => isSpecialOrder;
        public string SpecialStoryId => specialStoryId;
        public int SpecialStorySequence => specialStorySequence;
        public bool OverridePreferredRepairMethod => overridePreferredRepairMethod;
        public RepairMethodType PreferredRepairMethodType => preferredRepairMethodType;
        public string CustomerNote => customerNote;
        public string FeedbackText => feedbackText;
        public string MomentText => momentText;
        public string TodayStoryText => todayStoryText;
        public string DiaryText => diaryText;
    }
}
