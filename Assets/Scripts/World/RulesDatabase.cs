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
        public List<string> GetIndexedList(string section)
        {
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
        public string Primary;
        public string Secondary;
        public string Category;
        public string DeploysInto;
        public bool Crewed;
        public bool Crusher;
        public bool CrateGoodie;

        // 每个类型持有“本类型所有实例”的数组（用户要求：类型对象自己存自己那批单位）。
        // 放这层会与 TypeClassBase 重复，故只保留基类那一份。

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
            Primary = sec.GetString("Primary", string.Empty);
            Secondary = sec.GetString("Secondary", string.Empty);
            Category = sec.GetString("Category", string.Empty);
            DeploysInto = sec.GetString("DeploysInto", string.Empty);
            Crewed = sec.GetBool("Crewed");
            Crusher = sec.GetBool("Crusher");
            CrateGoodie = sec.GetBool("CrateGoodie");
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
            BaseNormal = sec.GetBool("BaseNormal");
            Unsellable = sec.GetBool("Unsellable");
            WallOwner = sec.GetBool("WallOwner");
        }
    }

    /// <summary>BulletTypeClass（[Projectiles] 表）。字段是子弹类型自己的，不含 Techno 属性。</summary>
    public sealed class BulletTypeClass : TypeClassBase
    {
        public static readonly List<BulletTypeClass> Array = new List<BulletTypeClass>();
        public string Image;
        public bool Inviso;
        public int Arcing;
        public int Anim;
        public string AnimName;
        public static BulletTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Image = sec.GetString("Image", string.Empty);
            Inviso = sec.GetBool("Inviso");
            Arcing = sec.GetInt("Arcing", 0);
            AnimName = sec.GetString("Anim", string.Empty);
            Anim = sec.GetInt("Anim", 0);
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

    /// <summary>SuperWeaponTypeClass（[SuperWeaponTypes] 表）—— Type 决定行为（原版 SuperWeaponTypeClass::Type）。</summary>
    public sealed class SuperWeaponTypeClass : TypeClassBase
    {
        public static readonly List<SuperWeaponTypeClass> Array = new List<SuperWeaponTypeClass>();
        public string Type = string.Empty;      // 原版枚举名：IonCannon / Nuke / LightningStorm ...
        public int RechargeTime;
        public bool IsHouseAlways;              // 房主默认拥有（原版 SuperWeaponTypeClass 的 AlwaysGranted 语义近似）
        public string Action;

        public static SuperWeaponTypeClass Find(string id) => TypeTables.Find(Array, id);

        public override void LoadFrom(RulesDatabase rules, string id, int index)
        {
            base.LoadFrom(rules, id, index);
            var sec = rules.GetSection(id);
            if (sec == null) return;
            Type = sec.GetString("Type", string.Empty);
            RechargeTime = sec.GetInt("RechargeTime", 0);
            IsHouseAlways = sec.GetBool("IsHouseAlways");
            Action = sec.GetString("Action", string.Empty);
        }
    }

    /// <summary>[ArmorTypes] 顺序 → 装甲索引（原版 ArmorType 枚举就来自这段）。</summary>
    public static class ArmorTypes
    {
        public static readonly List<string> Array = new List<string>();

        public static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (var i = 0; i < Array.Count; i++)
                if (string.Equals(Array[i], name.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    /// <summary>类型表的统一装配入口：顺序照 [VehicleTypes]…，就地表里查 art。</summary>
    public static class TypeTables
    {
        public static bool Built { get; private set; }
        public static int UnitCount, InfantryCount, AircraftCount, BuildingCount, BulletCount, AnimCount, SuperCount;

        public static void Build(RulesDatabase rules, ArtDatabase art)
        {
            rules = rules ?? RulesDatabase.EnsureLoaded();
            art = art ?? ArtDatabase.EnsureLoaded();

            ArmorTypes.Array.Clear();
            ArmorTypes.Array.AddRange(rules.GetValueList("ArmorTypes"));

            BuildList(rules, art, "VehicleTypes", UnitTypeClass.Array, (id, i) => new UnitTypeClass());
            BuildList(rules, art, "InfantryTypes", InfantryTypeClass.Array, (id, i) => new InfantryTypeClass());
            BuildList(rules, art, "AircraftTypes", AircraftTypeClass.Array, (id, i) => new AircraftTypeClass());
            BuildList(rules, art, "BuildingTypes", BuildingTypeClass.Array, (id, i) => new BuildingTypeClass());
            BuildList(rules, art, "Projectiles", BulletTypeClass.Array, (id, i) => new BulletTypeClass());
            BuildList(rules, art, "Animations", AnimTypeClass.Array, (id, i) => new AnimTypeClass());
            BuildList(rules, art, "SuperWeaponTypes", SuperWeaponTypeClass.Array, (id, i) => new SuperWeaponTypeClass());

            UnitCount = UnitTypeClass.Array.Count;
            InfantryCount = InfantryTypeClass.Array.Count;
            AircraftCount = AircraftTypeClass.Array.Count;
            BuildingCount = BuildingTypeClass.Array.Count;
            BulletCount = BulletTypeClass.Array.Count;
            AnimCount = AnimTypeClass.Array.Count;
            SuperCount = SuperWeaponTypeClass.Array.Count;
            Built = true;

            BootLog.Log("[类型表] 单位=" + UnitCount + " 步兵=" + InfantryCount + " 飞机=" + AircraftCount
                + " 建筑=" + BuildingCount + " 子弹=" + BulletCount + " 特效=" + AnimCount + " 超武=" + SuperCount);
        }

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
