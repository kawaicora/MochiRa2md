// MapRecords.cs —— 场景文件解析后的强类型记录。
//
// 字段顺序对照：
//   * ConcordRTS MapDatabase.h（MapUnitObject / MapInfantryObject / MapStructureObject /
//     MapTerrainObject / MapWaypoint 等，含字段语义与缺省值）；
//   * YRpp TeamTypeClass / TriggerTypeClass / TEventClass / TActionClass / TeamClass 字段。
// 坐标：原版 CellStruct 编码 cell = y*1000 + x（ObjectCellCoordinateBase=1000）。
using System.Collections.Generic;

namespace Ra2md.World
{
    // ---- 事件 / 动作枚举（GeneralDefinitions.h: TriggerEvent / TriggerAction，数值照抄）----

    public enum TriggerEventKind
    {
        None = 0x0,
        EnteredBy = 0x1, SpiedBy = 0x2, ThievedBy = 0x3, DiscoveredByPlayer = 0x4,
        HouseDiscovered = 0x5, AttackedByAnybody = 0x6, DestroyedByAnybody = 0x7,
        AnyEvent = 0x8, DestroyedUnitsAll = 0x9, DestroyedBuildingsAll = 0xA, DestroyedAll = 0xB,
        CreditsExceed = 0xC, ElapsedTime = 0xD, MissionTimerExpired = 0xE,
        DestroyedBuildingsNum = 0xF, DestroyedUnitsNum = 0x10, NoFactoriesLeft = 0x11,
        CiviliansEvacuated = 0x12, BuildBuildingType = 0x13, BuildUnitType = 0x14,
        BuildInfantryType = 0x15, BuildAircraftType = 0x16, TeamLeavesMap = 0x17,
        ZoneEntryBy = 0x18, CrossesHorizontalLine = 0x19, CrossesVerticalLine = 0x1A,
        GlobalSet = 0x1B, GlobalCleared = 0x1C, DestroyedFakesAll = 0x1D, LowPower = 0x1E,
        AllBridgesDestroyed = 0x1F, BuildingExists = 0x20, SelectedByPlayer = 0x21,
        ComesNearWaypoint = 0x22, EnemyInSpotlight = 0x23, LocalSet = 0x24, LocalCleared = 0x25,
        FirstDamagedCombat = 0x26, HalfHealthCombat = 0x27, QuarterHealthCombat = 0x28,
        FirstDamagedAny = 0x29, HalfHealthAny = 0x2A, QuarterHealthAny = 0x2B, AttackedByHouse = 0x2C,
        AmbientLightBelow = 0x2D, AmbientLightAbove = 0x2E, ElapsedScenarioTime = 0x2F,
        DestroyedByAnything = 0x30, PickupCrate = 0x31, PickupCrateAny = 0x32, RandomDelay = 0x33,
        CreditsBelow = 0x34, SpyAsHouse = 0x35, SpyAsInfantry = 0x36, DestroyedUnitsNaval = 0x37,
        DestroyedUnitsLand = 0x38, BuildingDoesNotExist = 0x39, PowerFull = 0x3A,
        EnteredOrOverflownBy = 0x3B, TechTypeExists = 0x3C, TechTypeDoesntExist = 0x3D,
    }

