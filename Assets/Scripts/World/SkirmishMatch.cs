// SkirmishMatch.cs —— 遭遇战「开始游戏」的装配层：把大厅的 SkirmishSelection 落成一局真实开局数据。
//
// 对照原版：
//   * sub_6ACEE0（0x6ACEE0，Skirmish 对话框 WM_COMMAND）：0x617 = 开始游戏 / 0x5aa = 选图 / 0x5c0 = 返回。
//     开始前校验「启用人数是否落在地图的 Min/MaxPlayers 区间」，不满足就弹报错并中止（不改变状态）。
//   * SessionClass::StartGame / MapSelect 后续：以地图决定起始点（Waypoint 0..7 为出生点），
//     每个参战方一个 HouseClass，起始资金 / 初始部队 / 难度 / 国家 / 颜色 / 队伍全部来自大厅。
//   * 全局选项：a8b2xx 段的 ShortGame / MCVRepacks / CratesAppear / SuperWeaponsAllowed / BuildOffAlly
//     与 GameSpeed / Credits / UnitCount 三个 trackbar 值，进局时一次性写进规则对象。
//
// 本类只做「装配 + 状态迁移 + 数据落地」；世界实体（单位/步兵/建筑实例、地形渲染、HUD、AI）
// 尚未落地，凡是需要那些系统的入口一律打 [未实现]，不伪造行为。
using System;
using System.Collections.Generic;
using System.Text;
using Ra2md.Core;
using Ra2md.Menu;

namespace Ra2md.World
{
    /// <summary>一名参战方的开局设置（HouseClass 的初始状态快照）。</summary>
    public sealed class SkirmishHouseSetup
    {
        public int SlotIndex;
        public string Name;            // 空 = 显示 GUI:Player / 电脑
        public bool Human;
        public string CountryId;       // [Countries] 的 id（Americans…）
        public string ColorId;         // 颜色 id（Gold/Red/…）
        public int ColorIndex;         // 颜色序号（调色板/SHP 重染用）
        public string Team = "0";      // "0"=无队伍，1..4 = A..D
        public int Difficulty;         // 0 简单 / 1 普通 / 2 困难
        public int StartPointIndex = -1; // 航路点序号（0..7），-1 = 没有分配
        public int StartX = -1;
        public int StartY = -1;
        public int Credits;            // 开局资金
        public int InitialUnitCount;    // 开局部队数（UnitCount trackbar）
        public bool McvRepacks;
    }

    /// <summary>本局生效的全局规则（一次性快照，进局后不再受大厅改动影响）。</summary>
    public sealed class SkirmishRules
    {
        public string GameMode;        // 大厅选的模式（standard / teamgame / …）
        public int StartingCredits = 10000;
        public int UnitCount = 10;
        public int GameSpeed = 6;
        public bool ShortGame;
        public bool McvRepacks = true;
        public bool Crates;
        public bool Superweapons = true;
        public bool BuildOffAlly;
        public bool ComputerAiEnabled = true;
    }

    /// <summary>一局遭遇战的装配结果。</summary>
    public sealed class SkirmishMatch
    {
        private const int MaxStartPoints = 8;          // 原版遭遇战最多 8 席（Waypoint 0..7）

        public static SkirmishMatch Current { get; private set; }

        public ScenarioContext Context { get; private set; }
        public ScenarioDocument Document { get; private set; }
        public string MapKey { get; private set; }
        public string MapSourceName { get; private set; }
        public readonly SkirmishRules Rules = new SkirmishRules();

        /// <summary>本局地形（[IsoMapPack5] 解出来的格数据）。</summary>
        public IsoMapData Terrain { get; private set; }
        public readonly List<SkirmishHouseSetup> Houses = new List<SkirmishHouseSetup>();

        /// <summary>本局实体化的 HouseClass（顺序 = Houses 顺序，ArrayIndex 与之一致）。</summary>
        public readonly List<Ra2md.World.HouseClass> RuntimeHouses = new List<Ra2md.World.HouseClass>();

