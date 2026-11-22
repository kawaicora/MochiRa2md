// ScenarioParser.cs —— 把 MapIniDocument 解析成 ScenarioDocument。
//
// 段格式对照库存样本（IrvineCa.yro / DeepFrze.yro）与 ConcordRTS MapDatabase.cpp：
//   [Map]         Theater / Size=x0,y0,x1,y1 / LocalSize
//   [Houses]      index=房子名（顺序即房子索引）
//   [Waypoints]   index=编码格（cell = y*1000 + x）
//   [Units]       房子,类型,血量,X,Y,朝向,任务,标签,[老兵],[?],[桥上]
//   [Infantry]    房子,类型,血量,X,Y,子格,任务,朝向,标签,[老兵]…
//   [Structures]  房子,类型,血量,X,Y,朝向,标签,…
//   [Terrain]     编码格=类型
//   [TaskForces]  index=队伍力量ID；[ID] 内 Name=、i=数量,类型、Group=
//   [TeamTypes]   index=小队类型ID；[ID] 内字段对照 TeamTypeClass
//   [Scripts]     [ID] 内 Name=、i=动作,参数
//   [Triggers]    ID=房子,标签,名称,?,启用,重复,转移,?
//   [Events]      ID=条数,事件,值,引用串
//   [Actions]     ID=条数,其后每组 8 字段：动作,启用,引用ID,参3,参4,参5,参6,航路点/文本
//   [Tags]         ID=种类,名称,触发器ID
using System;
using System.Collections.Generic;

namespace Ra2md.World
{
    public static class ScenarioParser
    {
        private const int CellBase = 1000; // ObjectCellCoordinateBase

        public static ScenarioDocument Parse(MapIniDocument ini, string sourceName)
        {
            var doc = new ScenarioDocument { SourceName = sourceName, Ini = ini };

            ParseMap(ini, doc);
            ParseHeader(ini, doc);
            ParseBasic(ini, doc);
            ParseHouses(ini, doc);
            ParseWaypoints(ini, doc);
            ParseObjects(ini, "Units", MapObjectKind.Unit, doc);
            ParseObjects(ini, "Infantry", MapObjectKind.Infantry, doc);
            ParseObjects(ini, "Structures", MapObjectKind.Structure, doc);
            ParseTerrain(ini, doc);
            ParseTaskForces(ini, doc);
            ParseScripts(ini, doc);
            ParseTeamTypes(ini, doc);
            ParseTriggers(ini, doc);
            ParseEvents(ini, doc);
            ParseActions(ini, doc);
            ParseTags(ini, doc);

            ResolveReferences(doc);
            return doc;
        }

        // ------------------------------------------------------------ [Map]

        private static void ParseMap(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Map");
            if (sec == null) return;

            doc.Theater = sec.GetString("Theater", doc.Theater);
            doc.Briefing = sec.GetString("Briefing", doc.Briefing);

            var size = MapIniDocument.SplitComma(sec.GetString("Size", ""));
            if (size.Length >= 4)
            {
                doc.Bounds.X0 = MapIniDocument.ParseInt(size[0]);
                doc.Bounds.Y0 = MapIniDocument.ParseInt(size[1]);
                doc.Bounds.X1 = MapIniDocument.ParseInt(size[2]);
                doc.Bounds.Y1 = MapIniDocument.ParseInt(size[3]);
            }
        }

        // ------------------------------------------------------------ [Header] / [Basic]
        //
        // 遭遇战大厅的地图元数据（对照 ConcordRTS RuntimeSkirmishMapResources.cpp:140-185）：
        //   [Header] StartX/StartY/Width/Height/NumberStartingPoints/WaypointN=x,y
        //   [Basic]  Name=显示名 / GameMode=过滤串 / MinPlayers / MaxPlayers
        // RA2 旧格式的地图没有 [Header]，起始点数量后面从 [Waypoints] 兜底推算。

        private static void ParseHeader(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Header");
            if (sec == null) return;

            doc.NumberStartingPoints = sec.GetInt("NumberStartingPoints", doc.NumberStartingPoints);
            if (doc.NumberStartingPoints < 0) doc.NumberStartingPoints = 0;
        }