    public enum TriggerActionKind
    {
        None = 0x0,
        Win = 0x1, Lose = 0x2, ProductionBegins = 0x3, CreateTeam = 0x4, DestroyTeam = 0x5,
        AllToHunt = 0x6, Reinforcement = 0x7, DropZoneFlare = 0x8, FireSale = 0x9, PlayMovie = 0xA,
        TextTrigger = 0xB, DestroyTrigger = 0xC, AutocreateBegins = 0xD, ChangeHouse = 0xE,
        AllowWin = 0xF, RevealAllMap = 0x10, RevealAroundWaypoint = 0x11, RevealWaypointZone = 0x12,
        PlaySoundEffect = 0x13, PlayMusicTheme = 0x14, PlaySpeech = 0x15, ForceTrigger = 0x16,
        TimerStart = 0x17, TimerStop = 0x18, TimerExtend = 0x19, TimerShorten = 0x1A, TimerSet = 0x1B,
        GlobalSet = 0x1C, GlobalClear = 0x1D, AutoBaseBuilding = 0x1E, GrowShroud = 0x1F,
        DestroyAttachedObject = 0x20, AddOneTimeSuperWeapon = 0x21, AddRepeatingSuperWeapon = 0x22,
        PreferredTarget = 0x23, AllChangeHouse = 0x24, MakeAlly = 0x25, MakeEnemy = 0x26,
        ChangeZoomLevel = 0x27, ResizePlayerView = 0x28, PlayAnimAt = 0x29, DoExplosionAt = 0x2A,
        CreateVoxelAnim = 0x2B, IonStormStart = 0x2C, IonStormStop = 0x2D, LockInput = 0x2E,
        UnlockInput = 0x2F, MoveCameraToWaypoint = 0x30, ZoomIn = 0x31, ZoomOut = 0x32,
        ReshroudMap = 0x33, ChangeLightBehavior = 0x34, EnableTrigger = 0x35, DisableTrigger = 0x36,
        CreateRadarEvent = 0x37, LocalSet = 0x38, LocalClear = 0x39, MeteorShower = 0x3A,
        ReduceTiberium = 0x3B, SellBuilding = 0x3C, TurnOffBuilding = 0x3D, TurnOnBuilding = 0x3E,
        Apply100Damage = 0x3F, SmallLightFlash = 0x40, MediumLightFlash = 0x41, LargeLightFlash = 0x42,
        AnnounceWin = 0x43, AnnounceLose = 0x44, ForceEnd = 0x45, DestroyTag = 0x46,
        SetAmbientStep = 0x47, SetAmbientRate = 0x48, SetAmbientLight = 0x49, AITriggersBegin = 0x4A,
        AITriggersStop = 0x4B, RatioOfAITriggerTeams = 0x4C, RatioOfTeamAircraft = 0x4D,
        RatioOfTeamInfantry = 0x4E, RatioOfTeamUnits = 0x4F, ReinforcementAt = 0x50, WakeupSelf = 0x51,
        WakeupAllSleepers = 0x52, WakeupAllHarmless = 0x53, WakeupGroup = 0x54, VeinGrowth = 0x55,
        TiberiumGrowth = 0x56, IceGrowth = 0x57, ParticleAnim = 0x58, RemoveParticleAnim = 0x59,
        LightningStrike = 0x5A, GoBerzerk = 0x5B, ActivateFirestorm = 0x5C, DeactivateFirestorm = 0x5D,
        IonCannonStrike = 0x5E, NukeStrike = 0x5F, ChemMissileStrike = 0x60, ToggleTrainCargo = 0x61,
        PlaySoundEffectRandom = 0x62, PlaySoundEffectAtWaypoint = 0x63, PlayIngameMovie = 0x64,
        ReshroudMapAtWaypoint = 0x65, LightningStormStrike = 0x66, TimerText = 0x67, FlashTeam = 0x68,
        TalkBubble = 0x69, SetObjectTechLevel = 0x6A, ReinforcementByChrono = 0x6B, CreateCrate = 0x6C,
        IronCurtain = 0x6D, PauseGame = 0x6E, EvictOccupiers = 0x6F, CenterCameraAtWaypoint = 0x70,
        MakeHouseCheer = 0x71, SetTabTo = 0x72, FlashCameo = 0x73, StopSounds = 0x74,
        PlayIngameMovieAndPause = 0x75, ClearAllSmudges = 0x76, DestroyAll = 0x77,
        DestroyAllBuildings = 0x78, DestroyAllLandUnits = 0x79, DestroyAllNavalUnits = 0x7A,
        MindControlBase = 0x7B, RestoreMindControlledBase = 0x7C, CreateBuilding = 0x7D,
        RestoreStartingUnits = 0x7E, StartChronoScreenEffect = 0x7F, TeleportAll = 0x80,
        SetSuperWeaponCharge = 0x81, RestoreStartingBuildings = 0x82, FlashBuildingsOfType = 0x83,
        SuperWeaponSetRechargeTime = 0x84, SuperWeaponResetRechargeTime = 0x85, SuperWeaponReset = 0x86,
        SetPreferredTargetCell = 0x87, ClearPreferredTargetCell = 0x88, SetBaseCenterCell = 0x89,
        ClearBaseCenterCell = 0x8A, BlackoutRadar = 0x8B, SetDefensiveTargetCell = 0x8C,
        ClearDefensiveTargetCell = 0x8D, RetintRed = 0x8E, RetintGreen = 0x8F, RetintBlue = 0x90,
        JumpCameraHome = 0x91,
    }