        /// <summary>本机玩家对应的 HouseClass（= HouseClass::CurrentPlayer）。</summary>
        public HouseClass PlayerHouseClass { get; private set; }
        /// <summary>本局随机种子：地图 + 规则 + 参战方，保证同配置重开一致（联机/事件化前提）。</summary>
        public uint Seed { get; private set; }
        /// <summary>玩家（第 0 席）对应的 House 设置。</summary>
        public SkirmishHouseSetup PlayerHouse
        {
            get
            {
                for (var i = 0; i < Houses.Count; i++) if (Houses[i].Human) return Houses[i];
                return Houses.Count > 0 ? Houses[0] : null;
            }
        }

        private bool _open;

        /// <summary>
        /// 装配并进入一局。返回 null 表示被校验拦下（调用方保持在大厅，不改会话状态）。
        /// </summary>
        public static SkirmishMatch Start(SkirmishSelection selection, SkirmishMapInfo map, Catalog catalog)
        {
            if (selection == null || map == null)
            {
                BootLog.Log("[遭遇战] 开始游戏失败：没有选中地图");
                return null;
            }

            // ---- 1. 参赛人数校验（对照 sub_6ACEE0 开始前的 Min/MaxPlayers 判定）----
            var enabled = 0;
            for (var i = 0; i < selection.Slots.Count; i++) if (selection.Slots[i].Enabled) enabled++;
            var min = map.MinPlayers > 0 ? map.MinPlayers : 1;
            var max = map.SlotLimit > 0 ? map.SlotLimit : SkirmishLimits.SlotCapacity;
            if (enabled < min || enabled > max)
            {
                BootLog.Log($"[遭遇战] 开始游戏被拦下：启用 {enabled} 席，地图要求 {min}..{max} 席");
                return null;
            }

            // ---- 2. 装载场景（松散图按文件名，归档图按 id:XXXXXXXX@归档）----
            var doc = LoadDocument(map);
            if (doc == null)
            {
                BootLog.Log($"[遭遇战] 开始游戏失败：地图装载不出 ({map.FileName})");
                return null;
            }

            var match = new SkirmishMatch
            {
                Document = doc,
                MapKey = string.IsNullOrEmpty(map.SourceKey) ? map.FileName : map.SourceKey,
                MapSourceName = string.IsNullOrEmpty(map.SourceKey) ? map.FileName : map.SourceKey,
                _open = true,
            };

            // ---- 2b. 规则库：rulesmd.ini + 地图专用配置（地图段覆盖同键）----
            var rulesDb = RulesDatabase.Load(doc.Ini);
            var artDb = ArtDatabase.Load(doc.Ini);
            TypeTables.Build(rulesDb, artDb);
            WorldObjects.Reset();
            BootLog.Log($"[遭遇战] 地图专用配置层：{doc.Ini?.Sections.Count ?? 0} 段；规则覆盖命中 {rulesDb.MapOverrideHits} 次");
            match.Terrain = IsoMap.Decode(doc.Ini);
            BootLog.Log("[遭遇战] 地形解包: " + match.Terrain.Summary());

            // ---- 3. 全局规则快照（大厅控件 → 对局规则）----
            var rules = match.Rules;
            rules.GameMode = !string.IsNullOrEmpty(selection.GameMode)
                ? selection.GameMode
                : (map.Modes.Count > 0 ? map.Modes[0] : "standard");
            rules.StartingCredits = selection.StartingCredits;
            rules.UnitCount = selection.UnitCount;
            rules.GameSpeed = selection.GameSpeed;
            rules.ShortGame = selection.ShortGame;
            rules.McvRepacks = selection.McvRepacks;
            rules.Crates = selection.Crates;
            rules.Superweapons = selection.Superweapons;
            rules.BuildOffAlly = selection.BuildOffAlly;
            rules.ComputerAiEnabled = selection.ComputerAiEnabled;

            // ---- 4. 起始点表（Waypoint 0..7 且坐标有效）----
            var startPoints = CollectStartPoints(doc);
            if (startPoints.Count == 0)
            {
                BootLog.Log($"[遭遇战] 地图 {map.FileName} 没有可用起始点（Waypoint 0..7 全空）");
                return null;
            }

            // ---- 5. 逐席建 House 设置；起始点按「指定优先、其余顺位补空位」----
            var used = new List<int>(startPoints.Count);
            var nextFree = 0;
            for (var i = 0; i < selection.Slots.Count; i++)
            {
                var slot = selection.Slots[i];
                if (!slot.Enabled) continue;

                var setup = new SkirmishHouseSetup
                {
                    SlotIndex = i,
                    Name = slot.Name,
                    Human = slot.Human,
                    CountryId = slot.CountryId,
                    ColorId = slot.ColorId,
                    Team = string.IsNullOrEmpty(slot.Team) ? "0" : slot.Team,
                    Difficulty = slot.Difficulty,
                    Credits = rules.StartingCredits,
                    InitialUnitCount = rules.UnitCount,
                    McvRepacks = rules.McvRepacks,
                };
                setup.ColorIndex = catalog != null ? catalog.IndexOfColor(setup.ColorId) : -1;

                var desired = slot.StartPosition > 0 ? slot.StartPosition - 1 : -1;   // 0 = 随机
                var pick = PickStartPoint(startPoints, used, desired, ref nextFree);
                if (pick >= 0)
                {
                    setup.StartPointIndex = startPoints[pick].Index;
                    setup.StartX = startPoints[pick].X;
                    setup.StartY = startPoints[pick].Y;
                    used.Add(pick);
                }
                else
                {
                    BootLog.Log($"[遭遇战] 第 {i} 席没有空闲起始点，按无起点处理 [未实现 挤位]");
                }

                match.Houses.Add(setup);
            }

            match.BuildRuntimeHouses(catalog);
            match.SpawnStartingForces();
            match.SpawnMapActors();

            match.Seed = ComputeSeed(match);

            // ---- 6. 场景上下文（父子系统：小队注册表 + 触发器）----
            match.Context = ScenarioContext.OpenDocument(doc, match.MapSourceName);
            Current = match;

            // ---- 7. 会话状态迁移 ----
            GameStatics.GameMode = GameModeKind.Skirmish;
            GameStatics.State = SessionState.Skirmish;

            // 进局即切到“跟随窗口”的分辨率（菜单/设置仍走 800×600 外壳口径）
            Ra2md.Gfx.RenderResolution.EnterGameplay(match.MapSourceName);

            match.LogApplied();
            return match;
        }

