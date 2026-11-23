// RulesDatabase.cs —— rulesmd.ini / rules.ini 的运行时规则库 + 地图专用配置覆盖 + 类型表 + art(md).ini。
//
// 对照原版：
//   * CCINIClass 读 rulesmd.ini，随后地图文件自己的段与键会覆盖规则值（地图里写 [AMCV] Cost=... 即只对本局生效）；
//     这也是地图能带“专用配置”的原因：段名相同、键名相同，作用域就是这一局。
//   * 类型表顺序来自 [VehicleTypes] / [InfantryTypes] / [AircraftTypes] / [BuildingTypes] /
//     [Projectiles] / [Animations] / [SuperWeaponTypes]，ArrayIndex 就是该表里的序号；
//     每个类型自己的段（如 [AMCV]）给 Cost/Strength/Armor/Speed/Owner/...。
//   * art(md).ini 给 Image / Cameo / Foundation / Voxel / Sequence 等表现层字段，
//     按“Image 有值则用 Image，否则用类型 ID”查段（原版 TechnoTypeClass::LoadFromINI 同口径）。
//
// 未落地的部分只在字段上留位，不伪造数值；缺失键一律回到原版默认（Cost=0 等）并计入 Missing 计数。
using System;
using System.Collections.Generic;
using Ra2md.Core;
using Ra2md.Menu;

namespace Ra2md.World
{
    // ---------------------------------------------------------------- 规则库（含地图覆盖）

    public sealed class RulesDatabase
    {
        public static RulesDatabase Current { get; private set; }

        private readonly MapIniDocument _rules;
        private readonly MapIniDocument _map;

        public int MapOverrideHits { get; private set; }
        public string RulesSource { get; private set; }

        private RulesDatabase(MapIniDocument rules, MapIniDocument map, string source)
        {
            _rules = rules;
            _map = map;
            RulesSource = source;
        }

        /// <summary>加载 rulesmd.ini（缺则 rules.ini）；mapDoc 可为 null（大厅阶段没有地图）。</summary>
        public static RulesDatabase Load(MapIniDocument mapDoc)
        {
            MapIniDocument rules = null;
            var source = "none";
            rules = LoadIniWithDirectives(Ra2md.Managers.GlobalConfigManager.Resources.RulesIniNames, out source);
            Current = new RulesDatabase(rules, mapDoc, source);
            BootLog.Log("[规则] 载入 " + source + "，段数=" + (rules != null ? rules.Sections.Count : 0)
                + "，地图覆盖层=" + (mapDoc != null ? "有" : "无"));
            return Current;
        }

