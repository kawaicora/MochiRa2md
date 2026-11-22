// ObjectClasses.cs —— 世界实体的最小运行时骨架（UnitClass / InfantryClass / AircraftClass / BuildingClass / SuperClass）。
//
// 说明：这是「有身份、有类型、有归属、有血量、有位置」的实例层，不是完整的 TechnoClass 行为层。
//   * 每个类都有 static Array（本类全部实例），与 YRpp 的 ABSTRACTTYPE_ARRAY 同构；
//   * 每个实例在构造时把自己登记进 **类型对象自己的 Instances 列表**
//     （用户要求：每个类型对象存自己那批单位），销毁时从类型列表里摘掉；
//   * 行为（移动/开火/建造/超武释放）一律没有，方法标 [未实现]，不假装执行。
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    /// <summary>WorldObjectClass 的公共部分（位置 / 归属 / 血量 / 存活）。</summary>
    public abstract class WorldObjectClass
    {
        public int ArrayIndex;
        public HouseClass Owner;
        public int CellX = -1;
        public int CellY = -1;
        public int Health;
        public int MaxHealth;
        public bool IsAlive = true;

        public string PositionText => CellX >= 0 ? "(" + CellX + "," + CellY + ")" : "(无)";
    }

    public abstract class TechnoObjectClass : WorldObjectClass
    {
        public TechnoTypeClass Type;

        /// <summary>类型表里的 Instances 列表（构造时由子类调用 AttachToType 挂上）。</summary>
        protected void AttachToType()
        {
            if (Type != null && !Type.Instances.Contains(this)) Type.Instances.Add(this);
        }

        protected void DetachFromType()
        {
            if (Type != null) Type.Instances.Remove(this);
        }

        /// <summary>按类型 Strength 起血（原版 TechnoClass::TechnoClass 用 Strength）。</summary>
        protected void InitHealth()
        {
            MaxHealth = Type != null ? Type.Strength : 0;
            Health = MaxHealth;
        }
    }

    // ---------------------------------------------------------------- UnitClass

    public sealed class UnitClass : TechnoObjectClass
    {
        public static readonly List<UnitClass> Array = new List<UnitClass>();

        public string Facing = "N";          // 原版 FacingClass；这里只存朝向名
        public bool IsVoxel => Type != null && Type.Art != null && Type.Art.Voxel;

        public UnitClass(UnitTypeClass type, HouseClass owner, int cellX, int cellY)
        {
            Type = type;
            Owner = owner;
            CellX = cellX;
            CellY = cellY;
            ArrayIndex = Array.Count;
            Array.Add(this);
            AttachToType();
            InitHealth();
        }

        /// <summary>UnitTypeClass::Init：原版开局创建单位走这里；我们是构造 + 登记。</summary>
        public static UnitClass Create(UnitTypeClass type, HouseClass owner, int cellX, int cellY)
        {
            if (type == null) return null;
            return new UnitClass(type, owner, cellX, cellY);
        }

        public void Destroy()
        {
            if (!IsAlive) return;
            IsAlive = false;
            DetachFromType();
            Array.Remove(this);
        }

        public void Move(int cellX, int cellY) { CellX = cellX; CellY = cellY; }   // 只改坐标，寻路 [未实现]
        public void Fire() => BootLog.Log("[实体] UnitClass Fire [未实现 战斗系统]");
    }

    // ---------------------------------------------------------------- InfantryClass

    public sealed class InfantryClass : TechnoObjectClass
    {
        public static readonly List<InfantryClass> Array = new List<InfantryClass>();
        public int SubCell = 0;              // 原版步兵在格子里的 sub-cell（0..4）

        public InfantryClass(InfantryTypeClass type, HouseClass owner, int cellX, int cellY, int subCell = 0)
        {
            Type = type;
            Owner = owner;
            CellX = cellX;
            CellY = cellY;
            SubCell = subCell;
            ArrayIndex = Array.Count;
            Array.Add(this);
            AttachToType();
            InitHealth();
        }

        public void Destroy()
        {
            if (!IsAlive) return;
            IsAlive = false;
            DetachFromType();
            Array.Remove(this);
        }
    }

    // ---------------------------------------------------------------- AircraftClass

    public sealed class AircraftClass : TechnoObjectClass
    {
        public static readonly List<AircraftClass> Array = new List<AircraftClass>();
        public int Altitude;                // 原版 Height

        public AircraftClass(AircraftTypeClass type, HouseClass owner, int cellX, int cellY)
        {
            Type = type;
            Owner = owner;
            CellX = cellX;
            CellY = cellY;
            ArrayIndex = Array.Count;
            Array.Add(this);
            AttachToType();
            InitHealth();
        }

        public void Destroy()
        {
            if (!IsAlive) return;
            IsAlive = false;
            DetachFromType();
            Array.Remove(this);
        }
    }

    // ---------------------------------------------------------------- BuildingClass

    public sealed class BuildingClass : TechnoObjectClass
    {
        public static readonly List<BuildingClass> Array = new List<BuildingClass>();
        public int OccupyWidth = 1;         // 由 art Foundation 推出来（1x1/2x2/3x3…）
        public int OccupyHeight = 1;
        public bool IsOnHold;
        public bool IsPowerOnline;

        public BuildingClass(BuildingTypeClass type, HouseClass owner, int cellX, int cellY)
        {
            Type = type;
            Owner = owner;
            CellX = cellX;
            CellY = cellY;
            ArrayIndex = Array.Count;
            Array.Add(this);
            AttachToType();
            InitHealth();
            ParseFoundation(type);
        }

        private void ParseFoundation(BuildingTypeClass type)
        {
            var f = type != null && type.Art != null ? type.Art.Foundation : null;
            if (string.IsNullOrEmpty(f)) return;
            // art 里 Foundation=2x2 / 3x3 这种；只有一个数字时视为正方形
            var lower = f.ToLowerInvariant();
            var x = lower.IndexOf('x');
            if (x <= 0) return;
            int.TryParse(lower.Substring(0, x), out OccupyWidth);
            int.TryParse(lower.Substring(x + 1), out OccupyHeight);
            if (OccupyWidth <= 0) OccupyWidth = 1;
            if (OccupyHeight <= 0) OccupyHeight = 1;
        }

        public void Destroy()
        {
            if (!IsAlive) return;
            IsAlive = false;
            DetachFromType();
            Array.Remove(this);
        }

        /// <summary>原版 UpdatePower 统计建筑供电；我们只算数，不驱动电网。</summary>
        public int PowerOutput => (Type as BuildingTypeClass)?.Power ?? 0;
        public int PowerDrain => (Type as BuildingTypeClass)?.PowerDrain ?? 0;
        public void Sell() => BootLog.Log("[实体] BuildingClass Sell [未实现 出售流程]");
        public void Repair() => BootLog.Log("[实体] BuildingClass Repair [未实现 修理流程]");
    }

    // ---------------------------------------------------------------- TerrainClass
    // 地图 [Terrain] 段里的“地形对象”（树 TIBTRE / 路灯 LT_SGN / 石头等）。
    // 与 overlay 的区别：overlay 走 [OverlayPack] 的序号表，terrain 走 key=y*1000+x 的段；
    // 两者原版都各自是独立 ObjectClass 实例（YRpp: TerrainClass / OverlayClass），这里各建一个。
    public sealed class TerrainClass
    {
        public static readonly List<TerrainClass> Array = new List<TerrainClass>();
        public int ArrayIndex;
        public string TypeName;
        public string ImageName;
        public int CellX;
        public int CellY;

        public TerrainClass(string typeName, string imageName, int cellX, int cellY)
        {
            TypeName = typeName;
            ImageName = imageName;
            CellX = cellX;
            CellY = cellY;
            ArrayIndex = Array.Count;
            Array.Add(this);
        }
    }

    // ---------------------------------------------------------------- SuperClass

    /// <summary>
    /// SuperClass（超武实例）：一个 House 一份，Charge 来自 SuperWeaponTypeClass::RechargeTime。
    /// 只做计时/就绪判定，释放行为 [未实现]。
    /// </summary>
    public sealed class SuperClass
    {
        public static readonly List<SuperClass> Array = new List<SuperClass>();
        public int ArrayIndex;
        public SuperWeaponTypeClass Type;
        public HouseClass Owner;
        public int Charge;                  // 0..RechargeTime
        public bool IsReady;
        public bool IsActive;               // 原版 Active（正在释放）
        public HouseClass TargetHouse;
        public int TargetCellX = -1, TargetCellY = -1;

        public SuperClass(SuperWeaponTypeClass type, HouseClass owner, bool startReady)
        {
            Type = type;
            Owner = owner;
            ArrayIndex = Array.Count;
            Array.Add(this);
            if (type != null) type.Instances.Add(this);
            if (startReady && type != null)
            {
                Charge = type.RechargeTime;
                IsReady = true;
            }
        }

        public float Progress
        {
            get
            {
                if (Type == null || Type.RechargeTime <= 0) return 1f;
                return Charge >= Type.RechargeTime ? 1f : Charge / (float)Type.RechargeTime;
            }
        }

        /// <summary>每帧充能（原版 SuperClass::AI 同一口径：只加不自动清零）。</summary>
        public void Update()
        {
            if (Type == null || IsReady) return;
            Charge++;
            if (Charge >= Type.RechargeTime) { Charge = Type.RechargeTime; IsReady = true; }
        }

        public bool Launch(int cellX, int cellY)
        {
            if (!IsReady) return false;
            IsActive = true;
            TargetCellX = cellX;
            TargetCellY = cellY;
            Charge = 0;
            IsReady = false;
            BootLog.Log("[超武] " + (Type != null ? Type.ID : "?") + " 在 " + Owner?.ArrayIndex + " 释放于 (" + cellX + "," + cellY + ") [未实现 超武效果]");
            return true;
        }
    }

    /// <summary>全局实体表（原版是各类型自己的 DynamicVectorClass + TechnoClass::Array）。</summary>
    public static class WorldObjects
    {
        public static void Reset()
        {
            UnitClass.Array.Clear();
            InfantryClass.Array.Clear();
            AircraftClass.Array.Clear();
            BuildingClass.Array.Clear();
            SuperClass.Array.Clear();
            TerrainClass.Array.Clear();
            foreach (var t in UnitTypeClass.Array) t.Instances.Clear();
            foreach (var t in InfantryTypeClass.Array) t.Instances.Clear();
            foreach (var t in AircraftTypeClass.Array) t.Instances.Clear();
            foreach (var t in BuildingTypeClass.Array) t.Instances.Clear();
            foreach (var t in SuperWeaponTypeClass.Array) t.Instances.Clear();
        }

        public static string Summary()
        {
            return "单位=" + UnitClass.Array.Count + " 步兵=" + InfantryClass.Array.Count
                + " 飞机=" + AircraftClass.Array.Count + " 建筑=" + BuildingClass.Array.Count
                + " 超武=" + SuperClass.Array.Count + " 地形对象=" + TerrainClass.Array.Count;
        }
    }
}