    // ---- 地图几何 ----

    public sealed class MapBounds
    {
        public int X0, Y0, X1, Y1;
        public int Width => X1 - X0;
        public int Height => Y1 - Y0;
        public bool IsValid => Width > 0 && Height > 0;
    }

    public sealed class MapWaypoint
    {
        /// <summary>索引（[Waypoints] 键），同时作为字母序号：0=A,1=B…</summary>
        public string Id;
        public int Cell;
        public int X;
        public int Y;
        public bool Valid;
    }

    // ---- 场景对象（单位 / 步兵 / 建筑 / 地形统一记录，按 Section 区分）----

    public enum MapObjectKind { Unit, Infantry, Structure, Terrain }

    public sealed class MapObject
    {
        public MapObjectKind Kind;
        public string Id;
        public string Owner;
        public string Type;
        /// <summary>地图血量，Westwood 0..256 比例值。</summary>
        public int Health = 256;
        public int X = -1;
        public int Y = -1;
        public int Facing = -1;
        public int SubCell = -1;
        public string Mission;
        public string Tag;
        public int Veterancy;
        public bool OnBridge;
    }

    // ---- 任务力量 / 脚本 / 小队类型 ----

    public sealed class TaskForceItem
    {
        public int Count;
        public string Type;
    }

    public sealed class TaskForceEntry
    {
        public string Id;
        public string Name;
        public readonly List<TaskForceItem> Items = new List<TaskForceItem>();
        public int Group = -1;
    }

    public sealed class ScriptStep
    {
        public int Action;
        public int Param;
    }

    public sealed class ScriptEntry
    {
        public string Id;
        public string Name;
        public readonly List<ScriptStep> Steps = new List<ScriptStep>();
    }

    /// <summary>对照 YRpp TeamTypeClass 字段。</summary>
    public sealed class TeamTypeEntry
    {
        public string Id;
        public string Name;
        public string House;
        public string TaskForceId;
        public string ScriptId;
        /// <summary>航路点字母（如 HA），装载后解析为索引。</summary>
        public string Waypoint;
        public int WaypointIndex = -1;

        public int VeteranLevel;
        public int Priority = 100;
        public int Max = 1;
        public int TechLevel;
        public int Group = -1;
        public int MindControlDecision;

        public bool Loadable;
        public bool Full;
        public bool Annoyance;
        public bool GuardSlower;
        public bool Recruiter;
        public bool Autocreate;
        public bool Prebuild;
        public bool Reinforce;
        public bool Whiner;
        public bool Aggressive;
        public bool LooseRecruit;
        public bool Suicide;
        public bool Droppod;
        public bool UseTransportOrigin;
        public bool DropshipLoadout;
        public bool OnTransOnly;
        public bool AvoidThreats;
        public bool IonImmune;
        public bool TransportsReturnOnUnload;
        public bool AreTeamMembersRecruitable;
        public bool IsBaseDefense;
        public bool OnlyTargetHouseEnemy;
    }

    // ---- 触发器 / 事件 / 动作 / 标签 ----