        /// <summary>关闭本局（会话收尾时调用）。</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            Context?.Close();
            Context = null;
            RuntimeHouses.Clear();
            PlayerHouseClass = null;
            HouseClass.ResetAll();
            ScenarioRuntime.Reset();
            WorldObjects.Reset();
            // 退局回外壳：面尺寸回 800×600，菜单缩放维持原样
            Ra2md.Gfx.RenderResolution.EnterShell();
            if (Current == this) Current = null;
            BootLog.Log("[遭遇战] 对局关闭 " + MapSourceName);
        }

        private static ScenarioDocument LoadDocument(SkirmishMapInfo map)
        {
            var doc = !string.IsNullOrEmpty(map.SourceKey)
                ? ScenarioLoader.LoadByKey(map.SourceKey)
                : ScenarioLoader.LoadByName(map.FileName);
            if (doc == null && !string.IsNullOrEmpty(map.FileName))
            {
                // 归档键失效时按文件名再试一次（散装同名图覆盖归档图是原版的解析顺序）
                doc = ScenarioLoader.LoadByName(map.FileName);
            }
            return doc;
        }

        private struct StartPoint
        {
            public int Index;
            public int X;
            public int Y;
        }

        /// <summary>起始点 = [Waypoints] 里索引 0..7 且坐标有效的条目（与大厅 SlotLimit 同口径）。</summary>
        private static List<StartPoint> CollectStartPoints(ScenarioDocument doc)
        {
            var list = new List<StartPoint>(MaxStartPoints);
            for (var i = 0; i < doc.Waypoints.Count; i++)
            {
                var wp = doc.Waypoints[i];
                if (!wp.Valid) continue;
                int index;
                if (!int.TryParse(wp.Id, out index)) continue;
                if (index < 0 || index >= MaxStartPoints) continue;
                list.Add(new StartPoint { Index = index, X = wp.X, Y = wp.Y });
            }
            list.Sort((a, b) => a.Index.CompareTo(b.Index));
            return list;
        }

