// ProductionController.cs —— 玩家生产队列与可建造条目（建造栏数据源）。
//
// 口径对照参考 production 模块：
//   · 分类 Building / Defense / Infantry / Vehicle / Aircraft / Ship；
//   · 条目 BuildOption（造价、可否建造、不可用原因）；队列条目带状态与进度；
//   · 造好的建筑进待放置列表；机动单位在集结点附近出图。
// 说明：真实 BuildTime / 工厂归属判定（Factory 槽位）随模拟层继续补；这里先用造价估时长、
//       前置条件按「己方已拥有的建筑 ID」粗判，不伪造规则里尚未解析的字段。
using System;
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    /// <summary>建造栏分类（与参考 ProductionCategory 同序）。</summary>
    public enum ProdCategory
    {
        Building = 0,
        Defense = 1,
        Infantry = 2,
        Vehicle = 3,
        Aircraft = 4,
        Ship = 5,
    }

    /// <summary>不可用原因（对应参考 BuildDisabledReason）。</summary>
    public enum ProdBlockReason
    {
        None,
        NoFactory,      // 缺少所需前置建筑（灰/移出条目）
        CannotBuild,    // 国家/科技等级不可造（永久移出条目）
        AtLimit,        // 到数量上限
    }

    /// <summary>一条可建造条目（建造栏 cameo 的数据源）。</summary>
    public sealed class ProdOption
    {
        public TechnoTypeClass Type;
        public ProdCategory Category;
        public int Cost;
        public bool Enabled;
        public ProdBlockReason Reason;
        public string Label => Type != null ? Type.UIName : "?";
    }

    /// <summary>队列里的一个生产项。</summary>
    public sealed class ProdItem
    {
        public TechnoTypeClass Type;
        public ProdCategory Category;
        public double Progress;     // 0..1
        public double Duration;
        public bool Done;
    }

    public static class ProductionController
    {
        public static readonly List<ProdItem> Queue = new List<ProdItem>();
        public static ProdCategory ActiveTab = ProdCategory.Building;
        /// <summary>造好、等待放置的建筑（对应参考 ReadyBuildingView）。</summary>
        public static readonly List<BuildingTypeClass> ReadyBuildings = new List<BuildingTypeClass>();

        private const double MinBuildSeconds = 2.0;

        // ---- 分类归属 ----

        public static ProdCategory CategoryOf(TechnoTypeClass type)
        {
            if (type is BuildingTypeClass)
            {
                return string.Equals(type.Category, "Combat", StringComparison.OrdinalIgnoreCase)
                    ? ProdCategory.Defense
                    : ProdCategory.Building;
            }
            if (type is InfantryTypeClass) return ProdCategory.Infantry;
            if (type is AircraftTypeClass) return ProdCategory.Aircraft;
            return ProdCategory.Vehicle;
        }

        // ---- 可建造判定 ----

        private static bool HasPrerequisite(HouseClass house, TechnoTypeClass type)
        {
            if (type.Prerequisite == null || type.Prerequisite.Count == 0) return true;
            for (var i = 0; i < BuildingClass.Array.Count; i++)
            {
                var b = BuildingClass.Array[i];
                if (b == null || !b.IsAlive || b.Owner != house || b.Type == null) continue;
                for (var j = 0; j < type.Prerequisite.Count; j++)
                {
                    if (string.Equals(b.Type.ID, type.Prerequisite[j], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public static ProdBlockReason CanBuild(HouseClass house, TechnoTypeClass type, out bool buildable)
        {
            buildable = false;
            if (house == null || type == null) return ProdBlockReason.CannotBuild;
            if (type.Cost <= 0) return ProdBlockReason.CannotBuild;
            if (!type.OwnedBy(house.GetId())) return ProdBlockReason.CannotBuild;
            if (type.TechLevel >= 0 && house.TechLevel > 0 && type.TechLevel > house.TechLevel)
                return ProdBlockReason.CannotBuild;
            if (!HasPrerequisite(house, type)) return ProdBlockReason.NoFactory;
            buildable = true;
            return ProdBlockReason.None;
        }

        /// <summary>某分类下应在建造栏显示的条目（参考 visible_in_sidebar：永久不可造的移出）。</summary>
        public static List<ProdOption> OptionsFor(HouseClass house, ProdCategory category)
        {
            var result = new List<ProdOption>();
            AddOptions(house, UnitTypeClass.Array, category, ProdCategory.Vehicle, result);
            AddOptions(house, InfantryTypeClass.Array, category, ProdCategory.Infantry, result);
            AddOptions(house, AircraftTypeClass.Array, category, ProdCategory.Aircraft, result);
            AddOptions(house, BuildingTypeClass.Array, category, ProdCategory.Building, result);
            // Defense 与 Building 同表，分类时再筛
            if (category == ProdCategory.Defense)
            {
                AddDefense(house, result);
            }
            return result;
        }

        private static void AddOptions<T>(HouseClass house, List<T> list, ProdCategory want,
            ProdCategory own, List<ProdOption> result) where T : TechnoTypeClass
        {
            if (want != own) return;
            for (var i = 0; i < list.Count; i++)
            {
                var t = list[i];
                var reason = CanBuild(house, t, out var buildable);
                if (reason == ProdBlockReason.CannotBuild) continue; // 永久不可造，移出条目
                result.Add(new ProdOption
                {
                    Type = t,
                    Category = own,
                    Cost = t.Cost,
                    Enabled = buildable,
                    Reason = reason,
                });
            }
        }

        private static void AddDefense(HouseClass house, List<ProdOption> result)
        {
            for (var i = 0; i < BuildingTypeClass.Array.Count; i++)
            {
                var t = BuildingTypeClass.Array[i];
                if (!string.Equals(t.Category, "Combat", StringComparison.OrdinalIgnoreCase)) continue;
                var reason = CanBuild(house, t, out var buildable);
                if (reason == ProdBlockReason.CannotBuild) continue;
                result.Add(new ProdOption
                {
                    Type = t,
                    Category = ProdCategory.Defense,
                    Cost = t.Cost,
                    Enabled = buildable,
                    Reason = reason,
                });
            }
        }

        // ---- 入队 / 推进 / 出图 ----

        public static bool Enqueue(HouseClass house, TechnoTypeClass type)
        {
            if (house == null || type == null) return false;
            var reason = CanBuild(house, type, out var buildable);
            if (!buildable)
            {
                BootLog.Log("[生产] 不可造 " + type.ID + " 原因=" + reason);
                return false;
            }
            if (house.Balance < type.Cost)
            {
                BootLog.Log("[生产] 资金不足 " + type.ID);
                return false;
            }

            house.Balance -= type.Cost;
            var duration = Math.Max(MinBuildSeconds, type.Cost / 100.0 * 1.5);
            Queue.Add(new ProdItem
            {
                Type = type,
                Category = CategoryOf(type),
                Progress = 0.0,
                Duration = duration,
            });
            BootLog.Log("[生产] 入队 " + type.UIName + " 造价=" + type.Cost);
            return true;
        }

        public static void Tick(float dt)
        {
            if (Queue.Count == 0) return;
            var head = Queue[0];
            if (head.Done) { Queue.RemoveAt(0); return; }

            var house = SkirmishMatch.Current != null ? SkirmishMatch.Current.PlayerHouseClass : null;
            // 资金卡住时冻结进度（参考：资金不足保留当前速率并停表）。
            if (house == null) return;

            head.Progress += dt / Math.Max(0.01, head.Duration);
            if (head.Progress >= 1.0)
            {
                head.Done = true;
                Produce(house, head);
                Queue.RemoveAt(0);
            }
        }

        private static void Produce(HouseClass house, ProdItem item)
        {
            var sx = house.StartingCellX >= 0 ? house.StartingCellX + 1 : 0;
            var sy = house.StartingCellY >= 0 ? house.StartingCellY : 0;

            if (item.Type is BuildingTypeClass buildingType)
            {
                ReadyBuildings.Add(buildingType);
                BootLog.Log("[生产] 建筑就绪待放置 " + buildingType.UIName);
                return;
            }

            if (item.Type is UnitTypeClass unitType)
            {
                var unit = UnitClass.Create(unitType, house, sx, sy);
                if (MapObjectLayer.Instance != null) MapObjectLayer.Instance.SpawnUnit(unit);
                BootLog.Log("[生产] 车辆出图 " + unitType.UIName + " @(" + sx + "," + sy + ")");
                return;
            }

            if (item.Type is InfantryTypeClass infantryType)
            {
                // 步兵视图分层随对象层补；实体先登记。
                new InfantryClass(infantryType, house, sx, sy);
                BootLog.Log("[生产] 步兵登记 " + infantryType.UIName);
                return;
            }

            if (item.Type is AircraftTypeClass aircraftType)
            {
                new AircraftClass(aircraftType, house, sx, sy);
                BootLog.Log("[生产] 飞机登记 " + aircraftType.UIName);
            }
        }

        // ---- 变卖 / 维修（作用于当前选中建筑）----

        public static void SellBuilding(BuildingClass building)
        {
            if (building == null || !building.IsAlive) return;
            var refund = building.Type != null ? building.Type.Cost / 2 : 0;
            if (building.Owner != null) building.Owner.Balance += refund;
            BootLog.Log("[生产] 变卖 " + (building.Type != null ? building.Type.UIName : "?") + " 返还 $" + refund);
            building.Destroy();
        }

        public static void RepairBuilding(BuildingClass building)
        {
            if (building == null || !building.IsAlive) return;
            if (building.Health >= building.MaxHealth) return;
            var missing = building.MaxHealth - building.Health;
            var cost = Math.Max(1, missing / 2);
            if (building.Owner == null || building.Owner.Balance < cost)
            {
                BootLog.Log("[生产] 维修资金不足");
                return;
            }
            building.Owner.Balance -= cost;
            building.Health = building.MaxHealth;
            BootLog.Log("[生产] 维修完成 " + (building.Type != null ? building.Type.UIName : "?"));
        }

        public static void Clear()
        {
            Queue.Clear();
            ReadyBuildings.Clear();
            ActiveTab = ProdCategory.Building;
        }
    }
}