        /// <summary>是否挂载了 ares.mix：挂载后 INI 按 Ares 方式解析（[#include] 等）。</summary>
        internal static bool ShipsAres()
        {
            var vfs = MenuResourceLibrary.EnsureMounted();
            var archiveName = Ra2md.Managers.GlobalConfigManager.Resources.AresArchiveName;
            foreach (var archive in vfs.Archives)
            {
                if (string.Equals(archive.Name, archiveName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>按候选名取根 INI，并展开 include / inherits 扩展指令。</summary>
        internal static MapIniDocument LoadIniWithDirectives(string[] candidates, out string origin)
        {
            if (!MenuResourceLibrary.TryLoadFirst(candidates, out var bytes, out origin) || bytes == null)
            {
                origin = "none";
                return null;
            }
            var doc = MapIniDocument.Parse(MapIniDocument.DecodeText(bytes));
            // Phobos 按构建开关；当前工程未带 Phobos 运行库，方言关闭。
            return MapIniDocument.ExpandDirectives(doc, origin,
                name => MenuResourceLibrary.LoadRawByName(name, out _), ShipsAres(), false);
        }

        public static RulesDatabase EnsureLoaded()
        {
            return Current ?? Load(null);
        }

        /// <summary>取段：地图段优先，其次规则段（地图只覆盖它写过的键）。</summary>
        public IniSection GetSection(string name)
        {
            var mapSec = _map != null ? _map.GetSection(name) : null;
            var ruleSec = _rules != null ? _rules.GetSection(name) : null;
            if (mapSec != null && ruleSec != null)
            {
                var merged = new IniSection { Name = name };
                merged.Entries.AddRange(ruleSec.Entries);
                merged.Entries.AddRange(mapSec.Entries);
                MapOverrideHits++;
                return merged;
            }
            return mapSec ?? ruleSec;
        }

        /// <summary>只在地图里出现的段（例如地图自己的 [Basic]）。</summary>
        public IniSection GetMapSection(string name) => _map != null ? _map.GetSection(name) : null;

        public string GetString(string section, string key, string fallback = "")
        {
            var sec = GetSection(section);
            return sec != null ? sec.GetString(key, fallback) : fallback;
        }

        public int GetInt(string section, string key, int fallback = 0)
        {
            var sec = GetSection(section);
            return sec != null ? sec.GetInt(key, fallback) : fallback;
        }

        public double GetDouble(string section, string key, double fallback = 0.0)
        {
            var text = GetString(section, key, null);
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public bool GetBool(string section, string key, bool fallback = false)
        {
            var sec = GetSection(section);
            return sec != null ? sec.GetBool(key, fallback) : fallback;
        }

        /// <summary>角度键：缺省回 defaultDegrees 弧度；-1 保留当前弧度；其余度数转弧度（参考 RollAngle/PitchAngle 口径）。</summary>
        internal static double ReadDegreesRadians(IniSection sec, string key, double defaultDegrees, double currentRadians)
        {
            var text = sec.GetString(key, null);
            if (string.IsNullOrWhiteSpace(text)) return defaultDegrees * Math.PI / 180.0;
            if (!double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d)) return currentRadians;
            if (d == -1.0) return currentRadians;
            return d * Math.PI / 180.0;
        }

        /// <summary>按顺序取段里的值（类型表用）：地图段存在就以地图为准，否则用规则段。</summary>
        public List<string> GetValueList(string section)
        {
            var result = new List<string>();
            var sec = GetSection(section);
            if (sec == null) return result;
            foreach (var entry in sec.Entries)
            {
                var value = (entry.Value ?? string.Empty).Trim();
                if (value.Length == 0) continue;
                if (!result.Contains(value)) result.Add(value);
            }
            return result;
        }

        /// <summary>把逗号列表按索引取（[VehicleTypes] 的 1=AMCV 这种，不保证从 0 开始）。</summary>
        public List<string> GetIndexedList(string section)        {
            var result = new List<string>();
            var sec = GetSection(section);
            if (sec == null) return result;
            var pairs = new List<KeyValuePair<int, string>>();
            foreach (var entry in sec.Entries)
            {
                if (!int.TryParse(entry.Key, out var index)) continue;
                var value = (entry.Value ?? string.Empty).Trim();
                if (value.Length == 0) continue;
                pairs.Add(new KeyValuePair<int, string>(index, value));
            }
            pairs.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var p in pairs) if (!result.Contains(p.Value)) result.Add(p.Value);
            return result;
        }

        /// <summary>数字键 → 值（保留键里的跳号）：[OverlayTypes] 这种 "1=GASAND … 39 后跳 42" 必须按真键查。</summary>
        public Dictionary<int, string> GetIndexMap(string section)
        {
            var map = new Dictionary<int, string>();
            var sec = GetSection(section);
            if (sec == null) return map;
            foreach (var entry in sec.Entries)
            {
                if (!int.TryParse((entry.Key ?? string.Empty).Trim(), out var index)) continue;
                var value = (entry.Value ?? string.Empty).Trim();
                if (value.Length == 0) continue;
                map[index] = value;
            }
            return map;
        }

        /// <summary>规则段名并集（地图覆盖段同名合并），供无清单的家族（武器/弹头）扫描装配。</summary>
        public List<string> AllSectionNames()
        {
            var names = new List<string>();
            if (_rules != null)
            {
                foreach (var s in _rules.Sections) if (!names.Contains(s.Name)) names.Add(s.Name);
            }
            if (_map != null)
            {
                foreach (var s in _map.Sections) if (!names.Contains(s.Name)) names.Add(s.Name);
            }
            return names;
        }
    }

    // ---------------------------------------------------------------- art(md).ini

    /// <summary>一条 art 记录（artmd.ini 的段）。字段名照 art.ini 的常用键；未出现的键保持空。</summary>
    public sealed class ArtRecord
    {
        public string Section;
        public string Image;        // Image= 覆盖类型 ID
        public string Cameo;
        public string AltCameo;
        public string Foundation;   // 1x1 / 2x2 / 3x3 ...
        public string Sequence;
        public int Layer;
        public bool Voxel;          // Voxel=yes（单位/飞机是体素）
        public bool NewTheater;     // NewTheater=yes → 文件名带剧场后缀
        public int ShadowIndex;
        public string TurretAnim;
        // ---- 通用动画参数（所有对象共用一套口径，不在各处重复解析）----
        public int AnimStart;
        public int AnimLoopStart;
        public int AnimLoopEnd;
        public int AnimEndCount;   // End= 是帧数（参考注释：End 不是结束帧号，是序列帧数）
        public int AnimLoopCount = -1; // -1 = 无限循环；>=0 = 播完指定次数后停在末帧
        public int AnimRateMs;
        public bool AnimPingPong;      // PingPong=yes：到头反向回放（不是回到首帧）
        // ---- SHP 身体帧布局（待机 / 行走 / 开火条带，8 朝向）----
        public int Facings = 8;
        public int WalkFrames = 12;
        public int FiringFrames;
        public int StandingFrames;
        public int StartWalkFrame = -1;
        public int StartStandFrame = -1;
        public int StartFiringFrame = -1;
        public readonly List<string> Extra = new List<string>();

        public string ResolvedImage(string typeId)
        {
            return string.IsNullOrEmpty(Image) ? typeId : Image;
        }

        /// <summary>该 art 段原文（动画键 ActiveAnim/IdleAnim/SpecialAnim 以及 ZAdjust/YSort 都从这里读）。</summary>
        public IniSection Raw;
    }

    public sealed class ArtDatabase
    {
        private static ArtDatabase _current;
        private readonly MapIniDocument _map;
        private readonly MapIniDocument _art;

        public static ArtDatabase Current => _current;

        private ArtDatabase(MapIniDocument art, MapIniDocument map)
        {
            _art = art;
            _map = map;
        }

        public static ArtDatabase Load(MapIniDocument mapDoc)
        {
            MapIniDocument art = RulesDatabase.LoadIniWithDirectives(
                Ra2md.Managers.GlobalConfigManager.Resources.ArtIniNames, out var origin);
            _current = new ArtDatabase(art, mapDoc);
            BootLog.Log("[美术] 载入 art " + (art != null ? "OK" : "缺失") + "，段数=" + (art != null ? art.Sections.Count : 0));
            return _current;
        }

        public static ArtDatabase EnsureLoaded() => _current ?? Load(null);

        public ArtRecord Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var sec = (_map != null ? _map.GetSection(id) : null) ?? (_art != null ? _art.GetSection(id) : null);
            if (sec == null) return null;
            return new ArtRecord
            {
                Section = id,
                Image = sec.GetString("Image", string.Empty),
                Cameo = sec.GetString("Cameo", string.Empty),
                AltCameo = sec.GetString("AltCameo", string.Empty),
                Foundation = sec.GetString("Foundation", string.Empty),
                Sequence = sec.GetString("Sequence", string.Empty),
                Layer = sec.GetInt("Layer", 0),
                Voxel = sec.GetBool("Voxel", false),
                NewTheater = sec.GetBool("NewTheater", false),
                ShadowIndex = sec.GetInt("ShadowIndex", 0),
                TurretAnim = sec.GetString("TurretAnim", string.Empty),
                AnimStart = sec.GetInt("Start", 0),
                AnimLoopStart = sec.GetInt("LoopStart", sec.GetInt("Start", 0)),
                AnimLoopEnd = sec.GetInt("LoopEnd", 0),
                AnimEndCount = sec.GetInt("End", 0),
                AnimLoopCount = sec.GetInt("LoopCount", -1),
                AnimRateMs = sec.GetInt("Rate", 0),
                AnimPingPong = sec.GetBool("PingPong", false),
                Facings = sec.GetInt("Facings", 8),
                WalkFrames = sec.GetInt("WalkFrames", 12),
                FiringFrames = sec.GetInt("FiringFrames", 0),
                StandingFrames = sec.GetInt("StandingFrames", sec.GetInt("FiringFrames", 0) > 0 ? 1 : 0),
                StartWalkFrame = sec.GetInt("StartWalkFrame", -1),
                StartStandFrame = sec.GetInt("StartStandFrame", -1),
                StartFiringFrame = sec.GetInt("StartFiringFrame", -1),
                Raw = sec,
            };
        }
    }

    // ---------------------------------------------------------------- 类型表

    /// <summary>所有类型共有的身份字段（AbstractTypeClass）。</summary>
    public abstract class TypeClassBase
    {
        public string ID;
        /// <summary>rules 的 Image=（原版 AbstractTypeClass::ImageName）：art 段名与图像名都用它，缺省 = 段名。</summary>
        public string ImageId;
        public int ArrayIndex;
        public string Name;
        public string UINameLabel;
        public string UIName;
        public ArtRecord Art;

        // 每个类型对象持有“本类型所有实例”的数组（类型 → 实例的归属表）。
        public readonly List<object> Instances = new List<object>();

        public string ResolvedImage => ImageId ?? ID;

        public virtual void LoadFrom(RulesDatabase rules, string id, int index)
        {
            ID = id;
            ArrayIndex = index;
            ImageId = id;
            var sec = rules.GetSection(id);
            if (sec == null)
            {
                Name = id;
                UINameLabel = id;
                UIName = id;
                return;
            }
            // Image=：AMCV 写 Image=MCV，art 段与 .vxl/.shp 名都是 MCV（原版 AbstractTypeClass 同口径）
            var imageId = sec.GetString("Image", string.Empty).Trim();
            if (imageId.Length > 0) ImageId = imageId;
            Name = sec.GetString("Name", id);
            UINameLabel = sec.GetString("UIName", id);
            UIName = MenuStrings.Get(UINameLabel, Name);
        }
    }

    /// <summary>技术类类型（单位/步兵/飞机/建筑）的公共字段（TechnoTypeClass 的常用子集）。</summary>
    public abstract class TechnoTypeClass : TypeClassBase
    {
        public int Cost;
        public int Strength;
        public string ArmorName;
        public int ArmorIndex = -1;
        public int Speed;
        public int Sight;
        public int TechLevel = -1;
        public int ROT;
        public readonly List<string> Owner = new List<string>();
        public readonly List<string> Prerequisite = new List<string>();
        public readonly List<string> PrerequisiteOverride = new List<string>();
        public string Primary;
        public string Secondary;
        public string Category;
        public string DeploysInto;
        public bool Crewed;
        public bool Crusher;
        public bool CrateGoodie;
        public bool HasTurret;          // rules 的 Turret=yes（SHP 身体 SHP 末尾带炮塔环）

        // ---- 以下字段按参考 object_type reader 口径补齐（默认值与参考一致）----
        public string BuildCat;
        public int BuildLimit = int.MaxValue;
        public bool Adjacent;
        public int AdjacentCount = 3;
        public bool ProtectWithWall;
        public bool WantsExtraSpace;
        public bool Bib;
        public bool Gate;
        public int Storage;
        public bool Repairable = true;
        public bool Capturable;
        public bool Deployer;
        public bool Harvester;
        public bool Refinery;
        public bool Radar;
        public bool GapGenerator;
        public bool SpySat;
        public bool NukeSilo;
        public string SuperWeapon;
        public int Ammo = -1;
        public int Reload;
        public string Locomotor;
        public string MovementZone;
        public LocomotorKind LocomotorKind;
        public SpeedType SpeedType;
        public MovementZone MovementZoneKind;
        public int FlightLevel = -1;
        public int WalkRate = 1;
        public int IdleRate;
        public int ThreatPosed;
        public int LeadershipRating = 5;
        public bool Trainable = true;
        public bool AIBuildThis;
        public bool AllowedToStartInMultiplayer = true;
        public bool IsBaseDefense;
        public bool ConstructionYard;
        public bool AirportBound;
        public bool Fighter;
        public bool Landable;
        public bool JumpJet;
        // 跳喷参数（参考 rules/jumpjet_params.rs，构造器种子值）
        public int JumpjetTurnRate = 4;
        public int JumpjetSpeed = 14;
        public float JumpjetClimb = 5f;
        public float JumpjetCrash = 5f;
        public int JumpjetHeight = 500;
        public float JumpjetAccel = 2f;
        public float JumpjetWobbles = 0.15f;
        public int JumpjetDeviation = 40;
        public bool JumpjetNoWobbles;
        // 飞行姿态（参考 object_type/reader.rs：角度存弧度，-1 保留构造器默认）
        public double PitchSpeed = 0.25;
        public double RollAngle;          // 缺省 30° 弧度
        public double PitchAngle;          // 缺省 20° 弧度
        public bool NoSpawnAlt;
        public string WaterImage;
        public bool SensorArray;
        public bool HunterSeekerIgnore;
        public int? DesignatorRange;
        public int? InhibitorRange;
        public bool ChronoshiftAllow = true;
        public bool ChronoshiftIsVehicle;
        public bool ChronoshiftCrushable = true;
        public bool ResourceGatherer;
        public bool ResourceDestination;
        public bool Crushable;
        public bool ConsideredAircraft;
        public bool HoverAttack;
        public bool BalloonHover;
        public bool Teleporter;
        public string ShieldType;
        public int Soylent;
        public bool DontScore;
        public int DeployFacing;
        public bool Spawned;
        public bool Cloning;
        public bool IsTrain;
        public bool IsDropship;
        public bool IsSimpleDeployer;

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Cost = sec.GetInt("Cost", 0);
            Strength = sec.GetInt("Strength", 0);
            ArmorName = sec.GetString("Armor", string.Empty);
            ArmorIndex = ArmorTypes.IndexOf(ArmorName);
            Speed = sec.GetInt("Speed", 0);
            Sight = sec.GetInt("Sight", 0);
            TechLevel = sec.GetInt("TechLevel", -1);
            ROT = sec.GetInt("ROT", 0);
            Owner.AddRange(MapIniDocument.SplitComma(sec.GetString("Owner", string.Empty)));
            Prerequisite.AddRange(MapIniDocument.SplitComma(sec.GetString("Prerequisite", string.Empty)));
            PrerequisiteOverride.AddRange(MapIniDocument.SplitComma(sec.GetString("PrerequisiteOverride", string.Empty)));
            Primary = sec.GetString("Primary", string.Empty);
            Secondary = sec.GetString("Secondary", string.Empty);
            Category = sec.GetString("Category", string.Empty);
            DeploysInto = sec.GetString("DeploysInto", string.Empty);
            Crewed = sec.GetBool("Crewed");
            Crusher = sec.GetBool("Crusher");
            CrateGoodie = sec.GetBool("CrateGoodie");
            HasTurret = sec.GetBool("Turret", false);

            BuildCat = sec.GetString("BuildCat", string.Empty);
            BuildLimit = sec.GetInt("BuildLimit", int.MaxValue);
            AdjacentCount = sec.GetInt("Adjacent", 3);
            ProtectWithWall = sec.GetBool("ProtectWithWall");
            WantsExtraSpace = sec.GetBool("WantsExtraSpace");
            Bib = sec.GetBool("Bib");
            Gate = sec.GetBool("Gate");
            Storage = sec.GetInt("Storage", 0);
            Repairable = sec.GetBool("Repairable", true);
            Capturable = sec.GetBool("Capturable");
            Deployer = sec.GetBool("Deployer");
            Harvester = sec.GetBool("Harvester");
            Refinery = sec.GetBool("Refinery");
            Radar = sec.GetBool("Radar");
            GapGenerator = sec.GetBool("GapGenerator");
            SpySat = sec.GetBool("SpySat");
            NukeSilo = sec.GetBool("NukeSilo");
            SuperWeapon = sec.GetString("SuperWeapon", string.Empty);
            Ammo = sec.GetInt("Ammo", -1);
            Reload = sec.GetInt("Reload", 0);
            Locomotor = sec.GetString("Locomotor", string.Empty);
            MovementZone = sec.GetString("MovementZone", string.Empty);
            LocomotorKind = LocomotorTypes.KindFromClsid(Locomotor);
            SpeedType = LocomotorTypes.SpeedTypeFromIni(sec.GetString("SpeedType", string.Empty));
            MovementZoneKind = LocomotorTypes.MovementZoneFromIni(MovementZone);
            FlightLevel = sec.GetInt("FlightLevel", -1);
            WalkRate = sec.GetInt("WalkRate", 1);
            IdleRate = sec.GetInt("IdleRate", 0);
            ThreatPosed = sec.GetInt("ThreatPosed", 0);
            LeadershipRating = sec.GetInt("LeadershipRating", 5);
            Trainable = sec.GetBool("Trainable", Category != "Building");
            AIBuildThis = sec.GetBool("AIBuildThis");
            AllowedToStartInMultiplayer = sec.GetBool("AllowedToStartInMultiplayer", true);
            IsBaseDefense = sec.GetBool("IsBaseDefense");
            ConstructionYard = sec.GetBool("ConstructionYard");
            AirportBound = sec.GetBool("AirportBound");
            Fighter = sec.GetBool("Fighter");
            Landable = sec.GetBool("Landable");
            JumpJet = sec.GetBool("JumpJet");
            JumpjetTurnRate = sec.GetInt("JumpjetTurnRate", 4);
            JumpjetSpeed = sec.GetInt("JumpjetSpeed", 14);
            JumpjetClimb = (float)sec.GetDouble("JumpjetClimb", 5.0);
            JumpjetCrash = (float)sec.GetDouble("JumpjetCrash", 5.0);
            JumpjetHeight = sec.GetInt("JumpjetHeight", 500);
            JumpjetAccel = (float)sec.GetDouble("JumpjetAccel", 2.0);
            JumpjetWobbles = (float)sec.GetDouble("JumpjetWobbles", 0.15);
            JumpjetDeviation = sec.GetInt("JumpjetDeviation", 40);
            JumpjetNoWobbles = sec.GetBool("JumpjetNoWobbles", false);
            RollAngle = 30.0 * Math.PI / 180.0;
            PitchAngle = 20.0 * Math.PI / 180.0;
            PitchSpeed = sec.GetDouble("PitchSpeed", 0.25);
            RollAngle = RulesDatabase.ReadDegreesRadians(sec, "RollAngle", 30.0, RollAngle);
            PitchAngle = RulesDatabase.ReadDegreesRadians(sec, "PitchAngle", 20.0, PitchAngle);
            NoSpawnAlt = sec.GetBool("NoSpawnAlt", false);
            WaterImage = sec.GetString("WaterImage", string.Empty);
            SensorArray = sec.GetBool("SensorArray", false);
            HunterSeekerIgnore = sec.GetBool("HunterSeeker.Ignore", false);
            DesignatorRange = sec.HasKey("DesignatorRange") ? sec.GetInt("DesignatorRange") : (int?)null;
            InhibitorRange = sec.HasKey("InhibitorRange") ? sec.GetInt("InhibitorRange") : (int?)null;
            ChronoshiftAllow = sec.GetBool("Chronoshift.Allow", true);
            ChronoshiftIsVehicle = sec.GetBool("Chronoshift.IsVehicle", false);
            ChronoshiftCrushable = sec.GetBool("Chronoshift.Crushable", true);
            ResourceGatherer = sec.GetBool("ResourceGatherer");
            ResourceDestination = sec.GetBool("ResourceDestination");
            Crushable = sec.GetBool("Crushable", Category == "Infantry");
            ConsideredAircraft = sec.GetBool("ConsideredAircraft", Category == "Aircraft");
            HoverAttack = sec.GetBool("HoverAttack");
            BalloonHover = sec.GetBool("BalloonHover");
            Teleporter = sec.GetBool("Teleporter");
            ShieldType = sec.GetString("ShieldType", string.Empty);
            Soylent = sec.GetInt("Soylent", 0);
            DontScore = sec.GetBool("DontScore");
            DeployFacing = sec.GetInt("DeployFacing", 4);
            Spawned = sec.GetBool("Spawned");
            Cloning = sec.GetBool("Cloning");
            IsTrain = sec.GetBool("IsTrain");
            IsDropship = sec.GetBool("IsDropship");
            IsSimpleDeployer = sec.GetBool("IsSimpleDeployer");
        }

        /// <summary>Owner 列表是否含该国家（空 Owner = 谁都能造）。</summary>
        public bool OwnedBy(string countryId)
        {
            if (Owner.Count == 0) return true;
            for (var i = 0; i < Owner.Count; i++)
                if (string.Equals(Owner[i], countryId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    public sealed class UnitTypeClass : TechnoTypeClass
    {
        public static readonly List<UnitTypeClass> Array = new List<UnitTypeClass>();
        public static UnitTypeClass Find(string id) => TypeTables.Find(Array, id);
        public static UnitTypeClass FindByIndex(int i) => TypeTables.FindByIndex(Array, i);
    }

    public sealed class InfantryTypeClass : TechnoTypeClass
    {
        public static readonly List<InfantryTypeClass> Array = new List<InfantryTypeClass>();
        public static InfantryTypeClass Find(string id) => TypeTables.Find(Array, id);
        public static InfantryTypeClass FindByIndex(int i) => TypeTables.FindByIndex(Array, i);
    }

    public sealed class AircraftTypeClass : TechnoTypeClass
    {
        public static readonly List<AircraftTypeClass> Array = new List<AircraftTypeClass>();
        public static AircraftTypeClass Find(string id) => TypeTables.Find(Array, id);
        public static AircraftTypeClass FindByIndex(int i) => TypeTables.FindByIndex(Array, i);
    }

    public sealed class BuildingTypeClass : TechnoTypeClass
    {
        public static readonly List<BuildingTypeClass> Array = new List<BuildingTypeClass>();
        public int Power;
        public int PowerDrain;      // 原版用负的 Power 表示耗电；这里保留两项并按符号归一
        public bool BaseNormal;
        public bool Unsellable;
        public bool WallOwner;
        // ---- 建筑专属字段（按参考 reader 口径）----
        public bool Powered;
        public bool PoweredSpecial;
        public bool TogglePower = true;
        public bool LeaveRubble;
        public bool CanBeOccupied;
        public bool BridgeRepairHut;
        public bool FirestormWall;
        public bool Spyable;
        public bool UnitRepair;
        public bool ClickRepairable = true;
        public bool HoverPad;
        public bool IsTemple;
        public bool IsPlug;
        public int AntiAirValue;
        public int AntiArmorValue;
        public int AntiInfantryValue;
        public bool WeaponsFactory;
        public bool EligibileForAllyBuilding;
        public bool CanC4 = true;
        public double GateCloseDelay;
        public bool SuperWeaponPerBuilding;
        public bool CrateBeneath;
        public static BuildingTypeClass Find(string id) => TypeTables.Find(Array, id);
        public static BuildingTypeClass FindByIndex(int i) => TypeTables.FindByIndex(Array, i);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            var power = sec.GetInt("Power", 0);
            if (power < 0) { PowerDrain = -power; Power = 0; }
            else { Power = power; PowerDrain = 0; }
            BaseNormal = sec.GetBool("BaseNormal", true);
            Unsellable = sec.GetBool("Unsellable");
            WallOwner = sec.GetBool("WallOwner");
            Powered = sec.GetBool("Powered");
            PoweredSpecial = sec.GetBool("PoweredSpecial");
            TogglePower = sec.GetBool("TogglePower", true);
            LeaveRubble = sec.GetBool("LeaveRubble");
            CanBeOccupied = sec.GetBool("CanBeOccupied");
            BridgeRepairHut = sec.GetBool("BridgeRepairHut");
            FirestormWall = sec.GetBool("FirestormWall");
            Spyable = sec.GetBool("Spyable");
            UnitRepair = sec.GetBool("UnitRepair");
            ClickRepairable = sec.GetBool("ClickRepairable", true);
            HoverPad = sec.GetBool("HoverPad");
            IsTemple = sec.GetBool("IsTemple");
            IsPlug = sec.GetBool("IsPlug");
            AntiAirValue = sec.GetInt("AntiAirValue", 0);
            AntiArmorValue = sec.GetInt("AntiArmorValue", 0);
            AntiInfantryValue = sec.GetInt("AntiInfantryValue", 0);
            WeaponsFactory = sec.GetBool("WeaponsFactory");
            EligibileForAllyBuilding = sec.GetBool("EligibileForAllyBuilding");
            CanC4 = sec.GetBool("CanC4", true);
            GateCloseDelay = sec.GetDouble("GateCloseDelay", 0.0);
            SuperWeaponPerBuilding = sec.GetBool("SuperWeaponPerBuilding");
            CrateBeneath = sec.GetBool("CrateBeneath");
        }
    }

    /// <summary>BulletTypeClass（[Projectiles] 表，参考 rules/projectile_type.rs）。</summary>
    public sealed class BulletTypeClass : TypeClassBase
    {
        public static readonly List<BulletTypeClass> Array = new List<BulletTypeClass>();
        public string Image;
        public bool Inviso;
        public bool Arcing;
        public int Anim;
        public string AnimName;
        // 瞄准
        public bool AA;
        public bool AG = true;
        public int ROT;
        public bool Inaccurate;
        // 飞行行为标志
        public bool Airburst;
        public bool Floater;
        public bool SubjectToCliffs;
        public bool SubjectToElevation;
        public bool SubjectToWalls;
        public bool VeryHigh;
        public bool Shadow = true;
        public bool Dropping;
        public bool Level;
        public bool Proximity;
        public bool Ranged;
        public bool Rotates = true;
        public bool FlakScatter;
        public bool Degenerates;
        public bool Bouncy;
        public bool AnimPalette;
        public bool FirersPalette;
        public bool Scalable;
        public bool Vertical;
        public bool Flat;
        // 整数/浮点
        public int Cluster = 1;
        public int ShrapnelCount;
        public int DetonationAltitude;
        public int Acceleration = 3;
        public int CourseLockDuration;
        public int SpawnDelay = 3;
        public int Arm;
        public int AnimLow;
        public int AnimHigh;
        public int AnimRate;
        public double Elasticity = 0.75;
        public string Trailer;
        public string ShrapnelWeapon;
        public string AirburstWeapon;
        public bool UseLineTrail;
        public int LineTrailColorDecrement;
        public static BulletTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Image = sec.GetString("Image", string.Empty);
            Inviso = sec.GetBool("Inviso");
            Arcing = sec.GetBool("Arcing");
            AnimName = sec.GetString("Anim", string.Empty);
            Anim = sec.GetInt("Anim", 0);
            AA = sec.GetBool("AA");
            AG = sec.GetBool("AG", true);
            ROT = sec.GetInt("ROT", 0);
            Inaccurate = sec.GetBool("Inaccurate");
            Airburst = sec.GetBool("Airburst");
            Floater = sec.GetBool("Floater");
            SubjectToCliffs = sec.GetBool("SubjectToCliffs");
            SubjectToElevation = sec.GetBool("SubjectToElevation");
            SubjectToWalls = sec.GetBool("SubjectToWalls");
            VeryHigh = sec.GetBool("VeryHigh");
            Shadow = sec.GetBool("Shadow", true);
            Dropping = sec.GetBool("Dropping");
            Level = sec.GetBool("Level");
            Proximity = sec.GetBool("Proximity");
            Ranged = sec.GetBool("Ranged");
            Rotates = sec.GetBool("Rotates", true);
            FlakScatter = sec.GetBool("FlakScatter");
            Degenerates = sec.GetBool("Degenerates");
            Bouncy = sec.GetBool("Bouncy");
            AnimPalette = sec.GetBool("AnimPalette");
            FirersPalette = sec.GetBool("FirersPalette");
            Scalable = sec.GetBool("Scalable");
            Vertical = sec.GetBool("Vertical");
            Flat = sec.GetBool("Flat");
            Cluster = sec.GetInt("Cluster", 1);
            ShrapnelCount = sec.GetInt("ShrapnelCount", 0);
            DetonationAltitude = sec.GetInt("DetonationAltitude", 0);
            Acceleration = sec.GetInt("Acceleration", 3);
            CourseLockDuration = sec.GetInt("CourseLockDuration", 0);
            SpawnDelay = sec.GetInt("SpawnDelay", 3);
            Arm = sec.GetInt("Arm", 0);
            AnimLow = sec.GetInt("AnimLow", 0);
            AnimHigh = sec.GetInt("AnimHigh", 0);
            AnimRate = sec.GetInt("AnimRate", 0);
            Elasticity = sec.GetDouble("Elasticity", 0.75);
            Trailer = sec.GetString("Trailer", string.Empty);
            ShrapnelWeapon = sec.GetString("ShrapnelWeapon", string.Empty);
            AirburstWeapon = sec.GetString("AirburstWeapon", string.Empty);
            UseLineTrail = sec.GetBool("UseLineTrail");
            LineTrailColorDecrement = sec.GetInt("LineTrailColorDecrement", 0);
        }
    }

    /// <summary>WeaponTypeClass（武器段，参考 rules/weapon_type.rs + 有序处理 pass_families）。</summary>
    public sealed class WeaponTypeClass : TypeClassBase
    {
        public static readonly List<WeaponTypeClass> Array = new List<WeaponTypeClass>();
        public int Damage;
        public int RangeLeptons;
        public int MinRangeLeptons;
        public int ROF;
        public int Speed;               // ReadSpeed 口径：0..100 → (v&lt;&lt;8)/100，上限 255
        public int Burst = 1;
        public string Projectile;
        public string Warhead;
        public readonly List<string> Report = new List<string>();
        public int AmbientDamage;
        public int LaserDuration = 10;
        public int RadLevel;
        public string RadType;
        public bool RevealOnFire = true;
        public bool TerrainFire;
        public bool OmniFire;
        public bool DistributedWeaponFire;
        public bool IsRailgun;
        public bool Lobber;
        public bool Bright;
        public bool IsSonic;
        public bool Spawner;
        public bool LimboLaunch;
        public bool DecloakToFire = true;
        public bool CellRangefinding;
        public bool FireOnce;
        public bool NeverUse;
        public bool InfiniteMindControl;
        public bool FireWhileMoving = true;
        public bool DrainWeapon;
        public bool FireInTransport = true;
        public bool Suicide;
        public bool TurboBoost;
        public bool Supress;
        public bool Camera;
        public bool Charges;
        public bool IsLaser;
        public bool DiskLaser;
        public bool IsLine;
        public bool IsBigLaser;
        public bool IsHouseColor;
        public bool IonSensitive;
        public bool AreaFire;
        public bool IsElectricBolt;
        public bool DrawBoltAsLaser;
        public bool IsAlternateColor;
        public bool IsRadBeam;
        public bool IsRadEruption;
        public bool IsMagBeam;
        public int ProjectileRange = 100000;
        public string AssaultAnim;
        public string OccupantAnim;
        public string OpenToppedAnim;
        public string AttachedParticleSystem;
        public readonly List<string> Anim = new List<string>();
        public string LaserInnerColor;
        public string LaserOuterColor;
        public string LaserOuterSpread;

        public static WeaponTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Damage = sec.GetInt("Damage", 0);
            RangeLeptons = sec.GetRangeLeptons("Range", 0);
            MinRangeLeptons = sec.GetRangeLeptons("MinimumRange", 0);
            ROF = sec.GetInt("ROF", 0);
            Speed = sec.GetWeaponSpeed("Speed", 0);
            Burst = sec.GetInt("Burst", 1);
            Projectile = sec.GetString("Projectile", string.Empty);
            Warhead = sec.GetString("Warhead", string.Empty);
            Report.AddRange(MapIniDocument.SplitComma(sec.GetString("Report", string.Empty)));
            AmbientDamage = sec.GetInt("AmbientDamage", 0);
            LaserDuration = sec.GetInt("LaserDuration", 10);
            RadLevel = sec.GetInt("RadLevel", 0);
            RadType = sec.GetString("RadType", string.Empty);
            RevealOnFire = sec.GetBool("RevealOnFire", true);
            TerrainFire = sec.GetBool("TerrainFire");
            OmniFire = sec.GetBool("OmniFire");
            DistributedWeaponFire = sec.GetBool("DistributedWeaponFire");
            IsRailgun = sec.GetBool("IsRailgun");
            Lobber = sec.GetBool("Lobber");
            Bright = sec.GetBool("Bright");
            IsSonic = sec.GetBool("IsSonic");
            Spawner = sec.GetBool("Spawner");
            LimboLaunch = sec.GetBool("LimboLaunch");
            DecloakToFire = sec.GetBool("DecloakToFire", true);
            CellRangefinding = sec.GetBool("CellRangefinding");
            FireOnce = sec.GetBool("FireOnce");
            NeverUse = sec.GetBool("NeverUse");
            InfiniteMindControl = sec.GetBool("InfiniteMindControl");
            FireWhileMoving = sec.GetBool("FireWhileMoving", true);
            DrainWeapon = sec.GetBool("DrainWeapon");
            FireInTransport = sec.GetBool("FireInTransport", true);
            Suicide = sec.GetBool("Suicide");
            TurboBoost = sec.GetBool("TurboBoost");
            Supress = sec.GetBool("Supress");
            Camera = sec.GetBool("Camera");
            Charges = sec.GetBool("Charges");
            IsLaser = sec.GetBool("IsLaser");
            DiskLaser = sec.GetBool("DiskLaser");
            IsLine = sec.GetBool("IsLine");
            IsBigLaser = sec.GetBool("IsBigLaser");
            IsHouseColor = sec.GetBool("IsHouseColor");
            IonSensitive = sec.GetBool("IonSensitive");
            AreaFire = sec.GetBool("AreaFire");
            IsElectricBolt = sec.GetBool("IsElectricBolt");
            DrawBoltAsLaser = sec.GetBool("DrawBoltAsLaser");
            IsAlternateColor = sec.GetBool("IsAlternateColor");
            IsRadBeam = sec.GetBool("IsRadBeam");
            IsRadEruption = sec.GetBool("IsRadEruption");
            IsMagBeam = sec.GetBool("IsMagBeam");
            ProjectileRange = sec.GetRangeLeptons("ProjectileRange", 100000);
            AssaultAnim = sec.GetString("AssaultAnim", string.Empty);
            OccupantAnim = sec.GetString("OccupantAnim", string.Empty);
            OpenToppedAnim = sec.GetString("OpenToppedAnim", string.Empty);
            AttachedParticleSystem = sec.GetString("AttachedParticleSystem", string.Empty);
            Anim.AddRange(MapIniDocument.SplitComma(sec.GetString("Anim", string.Empty)));
            LaserInnerColor = sec.GetString("LaserInnerColor", string.Empty);
            LaserOuterColor = sec.GetString("LaserOuterColor", string.Empty);
            LaserOuterSpread = sec.GetString("LaserOuterSpread", string.Empty);
        }
    }

    /// <summary>WarheadTypeClass（弹头段，参考 rules/warhead_type.rs）。</summary>
    public sealed class WarheadTypeClass : TypeClassBase
    {
        public static readonly List<WarheadTypeClass> Array = new List<WarheadTypeClass>();
        public readonly List<double> Verses = new List<double>(11);
        public double CellSpread;
        public double CellInset;
        public double PercentAtMax = 1.0;
        public double DelayKillAtMax = 1.0;
        public bool CausesDelayKill;
        public int DelayKillFrames = 5;
        public bool Wall;
        public bool Wood;
        public bool Fire;
        public bool PenetratesBunker;
        public bool AffectsAllies = true;
        public bool? AffectsOwner;
        public bool AffectsEnemies = true;
        public bool AffectsNeutral = true;
        public bool PsychicDamage;
        public int InfDeath;
        public double Deform;
        public int DeformThreshold;
        public int DeformDepth = 1;
        public int DeformRadius;
        public bool Bullets;
        public bool Veinhole;
        public int ShakeXlo, ShakeXhi, ShakeYlo, ShakeYhi;
        public bool Rocker;
        public bool DirectRocker;
        public bool Tiberium;
        public bool Bright;
        public bool CLDisableRed, CLDisableGreen, CLDisableBlue;
        public double CombatLightSize;
        public double ProneDamage = 1.0;
        public bool WallAbsoluteDestroyer;
        public bool Temporal;
        public bool IsLocomotor;
        public string Locomotor;
        public bool Parasite;
        public bool Psychedelic;
        public bool IvanBomb;
        public bool MindControl;
        public bool Poison;
        public bool Airstrike;
        public bool Radiation;
        public bool Culling;
        public int Paralyzes;
        public bool Sonic;
        public bool MakesDisguise;
        public bool ElectricAssault;
        public bool BombDisarm;
        public bool NukeMaker;
        public bool AffectsGround = true;
        public bool AffectsAir = true;
        public bool EffectsRequireVerses = true;
        public int MaxDebris;
        public int MinDebris;

        public static WarheadTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Verses.Clear();
            var verses = sec.GetVerses("Verses", 1.0);
            if (verses != null) Verses.AddRange(verses);
            while (Verses.Count < 11) Verses.Add(1.0);
            CellSpread = sec.GetDouble("CellSpread", 0.0);
            CellInset = sec.GetDouble("CellInset", 0.0);
            PercentAtMax = sec.GetDouble("PercentAtMax", 1.0);
            DelayKillAtMax = sec.GetDouble("DelayKillAtMax", 1.0);
            CausesDelayKill = sec.GetBool("CausesDelayKill");
            DelayKillFrames = sec.GetInt("DelayKillFrames", 5);
            Wall = sec.GetBool("Wall");
            Wood = sec.GetBool("Wood");
            Fire = sec.GetBool("Fire");
            PenetratesBunker = sec.GetBool("PenetratesBunker");
            AffectsAllies = sec.GetBool("AffectsAllies", true);
            AffectsOwner = sec.HasKey("AffectsOwner") ? sec.GetBool("AffectsOwner", false) : (bool?)null;
            AffectsEnemies = sec.GetBool("AffectsEnemies", true);
            AffectsNeutral = sec.GetBool("AffectsNeutral", true);
            PsychicDamage = sec.GetBool("PsychicDamage");
            InfDeath = sec.GetInt("InfDeath", 0);
            Deform = sec.GetDouble("Deform", 0.0);
            DeformThreshold = sec.GetInt("DeformThreshhold", 0);
            DeformDepth = sec.GetInt("DeformDepth", 1);
            DeformRadius = sec.GetInt("DeformRadius", 0);
            Bullets = sec.GetBool("Bullets");
            Veinhole = sec.GetBool("Veinhole");
            ShakeXlo = sec.GetInt("ShakeXlo", 0);
            ShakeXhi = sec.GetInt("ShakeXhi", 0);
            ShakeYlo = sec.GetInt("ShakeYlo", 0);
            ShakeYhi = sec.GetInt("ShakeYhi", 0);
            Rocker = sec.GetBool("Rocker");
            DirectRocker = sec.GetBool("DirectRocker");
            Tiberium = sec.GetBool("Tiberium");
            Bright = sec.GetBool("Bright");
            CLDisableRed = sec.GetBool("CLDisableRed");
            CLDisableGreen = sec.GetBool("CLDisableGreen");
            CLDisableBlue = sec.GetBool("CLDisableBlue");
            CombatLightSize = sec.GetDouble("CombatLightSize", 0.0);
            ProneDamage = sec.GetDouble("ProneDamage", 1.0);
            WallAbsoluteDestroyer = sec.GetBool("WallAbsoluteDestroyer");
            Temporal = sec.GetBool("Temporal");
            IsLocomotor = sec.GetBool("IsLocomotor");
            Locomotor = sec.GetString("Locomotor", string.Empty);
            Parasite = sec.GetBool("Parasite");
            Psychedelic = sec.GetBool("Psychedelic");
            IvanBomb = sec.GetBool("IvanBomb");
            MindControl = sec.GetBool("MindControl");
            Poison = sec.GetBool("Poison");
            Airstrike = sec.GetBool("Airstrike");
            Radiation = sec.GetBool("Radiation");
            Culling = sec.GetBool("Culling");
            Paralyzes = sec.GetInt("Paralyzes", 0);
            Sonic = sec.GetBool("Sonic");
            MakesDisguise = sec.GetBool("MakesDisguise");
            ElectricAssault = sec.GetBool("ElectricAssault");
            BombDisarm = sec.GetBool("BombDisarm");
            NukeMaker = sec.GetBool("NukeMaker");
            AffectsGround = sec.GetBool("AffectsGround", true);
            AffectsAir = sec.GetBool("AffectsAir", true);
            EffectsRequireVerses = sec.GetBool("EffectsRequireVerses", true);
            MinDebris = Math.Max(0, sec.GetInt("MinDebris", 0));
            MaxDebris = Math.Max(MinDebris, sec.GetInt("MaxDebris", 0));
        }
    }

