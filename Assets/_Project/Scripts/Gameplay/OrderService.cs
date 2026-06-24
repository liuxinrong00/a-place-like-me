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

            return GetRotatingOrders(orderPool, day, ordersPerDay);
        }

        public IReadOnlyList<OrderDefinition> GetOrdersForDay(
            IReadOnlyList<OrderDefinition> orderPool,
            GameSessionState state,
            int day,
            int ordersPerDay,
            int specialOrdersPerRefreshDay,
            int specialOrderRefreshIntervalDays)
        {
            if (orderPool == null || orderPool.Count == 0)
            {
                return new List<OrderDefinition>();
            }

            var normalOrders = orderPool
                .Where(order => order != null && !order.IsSpecialOrder)
                .ToList();
            var selectedOrders = GetRotatingOrders(normalOrders, day, ordersPerDay).ToList();
            selectedOrders.AddRange(GetSpecialOrdersForDay(orderPool, state, day, specialOrdersPerRefreshDay, specialOrderRefreshIntervalDays));
            return selectedOrders;
        }

        public OrderResult TryAcceptOrder(GameSessionState state, OrderDefinition order)
        {
            return TryAcceptOrder(state, order, int.MaxValue);
        }

        public OrderResult TryAcceptOrder(GameSessionState state, OrderDefinition order, int maxSpecialOrdersPerDay)
        {
            if (order == null)
            {
                return new OrderResult(false, "请选择一个订单。");
            }

            if (order.IsSpecialOrder && state.TodayAcceptedSpecialOrderCount >= maxSpecialOrdersPerDay)
            {
                return new OrderResult(false, $"今天特殊订单最多只能接 {maxSpecialOrdersPerDay} 个。");
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
            var authenticityDelta = GetFinalAuthenticityDelta(order, repairMethod);
            state.SpendEnergy(energyCost);
            state.AddCoins(rewardCoins);
            state.AddAuthenticity(authenticityDelta);
            var feedback = GetMethodFeedback(order, repairMethod, authenticityDelta);
            var diaryEntry = BuildOrderDiaryEntry(order, repairMethod, authenticityDelta);
            state.CompleteOrder(order, order.FeedbackText, order.MomentText, diaryEntry);
            return new OrderResult(true, $"完成订单：{order.DisplayName}\n方式：{repairMethod.DisplayName}\n\n{feedback}");
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

        private static IReadOnlyList<OrderDefinition> GetRotatingOrders(
            IReadOnlyList<OrderDefinition> orders,
            int day,
            int ordersPerDay)
        {
            if (orders == null || orders.Count == 0 || ordersPerDay <= 0)
            {
                return new List<OrderDefinition>();
            }

            var startIndex = ((day - 1) * ordersPerDay) % orders.Count;
            return Enumerable.Range(0, ordersPerDay)
                .Select(offset => orders[(startIndex + offset) % orders.Count])
                .ToList();
        }

        private static IReadOnlyList<OrderDefinition> GetSpecialOrdersForDay(
            IReadOnlyList<OrderDefinition> orderPool,
            GameSessionState state,
            int day,
            int specialOrdersPerRefreshDay,
            int specialOrderRefreshIntervalDays)
        {
            if (state == null || specialOrdersPerRefreshDay <= 0 || !IsSpecialOrderRefreshDay(day, specialOrderRefreshIntervalDays))
            {
                return new List<OrderDefinition>();
            }

            return orderPool
                .Where(order => IsUnlockedSpecialOrder(order, state))
                .OrderBy(order => order.SpecialStoryId)
                .ThenBy(order => order.SpecialStorySequence)
                .Take(specialOrdersPerRefreshDay)
                .ToList();
        }

        private static bool IsSpecialOrderRefreshDay(int day, int specialOrderRefreshIntervalDays)
        {
            var interval = MathfMax(1, specialOrderRefreshIntervalDays);
            return day > 0 && (day - 1) % interval == 0;
        }

        private static bool IsUnlockedSpecialOrder(OrderDefinition order, GameSessionState state)
        {
            if (order == null || !order.IsSpecialOrder || string.IsNullOrWhiteSpace(order.SpecialStoryId))
            {
                return false;
            }

            var sequence = order.SpecialStorySequence;
            if (sequence <= 0 || state.HasCompletedSpecialStorySequence(order.SpecialStoryId, sequence))
            {
                return false;
            }

            return sequence == 1 || state.HasCompletedSpecialStorySequence(order.SpecialStoryId, sequence - 1);
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

            return IsPreferredRepairMethod(order, repairMethod.RepairMethodType) ? 1 : 0;
        }

        private static int GetCustomerPreferenceAuthenticityBonus(OrderDefinition order, RepairMethodDefinition repairMethod)
        {
            if (order.Customer == null)
            {
                return 0;
            }

            return IsPreferredRepairMethod(order, repairMethod.RepairMethodType) ? 1 : -1;
        }

        private static bool IsPreferredRepairMethod(OrderDefinition order, RepairMethodType repairMethodType)
        {
            if (order == null)
            {
                return false;
            }

            if (order.OverridePreferredRepairMethod)
            {
                return order.PreferredRepairMethodType == repairMethodType;
            }

            return order.Customer != null && IsPreferredRepairMethod(order.Customer.CustomerType, repairMethodType);
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
            var preferred = IsPreferredRepairMethod(order, repairMethod.RepairMethodType);
            var preferenceText = preferred ? "顾客很喜欢这个处理方向。" : "顾客接受了结果，但似乎还在想象另一种可能。";
            return string.IsNullOrWhiteSpace(order.FeedbackText)
                ? preferenceText
                : $"{preferenceText}\n{order.FeedbackText}";
        }

        private static string BuildOrderDiaryEntry(OrderDefinition order, RepairMethodDefinition repairMethod, int authenticityDelta)
        {
            if (order == null)
            {
                return string.Empty;
            }

            if (order.IsSpecialOrder)
            {
                var specialDiary = BuildSpecialDiaryEntry(order);
                if (!string.IsNullOrWhiteSpace(specialDiary))
                {
                    return specialDiary;
                }
            }

            return BuildNormalDiaryEntry(order, repairMethod, authenticityDelta);
        }

        private static string BuildSpecialDiaryEntry(OrderDefinition order)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(order.TodayStoryText))
            {
                lines.Add("今日故事");
                lines.Add(order.TodayStoryText.Trim());
            }

            if (!string.IsNullOrWhiteSpace(order.DiaryText))
            {
                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add("夜晚日记");
                lines.Add(order.DiaryText.Trim());
            }

            return string.Join("\n", lines);
        }

        private static string BuildNormalDiaryEntry(OrderDefinition order, RepairMethodDefinition repairMethod, int authenticityDelta)
        {
            var itemName = string.IsNullOrWhiteSpace(order.DisplayName) ? "旧物" : order.DisplayName;
            var methodName = repairMethod == null || string.IsNullOrWhiteSpace(repairMethod.DisplayName)
                ? "修补"
                : repairMethod.DisplayName;
            var orderText = order.Difficulty switch
            {
                OrderDifficulty.Easy => $"今天修的是{itemName}。坏掉的地方很清楚，手一伸过去就知道该从哪里开始。收工具时，桌面只乱了一小块。",
                OrderDifficulty.Normal => $"今天在{itemName}上花的时间比预计久。{methodName}不是最省力的做法，但做到一半时，手上的节奏慢慢稳了下来。",
                OrderDifficulty.Hard => $"今天那件{itemName}把时间拉得很长。细小的缝、旧漆下面的颜色、卡住的地方，都要一点点等它松开。",
                _ => $"今天修了一件旧物。它没有说什么，只是在工作台上安静地变轻了一点。"
            };
            var authenticityText = authenticityDelta switch
            {
                > 0 => "交付前，我没有把所有痕迹都急着藏起来。那一小会儿，店里像是比早上宽了一点。",
                < 0 => "交付前，我还是多看了几次顾客的表情。手机屏幕暗下去以后，那种紧绷才慢慢退开。",
                _ => "交付前后都很平静。也许有些日子就是这样，轻轻过去，却没有白过。"
            };

            return $"夜晚日记\n{orderText}\n\n{authenticityText}";
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
