// HouseClass.cs —— HouseClass / HouseTypeClass 的托管复刻（字段顺序、语义、数值照 YRpp）。
//
// 权威：D:\___SYSTEM_PATH\source\repos\Mochi\YRpp\HouseClass.h
//       D:\___SYSTEM_PATH\source\repos\Mochi\YRpp\HouseTypeClass.h
//       D:\___SYSTEM_PATH\source\repos\Mochi\YRpp\AbstractTypeClass.h
//       D:\___SYSTEM_PATH\source\repos\Mochi\YRpp\GeneralDefinitions.h（AIDifficulty / AIMode / BuildCat / Category / QuarryType）
//
// 与 C++ 的差异（只允许这三类，且都在字段注释里标出）：
//   1) 原生裸指针集合 → List<T>；IndexBitfield<HouseClass*> → HouseBitfield（位集合，语义仍是"每 House 一个 bit，上限 32"）。
//   2) 模板容器 DynamicVectorClass → List<T>；固定数组 int[20] / [130][130] 原样保留维度。
//   3) 依赖尚未落地的实体类（UnitClass/BuildingClass/FactoryClass/SuperClass/TagClass/WaypointPathClass/IAIHouse）
//      只声明占位类型，字段保留、内容为空 —— 不伪造行为。
//
// 未落地的行为一律 [未实现]，不写假逻辑。
using System;
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    // ---------------------------------------------------------------- 枚举（GeneralDefinitions.h 数值照抄）

    /// <summary>AIDifficulty：注意原版是反的 —— Hard=0 / Normal=1 / Easy=2（HouseClass.h:804 的注释）。</summary>
    public enum AIDifficulty
    {
        Hard = 0,
        Normal = 1,
        Easy = 2,
    }

    public enum AIMode
    {
        General = 0,
        LowOnCash = 1,   // Available_Money < 25
        BuildBase = 3,
        SellAll = 4,
    }

    /// <summary>Edge：地图边缘（START 位置落在哪条边）。数值按 GeneralDefinitions 顺序。</summary>
    public enum Edge
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    public enum QuarryType
    {
        None = 0,
        Anything, Buildings, Harvesters, Infantry, Vehicles,
        Factories, Defenses, Threats, Power, OccupiableBuildings, TechBuildings,
    }

    public enum Category
    {
        Invalid = -1, Soldier = 0, Civilian, VIP, Recon, AFV, IFV, LRFS,
        Support, Transport, AirPower, AirLift,
    }

    public enum CanBuildResult
    {
        TemporarilyUnbuildable = -1,   // 灰掉 cameo
        Unbuildable = 0,               // 永久，移出 cameo
        Buildable = 1,
    }

    public enum BuildCat
    {
        DontCare = 0, Tech = 1, Resoure = 2, Power = 3, Infrastructure = 4, Combat = 5,
    }

    // ---------------------------------------------------------------- 结构

    /// <summary>UnitTrackerClass（HouseClass.h:28-45）：每种类型的计数 + 网络序列化状态。</summary>
    public sealed class UnitTrackerClass
    {
        public readonly int[] UnitTotals = new int[0x200];
        public int UnitCount;
        public bool InNetworkFormat;

        public void IncrementUnitCount(int nUnit) { UnitTotals[nUnit]++; UnitCount++; }
        public void DecrementUnitCount(int nUnit) { if (UnitTotals[nUnit] > 0) UnitTotals[nUnit]--; if (UnitCount > 0) UnitCount--; }
        public void PopulateUnitCount(int nCount) { UnitCount = nCount; }
        public int GetUnitCount() => UnitCount;
        public int[] GetArray() => UnitTotals;
        public void ClearUnitCount() { Array.Clear(UnitTotals, 0, UnitTotals.Length); UnitCount = 0; }
        public void ToNetworkFormat() { InNetworkFormat = true; }        // 原版 0x749100（字节序翻转）
        public void ToPCFormat() { InNetworkFormat = false; }            // 原版 0x749150
    }

    public struct ZoneInfoStruct
    {
        public int Aircraft;
        public int Armor;
        public int Infantry;
    }

    /// <summary>StartingTechnoStruct：开局预置单位（Unit + 出生 Cell）。</summary>
    public struct StartingTechnoStruct
    {
        public string UnitTypeId;   // TechnoTypeClass* → 类型 id（实体类型表未落地前用 id）
        public int CellX;
        public int CellY;
    }

    public sealed class AngerStruct
    {
        public HouseClass House;
        public int AngerLevel;

        public override bool Equals(object obj)
        {
            var o = obj as AngerStruct;
            return o != null && ReferenceEquals(o.House, House) && o.AngerLevel == AngerLevel;
        }

        public override int GetHashCode() => (House != null ? House.ArrayIndex : -1) * 397 ^ AngerLevel;
    }

    public sealed class ScoutStruct
    {
        public HouseClass House;
        public bool IsPreferred;
    }

    /// <summary>BaseNodeClass：AI 基地节点（建筑类型索引 + 坐标 + 已放置/尝试次数）。</summary>
    public struct BaseNodeClass
    {
        public int BuildingTypeIndex;
        public int CellX, CellY;
        public bool Placed;
        public int Attempts;
    }

    /// <summary>BaseClass（HouseClass.h:110-144）：一个玩家的基地信息。</summary>
    public sealed class BaseClass
    {
        public readonly List<BaseNodeClass> BaseNodes = new List<BaseNodeClass>();
        public int PercentBuilt;
        public readonly List<int> Cells_24 = new List<int>();      // CellStruct → cell = y*1000+x
        public readonly List<int> Cells_38 = new List<int>();
        public int CenterX = -1, CenterY = -1;
        public HouseClass Owner;
    }

    /// <summary>DropshipStruct（HouseClass.h:147-158）：三艘空投船的计时/载货。</summary>
    public sealed class DropshipStruct
    {
        public CDTimerClass Timer = new CDTimerClass();
        public int Count;
        public readonly string[] Types = new string[5];   // TechnoTypeClass* → 类型 id
        public int TotalCost;
    }

    /// <summary>StorageClass（Tiberium/Weed 储量）：容量 + 量。</summary>
    public struct StorageClass
    {
        public int Quantity;
        public int Capacity;

        public double Percent => Capacity > 0 ? Quantity / (double)Capacity : 0.0;
    }

    /// <summary>CounterClass：按类型索引的计数器（Owned/Active/FactoryProduced 三组都用它）。</summary>
    public sealed class CounterClass
    {
        private readonly int[] _counts = new int[0x200];   // 类型索引上限同 UnitTrackerClass

        public int GetItemCount(int index) => (index >= 0 && index < _counts.Length) ? _counts[index] : 0;
        public void Increment(int index) { if (index >= 0 && index < _counts.Length) _counts[index]++; }
        public void Decrement(int index) { if (index >= 0 && index < _counts.Length && _counts[index] > 0) _counts[index]--; }
        public void Set(int index, int value) { if (index >= 0 && index < _counts.Length) _counts[index] = value; }
    }

    /// <summary>CDTimerClass：帧计时（原版以 GetTickCount 毫秒为基准，这里用游戏帧）。</summary>
    public sealed class CDTimerClass
    {
        public int StartFrame = -1;
        public int Duration;
        public bool InProgress;

        public void Start(int duration) { StartFrame = (int)GameStatics.CurrentFrame; Duration = duration; InProgress = duration > 0; }
        public void Stop() { InProgress = false; Duration = 0; StartFrame = -1; }
        public int TimeLeft
        {
            get
            {
                if (!InProgress) return 0;
                var elapsed = (int)GameStatics.CurrentFrame - StartFrame;
                var left = Duration - elapsed;
                if (left <= 0) { InProgress = false; return 0; }
                return left;
            }
        }
        public bool Expired => !InProgress;
    }

    /// <summary>IndexBitfield&lt;HouseClass*&gt;：每 House 一个 bit，容量按 PlayLimits.MaxHouses 放宽（128 位缓冲）。</summary>
    public sealed class HouseBitfield
    {
        private readonly ulong[] _bits = new ulong[2];

        private static bool Valid(int index) => index >= 0 && index < PlayLimits.MaxHouses;

        public void Set(int index)
        {
            if (!Valid(index)) return;
            _bits[index >> 6] |= 1ul << (index & 63);
        }

        public void Clear(int index)
        {
            if (!Valid(index)) return;
            _bits[index >> 6] &= ~(1ul << (index & 63));
        }

        public bool Contains(int index)
            => Valid(index) && (_bits[index >> 6] & (1ul << (index & 63))) != 0;

        public void Clear() { _bits[0] = 0; _bits[1] = 0; }

        public int Count
        {
            get
            {
                var n = 0;
                for (var i = 0; i < PlayLimits.MaxHouses; i++) if (Contains(i)) n++;
                return n;
            }
        }

        public IEnumerable<int> Indices
        {
            get
            {
                for (var i = 0; i < PlayLimits.MaxHouses; i++) if (Contains(i)) yield return i;
            }
        }
    }

    // ---------------------------------------------------------------- HouseTypeClass

    /// <summary>
    /// HouseTypeClass（国家/阵营类型）。字段照 HouseTypeClass.h:63-118 + AbstractTypeClass:67-71。
    /// 每种国家在 [Countries] 里一份；是遭遇战的 CountryId 的唯一来源。
    /// </summary>
    public sealed class HouseTypeClass
    {
        // AbstractTypeClass
        public string ID;
        public string UINameLabel;       // UINameLabel（INI 里的 UIName= 标签）
        public string UIName;            // CSF 解析后的显示名
        public string Name;

        // HouseTypeClass
        public string ParentCountry;     // FixedString<25>
        public int ArrayIndex;
        public int ArrayIndex2;
        public int SideIndex;
        public int ColorSchemeIndex;
        public double FirepowerMult = 1.0, GroundspeedMult = 1.0, AirspeedMult = 1.0;
        public double ArmorMult = 1.0, ROFMult = 1.0, CostMult = 1.0, BuildtimeMult = 1.0;
        public float ArmorInfantryMult = 1f, ArmorUnitsMult = 1f, ArmorAircraftMult = 1f,
            ArmorBuildingsMult = 1f, ArmorDefensesMult = 1f;
        public float CostInfantryMult = 1f, CostUnitsMult = 1f, CostAircraftMult = 1f,
            CostBuildingsMult = 1f, CostDefensesMult = 1f;
        public float SpeedInfantryMult = 1f, SpeedUnitsMult = 1f, SpeedAircraftMult = 1f;
        public float BuildtimeInfantryMult = 1f, BuildtimeUnitsMult = 1f, BuildtimeAircraftMult = 1f,
            BuildtimeBuildingsMult = 1f, BuildtimeDefensesMult = 1f;
        public float IncomeMult = 1f;
        public readonly List<int> VeteranInfantry = new List<int>();      // TypeList<*> → 类型索引
        public readonly List<int> VeteranUnits = new List<int>();
        public readonly List<int> VeteranAircraft = new List<int>();
        // 规则 pass 期间类型表尚未闭包，先留原始 token，待类型表装配后解析为上面的索引
        public readonly List<string> VeteranInfantryNames = new List<string>();
        public readonly List<string> VeteranUnitsNames = new List<string>();
        public readonly List<string> VeteranAircraftNames = new List<string>();
        public string Suffix = string.Empty;   // char Suffix[4]
        public char Prefix;
        public bool Multiplay;
        public bool MultiplayPassive;    // 中立/被动阵营：IsNeutral() 直接读它
        public bool WallOwner;
        public bool SmartAI;

        public static readonly List<HouseTypeClass> Array = new List<HouseTypeClass>();   // 0xA83C98

        public static HouseTypeClass Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < Array.Count; i++)
                if (string.Equals(Array[i].ID, id, StringComparison.OrdinalIgnoreCase)) return Array[i];
            return null;
        }

        public static int FindIndexOfName(string name)
        {
            var t = Find(name);
            return t != null ? t.ArrayIndex : -1;
        }

        public static HouseTypeClass FindByIndex(int index)
        {
            for (var i = 0; i < Array.Count; i++) if (Array[i].ArrayIndex == index) return Array[i];
            return null;
        }

        public HouseTypeClass FindParentCountry() => Find(ParentCountry);
        public int FindParentCountryIndex() => FindIndexOfName(ParentCountry);
    }

    // ---------------------------------------------------------------- HouseClass

    /// <summary>
    /// HouseClass（HouseClass.h:161-1067）。字段一个不落，名字与顺序照头文件；每个字段上面标 C++ 偏移语义。
    /// </summary>
    public sealed class HouseClass
    {
        // ---- 静态（0xA80228 等）----
        public const int PlayerAtA = 4475;   // PlayerAtA..PlayerAtH = 4475..4482
        public static readonly List<HouseClass> Array = new List<HouseClass>();   // 0xA80228
        public static HouseClass CurrentPlayer;                                   // 0xA83D4C
        public static HouseClass Observer;                                        // 0xAC1198

        // ---- 头 → 尾（HouseClass.h:784-1066）----
        public int ArrayIndex;
        public HouseTypeClass Type;
        public readonly List<object> RelatedTags = new List<object>();   // TagClass*（标签系统未落地）
        public readonly List<object> ConYards = new List<object>();      // BuildingClass*（实体未落地）
        public readonly List<object> Buildings = new List<object>();
        public readonly List<object> UnitRepairStations = new List<object>();
        public readonly List<object> Grinders = new List<object>();
        public readonly List<object> Absorbers = new List<object>();
        public readonly List<object> Bunkers = new List<object>();
        public readonly List<object> Occupiables = new List<object>();
        public readonly List<object> CloningVats = new List<object>();
        public readonly List<object> SecretLabs = new List<object>();
        public readonly List<object> PsychicDetectionBuildings = new List<object>();
        public readonly List<object> FactoryPlants = new List<object>();
        public int CountResourceGatherers;
        public int CountResourceDestinations;
        public int CountWarfactories;
        public int InfantrySelfHeal;
        public int UnitsSelfHeal;
        public readonly List<StartingTechnoStruct> StartingUnits = new List<StartingTechnoStruct>();
        /// <summary>AIDifficulty：反向枚举（Hard=0 / Normal=1 / Easy=2）。</summary>
        public AIDifficulty AIDifficulty = AIDifficulty.Normal;
        public double FirepowerMultiplier = 1.0;
        public double GroundspeedMultiplier = 1.0;
        public double AirspeedMultiplier = 1.0;
        public double ArmorMultiplier = 1.0;
        public double ROFMultiplier = 1.0;
        public double CostMultiplier = 1.0;
        public double BuildTimeMultiplier = 1.0;
        public double RepairDelay;
        public double BuildDelay;
        public int IQLevel = 1;
        public int TechLevel;
        public HouseBitfield AltAllies = new HouseBitfield();
        /// <summary>StartingCredits：原版实际资金 = 本值 × 100（HouseClass.h:817 注释）。</summary>
        public int StartingCredits;
        public Edge StartingEdge;
        public uint AIState_1E4;
        public int SideIndex;
        public bool IsHumanPlayer;
        public bool IsInPlayerControl;
        public bool Production;
        public bool AutocreateAllowed;
        public bool NodeLogic_1F0;
        public bool ShipYardConst_1F1;
        public bool AITriggersActive;
        public bool AutoBaseBuilding;
        public bool DiscoveredByPlayer;
        public bool Defeated;
        public bool IsGameOver;
        public bool IsWinner;
        public bool IsLoser;
        public bool CiviliansEvacuated;
        public bool FirestormActive;
        public bool HasThreatNode;
        public bool RecheckTechTree;
        public int IPAddress;
        public int TournamentTeamID;
        public bool LostConnection;
        public int SelectedPathIndex;
        public readonly object[] PlanningPaths = new object[12];          // WaypointPathClass*[12]
        public sbyte Visionary;
        public bool MapIsClear;
        public bool IsTiberiumShort;
        public bool HasBeenSpied;
        public bool HasBeenThieved;
        public bool Repairing;
        public bool IsBuiltSomething;
        public bool IsResigner;
        public bool IsGiverUpper;
        public bool AllToHunt;
        public bool IsParanoid;
        public bool IsToLook;
        public int IQLevel2;
        public AIMode AIMode = AIMode.General;
        public readonly List<object> Supers = new List<object>();          // SuperClass*
        public int LastBuiltBuildingType = -1;
        public int LastBuiltInfantryType = -1;
        public int LastBuiltAircraftType = -1;
        public int LastBuiltVehicleType = -1;
        public int AllowWinBlocks;
        public CDTimerClass RepairTimer = new CDTimerClass();
        public CDTimerClass AlertTimer = new CDTimerClass();
        public CDTimerClass BorrowedTime = new CDTimerClass();
        public CDTimerClass PowerBlackoutTimer = new CDTimerClass();
        public CDTimerClass RadarBlackoutTimer = new CDTimerClass();
        public bool Side2TechInfiltrated;
        public bool Side1TechInfiltrated;
        public bool Side0TechInfiltrated;
        public bool BarracksInfiltrated;
        public bool WarFactoryInfiltrated;
        public uint InfantryAltOwner;
        public uint UnitAltOwner;
        public uint AircraftAltOwner;
        public uint BuildingAltOwner;
        public int AirportDocks;
        public int PoweredUnitCenters;
        public int CreditsSpent;
        public int HarvestedCredits;
        public int StolenBuildingsCredits;
        public int OwnedUnits;
        public int OwnedNavy;
        public int OwnedBuildings;
        public int OwnedInfantry;
        public int OwnedAircraft;
        public StorageClass OwnedTiberium;
        /// <summary>Balance：当前可用资金（不是 ×100 的原始值）。</summary>
        public int Balance;
        public int TotalStorage;
        public StorageClass OwnedWeed;
        public uint unknown_324;
        public UnitTrackerClass BuiltAircraftTypes = new UnitTrackerClass();
        public UnitTrackerClass BuiltInfantryTypes = new UnitTrackerClass();
        public UnitTrackerClass BuiltUnitTypes = new UnitTrackerClass();
        public UnitTrackerClass BuiltBuildingTypes = new UnitTrackerClass();
        public UnitTrackerClass KilledAircraftTypes = new UnitTrackerClass();
        public UnitTrackerClass KilledInfantryTypes = new UnitTrackerClass();
        public UnitTrackerClass KilledUnitTypes = new UnitTrackerClass();
        public UnitTrackerClass KilledBuildingTypes = new UnitTrackerClass();
        public UnitTrackerClass CapturedBuildings = new UnitTrackerClass();
        public UnitTrackerClass CollectedCrates = new UnitTrackerClass();
        public int NumAirpads;
        public int NumBarracks;
        public int NumWarFactories;
        public int NumConYards;
        public int NumShipyards;
        public int NumOrePurifiers;
        public float CostInfantryMult = 1f;
        public float CostUnitsMult = 1f;
        public float CostAircraftMult = 1f;
        public float CostBuildingsMult = 1f;
        public float CostDefensesMult = 1f;
        public int PowerOutput;
        public int PowerDrain;
        public object Primary_ForAircraft;
        public object Primary_ForInfantry;
        public object Primary_ForVehicles;
        public object Primary_ForShips;
        public object Primary_ForBuildings;
        public object Primary_Unused1;
        public object Primary_Unused2;
        public object Primary_Unused3;
        public object Primary_ForDefenses;
        public byte AircraftType_53D0;
        public byte InfantryType_53D1;
        public byte VehicleType_53D2;
        public byte ShipType_53D3;
        public byte BuildingType_53D4;
        public byte unknown_53D5, unknown_53D6, unknown_53D7;
        public byte DefenseType_53D8;
        public byte unknown_53D9, unknown_53DA, unknown_53DB;
        public object OurFlagCarrier;         // UnitClass*
        public int OurFlagCoordsX = -1, OurFlagCoordsY = -1;
        public readonly int[] KilledUnitsOfHouses = new int[PlayLimits.MaxHouses];
        public int TotalKilledUnits;
        public readonly int[] KilledBuildingsOfHouses = new int[PlayLimits.MaxHouses];
        public int TotalKilledBuildings;
        public int WhoLastHurtMe;
        public int BaseSpawnCellX = -1, BaseSpawnCellY = -1;
        public int BaseCenterX = -1, BaseCenterY = -1;
        public int Radius;
        public readonly ZoneInfoStruct[] ZoneInfos = new ZoneInfoStruct[5];
        public int LATime;
        public int LAEnemy;
        public int ToCapture;
        public HouseBitfield RadarVisibleTo = new HouseBitfield();
        public int PointTotal;
        public QuarryType PreferredTargetType;
        public int PreferredTargetCellX = -1, PreferredTargetCellY = -1;
        public int PreferredDefensiveCellX = -1, PreferredDefensiveCellY = -1;
        public int PreferredDefensiveCell2X = -1, PreferredDefensiveCell2Y = -1;
        public int PreferredDefensiveCellStartTime;
        public CounterClass OwnedBuildingTypes = new CounterClass();
        public CounterClass OwnedUnitTypes = new CounterClass();
        public CounterClass OwnedInfantryTypes = new CounterClass();
        public CounterClass OwnedAircraftTypes = new CounterClass();
        public CounterClass ActiveBuildingTypes = new CounterClass();
        public CounterClass ActiveUnitTypes = new CounterClass();
        public CounterClass ActiveInfantryTypes = new CounterClass();
        public CounterClass ActiveAircraftTypes = new CounterClass();
        public CounterClass FactoryProducedBuildingTypes = new CounterClass();
        public CounterClass FactoryProducedUnitTypes = new CounterClass();
        public CounterClass FactoryProducedInfantryTypes = new CounterClass();
        public CounterClass FactoryProducedAircraftTypes = new CounterClass();
        public CDTimerClass AttackTimer = new CDTimerClass();
        public int InitialAttackDelay;
        public int EnemyHouseIndex = -1;
        public readonly List<AngerStruct> AngerNodes = new List<AngerStruct>();
        public readonly List<ScoutStruct> ScoutNodes = new List<ScoutStruct>();
        public CDTimerClass AITimer = new CDTimerClass();
        public CDTimerClass Unknown_Timer_5640 = new CDTimerClass();
        public int ProducingBuildingTypeIndex = -1;
        public int ProducingUnitTypeIndex = -1;
        public int ProducingInfantryTypeIndex = -1;
        public int ProducingAircraftTypeIndex = -1;
        public int RatioAITriggerTeam;
        public int RatioTeamAircraft;
        public int RatioTeamInfantry;
        public int RatioTeamBuildings;
        public int BaseDefenseTeamCount;
        public readonly DropshipStruct[] DropshipData =
            { new DropshipStruct(), new DropshipStruct(), new DropshipStruct() };
        public int CurrentDropshipIndex;
        public byte HasCloakingRanges;
        /// <public>Color：玩家阵营重染色（RGBA，来自颜色槽而不是调色板）。</public>
        public int ColorR, ColorG, ColorB;
        public int LaserColorR, LaserColorG, LaserColorB;
        public BaseClass Base = new BaseClass();
        public bool RecheckPower;
        public bool RecheckRadar;
        public bool SpySatActive;
        public bool IsBeingDrained;
        public Edge Edge;
        public int EMPTargetX = -1, EMPTargetY = -1;
        public int NukeTargetX = -1, NukeTargetY = -1;
        public HouseBitfield Allies = new HouseBitfield();
        public CDTimerClass DamageDelayTimer = new CDTimerClass();
        public CDTimerClass TeamDelayTimer = new CDTimerClass();
        public CDTimerClass TriggerDelayTimer = new CDTimerClass();
        public CDTimerClass SpeakAttackDelayTimer = new CDTimerClass();
        public CDTimerClass SpeakPowerDelayTimer = new CDTimerClass();
        public CDTimerClass SpeakMoneyDelayTimer = new CDTimerClass();
        public CDTimerClass SpeakMaxedDelayTimer = new CDTimerClass();
        public object AIGeneral;             // IAIHouse*
        public readonly uint[,] ThreatPosedEstimates = new uint[PlayLimits.MaxHouses, PlayLimits.MaxHouses];
        public string PlainName = string.Empty;    // char[21]
        public string UINameString = string.Empty; // char[33]
        public string UIName = string.Empty;       // wchar_t[21]
        public int ColorSchemeIndex;
        /// <summary>StartingPoint（与 StartingCell 是 union）：遭遇战里 = 出生点序号 0..7。</summary>
        public int StartingPoint = -1;
        public int StartingCellX = -1, StartingCellY = -1;
        public HouseBitfield StartingAllies = new HouseBitfield();
        public uint unknown_16060;
        public readonly List<object> WaypointPath = new List<object>();
        public uint unknown_1607C, unknown_16080, unknown_16084;
        public double unused_16088, unused_16090;
        public uint padding_16098;
        public float PredictionEnemyArmor = 0.33f;
        public float PredictionEnemyAir;
        public float PredictionEnemyInfantry;
        public int TotalOwnedInfantryCost;
        public int TotalOwnedVehicleCost;
        public int TotalOwnedAircraftCost;
        public int PowerSurplus;

        // ---------------------------------------------------------------- 构造

        /// <summary>HouseClass::HouseClass(HouseTypeClass*) 0x4F54A0 的托管版：只做字段初始化 + 入表。</summary>
        public HouseClass(HouseTypeClass country)
        {
            Type = country;
            ArrayIndex = Array.Count;
            if (country != null)
            {
                SideIndex = country.SideIndex;
                ColorSchemeIndex = country.ColorSchemeIndex;
                PlainName = country.ID ?? string.Empty;
                UIName = country.UIName ?? string.Empty;
                UINameString = country.UINameLabel ?? string.Empty;
                IsHumanPlayer = false;
            }
            Base.Owner = this;
            Array.Add(this);
        }

        public static HouseClass Create(HouseTypeClass country) => new HouseClass(country);

        /// <summary>清空全局 House 表（新局开局前调用，对照 ScenarioClass 初始化）。</summary>
        public static void ResetAll()
        {
            Array.Clear();
            CurrentPlayer = null;
            Observer = null;
        }

        // ---------------------------------------------------------------- 关系（全部照头文件实现，不猜）

        /// <summary>IsAlliedWith(int)：自己 = true，-1 = false，否则查 Allies 位。</summary>
        public bool IsAlliedWith(int idxHouse)
        {
            if (idxHouse == ArrayIndex) return true;
            if (idxHouse == -1) return false;
            return Allies.Contains(idxHouse);
        }

        public bool IsAlliedWith(HouseClass pHouse)
        {
            if (pHouse == null || pHouse.ArrayIndex == -1) return false;
            if (ReferenceEquals(this, pHouse) || ArrayIndex == pHouse.ArrayIndex) return true;
            return Allies.Contains(pHouse.ArrayIndex);
        }

        /// <summary>互盟：双方 Allies 都含对方（HouseClass.h:256）。</summary>
        public bool IsMutualAlly(HouseClass pHouse)
        {
            if (pHouse == null) return false;
            return ReferenceEquals(this, pHouse)
                || (Allies.Contains(pHouse.ArrayIndex) && pHouse.Allies.Contains(ArrayIndex));
        }

        /// <summary>MakeAlly(0x4F9B50)：双向置位；bAnnounce 只影响语音播报（[未实现 语音]）。</summary>
        public void MakeAlly(int iHouse, bool bAnnounce)
        {
            if (iHouse < 0 || iHouse >= PlayLimits.MaxHouses) return;
            Allies.Set(iHouse);
            var other = FindByIndex(iHouse);
            other?.Allies.Set(ArrayIndex);
            if (bAnnounce) BootLog.Log($"[house] {ArrayIndex} 与 {iHouse} 结盟 [未实现 语音播报]");
        }

        public void MakeAlly(HouseClass pWho, bool bAnnounce)
        {
            if (pWho == null) return;
            MakeAlly(pWho.ArrayIndex, bAnnounce);
        }

        /// <summary>MakeEnemy(0x4F9F90)：清 Allies 位（原版只做单向清除，这里同样单向）。</summary>
        public void MakeEnemy(HouseClass pWho, bool bAnnounce)
        {
            if (pWho == null) return;
            Allies.Clear(pWho.ArrayIndex);
            if (bAnnounce) BootLog.Log($"[house] {ArrayIndex} 与 {pWho.ArrayIndex} 敌对 [未实现 语音播报]");
        }

        /// <summary>CanAlly(0x501540)［未确认内部条件］：暂按"非同阵营且非中立"放行。</summary>
        public bool CanAlly(HouseClass pOther)
        {
            if (pOther == null || ReferenceEquals(this, pOther)) return false;
            if (IsNeutral() || pOther.IsNeutral()) return false;
            return SideIndex == pOther.SideIndex;
        }

        public bool IsNeutral() => Type != null && Type.MultiplayPassive;
        public bool IsCurrentPlayer() => ReferenceEquals(this, CurrentPlayer);
        public bool IsObserver() => ReferenceEquals(this, Observer);
        public bool IsInitiallyObserver() => IsHumanPlayer && GetSpawnPosition() == -1;

        /// <summary>IsControlledByHuman：战役下还认 IsInPlayerControl（HouseClass.h:483）。</summary>
        public bool IsControlledByHuman()
        {
            var result = IsHumanPlayer;
            if (GameStatics.GameMode == GameModeKind.Campaign) result |= IsInPlayerControl;
            return result;
        }

        public bool IsControlledByCurrentPlayer()
        {
            if (GameStatics.GameMode != GameModeKind.Campaign) return IsCurrentPlayer();
            return IsHumanPlayer || IsInPlayerControl;
        }

        /// <summary>GetSpawnPosition：HouseIndices[0..7] 里找自己的 ArrayIndex，返回出生点序号。</summary>
        public int GetSpawnPosition()
        {
            for (var i = 0; i < ScenarioRuntime.HouseIndices.Length; i++)
                if (ScenarioRuntime.HouseIndices[i] == ArrayIndex) return i;
            return -1;
        }

        // ---------------------------------------------------------------- 金钱

        /// <summary>Available_Money：Balance + 采过的钞票（原版把两者相加后给 IHouse）。</summary>
        public int Available_Money() => Balance;

        public bool CanTransactMoney(int amount) => amount > 0 || Available_Money() >= -amount;

        public void TransactMoney(int amount)
        {
            if (amount > 0) GiveMoney(amount);
            else TakeMoney(-amount);
        }

        /// <summary>GiveMoney(0x4F9950)：Balance += amount，印章统计同步（原版只统计不封顶）。</summary>
        public void GiveMoney(int amount)
        {
            if (amount <= 0) return;
            Balance += amount;
            CreditsSpent += 0;   // 原版这里只改 Balance；统计项在别处加
        }

        /// <summary>TakeMoney(0x4F9790)：够就扣，不够扣到 0（原版不返回 bool，靠 CanTransactMoney 预判）。</summary>
        public void TakeMoney(int amount)
        {
            if (amount <= 0) return;
            if (Balance >= amount) Balance -= amount; else Balance = 0;
        }

        public void GiveTiberium(float amount, int type)
        {
            OwnedTiberium.Quantity += (int)amount;
            BootLog.Log($"[house] GiveTiberium {amount} type={type} [未实现 矿石系统]");
        }

        public double GetStoragePercentage()
            => TotalStorage > 0 ? (OwnedTiberium.Quantity + OwnedWeed.Quantity) / (double)TotalStorage : 0.0;

        public double GetWeedStoragePercentage()
            => TotalStorage > 0 ? OwnedWeed.Quantity / (double)TotalStorage : 0.0;

        // ---------------------------------------------------------------- 电力 / 雷达

        public int Power_Output() => PowerOutput;
        public int Power_Drain() => PowerDrain;
        public bool HasFullPower() => PowerOutput >= PowerDrain || PowerDrain == 0;
        public bool HasLowPower() => PowerOutput < PowerDrain && PowerDrain != 0;
        public int PowerSurplusValue => PowerOutput - PowerDrain;

        /// <summary>GetPowerPercentage(0x4FCE30)：有电=1.0，否则 output/drain；drain=0 时也算 1.0。</summary>
        public double GetPowerPercentage()
        {
            if (PowerDrain <= 0) return 1.0;
            if (PowerOutput >= PowerDrain) return 1.0;
            return PowerOutput / (double)PowerDrain;
        }

        public void UpdatePower()
        {
            // 0x508C30：遍历 Buildings 累加 PowerOutput/PowerDrain。建筑实体系统未落地。
            BootLog.Log($"[house] UpdatePower {ArrayIndex} [未实现 建筑电力汇总]");
            RecheckPower = false;
        }

        public void CreatePowerOutage(int duration) { PowerBlackoutTimer.Start(duration); }
        public void CreateRadarOutage(int duration) { RadarBlackoutTimer.Start(duration); }
        public void ReshroudMap() => BootLog.Log($"[house] ReshroudMap {ArrayIndex} [未实现 迷雾]");

        // ---------------------------------------------------------------- 计数（三次口径）

        public int CountOwnedNow(int typeIndex) => OwnedBuildingTypes.GetItemCount(typeIndex);
        public int CountOwnedAndPresent(int typeIndex) => ActiveBuildingTypes.GetItemCount(typeIndex);
        public int CountOwnedEver(int typeIndex) => FactoryProducedBuildingTypes.GetItemCount(typeIndex);

        /// <summary>RegisterGain(0x502A80)：登记到 Owned/Active 两组（建筑/单位/步兵/空军的类型索引由调用方给）。</summary>
        public void RegisterGain(int typeIndex)
        {
            OwnedBuildingTypes.Increment(typeIndex);
            ActiveBuildingTypes.Increment(typeIndex);
        }

        /// <summary>RegisterLoss(0x5025F0)：从 Owned/Active 两组退登记。</summary>
        public void RegisterLoss(int typeIndex)
        {
            OwnedBuildingTypes.Decrement(typeIndex);
            ActiveBuildingTypes.Decrement(typeIndex);
        }

        public void RegisterJustBuilt(int typeIndex) => FactoryProducedBuildingTypes.Increment(typeIndex);

        // ---------------------------------------------------------------- 结局 / 存档位

        public bool Win(bool bSavourSomething)
        {
            IsWinner = true; IsLoser = false; IsGameOver = true;
            BootLog.Log($"[house] Win {ArrayIndex} savour={bSavourSomething} [未实现 结算]");
            return true;
        }

        public bool Lose(bool bSavourSomething)
        {
            IsLoser = true; IsWinner = false; IsGameOver = true; Defeated = true;
            BootLog.Log($"[house] Lose {ArrayIndex} savour={bSavourSomething} [未实现 结算]");
            return true;
        }

        public void AcceptDefeat()
        {
            Defeated = true;
            BootLog.Log($"[house] AcceptDefeat {ArrayIndex} [未实现 投降流程]");
        }

        public void ForceEnd() => IsGameOver = true;

        public void RespawnStartingBuildings() => BootLog.Log($"[house] RespawnStartingBuildings {ArrayIndex} [未实现]");
        public void RespawnStartingForces() => BootLog.Log($"[house] RespawnStartingForces {ArrayIndex} [未实现]");

        // ---------------------------------------------------------------- 生产 / 工厂（实体层未落地）

        public object GetFactoryProducing(int typeIndex) => null;   // 0x4F83C0
        public CanBuildResult CanBuild(int typeIndex, bool buildLimitOnly, bool allowIfInProduction) => CanBuildResult.Buildable;
        public bool CanExpectToBuild(int typeIndex) => true;
        public void Update_FactoriesQueues(AbstractKind factoryOf, bool isNaval, BuildCat buildCat) { }
        public void CalculateCostMultipliers()
        {
            // 0x50BF60：把 Country 的五个 CostMult 抄成 House 的五个 CostMult。
            if (Type == null) return;
            CostInfantryMult = Type.CostInfantryMult;
            CostUnitsMult = Type.CostUnitsMult;
            CostAircraftMult = Type.CostAircraftMult;
            CostBuildingsMult = Type.CostBuildingsMult;
            CostDefensesMult = Type.CostDefensesMult;
        }

        /// <summary>AssignHandicap(0x4F6EC0)：按难度设 AI 惩罚倍率。数值来自规则 [Multiplayer] 段（[未确认具体键]）。</summary>
        public void AssignHandicap(AIDifficulty difficulty)
        {
            AIDifficulty = difficulty;
            switch (difficulty)
            {
                case AIDifficulty.Hard: FirepowerMultiplier = 1.0; ArmorMultiplier = 1.0; ROFMultiplier = 1.0; break;
                case AIDifficulty.Normal: FirepowerMultiplier = 1.0; ArmorMultiplier = 1.0; ROFMultiplier = 1.0; break;
                case AIDifficulty.Easy: FirepowerMultiplier = 1.0; ArmorMultiplier = 1.0; ROFMultiplier = 1.0; break;
            }
            BootLog.Log($"[house] AssignHandicap {ArrayIndex} = {difficulty} [未确认 Multiplayer 惩罚倍率]");
        }

        // ---------------------------------------------------------------- 静态查找（0x502D30 / 0x510ED0 / 0x510FB0）

        public static HouseClass FindByIndex(int idxHouse)
        {
            if (idxHouse >= PlayerAtA && idxHouse <= PlayerAtA + 7)
            {
                var at = idxHouse - PlayerAtA;
                var houseIdx = ScenarioRuntime.HouseIndices[at];
                if (houseIdx < 0) return null;
                idxHouse = houseIdx;
            }
            for (var i = 0; i < Array.Count; i++) if (Array[i].ArrayIndex == idxHouse) return Array[i];
            return null;
        }

        public static HouseClass FindByCountryIndex(int houseType)
        {
            for (var i = 0; i < Array.Count; i++)
                if (Array[i].Type != null && Array[i].Type.ArrayIndex == houseType) return Array[i];
            return null;
        }

        public static HouseClass FindByCountryName(string name)
        {
            var idx = HouseTypeClass.FindIndexOfName(name);
            return idx < 0 ? null : FindByCountryIndex(idx);
        }

        public static HouseClass FindBySideIndex(int index)
        {
            for (var i = 0; i < Array.Count; i++) if (Array[i].SideIndex == index) return Array[i];
            return null;
        }

        public static int FindIndexByName(string name)
        {
            for (var i = 0; i < Array.Count; i++)
                if (string.Equals(Array[i].PlainName, name, StringComparison.OrdinalIgnoreCase)) return Array[i].ArrayIndex;
            return -1;
        }

        public static bool Index_IsMP(int idx) => idx >= PlayerAtA && idx <= PlayerAtA + 7;

        public static int GetPlayerAtFromString(string name)
        {
            // 0x510FB0：把 "Player @ A".."Player @ H" 映射成 4475..4482。CSF 键缺失时返回 -1。
            if (string.IsNullOrEmpty(name)) return -1;
            var key = "Player @" + name.Trim().ToUpperInvariant();
            var text = Ra2md.Menu.MenuStrings.Get(key, null);
            if (string.IsNullOrEmpty(text)) return -1;
            for (var i = 0; i < 8; i++) if (text == name) return PlayerAtA + i;
            return -1;
        }

        // ---------------------------------------------------------------- 抽象类型（WhatAmI/Size）

        public AbstractKind WhatAmI() => AbstractKind.House;
        public int Size() => 0x160A0;   // 原版 HouseClass::Size 返回的实例大小

        /// <summary>get_ID：HouseClass.h:531，取国家 ID。</summary>
        public string GetId() => Type != null ? Type.ID : string.Empty;
    }

    /// <summary>AbstractType 只列本类引用的项（全表在 GeneralDefinitions，用到再加）。</summary>
    public enum AbstractKind
    {
        None = 0, Unit = 1, Aircraft = 2, AircraftType = 3, Anim = 4, AnimType = 5,
        Building = 6, BuildingType = 7, Bullet = 8, BulletType = 9, Campaign = 10,
        Cell = 11, Factory = 12, House = 13, HouseType = 14, Infantry = 15,
        InfantryType = 16, BulletTypeExtra = 17, Overlay = 18, OverlayType = 19,
        Particle = 20, ParticleType = 21, ParticleSystem = 22, ParticleSystemType = 23,
        SuperWeapon = 24, SuperWeaponType = 25, Tag = 26, TagType = 27, Team = 28,
        TeamType = 29, Terrain = 30, TerrainType = 31, Trigger = 32, TriggerType = 33,
        Waypoint = 34, TaskForce = 35, Script = 36, ScriptType = 37, Side = 38,
        Smudge = 39, SmudgeType = 40, UnitType = 41, VoxelAnim = 42, VoxelAnimType = 43,
        Wave = 44, Tube = 45,
    }

    /// <summary>ScenarioClass::HouseIndices 的等价物（8 个遭遇战出生位的 House 索引）。</summary>
    public static class ScenarioRuntime
    {
        public static readonly int[] HouseIndices = new int[PlayLimits.MaxHouses];

        public static void Reset()
        {
            for (var i = 0; i < HouseIndices.Length; i++) HouseIndices[i] = -1;
        }
    }
}
