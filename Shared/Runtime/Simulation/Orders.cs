using System.Numerics;

namespace Bloodfall.Simulation
{
    public enum OrderType : byte
    {
        None = 0,
        Move = 1,
        AttackUnit = 2,
        AttackMove = 3,
        Stop = 4,
        Hold = 5,
        CastNoTarget = 6,
        CastUnit = 7,
        CastPoint = 8,
        ToggleAbility = 9,
        LevelAbility = 10,
        BuyItem = 11,
        SellItem = 12,
        SwapItems = 13,
        Follow = 14,
        Patrol = 15,
        Buyback = 16,
        Ping = 17,
        // RTS
        Build = 20,
        Train = 21,
        Research = 22,
        Harvest = 23,
        SetRally = 24,
        ReturnResources = 25,
        CancelQueue = 26,
        /// <summary>Blood War: the player's courier fetches the stash and brings it to their hero (the ` key).</summary>
        CourierDeliver = 30,
    }

    public enum PingKind : byte { Normal, Danger, OnMyWay, Attack, Defend, EnemyMissing, Ward }

    /// <summary>
    /// A command issued by a player (or AI) to one of its units. Orders are the only way clients influence the
    /// simulation, which is what makes the server authoritative: everything else is derived.
    /// </summary>
    public struct Order
    {
        public OrderType Type;
        public int UnitId;
        public int TargetId;
        public Vector2 Point;
        public Vector2 Point2;
        /// <summary>Ability index on the unit, or 100 + inventory slot for item actives.</summary>
        public int Slot;
        /// <summary>Second slot for swaps / item index for purchases.</summary>
        public int Slot2;
        public string ItemId;
        public bool Queue;
        /// <summary>
        /// RTS multi-select: further units that receive the same order (the server checks each one's owner).
        /// Training goes to the selected building with the shortest queue; building and casting use UnitId only.
        /// </summary>
        public int[] Group;
        /// <summary>Tick at which the order was issued (for latency statistics / replay).</summary>
        public int IssuedTick;

        public const int ItemSlotBase = 100;
        /// <summary>Most units one group order can command (UnitId plus Group).</summary>
        public const int MaxGroup = 64;

        public static Order MoveTo(int unit, Vector2 p, bool queue = false) => new Order { Type = OrderType.Move, UnitId = unit, Point = p, Queue = queue };
        public static Order Attack(int unit, int target, bool queue = false) => new Order { Type = OrderType.AttackUnit, UnitId = unit, TargetId = target, Queue = queue };
        public static Order AttackMoveTo(int unit, Vector2 p, bool queue = false) => new Order { Type = OrderType.AttackMove, UnitId = unit, Point = p, Queue = queue };
        public static Order StopOrder(int unit) => new Order { Type = OrderType.Stop, UnitId = unit };
        public static Order HoldOrder(int unit) => new Order { Type = OrderType.Hold, UnitId = unit };
        public static Order CastNoTargetOrder(int unit, int slot, bool queue = false) => new Order { Type = OrderType.CastNoTarget, UnitId = unit, Slot = slot, Queue = queue };
        public static Order CastUnitOrder(int unit, int slot, int target, bool queue = false) => new Order { Type = OrderType.CastUnit, UnitId = unit, Slot = slot, TargetId = target, Queue = queue };
        public static Order CastPointOrder(int unit, int slot, Vector2 p, bool queue = false) => new Order { Type = OrderType.CastPoint, UnitId = unit, Slot = slot, Point = p, Queue = queue };
        public static Order LevelUp(int unit, int slot) => new Order { Type = OrderType.LevelAbility, UnitId = unit, Slot = slot };
        public static Order Buy(int unit, string itemId) => new Order { Type = OrderType.BuyItem, UnitId = unit, ItemId = itemId };
        public static Order Sell(int unit, int slot) => new Order { Type = OrderType.SellItem, UnitId = unit, Slot = slot };
        public static Order Swap(int unit, int a, int b) => new Order { Type = OrderType.SwapItems, UnitId = unit, Slot = a, Slot2 = b };

        public bool IsImmediate => Type == OrderType.LevelAbility || Type == OrderType.BuyItem || Type == OrderType.SellItem
                                   || Type == OrderType.SwapItems || Type == OrderType.Buyback || Type == OrderType.Ping
                                   || Type == OrderType.ToggleAbility || Type == OrderType.SetRally || Type == OrderType.Train
                                   || Type == OrderType.Research || Type == OrderType.CancelQueue;

        public override string ToString() => $"{Type} unit={UnitId} target={TargetId} pt={Point} slot={Slot}{(Queue ? " (queued)" : "")}";
    }
}
