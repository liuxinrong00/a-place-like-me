using System.Collections.Generic;
using APlaceLikeMe.Data;
using UnityEngine;

namespace APlaceLikeMe.Gameplay
{
    [CreateAssetMenu(menuName = "A Place Like Me/Prototype/Prototype Game Config", fileName = "PrototypeGameConfig")]
    public sealed class PrototypeGameConfig : ScriptableObject
    {
        [SerializeField] private GameInitialConfig initialConfig;
        [SerializeField] private List<OrderDefinition> orderPool = new();
        [SerializeField] private List<RepairMethodDefinition> repairMethods = new();
        [SerializeField] private int ordersPerDay = 3;
        [SerializeField] private int specialOrdersPerRefreshDay = 2;
        [SerializeField] private int specialOrderRefreshIntervalDays = 2;
        [SerializeField] private int prototypeDays = 0;
        [SerializeField] private int nightSupplyCost = 6;
        [SerializeField] private int nightSupplyAmount = 6;

        public GameInitialConfig InitialConfig => initialConfig;
        public IReadOnlyList<OrderDefinition> OrderPool => orderPool;
        public IReadOnlyList<RepairMethodDefinition> RepairMethods => repairMethods;
        public int OrdersPerDay => ordersPerDay;
        public int SpecialOrdersPerRefreshDay => specialOrdersPerRefreshDay;
        public int SpecialOrderRefreshIntervalDays => specialOrderRefreshIntervalDays;
        public int PrototypeDays => prototypeDays;
        public bool HasPrototypeDayLimit => prototypeDays > 0;
        public int NightSupplyCost => nightSupplyCost;
        public int NightSupplyAmount => nightSupplyAmount;
    }
}