        private static void ParseBasic(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Basic");
            if (sec == null) return;

            doc.DisplayName = sec.GetString("Name", doc.DisplayName);
            doc.MinPlayers = sec.GetInt("MinPlayers", doc.MinPlayers);
            doc.MaxPlayers = sec.GetInt("MaxPlayers", doc.MaxPlayers);

            // MultiplayerOnly=1/yes/true/on 才是多人/遭遇战图；任务图缺省或为 0，不进遭遇战列表。
            var mp = sec.GetString("MultiplayerOnly", string.Empty).Trim().ToLowerInvariant();
            doc.MultiplayerOnly = mp == "1" || mp == "yes" || mp == "true" || mp == "on";

            foreach (var mode in MapIniDocument.SplitComma(sec.GetString("GameMode", "")))
            {
                var value = mode.Trim();
                if (value.Length > 0) doc.GameModes.Add(value);
            }
        }

        private static void ParseHouses(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Houses");
            if (sec == null) return;
            foreach (var entry in sec.Entries)
            {
                var name = entry.Value.Trim();
                if (name.Length > 0 && !doc.Houses.Contains(name)) doc.Houses.Add(name);
            }
        }

        private static void ParseWaypoints(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Waypoints");
            if (sec == null) return;
            foreach (var entry in sec.Entries)
            {
                int cell = MapIniDocument.ParseInt(entry.Value, -1);
                var wp = new MapWaypoint
                {
                    Id = entry.Key,
                    Cell = cell,
                    X = cell >= 0 ? cell % CellBase : -1,
                    Y = cell >= 0 ? cell / CellBase : -1,
                    Valid = cell >= 0,
                };
                doc.Waypoints.Add(wp);
            }
        }

        // ------------------------------------------------------------ 对象

        private static void ParseObjects(MapIniDocument ini, string section, MapObjectKind kind, ScenarioDocument doc)
        {
            var sec = ini.GetSection(section);
            if (sec == null) return;

            foreach (var entry in sec.Entries)
            {
                var f = MapIniDocument.SplitComma(entry.Value);
                var obj = new MapObject { Kind = kind, Id = entry.Key };

                int min = kind == MapObjectKind.Infantry ? 9 : 7;
                if (f.Length < min)
                {
                    doc.Warnings.Add($"{section} [{entry.Key}] 字段不足 ({f.Length}/{min})，已跳过");
                    continue;
                }

                obj.Owner = f[0];
                obj.Type = f[1];
                obj.Health = MapIniDocument.ParseInt(f[2], 256);
                obj.X = MapIniDocument.ParseInt(f[3]);
                obj.Y = MapIniDocument.ParseInt(f[4]);

                if (kind == MapObjectKind.Infantry)
                {
                    obj.SubCell = MapIniDocument.ParseInt(f[5]);
                    obj.Mission = f[6];
                    obj.Facing = MapIniDocument.ParseInt(f[7]);
                    obj.Tag = f[8];
                    if (f.Length > 9) obj.Veterancy = MapIniDocument.ParseInt(f[9]);
                }
                else
                {
                    obj.Facing = MapIniDocument.ParseInt(f[5]);
                    obj.Mission = f[6];
                    if (f.Length > 7) obj.Tag = f[7];
                    if (f.Length > 8) obj.Veterancy = MapIniDocument.ParseInt(f[8]);
                    if (f.Length > 10) obj.OnBridge = MapIniDocument.IsOneValue(f[10]);
                }

                TargetList(doc, kind).Add(obj);
            }
        }

        private static void ParseTerrain(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Terrain");
            if (sec == null) return;

            foreach (var entry in sec.Entries)
            {
                int cell = MapIniDocument.ParseInt(entry.Key, -1);
                var obj = new MapObject
                {
                    Kind = MapObjectKind.Terrain,
                    Id = entry.Key,
                    Type = entry.Value.Trim(),
                    X = cell >= 0 ? cell % CellBase : -1,
                    Y = cell >= 0 ? cell / CellBase : -1,
                };
                doc.Terrain.Add(obj);
            }
        }

        private static List<MapObject> TargetList(ScenarioDocument doc, MapObjectKind kind)
        {
            switch (kind)
            {
                case MapObjectKind.Unit: return doc.Units;
                case MapObjectKind.Infantry: return doc.Infantry;
                case MapObjectKind.Structure: return doc.Structures;
                default: return doc.Terrain;
            }
        }

