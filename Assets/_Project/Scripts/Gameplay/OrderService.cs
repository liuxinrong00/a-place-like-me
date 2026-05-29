using System.Collections.Generic;
using System.Linq;
using APlaceLikeMe.Core;
using APlaceLikeMe.Data;

namespace APlaceLikeMe.Gameplay
{
    public sealed class OrderService
    {
        public IReadOnlyList<OrderDefinition> GetOrdersForDay(IReadOnlyList<OrderDefinition> orderPool, int day, int ordersPerDay)
        {
            if (orderPool == null || orderPool.Count == 0)
            {
                return new List<OrderDefinition>();
            }

            var startIndex = ((day - 1) * ordersPerDay) % orderPool.Count;
            return Enumerable.Range(0, ordersPerDay)
                .Select(offset => orderPool[(startIndex + offset) % orderPool.Count])
                .ToList();
        }

        public OrderResult TryAcceptOrder(GameSessionState state, OrderDefinition order)
        {
            if (order == null)
            {
                return new OrderResult(false, "请选择一个订单。");
            }

            if (!state.AcceptOrder(order))
            {
                return new OrderResult(false, "这个订单今天不可接取。");
            }

            return new OrderResult(true, $"已接受订单：{order.DisplayName}");
        }

        public OrderResult TryCompleteOrder(GameSessionState state, OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order == null)
            {
                return new OrderResult(false, "请选择一个订单。");
            }

            if (repairMethod == null)
            {
                return new OrderResult(false, "请选择一种修补方式。");
            }

            if (!state.AcceptedOrders.Contains(order))
            {
                return new OrderResult(false, "请先接受这个订单。");
            }

            var energyCost = GetFinalEnergyCost(order, repairMethod);
            if (state.Energy < energyCost)
            {
                return new OrderResult(false, "能量不足，今天需要留一点力气。");
            }

            var requiredMaterials = MergeRequiredMaterials(order, repairMethod);
            foreach (var requiredMaterial in requiredMaterials)
            {
                if (!state.HasMaterial(requiredMaterial.Key, requiredMaterial.Value))
                {
                    return new OrderResult(false, $"材料不足：{requiredMaterial.Key.DisplayName}");
                }
            }

            foreach (var requiredMaterial in requiredMaterials)
            {
                state.TrySpendMaterial(requiredMaterial.Key, requiredMaterial.Value);
            }

            var rewardCoins = GetFinalRewardCoins(order, repairMethod);
            var materialCost = GetFinalMaterialCost(order, repairMethod);
            var authenticityDelta = GetFinalAuthenticityDelta(order, repairMethod);
            state.SpendEnergy(energyCost);
            state.AddCoins(rewardCoins);
            state.AddAuthenticity(authenticityDelta);
            var feedback = GetMethodFeedback(order, repairMethod, authenticityDelta);
            state.CompleteOrder(order, feedback);
            return new OrderResult(true, $"完成订单：{order.DisplayName}\n方式：{repairMethod.DisplayName}\n收入 +{rewardCoins}，净收益 {rewardCoins - materialCost}，真实度 {FormatSigned(authenticityDelta)}\n{feedback}");
        }

        public OrderResult TryBuyNightSupply(GameSessionState state, MaterialDefinition material, int cost, int amount)
        {
            if (material == null)
            {
                return new OrderResult(false, "请选择一种材料。");
            }

            if (!state.TrySpendCoins(cost))
            {
                return new OrderResult(false, $"金币不足，购买材料需要 {cost}。");
            }

            state.AddMaterial(material, amount);
            return new OrderResult(true, $"购买材料：{material.DisplayName} +{amount}，花费 {cost} 金币");
        }

        public int GetFinalEnergyCost(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order == null || repairMethod == null)
            {
                return 0;
            }

            var profile = GetRepairProfile(order, repairMethod);
            if (profile != null)
            {
                return MathfMax(1, profile.energyCost);
            }

