using System;
using APlaceLikeMe.Core;

namespace APlaceLikeMe.Data
{
    [Serializable]
    public sealed class RepairMethodMaterialProfile
    {
        public RepairMethodType repairMethodType;
        public MaterialAmount[] requiredMaterials = Array.Empty<MaterialAmount>();
        public int materialCost;
        public int energyCost = 1;
        public int rewardCoins = 1;
    }
}