    public sealed class MapTrigger
    {
        public string Id;
        public string House;
        public string Tag;
        public string Name;
        public bool Enabled = true;
        public bool Repeating;
        public bool MustTransfer;
    }

    public sealed class MapEvent
    {
        public string TriggerId;
        /// <summary>该类事件条件条数（[Events] 首字段）。</summary>
        public int Count = 1;
        public TriggerEventKind Kind;
        public int Value;
        /// <summary>引用串：队伍类型 / 房子 / 航路点 / 文本。</summary>
        public string StringRef;
    }

    public sealed class MapAction
    {
        public string TriggerId;
        public int Count = 1;
        public TriggerActionKind Kind;
        public bool Enabled = true;
        /// <summary>引用 ID：队伍类型 / 标签 / 触发器（按动作种类解释）。</summary>
        public string RefId;
        public int Param3, Param4, Param5, Param6;
        public int Waypoint = -1;
        public int Value;
        public string Text;
    }

    public sealed class MapTag
    {
        public string Id;
        public int Kind;
        public string Name;
        public string TriggerId;
    }

    /// <summary>整份场景装载结果。</summary>
    public sealed class ScenarioDocument
    {
        public string SourceName;
        /// <summary>原始 INI（地图专用配置覆盖规则用，段/键全部保留）。</summary>
        public MapIniDocument Ini;
        public string Theater;
        public string Briefing;
        /// <summary>[Basic] Name 显示名（遭遇战选图列表用）；缺省回落到文件名。</summary>
        public string DisplayName;
        /// <summary>[Basic] GameMode 过滤串。</summary>
        public readonly List<string> GameModes = new List<string>();
        /// <summary>[Basic] MultiplayerOnly=1：遭遇战/多人图；任务图为 0/缺省，绝不进遭遇战列表。</summary>
        public bool MultiplayerOnly;
        /// <summary>[Header] NumberStartingPoints：地图声明的起始点数；0 表示未声明。</summary>
        public int NumberStartingPoints;
        /// <summary>[Basic] MinPlayers / MaxPlayers（地图自身声明，缺省 0 表示未声明）。</summary>
        public int MinPlayers;
        public int MaxPlayers;
        public MapBounds Bounds = new MapBounds();
        public readonly List<string> Houses = new List<string>();
        public readonly List<MapWaypoint> Waypoints = new List<MapWaypoint>();
        public readonly List<MapObject> Units = new List<MapObject>();
        public readonly List<MapObject> Infantry = new List<MapObject>();
        public readonly List<MapObject> Structures = new List<MapObject>();
        public readonly List<MapObject> Terrain = new List<MapObject>();
        public readonly Dictionary<string, TaskForceEntry> TaskForces = new Dictionary<string, TaskForceEntry>();
        public readonly Dictionary<string, ScriptEntry> Scripts = new Dictionary<string, ScriptEntry>();
        public readonly Dictionary<string, TeamTypeEntry> TeamTypes = new Dictionary<string, TeamTypeEntry>();
        public readonly Dictionary<string, MapTrigger> Triggers = new Dictionary<string, MapTrigger>();
        public readonly List<MapEvent> Events = new List<MapEvent>();
        public readonly List<MapAction> Actions = new List<MapAction>();
        public readonly Dictionary<string, MapTag> Tags = new Dictionary<string, MapTag>();
        public readonly List<string> Warnings = new List<string>();

        public TeamTypeEntry FindTeamType(string id)
        {
            return !string.IsNullOrEmpty(id) && TeamTypes.TryGetValue(id, out var t) ? t : null;
        }

        public TaskForceEntry FindTaskForce(string id)
        {
            return !string.IsNullOrEmpty(id) && TaskForces.TryGetValue(id, out var t) ? t : null;
        }

        public ScriptEntry FindScript(string id)
        {
            return !string.IsNullOrEmpty(id) && Scripts.TryGetValue(id, out var s) ? s : null;
        }
    }
}