            return MathfMax(1, order.EnergyCost + repairMethod.EnergyModifier);
        }

        public int GetFinalRewardCoins(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order == null || repairMethod == null)
            {
                return 0;
            }

            var profile = GetRepairProfile(order, repairMethod);
            if (profile != null)
            {
                return MathfMax(1, profile.rewardCoins);
            }

            return MathfMax(1, order.RewardCoins + repairMethod.CoinRewardModifier + GetCustomerPreferenceCoinBonus(order, repairMethod));
        }

        public int GetFinalMaterialCost(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order == null || repairMethod == null)
            {
                return 0;
            }

            var profile = GetRepairProfile(order, repairMethod);
            if (profile != null)
            {
                return profile.materialCost;
            }

            return MergeRequiredMaterials(order, repairMethod)
                .Sum(material => material.Key.DefaultPrice * material.Value);
        }

        public int GetFinalAuthenticityDelta(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order == null || repairMethod == null)
            {
                return 0;
            }

            return repairMethod.AuthenticityModifier + GetCustomerPreferenceAuthenticityBonus(order, repairMethod);
        }

        public IReadOnlyDictionary<MaterialDefinition, int> GetRequiredMaterials(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            return MergeRequiredMaterials(order, repairMethod);
        }

        private static RepairMethodMaterialProfile GetRepairProfile(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order == null || repairMethod == null)
            {
                return null;
            }

            return order.RepairProfiles.FirstOrDefault(profile => profile != null && profile.repairMethodType == repairMethod.RepairMethodType);
        }

        private static Dictionary<MaterialDefinition, int> MergeRequiredMaterials(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            var requiredMaterials = new Dictionary<MaterialDefinition, int>();
            var profile = GetRepairProfile(order, repairMethod);
            var sourceMaterials = profile?.requiredMaterials ?? order.RequiredMaterials;
            foreach (var requiredMaterial in sourceMaterials)
            {
                if (requiredMaterial.material == null || requiredMaterial.amount <= 0)
                {
                    continue;
                }

                requiredMaterials.TryGetValue(requiredMaterial.material, out var currentAmount);
                requiredMaterials[requiredMaterial.material] = currentAmount + requiredMaterial.amount;
            }

            return requiredMaterials;
        }

        private static int GetCustomerPreferenceCoinBonus(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order.Customer == null)
            {
                return 0;
            }

            return IsPreferredRepairMethod(order.Customer.CustomerType, repairMethod.RepairMethodType) ? 1 : 0;
        }

        private static int GetCustomerPreferenceAuthenticityBonus(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order.Customer == null)
            {
                return 0;
            }

            return IsPreferredRepairMethod(order.Customer.CustomerType, repairMethod.RepairMethodType) ? 1 : -1;
        }

        private static bool IsPreferredRepairMethod(CustomerType customerType, RepairMethodType repairMethodType)
        {
            return customerType switch
            {
                CustomerType.Gentle => repairMethodType == RepairMethodType.PreserveTrace,
                CustomerType.Demanding => repairMethodType == RepairMethodType.PerfectRestore,
                CustomerType.Makeover => repairMethodType == RepairMethodType.CreativeRemake,
                _ => false
            };
        }

        private static string GetMethodFeedback(OrderDefinition order, RepairMethodDefinition repairMethod, int authenticityDelta)
        {
            var preferred = order.Customer != null && IsPreferredRepairMethod(order.Customer.CustomerType, repairMethod.RepairMethodType);
            var preferenceText = preferred ? "顾客很喜欢这个处理方向。" : "顾客接受了结果，但似乎还在想象另一种可能。";
            return $"{preferenceText}\n{order.FeedbackText}\n真实感变化：{FormatSigned(authenticityDelta)}";
        }

        private static string FormatSigned(int value)
        {
            return value >= 0 ? $"+{value}" : value.ToString();
        }

        private static int MathfMax(int left, int right)
        {
            return left > right ? left : right;
        }
    }
}
