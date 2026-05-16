using APlaceLikeMe.Data;

namespace APlaceLikeMe.Gameplay
{
    public sealed class CompletedOrderRecord
    {
        public CompletedOrderRecord(OrderDefinition order, int dayCompleted)
        {
            Order = order;
            DayCompleted = dayCompleted;
        }

        public OrderDefinition Order { get; }
        public int DayCompleted { get; }
    }
}