    /// <summary>TiberiumTypeClass（[Tiberiums] 表，参考 rules/tiberium_type.rs）。</summary>
    public sealed class TiberiumTypeClass : TypeClassBase
    {
        public static readonly List<TiberiumTypeClass> Array = new List<TiberiumTypeClass>();
        public int Value;
        public int Power;
        public int Growth;
        public double GrowthPercentage = 0.1;
        public int Spread;
        public double SpreadPercentage = 0.1;
        public int Image = -1;
        public static TiberiumTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Value = sec.GetInt("Value", 0);
            Power = sec.GetInt("Power", 0);
            Growth = sec.GetInt("Growth", 0);
            GrowthPercentage = sec.GetDouble("GrowthPercentage", 0.1);
            Spread = sec.GetInt("Spread", 0);
            SpreadPercentage = sec.GetDouble("SpreadPercentage", 0.1);
            Image = sec.GetInt("Image", -1);
        }
    }

    /// <summary>
    /// OverlayTypeClass（[OverlayTypes] 表；参考 rules/overlay_types.rs OverlayTypeFlags）。
    /// 序号保留数字键跳号；标志位缺省值照参考读取口径。Land 地形语义表未落地，先存原始 Land 名。
    /// </summary>
    public sealed class OverlayTypeClass : TypeClassBase
    {
        public static readonly List<OverlayTypeClass> Array = new List<OverlayTypeClass>();