        // ------------------------------------------------------------ 任务力量 / 脚本 / 小队

        private static void ParseTaskForces(MapIniDocument ini, ScenarioDocument doc)
        {
            var index = ini.GetSection("TaskForces");
            if (index != null)
            {
                foreach (var entry in index.Entries)
                {
                    var tf = new TaskForceEntry { Id = entry.Value.Trim() };
                    doc.TaskForces[tf.Id] = tf;
                }
            }

            foreach (var tf in doc.TaskForces.Values)
            {
                var sec = ini.GetSection(tf.Id);
                if (sec == null) continue;
                tf.Name = sec.GetString("Name", tf.Id);
                tf.Group = sec.GetInt("Group", -1);
                foreach (var entry in sec.Entries)
                {
                    if (entry.Key.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                        entry.Key.Equals("Group", StringComparison.OrdinalIgnoreCase)) continue;
                    var f = MapIniDocument.SplitComma(entry.Value);
                    if (f.Length >= 2)
                    {
                        tf.Items.Add(new TaskForceItem
                        {
                            Count = MapIniDocument.ParseInt(f[0], 1),
                            Type = f[1],
                        });
                    }
                }
            }
        }

        private static void ParseScripts(MapIniDocument ini, ScenarioDocument doc)
        {
            var index = ini.GetSection("Scripts");
            var ids = new List<string>();
            if (index != null)
            {
                foreach (var entry in index.Entries) ids.Add(entry.Value.Trim());
            }

            // 小队类型里直接引用的脚本 ID 也要补进来（样本里 [Scripts] 索引段可能缺失）
            foreach (var sec in ini.Sections)
            {
                if (sec.GetString("Name", null) != null &&
                    sec.Entries.Exists(e => e.Value.Contains(",")) &&
                    !ids.Contains(sec.Name) && LooksLikeHexId(sec.Name))
                {
                    // 由 TeamTypes 阶段按引用补登记；这里先按段名收集候选
                    if (!ids.Contains(sec.Name)) ids.Add(sec.Name);
                }
            }

            foreach (var id in ids)
            {
                var sec = ini.GetSection(id);
                if (sec == null) continue;
                var script = new ScriptEntry { Id = id, Name = sec.GetString("Name", id) };
                foreach (var entry in sec.Entries)
                {
                    if (entry.Key.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                    var f = MapIniDocument.SplitComma(entry.Value);
                    if (f.Length >= 2)
                    {
                        script.Steps.Add(new ScriptStep
                        {
                            Action = MapIniDocument.ParseInt(f[0]),
                            Param = MapIniDocument.ParseInt(f[1]),
                        });
                    }
                }
                doc.Scripts[id] = script;
            }
        }

        private static void ParseTeamTypes(MapIniDocument ini, ScenarioDocument doc)
        {
            var index = ini.GetSection("TeamTypes");
            var ids = new List<string>();
            if (index != null)
            {
                foreach (var entry in index.Entries) ids.Add(entry.Value.Trim());
            }

            foreach (var id in ids)
            {
                var sec = ini.GetSection(id);
                if (sec == null) continue;

                var team = new TeamTypeEntry
                {
                    Id = id,
                    Name = sec.GetString("Name", id),
                    House = sec.GetString("House", "Neutral"),
                    TaskForceId = sec.GetString("TaskForce", null),
                    ScriptId = sec.GetString("Script", null),
                    Waypoint = sec.GetString("Waypoint", null),
                    VeteranLevel = sec.GetInt("VeteranLevel"),
                    Priority = sec.GetInt("Priority", 100),
                    Max = sec.GetInt("Max", 1),
                    TechLevel = sec.GetInt("TechLevel"),
                    Group = sec.GetInt("Group", -1),
                    MindControlDecision = sec.GetInt("MindControlDecision"),
                    Loadable = sec.GetBool("Loadable"),
                    Full = sec.GetBool("Full"),
                    Annoyance = sec.GetBool("Annoyance"),
                    GuardSlower = sec.GetBool("GuardSlower"),
                    Recruiter = sec.GetBool("Recruiter"),
                    Autocreate = sec.GetBool("Autocreate"),
                    Prebuild = sec.GetBool("Prebuild"),
                    Reinforce = sec.GetBool("Reinforce"),
                    Whiner = sec.GetBool("Whiner"),
                    Aggressive = sec.GetBool("Aggressive"),
                    LooseRecruit = sec.GetBool("LooseRecruit"),
                    Suicide = sec.GetBool("Suicide"),
                    Droppod = sec.GetBool("Droppod"),
                    UseTransportOrigin = sec.GetBool("UseTransportOrigin"),
                    DropshipLoadout = sec.GetBool("DropshipLoadout"),
                    OnTransOnly = sec.GetBool("OnTransOnly"),
                    AvoidThreats = sec.GetBool("AvoidThreats"),
                    IonImmune = sec.GetBool("IonImmune"),
                    TransportsReturnOnUnload = sec.GetBool("TransportsReturnOnUnload"),
                    AreTeamMembersRecruitable = sec.GetBool("AreTeamMembersRecruitable"),
                    IsBaseDefense = sec.GetBool("IsBaseDefense"),
                    OnlyTargetHouseEnemy = sec.GetBool("OnlyTargetHouseEnemy"),
                };
                doc.TeamTypes[id] = team;
            }
        }

        // ------------------------------------------------------------ 触发器 / 事件 / 动作 / 标签

        private static void ParseTriggers(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Triggers");
            if (sec == null) return;

            foreach (var entry in sec.Entries)
            {
                var f = MapIniDocument.SplitComma(entry.Value);
                if (f.Length < 3)
                {
                    doc.Warnings.Add($"Triggers [{entry.Key}] 字段不足，已跳过");
                    continue;
                }

                var trigger = new MapTrigger
                {
                    Id = entry.Key,
                    House = f[0],
                    Tag = f[1],
                    Name = f[2],
                    Enabled = f.Length > 4 && MapIniDocument.ParseInt(f[4], 1) != 0,
                    Repeating = f.Length > 5 && MapIniDocument.ParseInt(f[5]) != 0,
                    MustTransfer = f.Length > 6 && MapIniDocument.ParseInt(f[6]) != 0,
                };
                doc.Triggers[trigger.Id] = trigger;
            }
        }

        private static void ParseEvents(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Events");
            if (sec == null) return;

            foreach (var entry in sec.Entries)
            {
                var f = MapIniDocument.SplitComma(entry.Value);
                if (f.Length < 3)
                {
                    doc.Warnings.Add($"Events [{entry.Key}] 字段不足，已跳过");
                    continue;
                }

                doc.Events.Add(new MapEvent
                {
                    TriggerId = entry.Key,
                    Count = Math.Max(1, MapIniDocument.ParseInt(f[0], 1)),
                    Kind = (TriggerEventKind)MapIniDocument.ParseInt(f[1]),
                    Value = MapIniDocument.ParseInt(f[2]),
                    StringRef = f.Length > 3 ? f[3] : null,
                });
            }
        }

        private static void ParseActions(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Actions");
            if (sec == null) return;

            foreach (var entry in sec.Entries)
            {
                var f = MapIniDocument.SplitComma(entry.Value);
                if (f.Length < 2)
                {
                    doc.Warnings.Add($"Actions [{entry.Key}] 字段不足，已跳过");
                    continue;
                }

                int count = Math.Max(1, MapIniDocument.ParseInt(f[0], 1));
                int cursor = 1;

                for (int g = 0; g < count; g++)
                {
                    // 每组 8 字段：动作,启用,引用ID,参3,参4,参5,参6,航路点/文本
                    if (cursor + 7 >= f.Length) break;

                    var action = new MapAction
                    {
                        TriggerId = entry.Key,
                        Count = count,
                        Kind = (TriggerActionKind)MapIniDocument.ParseInt(f[cursor]),
                        Enabled = MapIniDocument.ParseInt(f[cursor + 1]) != 0,
                        RefId = f[cursor + 2],
                        Param3 = MapIniDocument.ParseInt(f[cursor + 3]),
                        Param4 = MapIniDocument.ParseInt(f[cursor + 4]),
                        Param5 = MapIniDocument.ParseInt(f[cursor + 5]),
                        Param6 = MapIniDocument.ParseInt(f[cursor + 6]),
                    };

                    var tail = f[cursor + 7];
                    if (int.TryParse(tail, out var wp))
                    {
                        action.Waypoint = wp;
                    }
                    else
                    {
                        int letterIndex = WaypointLetterToIndex(tail);
                        if (letterIndex >= 0) action.Waypoint = letterIndex;
                        else action.Text = tail;
                    }

                    doc.Actions.Add(action);
                    cursor += 8;
                }
            }
        }

        private static void ParseTags(MapIniDocument ini, ScenarioDocument doc)
        {
            var sec = ini.GetSection("Tags");
            if (sec == null) return;

            foreach (var entry in sec.Entries)
            {
                var f = MapIniDocument.SplitComma(entry.Value);
                if (f.Length < 3) continue;
                doc.Tags[entry.Key] = new MapTag
                {
                    Id = entry.Key,
                    Kind = MapIniDocument.ParseInt(f[0]),
                    Name = f[1],
                    TriggerId = f[2],
                };
            }
        }

        // ------------------------------------------------------------ 引用解析

        private static void ResolveReferences(ScenarioDocument doc)
        {
            // 小队类型的航路点字母 → 索引（A=0, B=1, …, AA=26）
            foreach (var team in doc.TeamTypes.Values)
            {
                if (!string.IsNullOrEmpty(team.Waypoint))
                {
                    team.WaypointIndex = WaypointLetterToIndex(team.Waypoint);
                }

                // 引用 ID 可能带 -G 等后缀（全局/运输标记），按基址段在场景表内查找。
                string tfBase = StripRefSuffix(team.TaskForceId);
                string scriptBase = StripRefSuffix(team.ScriptId);

                if (doc.FindTaskForce(tfBase) == null && !string.IsNullOrEmpty(team.TaskForceId))
                {
                    doc.Warnings.Add($"小队类型 {team.Id} 引用的任务力量 {team.TaskForceId} 不存在");
                }
                if (doc.FindScript(scriptBase) == null && !string.IsNullOrEmpty(team.ScriptId))
                {
                    doc.Warnings.Add($"小队类型 {team.Id} 引用的脚本 {team.ScriptId} 不存在");
                }
            }

            // 动作引用按种类校验：队伍类型引用查 TeamTypes，触发器引用查 Triggers；
            // 房子/音频/文本/计时器等引用语义随动作种类变化，这里不做强校验。
            foreach (var action in doc.Actions)
            {
                if (string.IsNullOrEmpty(action.RefId) || action.RefId == "None") continue;

                switch (action.Kind)
                {
                    case TriggerActionKind.CreateTeam:
                    case TriggerActionKind.DestroyTeam:
                    case TriggerActionKind.Reinforcement:
                    case TriggerActionKind.ReinforcementAt:
                    case TriggerActionKind.FlashTeam:
                        if (doc.FindTeamType(action.RefId) == null)
                        {
                            doc.Warnings.Add($"动作 {action.Kind} 引用队伍类型 {action.RefId} 不存在");
                        }
                        break;

                    case TriggerActionKind.ForceTrigger:
                    case TriggerActionKind.EnableTrigger:
                    case TriggerActionKind.DisableTrigger:
                    case TriggerActionKind.DestroyTrigger:
                        if (!doc.Triggers.ContainsKey(action.RefId))
                        {
                            doc.Warnings.Add($"动作 {action.Kind} 引用触发器 {action.RefId} 不存在");
                        }
                        break;
                }
            }
        }

        /// <summary>航路点字母序号：A=0 … Z=25, AA=26 …（与 [Waypoints] 顺序一致）。</summary>
        public static int WaypointLetterToIndex(string letters)
        {
            if (string.IsNullOrWhiteSpace(letters)) return -1;
            int index = 0;
            foreach (var ch in letters.ToUpperInvariant())
            {
                if (ch < 'A' || ch > 'Z') return -1;
                index = index * 26 + (ch - 'A' + 1);
            }
            return index - 1;
        }

        public static string StripRefSuffix(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int dash = id.IndexOf('-');
            return dash > 0 ? id.Substring(0, dash) : id;
        }

        private static bool LooksLikeHexId(string token)
        {
            if (token == null || token.Length < 6) return false;
            foreach (var ch in token)
            {
                bool hex = (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F') || (ch >= 'a' && ch <= 'f');
                if (!hex) return false;
            }
            return true;
        }
    }
}