        private static int PickStartPoint(List<StartPoint> points, List<int> used, int desired, ref int nextFree)
        {
            if (desired >= 0)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    if (points[i].Index == desired && !used.Contains(i)) return i;
                }
            }
            while (nextFree < points.Count && used.Contains(nextFree)) nextFree++;
            if (nextFree < points.Count) return nextFree;
            return -1;
        }

        /// <summary>
        /// 把大厅的每席设置落成 HouseClass（字段语义照 YRpp HouseClass.h），并建立 HouseTypeClass 国家表。
        /// 顺序：HouseTypeClass.LoadFromINI（[Countries] 顺序 + [国家] 段）→ new HouseClass → 逐字段赋值 → 注册。
        /// </summary>
        private void BuildRuntimeHouses(Catalog catalog)
        {
            HouseTypeClass.Array.Clear();
            HouseClass.ResetAll();
            ScenarioRuntime.Reset();
            RuntimeHouses.Clear();
            PlayerHouseClass = null;

            var ini = LoadRulesIni();
            var sides = ReadSides(ini);
            if (ini == null) BootLog.Log("[遭遇战] rulesmd.ini 读不到，HouseTypeClass 只剩 id（Side/UIName 无法落地）");

            // ---- 1. HouseTypeClass 表（= HouseTypeClass::Array @0xA83C98）----
            if (catalog != null)
            {
                for (var i = 0; i < catalog.Countries.Count; i++)
                {
                    var c = catalog.Countries[i];
                    var sec = ini != null ? ini.GetSection(c.Id) : null;
                    var t = new HouseTypeClass
                    {
                        ID = c.Id,
                        ArrayIndex = i,
                        ArrayIndex2 = i,
                        Name = sec != null ? sec.GetString("Name", c.Id) : c.Id,
                        UINameLabel = sec != null ? sec.GetString("UIName", c.NameKey) : c.NameKey,
                        Suffix = sec != null ? sec.GetString("Suffix", string.Empty) : string.Empty,
                        Multiplay = sec != null && sec.GetBool("Multiplay"),
                        MultiplayPassive = sec != null && sec.GetBool("MultiplayPassive"),
                        WallOwner = sec != null && sec.GetBool("WallOwner"),
                        SmartAI = sec != null && sec.GetBool("SmartAI"),
                        ParentCountry = string.Empty,   // rulesmd 的 [国家] 段没有该键（[未确认 ParentCountry 来源]）
                        ColorSchemeIndex = -1,          // 颜色方案表（colorscheme）未落地 → 每次开局限 -1
                    };
                    var prefix = sec != null ? sec.GetString("Prefix", string.Empty) : string.Empty;
                    t.Prefix = prefix.Length > 0 ? prefix[0] : '\0';
                    t.UIName = MenuStrings.Get(t.UINameLabel, t.Name);
                    t.SideIndex = IndexOfSide(sides, sec != null ? sec.GetString("Side", string.Empty) : string.Empty);
                    HouseTypeClass.Array.Add(t);
                }
            }

            // ---- 2. 每席 HouseClass（= HouseClass::HouseClass(HouseTypeClass*) @0x4F54A0）----
            for (var i = 0; i < Houses.Count; i++)
            {
                var setup = Houses[i];
                var country = HouseTypeClass.Find(setup.CountryId);
                if (country == null && HouseTypeClass.Array.Count > 0)
                {
                    country = HouseTypeClass.Array[0];
                    BootLog.Log($"[遭遇战] 席 {setup.SlotIndex} 国家 {setup.CountryId} 不在 [Countries] 里，回落 "
                        + country.ID + "（原版同样按国家表索引取，不认得就是空）");
                }

                var house = HouseClass.Create(country);
                house.IsHumanPlayer = setup.Human;
                house.IsInPlayerControl = setup.Human;
                house.StartingPoint = setup.StartPointIndex;
                house.StartingCellX = setup.StartX;
                house.StartingCellY = setup.StartY;
                house.BaseSpawnCellX = setup.StartX;
                house.BaseSpawnCellY = setup.StartY;
                house.BaseCenterX = setup.StartX;
                house.BaseCenterY = setup.StartY;
                house.SideIndex = country != null ? country.SideIndex : -1;
                house.ColorSchemeIndex = country != null ? country.ColorSchemeIndex : -1;

                // 资金：天赋 Balance 是实际钱数，StartingCredits 是 ×100 的原始字段（HouseClass.h:817）。
                house.Balance = setup.Credits;
                house.StartingCredits = setup.Credits / 100;

                // 难度：大厅 0 简单 / 1 普通 / 2 困难 → YRpp 反向枚举 Easy=2 / Normal=1 / Hard=0。
                house.AssignHandicap(DifficultyToAIDifficulty(setup.Difficulty));

                // 名字：人类玩家用输入名（空则 GUI:Player），电脑用国家 UIName + 序号。
                var display = setup.Name;
                if (string.IsNullOrEmpty(display))
                    display = setup.Human ? MenuStrings.Get("GUI:Player", "Player") : (country != null ? country.UIName : "Computer");
                house.UIName = display;
                house.UINameString = display;
                house.PlainName = string.IsNullOrEmpty(setup.Name) ? (country != null ? country.ID : "House") : setup.Name;

                house.CalculateCostMultipliers();

                // 颜色：大厅颜色槽 → 调色板重染（色板颜色表未落地，只能存序号供渲染层查）。
                var color = catalog != null ? catalog.FindColor(setup.ColorId) : null;
                if (color != null)
                {
                    house.ColorR = color.Swatch.r;
                    house.ColorG = color.Swatch.g;
                    house.ColorB = color.Swatch.b;
                }

                // 出生位表（= ScenarioClass::HouseIndices[0..7]）
                if (house.StartingPoint >= 0 && house.StartingPoint < ScenarioRuntime.HouseIndices.Length)
                    ScenarioRuntime.HouseIndices[house.StartingPoint] = house.ArrayIndex;

                if (setup.Human && PlayerHouseClass == null) PlayerHouseClass = house;
                RuntimeHouses.Add(house);
            }

            // ---- 3. 全局：当前玩家 / 观察者 / 盟约位（队友号 → 互为盟友，对照 Team 编号 A..D）----
            HouseClass.CurrentPlayer = PlayerHouseClass;
            HouseClass.Observer = null;

            if (Rules.BuildOffAlly) BootLog.Log("[遭遇战] 友军建造 开（同盟关系已建，实际共建判定 [未实现 建筑系统]）");
            for (var i = 0; i < Houses.Count; i++)
            {
                var a = Houses[i];
                if (string.IsNullOrEmpty(a.Team) || a.Team == "0") continue;
                for (var j = i + 1; j < Houses.Count; j++)
                {
                    var b = Houses[j];
                    if (b.Team != a.Team) continue;
                    RuntimeHouses[i].MakeAlly(RuntimeHouses[j], false);
                }
            }
        }

        /// <summary>
        /// 开局实体：每席一台 MCV（[General] BaseUnit 里属于该国家的第一个），
        /// 超武按 SuperWeaponTypeClass::IsHouseAlways 发；其余初始部队/建造队列 [未实现]。
        /// </summary>
        private void SpawnStartingForces()
        {
            var baseUnits = new List<string>();
            var general = RulesDatabase.Current != null ? RulesDatabase.Current.GetSection("General") : null;
            if (general != null) baseUnits.AddRange(MapIniDocument.SplitComma(general.GetString("BaseUnit", string.Empty)));
            if (baseUnits.Count == 0) BootLog.Log("[遭遇战] [General] BaseUnit 为空，无法生成 MCV");

            for (var i = 0; i < RuntimeHouses.Count; i++)
            {
                var house = RuntimeHouses[i];
                var setup = Houses[i];
                if (house.StartingPoint < 0)
                {
                    BootLog.Log($"[遭遇战] House #{house.ArrayIndex} 没有出生点，跳过 MCV/超武 [未实现 挤位]");
                    continue;
                }

                // ---- MCV：BaseUnit 里挑属于该国家的第一项 ----
                var countryId = house.GetId();
                string mcvId = null;
                for (var k = 0; k < baseUnits.Count; k++)
                {
                    var t = UnitTypeClass.Find(baseUnits[k]);
                    if (t == null) continue;
                    if (t.OwnedBy(countryId)) { mcvId = baseUnits[k]; break; }
                }
                if (mcvId == null && baseUnits.Count > 0)
                {
                    BootLog.Log($"[遭遇战] House #{house.ArrayIndex} 国家 {countryId} 没有可用的 BaseUnit 变体");
                }
                else if (mcvId != null)
                {
                    var type = UnitTypeClass.Find(mcvId);
                    var unit = UnitClass.Create(type, house, setup.StartX, setup.StartY);
                    BootLog.Log($"[遭遇战] House #{house.ArrayIndex} MCV={mcvId} 于 ({setup.StartX},{setup.StartY}) "
                        + $"血量={unit.Health}/{unit.MaxHealth} 体素={(unit.IsVoxel ? "是" : "否")} "
                        + $"图像={type.ResolvedImage}");
                }

                if (setup.InitialUnitCount > 1)
                    BootLog.Log($"[遭遇战] House #{house.ArrayIndex} 初始部队 {setup.InitialUnitCount} 台 [未实现 部队配比]");

                // ---- 超武：只发 IsHouseAlways（其余由建筑授予，建筑系统未落地）----
                if (Rules.Superweapons)
                {
                    for (var k = 0; k < SuperWeaponTypeClass.Array.Count; k++)
                    {
                        var sw = SuperWeaponTypeClass.Array[k];
                        if (!sw.IsHouseAlways) continue;
                        var superClass = new SuperClass(sw, house, false);
                        BootLog.Log($"[遭遇战] House #{house.ArrayIndex} 超武 {sw.ID} 充能 {sw.RechargeTime} [未实现 释放效果]");
                    }
                }
            }

            BootLog.Log("[实体] 开局实体 " + WorldObjects.Summary());
        }

        /// <summary>
        /// 地图里摆好的对象（原版 ScenarioClass 的 Read_TerrainSection / Read_StructureSection）：
        ///   · [Terrain]     key = y*1000 + x，值 = terrain 类型名（TIBTRE01 / LT_SGN03 …）→ TerrainClass 实例；
        ///   · [Structures]  每一行 = Owner,Item,Health,X,Y,…  → BuildingClass 实例（中立建筑 Owner=Neutral）。
        /// 建筑血量上限仍取类型 Strength（原版地图里 256 是“满血标记”，不是绝对值）。
        /// </summary>
        private void SpawnMapActors()
        {
            var ini = Document != null ? Document.Ini : null;
            if (ini == null) { BootLog.Log("[地图对象] 无地图 INI"); return; }

            var rules = RulesDatabase.Current;

            // ---- [Terrain] ----
            var terrainSec = ini.GetSection("Terrain");
            var terrainCount = 0;
            if (terrainSec != null)
            {
                for (var i = 0; i < terrainSec.Entries.Count; i++)
                {
                    var entry = terrainSec.Entries[i];
                    if (!int.TryParse((entry.Key ?? string.Empty).Trim(), out var pos) || pos < 0) continue;
                    var name = (entry.Value ?? string.Empty).Trim();
                    if (name.Length == 0) continue;

                    var x = pos % 1000;
                    var y = pos / 1000;
                    var image = rules != null ? rules.GetString(name, "Image", string.Empty).Trim() : string.Empty;
                    if (image.Length == 0) image = name;
                    new TerrainClass(name, image, x, y);
                    terrainCount++;
                }
            }

            // ---- [Structures] ----
            var structSec = ini.GetSection("Structures");
            var structCount = 0;
            var unknown = 0;
            if (structSec != null)
            {
                for (var i = 0; i < structSec.Entries.Count; i++)
                {
                    var entry = structSec.Entries[i];
                    var fields = MapIniDocument.SplitComma(entry.Value ?? string.Empty);
                    // 原版字段顺序：House, Item, Health, X, Y, Facing, …
                    if (fields.Length < 5) { unknown++; continue; }
                    var ownerName = fields[0].Trim();
                    var typeId = fields[1].Trim();
                    var x = MapIniDocument.ParseInt(fields[3]);
                    var y = MapIniDocument.ParseInt(fields[4]);
                    if (typeId.Length == 0) { unknown++; continue; }

                    var type = BuildingTypeClass.Find(typeId);
                    if (type == null) { unknown++; continue; }

                    var owner = HouseClass.FindByCountryName(ownerName);
                    if (owner == null && ownerName.Length > 0 && ownerName != "Neutral")
                        owner = HouseClass.FindByCountryName("Neutral");
                    // 中立建筑 Owner 允许为 null（原版走 HouseClass::Find_Neutral，我们还没有中立 House 实例）
                    new BuildingClass(type, owner, x, y);
                    structCount++;
                }
            }

            BootLog.Log($"[地图对象] terrain={terrainCount} 建筑={structCount} 未知={unknown}"
                + $" 实例总量=" + WorldObjects.Summary());
        }

        private static AIDifficulty DifficultyToAIDifficulty(int difficulty)
        {
            switch (difficulty)
            {
                case 0: return AIDifficulty.Easy;
                case 2: return AIDifficulty.Hard;
                default: return AIDifficulty.Normal;
            }
        }

        private static MapIniDocument LoadRulesIni()
        {
            byte[] bytes;
            string origin;
            if (!MenuResourceLibrary.TryLoadFirst(
                    Ra2md.Managers.GlobalConfigManager.Resources.RulesIniNames, out bytes, out origin) || bytes == null)
                return null;
            return MapIniDocument.Parse(MapIniDocument.DecodeText(bytes));
        }

        /// <summary>[Sides] 段的键顺序即 SideIndex 顺序（GDI=0 / Nod=1 / ThirdSide=2 / Civilian=3）。</summary>
        private static List<string> ReadSides(MapIniDocument ini)
        {
            var list = new List<string>();
            var sec = ini != null ? ini.GetSection("Sides") : null;
            if (sec == null) return list;
            foreach (var entry in sec.Entries)
            {
                var key = (entry.Key ?? string.Empty).Trim();
                if (key.Length > 0 && !list.Contains(key)) list.Add(key);
            }
            return list;
        }

        private static int IndexOfSide(List<string> sides, string side)
        {
            if (sides == null || string.IsNullOrEmpty(side)) return -1;
            for (var i = 0; i < sides.Count; i++)
                if (string.Equals(sides[i], side.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>种子 = 地图 + 规则 + 每席（国家/颜色/队伍/难度/起点）；同配置重开结果一致。</summary>
        private static uint ComputeSeed(SkirmishMatch match)
        {
            unchecked
            {
                var h = (uint)2166136261u;
                void Mix(string s)
                {
                    if (string.IsNullOrEmpty(s)) { h = (h ^ 0u) * 16777619u; return; }
                    for (var i = 0; i < s.Length; i++) h = (h ^ (uint)s[i]) * 16777619u;
                }
                Mix(match.MapKey);
                Mix(match.Rules.GameMode);
                h = (h ^ (uint)match.Rules.StartingCredits) * 16777619u;
                h = (h ^ (uint)match.Rules.UnitCount) * 16777619u;
                h = (h ^ (uint)match.Rules.GameSpeed) * 16777619u;
                for (var i = 0; i < match.Houses.Count; i++)
                {
                    var s = match.Houses[i];
                    Mix(s.CountryId); Mix(s.ColorId); Mix(s.Team);
                    h = (h ^ (uint)s.Difficulty) * 16777619u;
                    h = (h ^ (uint)(s.StartPointIndex + 1)) * 16777619u;
                }
                return h;
            }
        }

        private void LogApplied()
        {
            var sb = new StringBuilder();
            sb.Append("[遭遇战] 开局装载 ").Append(MapSourceName)
              .Append(" 模式=").Append(Rules.GameMode)
              .Append(" 资金=").Append(Rules.StartingCredits)
              .Append(" 部队=").Append(Rules.UnitCount)
              .Append(" 速度=").Append(Rules.GameSpeed);
            BootLog.Log(sb.ToString());

            BootLog.Log("[遭遇战] 规则: 短局=" + (Rules.ShortGame ? "开" : "关")
                + " MCV重装=" + (Rules.McvRepacks ? "开" : "关")
                + " 箱子=" + (Rules.Crates ? "开" : "关")
                + " 超级武器=" + (Rules.Superweapons ? "开" : "关")
                + " 友军建造=" + (Rules.BuildOffAlly ? "开" : "关")
                + " 电脑=" + (Rules.ComputerAiEnabled ? "开" : "关"));

            for (var i = 0; i < Houses.Count; i++)
            {
                var h = Houses[i];
                BootLog.Log($"[遭遇战] 席 {h.SlotIndex} {(h.Human ? "(玩家)" : "(电脑)")} " +
                    $"国家={h.CountryId} 颜色={h.ColorId}#{h.ColorIndex} 队伍={h.Team} " +
                    $"难度={h.Difficulty} 起点={(h.StartPointIndex >= 0 ? h.StartPointIndex.ToString() : "无")}" +
                    $"({h.StartX},{h.StartY}) 资金={h.Credits} 部队={h.InitialUnitCount}");

                if (i >= RuntimeHouses.Count) continue;
                var house = RuntimeHouses[i];
                BootLog.Log($"[遭遇战] HouseClass #{house.ArrayIndex} id={house.GetId()} " +
                    $"side={house.SideIndex} 难度={house.AIDifficulty}(反向枚举) " +
                    $"human={house.IsHumanPlayer} 起点={house.GetSpawnPosition()} " +
                    $"Balance={house.Balance} StartingCredits={house.StartingCredits}(×100) " +
                    $"颜色=({house.ColorR},{house.ColorG},{house.ColorB}) " +
                    $"供电={house.PowerOutput}/{house.PowerDrain} 电量={house.GetPowerPercentage():P0}");
            }

            BootLog.Log($"[遭遇战] HouseClass 表={HouseClass.Array.Count} 席，当前玩家="
                + (PlayerHouseClass != null ? "#" + PlayerHouseClass.ArrayIndex : "无"));

            BootLog.Log($"[遭遇战] 种子=0x{Seed:X8}（同地图同配置重开一致）");
            BootLog.Log($"[遭遇战] 类型表: 单位={TypeTables.UnitCount} 步兵={TypeTables.InfantryCount} "
                + $"飞机={TypeTables.AircraftCount} 建筑={TypeTables.BuildingCount} "
                + $"子弹={TypeTables.BulletCount} 特效={TypeTables.AnimCount} 超武={TypeTables.SuperCount}");
            BootLog.Log($"[遭遇战] 实体: {WorldObjects.Summary()}（地形渲染 / HUD / 战斗系统 [未实现]）");
            BootLog.Log("[遭遇战] 规则项写入运行时（短局/箱子/超级武器/友军建造）[未实现 规则引擎]");
            BootLog.Log("[遭遇战] AI 行为 [未实现 AI 系统]");
        }

        /// <summary>会话收尾时由 GameSession.EndSession 调用。</summary>
        public static void CloseCurrent()
        {
            if (Current != null) Current.Close();
        }
    }
}