        public bool Tiberium;
        public string LandName = string.Empty;
        public int Strength = 1;
        public bool ChainReaction;
        public bool Wall;
        public bool DrawFlat = true;
        public bool ArmorIsWood;
        public bool LegalTarget = true;
        public bool IsVeins;
        public bool IsVeinholeMonster;
        public bool Gate;
        public bool Crushable;
        public bool Crate;
        public bool CrateTrigger;
        public bool Overrides;
        public bool IsRubble;
        public bool IsARock;
        public bool NoUseTileLandType = true;
        public string RadarColor = string.Empty;

        public static OverlayTypeClass Find(string id) => TypeTables.Find(Array, id);
        public static OverlayTypeClass FindByIndex(int index) => TypeTables.FindByIndex(Array, index);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Tiberium = sec.GetBool("Tiberium", false);
            LandName = sec.GetString("Land", string.Empty);
            Strength = sec.GetInt("Strength", 1);
            ChainReaction = sec.GetBool("ChainReaction", false);
            Wall = sec.GetBool("Wall", false);
            DrawFlat = sec.GetBool("DrawFlat", true);
            ArmorIsWood = string.Equals(sec.GetString("Armor", string.Empty).Trim(), "wood", StringComparison.OrdinalIgnoreCase);
            LegalTarget = sec.GetBool("LegalTarget", true);
            IsVeins = sec.GetBool("IsVeins", false);
            IsVeinholeMonster = sec.GetBool("IsVeinholeMonster", false);
            Gate = sec.GetBool("Gate", false);
            Crushable = sec.GetBool("Crushable", false);
            Crate = sec.GetBool("Crate", false);
            CrateTrigger = sec.GetBool("CrateTrigger", false);
            Overrides = sec.GetBool("Overrides", false);
            IsRubble = sec.GetBool("IsRubble", false);
            IsARock = sec.GetBool("IsARock", false);
            NoUseTileLandType = sec.GetBool("NoUseTileLandType", true);
            RadarColor = sec.GetString("RadarColor", string.Empty);
        }
    }

    /// <summary>
    /// TerrainTypeClass（[TerrainTypes] 表；参考 rules/terrain_object_type.rs）。
    /// Strength 缺省 -1（待 [General] TreeStrength 回填）；LegalTarget 构造缺省 false；
    /// IsVeinhole=true 时清 IsLogic 并强制 LegalTarget=true（参考口径）。
    /// </summary>
    public sealed class TerrainTypeClass : TypeClassBase
    {
        public static readonly List<TerrainTypeClass> Array = new List<TerrainTypeClass>();

        public string ArmorName = "wood";
        public int Strength = -1;
        public bool SpawnsTiberium;
        public bool IsAnimated;
        public int AnimationRate;
        public double AnimationProbability;
        public bool Immune;
        public bool LegalTarget;
        public bool IsFlammable;
        public bool Bombable = true;
        public bool Insignificant = true;
        public bool RadarInvisible = true;
        public bool WaterBound;
        public bool IsVeinhole;
        public bool IsLogic = true;
        public int TemperateOccupationBits = 7;
        public int SnowOccupationBits = 7;
        public string FoundationName = string.Empty;

        public static TerrainTypeClass Find(string id) => TypeTables.Find(Array, id);
        public static TerrainTypeClass FindByIndex(int index) => TypeTables.FindByIndex(Array, index);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            ArmorName = sec.GetString("Armor", "wood");
            Strength = sec.GetInt("Strength", -1);
            SpawnsTiberium = sec.GetBool("SpawnsTiberium", false);
            IsAnimated = sec.GetBool("IsAnimated", false);
            AnimationRate = sec.GetInt("AnimationRate", 0);
            AnimationProbability = sec.GetDouble("AnimationProbability", 0.0);
            Immune = sec.GetBool("Immune", false);
            LegalTarget = sec.GetBool("LegalTarget", false);
            IsFlammable = sec.GetBool("IsFlammable", false);
            Bombable = sec.GetBool("Bombable", true);
            Insignificant = sec.GetBool("Insignificant", true);
            RadarInvisible = sec.GetBool("RadarInvisible", true);
            WaterBound = sec.GetBool("WaterBound", false);
            IsVeinhole = sec.GetBool("IsVeinhole", false);
            if (IsVeinhole) { IsLogic = false; LegalTarget = true; }
            TemperateOccupationBits = sec.GetInt("TemperateOccupationBits", 7);
            SnowOccupationBits = sec.GetInt("SnowOccupationBits", 7);
        }
    }

    /// <summary>SmudgeTypeClass（[SmudgeTypes] 表；参考 rules/smudge_type.rs）。</summary>
    public sealed class SmudgeTypeClass : TypeClassBase
    {
        public static readonly List<SmudgeTypeClass> Array = new List<SmudgeTypeClass>();

        public string AlphaImage = string.Empty;
        public bool Crater;
        public bool Burn;
        public int Width = 1;
        public int Height = 1;

        public static SmudgeTypeClass Find(string id) => TypeTables.Find(Array, id);
        public static SmudgeTypeClass FindByIndex(int index) => TypeTables.FindByIndex(Array, index);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Crater = sec.GetBool("Crater", false);
            Burn = sec.GetBool("Burn", false);
            Width = sec.GetInt("Width", 1);
            Height = sec.GetInt("Height", 1);
            var img = sec.GetString("Image", string.Empty).Trim();
            if (img.Length > 0) ImageId = img;
        }
    }

    /// <summary>AnimTypeClass（[Animations] 表）。</summary>
    public sealed class AnimTypeClass : TypeClassBase
    {
        public static readonly List<AnimTypeClass> Array = new List<AnimTypeClass>();
        public string Image;
        public int Rate;
        public int LoopCount;
        public static AnimTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Image = sec.GetString("Image", string.Empty);
            Rate = sec.GetInt("Rate", 0);
            LoopCount = sec.GetInt("LoopCount", 0);
        }
    }

    /// <summary>SuperWeaponTypeClass（[SuperWeaponTypes] 表，参考 rules/superweapon_type.rs）。</summary>
    public sealed class SuperWeaponTypeClass : TypeClassBase
    {
        public static readonly List<SuperWeaponTypeClass> Array = new List<SuperWeaponTypeClass>();
        public string Type = string.Empty;      // 原版枚举名：IonCannon / Nuke / LightningStorm ...
        public int RechargeTime = 4500;         // 帧；RechargeTime 写分钟，×900 折算，0 保留构造默认
        public bool IsPowered = true;
        public bool IsHouseAlways;
        public string Action;
        public string SidebarImage;
        public string SidebarPalette;
        public bool ShowTimer;
        public bool DisableableFromShell;
        public bool UseChargeDrain;
        public bool PreClick;
        public bool PostClick;
        public string SpecialSound;
        public string StartSound;
        public int FlashSidebarTabFrames = -1;
        public bool AIDefendAgainst;
        public bool ManualControl;
        public bool ShowCameo = true;
        public int LineMultiplier;
        public double Range = -1.0;
        // 目标选择状态（参考 superweapon_targeting.rs 构造器默认）
        public bool ManualFire = true;
        public bool AutoFire;
        public bool FireIntoShroud = true;
        public bool UseAITargeting;
        public string AffectsHouse = string.Empty;
        public string AffectsTarget = string.Empty;
        public string RequiresTarget = string.Empty;
        public string RequiresHouse = string.Empty;
        public string AiRequiresTarget = string.Empty;
        public string AiRequiresHouse = string.Empty;
        public string AiTargeting = string.Empty;
        public string AiTargetConstraints = string.Empty;
        public string AiTargetPreference = string.Empty;
        public double RangeMinimum = -1.0;
        public double RangeMaximum = -1.0;
        public readonly List<string> Designators = new List<string>();
        public bool AnyDesignator;
        public readonly List<string> Inhibitors = new List<string>();
        public bool AnyInhibitor;

        public static SuperWeaponTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Type = sec.GetString("Type", string.Empty);
            var minutes = sec.GetDouble("RechargeTime", 0.0);
            RechargeTime = minutes == 0.0 ? 4500 : (int)System.Math.Truncate(minutes * 900.0);
            IsPowered = sec.GetBool("IsPowered", true);
            IsHouseAlways = sec.GetBool("IsHouseAlways");
            Action = sec.GetString("Action", string.Empty);
            SidebarImage = sec.GetString("SidebarImage", string.Empty);
            SidebarPalette = sec.GetString("SidebarPalette", string.Empty);
            ShowTimer = sec.GetBool("ShowTimer");
            DisableableFromShell = sec.GetBool("DisableableFromShell");
            UseChargeDrain = sec.GetBool("UseChargeDrain");
            PreClick = sec.GetBool("PreClick");
            PostClick = sec.GetBool("PostClick");
            SpecialSound = sec.GetString("SpecialSound", string.Empty);
            StartSound = sec.GetString("StartSound", string.Empty);
            FlashSidebarTabFrames = sec.GetInt("FlashSidebarTabFrames", -1);
            AIDefendAgainst = sec.GetBool("AIDefendAgainst");
            ManualControl = sec.GetBool("ManualControl");
            ShowCameo = sec.GetBool("SW.ShowCameo", true);
            LineMultiplier = sec.GetInt("LineMultiplier", 0);
            Range = sec.GetDouble("Range", -1.0);
            ManualFire = sec.GetBool("SW.ManualFire", true);
            AutoFire = sec.GetBool("SW.AutoFire", false);
            FireIntoShroud = sec.GetBool("SW.FireIntoShroud", true);
            UseAITargeting = sec.GetBool("SW.UseAITargeting", false);
            AffectsHouse = sec.GetString("SW.AffectsHouse", string.Empty);
            AffectsTarget = sec.GetString("SW.AffectsTarget", string.Empty);
            RequiresTarget = sec.GetString("SW.RequiresTarget", string.Empty);
            RequiresHouse = sec.GetString("SW.RequiresHouse", string.Empty);
            AiRequiresTarget = sec.GetString("SW.AIRequiresTarget", string.Empty);
            AiRequiresHouse = sec.GetString("SW.AIRequiresHouse", string.Empty);
            AiTargeting = sec.GetString("SW.AITargeting", string.Empty);
            AiTargetConstraints = sec.GetString("SW.AITargeting.Constraints", string.Empty);
            AiTargetPreference = sec.GetString("SW.AITargeting.Preference", string.Empty);
            RangeMinimum = sec.GetDouble("SW.RangeMinimum", -1.0);
            RangeMaximum = sec.GetDouble("SW.RangeMaximum", -1.0);
            Designators.AddRange(MapIniDocument.SplitComma(sec.GetString("SW.Designators", string.Empty)));
            AnyDesignator = sec.GetBool("SW.AnyDesignator");
            Inhibitors.AddRange(MapIniDocument.SplitComma(sec.GetString("SW.Inhibitors", string.Empty)));
            AnyInhibitor = sec.GetBool("SW.AnyInhibitor");
        }
    }

    /// <summary>[ArmorTypes] 顺序 → 装甲索引（原版 ArmorType 枚举就来自这段）。</summary>
    public static class ArmorTypes
    {
        public static readonly List<string> Array = new List<string>();

        public static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            for (var i = 0; i < Array.Count; i++)
                if (string.Equals(Array[i], name.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            // 参考口径：未知装甲名回落 none（索引 0）。
            return 0;
        }
    }

    /// <summary>移动控制器类别（参考 locomotor_type.rs LocomotorKind，按 CLSID 解析）。</summary>
    public enum LocomotorKind { Drive, Hover, Walk, Fly, Teleport, Ship, Jumpjet, Rocket, Tunnel, Mech }

    /// <summary>地形通行类型（参考 SpeedType 枚举）。</summary>
    public enum SpeedType { Invalid = -1, Foot = 0, Track, Wheel, Hover, Winged, Float, Amphibious, FloatBeach }

    /// <summary>寻路区域（参考 MovementZone 枚举，数值即通行矩阵行号）。</summary>
    public enum MovementZone { Invalid = -1, Normal = 0, Crusher, Destroyer, AmphibiousDestroyer, AmphibiousCrusher, Amphibious, Subterranean, Infantry, InfantryDestroyer, Fly, Water, WaterBeach, CrusherAll }

    public static class LocomotorTypes
    {
        private static readonly string[] Clsids =
        {
            "{4A582741-9839-11D1-B709-00A024DDAFD1}", // Drive
            "{4A582742-9839-11D1-B709-00A024DDAFD1}", // Hover
            "{4A582744-9839-11D1-B709-00A024DDAFD1}", // Walk
            "{4A582746-9839-11D1-B709-00A024DDAFD1}", // Fly
            "{4A582747-9839-11D1-B709-00A024DDAFD1}", // Teleport
            "{2BEA74E1-7CCA-11D3-BE14-00104B62A16C}", // Ship
            "{92612C46-F71F-11D1-AC9F-006008055BB5}", // Jumpjet
            "{B7B49766-E576-11D3-9BD9-00104B972FE8}", // Rocket
            "{4A582743-9839-11D1-B709-00A024DDAFD1}", // Tunnel
            "{55D141B8-DB94-11D1-AC98-006008055BB5}", // Mech
        };

        public static LocomotorKind KindFromClsid(string text)
        {
            var normalized = (text ?? string.Empty).Trim();
            if (normalized.StartsWith("{")) normalized = normalized.TrimStart('{').TrimEnd('}');
            for (var i = 0; i < Clsids.Length; i++)
            {
                var body = Clsids[i].Substring(1, Clsids[i].Length - 2);
                if (string.Equals(body, normalized, StringComparison.OrdinalIgnoreCase))
                    return (LocomotorKind)i;
            }
            return LocomotorKind.Drive;
        }

        public static SpeedType SpeedTypeFromIni(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "foot": return SpeedType.Foot;
                case "track": return SpeedType.Track;
                case "wheel": return SpeedType.Wheel;
                case "float": return SpeedType.Float;
                case "amphibious": return SpeedType.Amphibious;
                case "winged": return SpeedType.Winged;
                case "floatbeach": return SpeedType.FloatBeach;
                case "hover": return SpeedType.Hover;
                default: return SpeedType.Invalid;
            }
        }

        public static MovementZone MovementZoneFromIni(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "normal": return MovementZone.Normal;
                case "crusher": return MovementZone.Crusher;
                case "destroyer": return MovementZone.Destroyer;
                case "amphibiousdestroyer": return MovementZone.AmphibiousDestroyer;
                case "amphibiouscrusher": return MovementZone.AmphibiousCrusher;
                case "amphibious": return MovementZone.Amphibious;
                case "subterannean":
                case "subterranean": return MovementZone.Subterranean;
                case "infantry": return MovementZone.Infantry;
                case "infantrydestroyer": return MovementZone.InfantryDestroyer;
                case "fly": return MovementZone.Fly;
                case "water": return MovementZone.Water;
                case "waterbeach": return MovementZone.WaterBeach;
                case "crusherall": return MovementZone.CrusherAll;
                default: return MovementZone.Invalid;
            }
        }
    }

    /// <summary>地形 LandType（参考 rules/terrain_rules.rs，12 类，序号即原生行号）。</summary>
    public enum LandType { Clear = 0, Road, Water, Rock, Wall, Tiberium, Beach, Rough, Ice, Railroad, Tunnel, Weeds }

    /// <summary>一类地形的通行/建造语义；SpeedCosts 顺序对齐 SpeedType（Foot…FloatBeach），0 = 该速度类型不可通行。</summary>
    public sealed class LandTypeSemantics
    {
        public bool Buildable;
        public bool GroundBlocked;
        public readonly float[] SpeedCosts = new float[8];
    }

    /// <summary>
    /// 地形通行表（参考 rules/terrain_rules.rs）：缺段保留零行（全速度类型阻断、不可建造）；
    /// 段内缺键回满速 1.0；Winged 槽硬存 1.0；数值 ≥1.0 截到 1.0，% 按 /100。
    /// GroundBlocked 硬编码：Water / Rock / Wall。
    /// </summary>
    public static class TerrainRules
    {
        public const int LandTypeCount = 12;
        public static readonly LandTypeSemantics[] Rows = new LandTypeSemantics[LandTypeCount];
        private static readonly string[] SectionNames =
            { "Clear", "Road", "Water", "Rock", "Wall", "Tiberium", "Beach", "Rough", "Ice", "Railroad", "Tunnel", "Weeds" };

        static TerrainRules()
        {
            for (var i = 0; i < LandTypeCount; i++)
            {
                Rows[i] = new LandTypeSemantics();
                Rows[i].GroundBlocked = i == (int)LandType.Water || i == (int)LandType.Rock || i == (int)LandType.Wall;
            }
        }

        public static LandTypeSemantics Row(LandType land) => Rows[(int)land];

        /// <summary>原生 subtile 地形字节 → LandType 表（参考 terrain_rules.rs TMP_TERRAIN_TO_LAND_TYPE）；越界回落 Clear。</summary>
        private static readonly int[] TmpToLand = { 0, 8, 8, 8, 8, 10, 9, 3, 3, 2, 6, 1, 1, 0, 7, 3 };
        public static LandType LandFromTmpTerrain(byte b) => b < 16 ? (LandType)TmpToLand[b] : LandType.Clear;

        public static void Load(RulesDatabase rules)
        {
            for (var i = 0; i < LandTypeCount; i++)
            {
                var sec = rules.GetSection(SectionNames[i]);
                if (sec == null) continue; // 缺段保留零行
                var row = Rows[i];
                row.Buildable = sec.GetBool("Buildable", false);
                row.SpeedCosts[(int)SpeedType.Foot] = (float)ReadCost(sec, "Foot", 1.0);
                row.SpeedCosts[(int)SpeedType.Track] = (float)ReadCost(sec, "Track", 1.0);
                row.SpeedCosts[(int)SpeedType.Wheel] = (float)ReadCost(sec, "Wheel", 1.0);
                row.SpeedCosts[(int)SpeedType.Hover] = (float)ReadCost(sec, "Hover", 1.0);
                row.SpeedCosts[(int)SpeedType.Winged] = 1f; // 原生硬存 1.0，不读 INI
                row.SpeedCosts[(int)SpeedType.Float] = (float)ReadCost(sec, "Float", 1.0);
                row.SpeedCosts[(int)SpeedType.Amphibious] = (float)ReadCost(sec, "Amphibious", 1.0);
                row.SpeedCosts[(int)SpeedType.FloatBeach] = (float)ReadCost(sec, "FloatBeach", 1.0);
            }
        }

        private static double ReadCost(IniSection sec, string key, double fallback)
        {
            var text = sec.GetString(key, null);
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            text = text.Trim();
            var percent = text.EndsWith("%");
            var num = percent ? text.Substring(0, text.Length - 1).Trim() : text;
            if (!double.TryParse(num, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) return 0.0;
            if (percent) v /= 100.0;
            if (v >= 1.0) v = 1.0;
            if (!double.IsFinite(v)) v = 0.0;
            return v;
        }
    }

    /// <summary>建造前提六组（参考 prerequisite.rs：POWER/FACTORY/BARRACKS/RADAR/TECH/PROC），成员来自 [General] 对应列表。</summary>
    public static class PrerequisiteGroups
    {
        public static readonly List<string> Power = new List<string>();
        public static readonly List<string> Factory = new List<string>();
        public static readonly List<string> Barracks = new List<string>();
        public static readonly List<string> Radar = new List<string>();
        public static readonly List<string> Tech = new List<string>();
        public static readonly List<string> Proc = new List<string>();

        public static bool TryGroup(string token, out List<string> list)
        {
            switch ((token ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "POWER": list = Power; return true;
                case "FACTORY": list = Factory; return true;
                case "BARRACKS": list = Barracks; return true;
                case "RADAR": list = Radar; return true;
                case "TECH": list = Tech; return true;
                case "PROC": list = Proc; return true;
                default: list = null; return false;
            }
        }

        public static void Load(RulesDatabase rules)
        {
            Power.Clear(); Factory.Clear(); Barracks.Clear(); Radar.Clear(); Tech.Clear(); Proc.Clear();
            Power.AddRange(MapIniDocument.SplitComma(rules.GetString("General", "PrerequisitePower", string.Empty)));
            Factory.AddRange(MapIniDocument.SplitComma(rules.GetString("General", "PrerequisiteFactory", string.Empty)));
            Barracks.AddRange(MapIniDocument.SplitComma(rules.GetString("General", "PrerequisiteBarracks", string.Empty)));
            Radar.AddRange(MapIniDocument.SplitComma(rules.GetString("General", "PrerequisiteRadar", string.Empty)));
            Tech.AddRange(MapIniDocument.SplitComma(rules.GetString("General", "PrerequisiteTech", string.Empty)));
            Proc.AddRange(MapIniDocument.SplitComma(rules.GetString("General", "PrerequisiteProc", string.Empty)));
        }
    }

    /// <summary>类型表的统一装配入口：顺序照 [VehicleTypes]…，就地表里查 art。</summary>
    public static class TypeTables
    {
        public static bool Built { get; private set; }
        public static int UnitCount, InfantryCount, AircraftCount, BuildingCount, BulletCount, AnimCount, SuperCount, WeaponCount, WarheadCount, TiberiumCount;

        public static void Build(RulesDatabase rules, ArtDatabase art)
        {
            rules = rules ?? RulesDatabase.EnsureLoaded();
            art = art ?? ArtDatabase.EnsureLoaded();

            ArmorTypes.Array.Clear();
            ArmorTypes.Array.AddRange(rules.GetValueList("ArmorTypes"));
            PrerequisiteGroups.Load(rules);
            TerrainRules.Load(rules);

            BuildList(rules, art, "VehicleTypes", UnitTypeClass.Array, (id, i) => new UnitTypeClass());
            BuildList(rules, art, "InfantryTypes", InfantryTypeClass.Array, (id, i) => new InfantryTypeClass());
            BuildList(rules, art, "AircraftTypes", AircraftTypeClass.Array, (id, i) => new AircraftTypeClass());
            BuildList(rules, art, "BuildingTypes", BuildingTypeClass.Array, (id, i) => new BuildingTypeClass());
            BuildList(rules, art, "Projectiles", BulletTypeClass.Array, (id, i) => new BulletTypeClass());
            BuildList(rules, art, "Animations", AnimTypeClass.Array, (id, i) => new AnimTypeClass());
            BuildList(rules, art, "SuperWeaponTypes", SuperWeaponTypeClass.Array, (id, i) => new SuperWeaponTypeClass());
            BuildList(rules, art, "Tiberiums", TiberiumTypeClass.Array, (id, i) => new TiberiumTypeClass());
            BuildOverlays(rules);
            BuildTerrainAndSmudge(rules);
            BuildWeaponAndWarhead(rules);

            UnitCount = UnitTypeClass.Array.Count;
            InfantryCount = InfantryTypeClass.Array.Count;
            AircraftCount = AircraftTypeClass.Array.Count;
            BuildingCount = BuildingTypeClass.Array.Count;
            BulletCount = BulletTypeClass.Array.Count;
            AnimCount = AnimTypeClass.Array.Count;
            SuperCount = SuperWeaponTypeClass.Array.Count;
            WeaponCount = WeaponTypeClass.Array.Count;
            WarheadCount = WarheadTypeClass.Array.Count;
            TiberiumCount = TiberiumTypeClass.Array.Count;
            Built = true;

            BootLog.Log("[类型表] 单位=" + UnitCount + " 步兵=" + InfantryCount + " 飞机=" + AircraftCount
                + " 建筑=" + BuildingCount + " 子弹=" + BulletCount + " 特效=" + AnimCount + " 超武=" + SuperCount
                + " 武器=" + WeaponCount + " 弹头=" + WarheadCount + " 矿石=" + TiberiumCount);
        }

        private static readonly string[] ListSectionNames =
        {
            "VehicleTypes", "InfantryTypes", "AircraftTypes", "BuildingTypes", "Projectiles",
            "Animations", "SuperWeaponTypes", "ArmorTypes", "VehicleTypes", "InfantryTypes",
            "WeaponTypes", "Warheads", "Tiberiums", "SmudgeTypes", "TerrainTypes", "OverlayTypes",
            "ParticleSystems", "Particles", "Locomotors", "Sides", "Countries", "HouseTypes"
        };

        private static bool IsListSection(string name)
        {
            for (var i = 0; i < ListSectionNames.Length; i++)
                if (string.Equals(ListSectionNames[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void BuildOverlays(RulesDatabase rules)
        {
            OverlayTypeClass.Array.Clear();
            var keyed = rules.GetIndexMap("OverlayTypes");
            if (keyed.Count > 0)
            {
                var max = 0;
                foreach (var kv in keyed) if (kv.Key > max) max = kv.Key;
                for (var i = 0; i <= max; i++)
                {
                    if (!keyed.TryGetValue(i, out var name) || string.IsNullOrEmpty(name)) continue;
                    var o = new OverlayTypeClass();
                    o.LoadFrom(rules, name, i);
                    OverlayTypeClass.Array.Add(o);
                }
            }
            else
            {
                var names = rules.GetValueList("OverlayTypes");
                for (var i = 0; i < names.Count; i++)
                {
                    var o = new OverlayTypeClass();
                    o.LoadFrom(rules, names[i], i);
                    OverlayTypeClass.Array.Add(o);
                }
            }
        }

        private static void BuildTerrainAndSmudge(RulesDatabase rules)
        {
            TerrainTypeClass.Array.Clear();
            var terrainNames = rules.GetIndexedList("TerrainTypes");
            for (var i = 0; i < terrainNames.Count; i++)
            {
                var t = new TerrainTypeClass();
                t.LoadFrom(rules, terrainNames[i], i);
                TerrainTypeClass.Array.Add(t);
            }

            SmudgeTypeClass.Array.Clear();
            var smudgeNames = rules.GetIndexedList("SmudgeTypes");
            for (var i = 0; i < smudgeNames.Count; i++)
            {
                var s = new SmudgeTypeClass();
                s.LoadFrom(rules, smudgeNames[i], i);
                SmudgeTypeClass.Array.Add(s);
            }
        }

        private static void BuildWeaponAndWarhead(RulesDatabase rules)
        {
            WeaponTypeClass.Array.Clear();
            WarheadTypeClass.Array.Clear();
            var weapons = new List<WeaponTypeClass>();
            var warheads = new List<WarheadTypeClass>();
            var names = rules.AllSectionNames();
            var wi = 0;
            var whi = 0;
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (IsListSection(name)) continue;
                var sec = rules.GetSection(name);
                if (sec == null) continue;
                if (IsWarheadSection(sec))
                {
                    var wh = new WarheadTypeClass();
                    wh.LoadFrom(rules, name, whi++);
                    warheads.Add(wh);
                }
                else if (IsWeaponSection(sec))
                {
                    var w = new WeaponTypeClass();
                    w.LoadFrom(rules, name, wi++);
                    weapons.Add(w);
                }
            }
            WeaponTypeClass.Array.AddRange(weapons);
            WarheadTypeClass.Array.AddRange(warheads);
        }

        private static bool HasAny(IniSection sec, params string[] keys)
        {
            for (var i = 0; i < keys.Length; i++) if (sec.HasKey(keys[i])) return true;
            return false;
        }

        private static bool IsWeaponSection(IniSection sec) => HasAny(sec,
            "Damage", "Warhead", "Projectile", "ROF", "Burst", "MinimumRange", "Report",
            "Lobber", "IsRailgun", "IsSonic", "IsLaser", "IsLine", "IsBigLaser", "OmniFire",
            "CellRangefinding", "FireOnce", "NeverUse", "InfiniteMindControl", "FireWhileMoving",
            "DrainWeapon", "FireInTransport", "Suicide", "TurboBoost", "Supress", "Charges",
            "DiskLaser", "IsHouseColor", "IonSensitive", "AreaFire", "IsElectricBolt",
            "IsAlternateColor", "IsRadBeam", "IsRadEruption", "IsMagBeam", "AssaultAnim",
            "OccupantAnim", "OpenToppedAnim", "AttachedParticleSystem", "AmbientDamage", "RadLevel");

        private static bool IsWarheadSection(IniSection sec) => HasAny(sec,
            "Verses", "CellSpread", "CellInset", "PercentAtMax", "InfDeath", "Deform",
            "DeformThreshhold", "DeformDepth", "DeformRadius", "Wall", "Wood", "Paralyzes",
            "MindControl", "Radiation", "Sonic", "AffectsAllies", "AffectsEnemies", "AffectsNeutral",
            "Rocker", "Tiberium", "ProneDamage", "Locomotor", "Parasite", "Psychedelic",
            "IvanBomb", "Poison", "Airstrike", "Culling", "MakesDisguise", "ElectricAssault",
            "BombDisarm", "NukeMaker", "EffectsRequireVerses", "MaxDebris", "MinDebris",
            "Bullets", "Veinhole", "Temporal", "WallAbsoluteDestroyer", "PenetratesBunker",
            "PsychicDamage", "CombatLightSize", "CLDisableRed", "CLDisableGreen", "CLDisableBlue",
            "CausesDelayKill", "DelayKillFrames", "ShakeXlo", "ShakeXhi", "ShakeYlo", "ShakeYhi");

        private static void BuildList<T>(RulesDatabase rules, ArtDatabase art, string listSection,
            List<T> output, Func<string, int, T> factory) where T : TypeClassBase
        {
            output.Clear();
            var ids = rules.GetIndexedList(listSection);
            for (var i = 0; i < ids.Count; i++)
            {
                var type = factory(ids[i], i);
                type.LoadFrom(rules, ids[i], i);
                // art 段名 = rules 的 Image=（AMCV → [MCV]），否则 = 类型 id
                type.Art = art.Get(type.ImageId);
                output.Add(type);
            }
            if (ids.Count == 0) BootLog.Log("[类型表] " + listSection + " 为空（规则文件缺段）");
        }

        public static T Find<T>(List<T> list, string id) where T : TypeClassBase
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < list.Count; i++)
                if (string.Equals(list[i].ID, id, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        public static T FindByIndex<T>(List<T> list, int index) where T : TypeClassBase
        {
            for (var i = 0; i < list.Count; i++) if (list[i].ArrayIndex == index) return list[i];
            return null;
        }
    }
}
