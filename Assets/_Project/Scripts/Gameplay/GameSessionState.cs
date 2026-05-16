using System.Collections.Generic;
using APlaceLikeMe.Core;
using APlaceLikeMe.Data;

namespace APlaceLikeMe.Gameplay
{
    public sealed class GameSessionState
    {
        private readonly Dictionary<MaterialDefinition, int> materialStock = new();
        private readonly List<OrderDefinition> todaysOrders = new();
        private readonly List<CompletedOrderRecord> completedOrders = new();
        private readonly List<string> feedbackLog = new();

        public int CurrentDay { get; private set; } = 1;
        public int Coins { get; private set; }
        public int Energy { get; private set; }
        public int Authenticity { get; private set; }
        public int DailyEnergyRecovery { get; private set; }
        public int TodayIncome { get; private set; }
        public int TodayAuthenticityDelta { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public IReadOnlyList<OrderDefinition> TodaysOrders => todaysOrders;
        public IReadOnlyList<CompletedOrderRecord> CompletedOrders => completedOrders;
        public IReadOnlyList<string> FeedbackLog => feedbackLog;
        public IReadOnlyDictionary<MaterialDefinition, int> MaterialStock => materialStock;

        public void Initialize(GameInitialConfig config)
        {
            CurrentDay = 1;
            Coins = config.InitialCoins;
            Energy = config.InitialEnergy;
            Authenticity = config.InitialAuthenticity;
            DailyEnergyRecovery = config.DailyEnergyRecovery;
            TodayIncome = 0;
            TodayAuthenticityDelta = 0;
            Phase = GamePhase.Boot;

            materialStock.Clear();
            foreach (var stock in config.InitialMaterials)
            {
                if (stock.material == null || stock.amount <= 0)
                {
                    continue;
                }

                materialStock[stock.material] = stock.amount;
            }

            todaysOrders.Clear();
            completedOrders.Clear();
            feedbackLog.Clear();
        }

        public void SetPhase(GamePhase phase)
        {
            Phase = phase;
        }

        public void SetTodaysOrders(IEnumerable<OrderDefinition> orders)
        {
            todaysOrders.Clear();
            todaysOrders.AddRange(orders);
            TodayIncome = 0;
            TodayAuthenticityDelta = 0;
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
            return true;
        }

        public void SpendEnergy(int amount)
        {
            Energy -= amount;
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

            Coins -= amount;
            return true;
        }

        public void AddAuthenticity(int amount)
        {
            Authenticity += amount;
            TodayAuthenticityDelta += amount;
        }

        public void CompleteOrder(OrderDefinition order, string feedbackText = null)
        {
            todaysOrders.Remove(order);
            completedOrders.Add(new CompletedOrderRecord(order, CurrentDay));
            var finalFeedbackText = string.IsNullOrWhiteSpace(feedbackText) ? order.FeedbackText : feedbackText;
            if (!string.IsNullOrWhiteSpace(finalFeedbackText))
            {
                feedbackLog.Add(finalFeedbackText);
            }
        }

        public void StartNextDay(IEnumerable<OrderDefinition> orders)
        {
            CurrentDay++;
            Energy = DailyEnergyRecovery;
            TodayIncome = 0;
            TodayAuthenticityDelta = 0;
            feedbackLog.Clear();
            SetTodaysOrders(orders);
            SetPhase(GamePhase.OrderSelection);
        }
    }
}
