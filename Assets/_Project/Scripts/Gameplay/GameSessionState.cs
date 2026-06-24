using System.Collections.Generic;
using APlaceLikeMe.Core;
using APlaceLikeMe.Data;

namespace APlaceLikeMe.Gameplay
{
    public sealed class GameSessionState
    {
        private readonly Dictionary<MaterialDefinition, int> materialStock = new();
        private readonly List<OrderDefinition> todaysOrders = new();
        private readonly List<OrderDefinition> acceptedOrders = new();
        private readonly List<CompletedOrderRecord> completedOrders = new();
        private readonly List<string> feedbackLog = new();
        private readonly List<string> momentLog = new();
        private readonly List<string> diaryLog = new();
        private readonly Dictionary<MaterialDefinition, int> todayMaterialConsumption = new();
        private int todayAcceptedSpecialOrderCount;

        public int CurrentDay { get; private set; } = 1;
        public int Coins { get; private set; }
        public int Energy { get; private set; }
        public int Authenticity { get; private set; }
        public int DailyEnergyRecovery { get; private set; }
        public int TodayIncome { get; private set; }
        public int TodayExpenses { get; private set; }
        public int TodayEnergySpent { get; private set; }
        public int TodayAuthenticityDelta { get; private set; }
        public int TodayAcceptedSpecialOrderCount => todayAcceptedSpecialOrderCount;
        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public IReadOnlyList<OrderDefinition> TodaysOrders => todaysOrders;
        public IReadOnlyList<OrderDefinition> AcceptedOrders => acceptedOrders;
        public IReadOnlyList<CompletedOrderRecord> CompletedOrders => completedOrders;
        public IReadOnlyList<string> FeedbackLog => feedbackLog;
        public IReadOnlyList<string> MomentLog => momentLog;
        public IReadOnlyList<string> DiaryLog => diaryLog;
        public IReadOnlyDictionary<MaterialDefinition, int> MaterialStock => materialStock;
        public IReadOnlyDictionary<MaterialDefinition, int> TodayMaterialConsumption => todayMaterialConsumption;

        public void Initialize(GameInitialConfig config)
        {
            CurrentDay = 1;
            Coins = config.InitialCoins;
            Energy = config.InitialEnergy;
            Authenticity = config.InitialAuthenticity;
            DailyEnergyRecovery = config.DailyEnergyRecovery;
            TodayIncome = 0;
            TodayExpenses = 0;
            TodayEnergySpent = 0;
            TodayAuthenticityDelta = 0;
            todayAcceptedSpecialOrderCount = 0;
            Phase = GamePhase.Boot;

            materialStock.Clear();
            todayMaterialConsumption.Clear();
            foreach (var stock in config.InitialMaterials)
            {
                if (stock.material == null || stock.amount <= 0)
                {
                    continue;
                }

                materialStock[stock.material] = stock.amount;
            }

            todaysOrders.Clear();
            acceptedOrders.Clear();
            completedOrders.Clear();
            feedbackLog.Clear();
            momentLog.Clear();
            diaryLog.Clear();
        }

        public void SetPhase(GamePhase phase)
        {
            Phase = phase;
        }

        public void SetTodaysOrders(IEnumerable<OrderDefinition> orders)
        {
            todaysOrders.Clear();
            todaysOrders.AddRange(orders);
            acceptedOrders.Clear();
            TodayIncome = 0;
            TodayExpenses = 0;
            TodayEnergySpent = 0;
            TodayAuthenticityDelta = 0;
            todayAcceptedSpecialOrderCount = 0;
            todayMaterialConsumption.Clear();
        }

        public bool AcceptOrder(OrderDefinition order)
        {
            if (order == null || !todaysOrders.Remove(order))
            {
                return false;
            }

            if (!acceptedOrders.Contains(order))
            {
                acceptedOrders.Add(order);
                if (order.IsSpecialOrder)
                {
                    todayAcceptedSpecialOrderCount++;
                }
            }

            return true;
        }

        public bool HasCompletedSpecialStorySequence(string storyId, int sequence)
        {
            if (string.IsNullOrWhiteSpace(storyId) || sequence <= 0)
            {
                return false;
            }

            foreach (var completedOrder in completedOrders)
            {
                var order = completedOrder.Order;
                if (order != null &&
                    order.IsSpecialOrder &&
                    order.SpecialStoryId == storyId &&
                    order.SpecialStorySequence == sequence)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasMaterial(MaterialDefinition material, int amount)
        {
            return material != null && materialStock.TryGetValue(material, out var currentAmount) && currentAmount >= amount;
        }

        public void AddMaterial(MaterialDefinition material, int amount)
        {
            if (material == null || amount <= 0)
            {
                return;
            }

            materialStock.TryGetValue(material, out var currentAmount);
            materialStock[material] = currentAmount + amount;
        }

        public bool TrySpendMaterial(MaterialDefinition material, int amount)
        {
            if (material == null || amount <= 0)
            {
                return false;
            }

            materialStock.TryGetValue(material, out var currentAmount);
            if (currentAmount < amount)
            {
                return false;
            }

            materialStock[material] = currentAmount - amount;
            todayMaterialConsumption.TryGetValue(material, out var consumedAmount);
            todayMaterialConsumption[material] = consumedAmount + amount;
            return true;
        }

        public void SpendEnergy(int amount)
        {
            Energy -= amount;
            TodayEnergySpent += amount;
        }

        public void AddCoins(int amount)
        {
            Coins += amount;
            TodayIncome += amount;
        }

        public bool TrySpendCoins(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            if (Coins < amount)
            {
                return false;
            }

            SpendCoins(amount);
            return true;
        }

        public void SpendCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Coins -= amount;
            TodayExpenses += amount;
        }

        public void AddAuthenticity(int amount)
        {
            Authenticity += amount;
            TodayAuthenticityDelta += amount;
        }

        public void CompleteOrder(OrderDefinition order, string feedbackText = null, string momentText = null, string diaryText = null)
        {
            todaysOrders.Remove(order);
            acceptedOrders.Remove(order);
            completedOrders.Add(new CompletedOrderRecord(order, CurrentDay));
            var finalFeedbackText = string.IsNullOrWhiteSpace(feedbackText) ? order.FeedbackText : feedbackText;
            if (!string.IsNullOrWhiteSpace(finalFeedbackText))
            {
                feedbackLog.Add(finalFeedbackText);
            }

            var finalMomentText = string.IsNullOrWhiteSpace(momentText) ? order.MomentText : momentText;
            if (!string.IsNullOrWhiteSpace(finalMomentText))
            {
                momentLog.Add(finalMomentText);
            }

            if (!string.IsNullOrWhiteSpace(diaryText))
            {
                diaryLog.Add(diaryText);
            }
        }

        public void StartNextDay(IEnumerable<OrderDefinition> orders)
        {
            CurrentDay++;
            Energy = DailyEnergyRecovery;
            TodayIncome = 0;
            TodayExpenses = 0;
            TodayEnergySpent = 0;
            TodayAuthenticityDelta = 0;
            todayAcceptedSpecialOrderCount = 0;
            todayMaterialConsumption.Clear();
            feedbackLog.Clear();
            momentLog.Clear();
            diaryLog.Clear();
            SetTodaysOrders(orders);
            SetPhase(GamePhase.OrderSelection);
        }
    }
}
