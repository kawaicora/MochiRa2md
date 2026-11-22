// SkirmishModel.cs —— 遭遇战（Skirmish）大厅的数据模型。
//
// 对照：
//   * RuntimeSkirmishController.h/.cpp（RuntimeSkirmishPlayerSlot / RuntimeSkirmishSelection）
//   * RuntimeSkirmishMapResources.cpp:140-250（[Header] NumberStartingPoints / [Basic] Name、
//     MinPlayers、MaxPlayers → MapInfo.maxPlayers）
//
// 与参考实现的一处刻意差异：参考把玩家槽上限写死 8（kPlayerSlots），这里改为
//   SlotCapacity = 0x40（64），并且「本局可加入人数」完全由地图决定（map.SlotLimit）。
//   数量上限只作为数组/滚动的容量边界，界面按行滚动，不再有一屏 8 人的隐含限制。
using System.Collections.Generic;
using UnityEngine;
using Ra2md.Assets;
using Ra2md.Core;
using Ra2md.World;

namespace Ra2md.Menu
{
    /// <summary>遭遇战容量与界面常量（唯一改容量的地方）。</summary>
    public static class SkirmishLimits
    {
        /// <summary>玩家槽数组容量：0x40 = 64。改这里即可扩容，界面滚动条自动跟着变。</summary>
        public const int SlotCapacity = 0x40;

        /// <summary>一屏可见行数（只影响滚动窗口，不限制总槽数）。</summary>
        public const int VisibleRows = 8;

        /// <summary>地图一次最多能声明多少个起始点（超过容量的部分裁掉）。</summary>
        public static int ClampMapSlots(int value)
        {
            return Mathf.Clamp(value, 1, SlotCapacity);
        }
    }

    /// <summary>一个玩家槽（对照 RuntimeSkirmishPlayerSlot）。</summary>
    public sealed class SkirmishPlayerSlot
    {
        public bool Enabled;
        public bool Human;
        public string Name;
        public string CountryId;
        public string ColorId;
        /// <summary>队伍："0"=无，1..4 = A..D；字符串与地图/存档格式保持一致。</summary>
        public string Team = "0";
        /// <summary>AI 难度索引（0 简单 / 1 普通 / 2 困难）。</summary>
        public int Difficulty;
        /// <summary>起始位置：0=随机，其余为航路点序号。</summary>
        public int StartPosition;

        public SkirmishPlayerSlot Clone()
        {
            return new SkirmishPlayerSlot
            {
                Enabled = Enabled, Human = Human, Name = Name,
                CountryId = CountryId, ColorId = ColorId, Team = Team,
                Difficulty = Difficulty, StartPosition = StartPosition,
            };
        }
    }

    /// <summary>可选国家（Name:&lt;Id&gt; CSF key + 英文兜底）。</summary>
    public sealed class SkirmishCountry
    {
        public string Id;
        public string NameKey;
        public string FallbackName;
        public string Side;

        public string DisplayName { get { return MenuStrings.Get(NameKey, FallbackName); } }
    }

    /// <summary>可选颜色（Color:&lt;Id&gt; CSF key + 色块值）。</summary>
    public sealed class SkirmishColor
    {
        public string Id;
        public string NameKey;
        public string FallbackName;
        public Color32 Swatch;

        public string DisplayName { get { return MenuStrings.Get(NameKey, FallbackName); } }
    }

    /// <summary>遭遇战地图条目（对照 RuntimeSkirmishMapResources.h 的 MapInfo）。</summary>
    public sealed class SkirmishMapInfo
    {
        public string Id;
        public string FileName;
        public string DisplayName;
        public string Theater;
        public int Width;
        public int Height;
        public int MinPlayers = 1;
        /// <summary>地图自身声明的最大人数（已被 SlotCapacity 裁剪）。</summary>
        public int MaxPlayers = SkirmishLimits.SlotCapacity;
        /// <summary>可用的起始坐标数量（起始点不足时用来兜底 MaxPlayers）。</summary>
        public int StartPointCount;
        public readonly List<string> Modes = new List<string>();
        /// <summary>归档条目的唯一键（"id:XXXXXXXX@归档"）；松散地图为 null。</summary>
        public string SourceKey;
        /// <summary>[PreviewPack] 解出的缩略图（未解出时为 null）。</summary>
        public Texture2D Preview;

        /// <summary>本局实际允许加入的槽数：地图声明优先，其次起始点数量，最后容量。</summary>
        public int SlotLimit
        {
            get
            {
                var limit = MaxPlayers > 0 ? MaxPlayers : (StartPointCount > 0 ? StartPointCount : SkirmishLimits.SlotCapacity);
                if (StartPointCount > 0 && StartPointCount < limit) limit = StartPointCount;
                return SkirmishLimits.ClampMapSlots(limit);
            }
        }
    }

    /// <summary>大厅里当前选择的一块状态（对照 RuntimeSkirmishSelection）。</summary>
    public sealed class SkirmishSelection
    {
        public readonly List<SkirmishPlayerSlot> Slots = new List<SkirmishPlayerSlot>(SkirmishLimits.SlotCapacity);
        public int MapIndex = -1;
        public int StartingCredits = 10000;
        public int UnitCount = 10;
        public int GameSpeed = 6;
        /// <summary>本局游戏模式（[Basic] GameMode 之一：作战 / 自由交战 / 小队联盟…）。</summary>
        public string GameMode;
        public bool ShortGame;
        public bool McvRepacks = true;
        public bool Crates;
        public bool Superweapons = true;
        public bool BuildOffAlly;
        public bool ComputerAiEnabled = true;
        public bool NoCooldown;

        public SkirmishSelection()
        {
            // 容量固定按 SlotCapacity 开，启用位由地图限制裁剪，滚动的只是显示窗口。
            for (var i = 0; i < SkirmishLimits.SlotCapacity; i++) Slots.Add(new SkirmishPlayerSlot());
        }

        public void Reset(Catalog catalog)
        {
            for (var i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                slot.Enabled = i < 2;            // 原版默认开 2 席：玩家 + 1 个电脑
                slot.Human = i == 0;
                slot.Name = i == 0 ? string.Empty : null;
                slot.CountryId = catalog.DefaultCountryId(i);
                slot.ColorId = catalog.DefaultColorId(i);
                slot.Team = "0";
                slot.Difficulty = 1;
                slot.StartPosition = 0;
            }
            MapIndex = -1;
        }

        /// <summary>地图决定可加入人数：超出限制的槽一律关闭（对照 applyMapSlotLimit）。</summary>
        public void ApplyMapLimit(int limit)
        {
            limit = SkirmishLimits.ClampMapSlots(limit);
            for (var i = limit; i < Slots.Count; i++) Slots[i].Enabled = false;

            // 至少保留一个对手（对照：limit>=2 且 1..limit 全关时开第二席）。
            if (limit >= 2)
            {
                var hasOpponent = false;
                for (var i = 1; i < limit; i++)
                {
                    if (Slots[i].Enabled) { hasOpponent = true; break; }
                }
                if (!hasOpponent) Slots[1].Enabled = true;
            }
        }
    }

    /// <summary>国家 / 颜色 / 地图目录：所有数量都由数据决定，界面不写死。</summary>
    public sealed class Catalog
    {
        public readonly List<SkirmishCountry> Countries = new List<SkirmishCountry>();
        public readonly List<SkirmishColor> Colors = new List<SkirmishColor>();
        public readonly List<SkirmishMapInfo> Maps = new List<SkirmishMapInfo>();

        /// <summary>原版遭遇战默认九国（CSF key = Name:&lt;Id&gt;），缺条时用英文兜底。</summary>
        private static readonly string[] DefaultCountryIds =
        {
            "Americans", "Alliance", "French", "Germans", "British",
            "Russians", "Africans", "Confederation", "Arabs",
        };

        private static readonly string[] DefaultCountryFallbacks =
        {
            "Americans", "Alliance", "French", "Germans", "British",
            "Russians", "Africans", "Confederation", "Arabs",
        };

        private static readonly string[] DefaultColorIds =
        {
            "Gold", "Red", "Blue", "Green", "Orange", "Purple", "Teal", "Silver",
        };

        private static readonly Color32[] DefaultColorSwatches =
        {
            new Color32(255, 214, 0, 255), new Color32(214, 40, 32, 255),
            new Color32(58, 110, 220, 255), new Color32(56, 170, 72, 255),
            new Color32(232, 130, 24, 255), new Color32(148, 76, 196, 255),
            new Color32(28, 168, 168, 255), new Color32(196, 196, 196, 255),
        };

        public void BuildCountries()
        {
            Countries.Clear();
            // 先按规则文件 [Countries] 的顺序取（存在就跟着规则走，加国家不需要改界面）。
            var rules = TryLoadRulesCountries();
            var ids = rules.Count > 0 ? rules : new List<string>(DefaultCountryIds);
            for (var i = 0; i < ids.Count; i++)
            {
                Countries.Add(new SkirmishCountry
                {
                    Id = ids[i],
                    NameKey = "Name:" + ids[i],
                    FallbackName = i < DefaultCountryFallbacks.Length && ids[i] == DefaultCountryIds[i]
                        ? DefaultCountryFallbacks[i]
                        : ids[i],
                    Side = string.Empty,
                });
            }
        }

        public void BuildColors()
        {
            Colors.Clear();
            for (var i = 0; i < DefaultColorIds.Length; i++)
            {
                Colors.Add(new SkirmishColor
                {
                    Id = DefaultColorIds[i],
                    NameKey = "Color:" + DefaultColorIds[i],
                    FallbackName = DefaultColorIds[i],
                    Swatch = DefaultColorSwatches[i],
                });
            }
        }

        /// <summary>规则文件里的国家顺序；读不到就返回空（调用方回退默认九国）。</summary>
        private static List<string> TryLoadRulesCountries()
        {
            var result = new List<string>();
            byte[] bytes;
            string origin;
            if (!Ra2md.Menu.MenuResourceLibrary.TryLoadFirst(
                    new[] { "rulesmd.ini", "rules.ini" }, out bytes, out origin) || bytes == null)
            {
                return result;
            }

            var ini = MapIniDocument.Parse(MapIniDocument.DecodeText(bytes));
            var sec = ini.GetSection("Countries");
            if (sec == null) return result;
            foreach (var entry in sec.Entries)
            {
                var value = entry.Value.Trim();
                if (value.Length > 0 && !result.Contains(value)) result.Add(value);
            }
            return result;
        }

        public string DefaultCountryId(int slotIndex)
        {
            if (Countries.Count == 0) return "Americans";
            return Countries[slotIndex % Countries.Count].Id;
        }

        public string DefaultColorId(int slotIndex)
        {
            if (Colors.Count == 0) return "Gold";
            return Colors[slotIndex % Colors.Count].Id;
        }

        public SkirmishCountry FindCountry(string id)
        {
            for (var i = 0; i < Countries.Count; i++)
            {
                if (Countries[i].Id == id) return Countries[i];
            }
            return null;
        }

        public SkirmishColor FindColor(string id)
        {
            for (var i = 0; i < Colors.Count; i++)
            {
                if (Colors[i].Id == id) return Colors[i];
            }
            return null;
        }

        public int IndexOfCountry(string id)
        {
            for (var i = 0; i < Countries.Count; i++)
            {
                if (Countries[i].Id == id) return i;
            }
            return -1;
        }

        public int IndexOfColor(string id)
        {
            for (var i = 0; i < Colors.Count; i++)
            {
                if (Colors[i].Id == id) return i;
            }
            return -1;
        }

        /// <summary>
        /// 扫描地图：松散目录（.mmx/.yro/.map/.mpr）→ 逐个解析 [Header]/[Basic]/[Waypoints]，
        /// 再从归档（MULTI.MIX / MAPS01 / MAPS02 / mapsmd03）按内容嗅探补进来，
        /// 每张地图顺手解 [PreviewPack] 缩略图。
        /// </summary>
        public void BuildMaps()
        {
            Maps.Clear();

            // 1) 官方地图：名单来自 missionsmd.pkt [MultiMaps]，显示名来自 CSR 的 NAME:/DESC: 键
            var official = SkirmishMapCatalog.Entries;
            var seen = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < official.Count; i++)
            {
                var entry = official[i];
                var doc = ScenarioLoader.LoadByName(entry.FileName);
                if (doc == null) continue;
                var info = ToMapInfo(entry.FileName, doc);
                info.FileName = entry.FileName;
                info.DisplayName = entry.DisplayName;
                if (entry.MinPlayers > 0) info.MinPlayers = entry.MinPlayers;
                if (entry.MaxPlayers > 0) info.MaxPlayers = SkirmishLimits.ClampMapSlots(entry.MaxPlayers);
                info.Modes.AddRange(entry.GameModes);
                info.Preview = MapPreview.ToTexture(MapPreview.Decode(doc.Ini));
                Maps.Add(info);
                seen[entry.Section] = true;
            }

            // 2) 用户地图 / 松散 .mmx：包里那份 .map 的 [Basic] Name 就是显示名；
            //    只有 MultiplayerOnly=1 的多人图进遭遇战列表，任务图一律不解析进来。
            var names = ScenarioLoader.EnumerateLooseMaps();
            for (var i = 0; i < names.Count; i++)
            {
                var baseName = System.IO.Path.GetFileNameWithoutExtension(names[i]);
                if (seen.ContainsKey(baseName)) continue;
                byte[] mapBytes;
                string innerName;
                if (!ScenarioLoader.TryReadScenarioBytes(names[i], out mapBytes, out innerName)) continue;
                var doc = ScenarioLoader.LoadFromBytes(mapBytes, innerName);
                if (doc == null || !doc.MultiplayerOnly) continue;
                var info = ToMapInfo(names[i], doc);
                info.FileName = names[i];
                info.Preview = MapPreview.ToTexture(MapPreview.Decode(
                    MapIniDocument.Parse(MapIniDocument.DecodeText(mapBytes))));
                Maps.Add(info);
                seen[baseName] = true;
            }

            // 3) 归档里的多人图（无 pkt 名单的 MOD / 自制图）；任务图（MAPS01/02 战役包）在这里被 MultiplayerOnly 滤掉。
            //    官方多人图没有 [Basic] Name、已在分支 1 按 pkt 名单入列，这里不带 Name 的条目直接跳过，避免重复和 CRC 上屏。
            foreach (var source in ScenarioLoader.EnumerateArchiveMaps())
            {
                var doc = ScenarioLoader.LoadFromBytes(source.Data, source.Name);
                if (doc == null || !doc.MultiplayerOnly) continue;
                if (string.IsNullOrEmpty(doc.DisplayName)) continue;
                var info = ToMapInfo(source.Name, doc);
                info.Id = source.Key;
                info.SourceKey = source.Key;
                info.Preview = MapPreview.ToTexture(MapPreview.Decode(
                    MapIniDocument.Parse(MapIniDocument.DecodeText(source.Data))));
                Maps.Add(info);
            }

            Maps.Sort((a, b) =>
            {
                if (a.MaxPlayers != b.MaxPlayers) return a.MaxPlayers.CompareTo(b.MaxPlayers);
                return string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase);
            });

            BootLog.Log($"[遭遇战] 地图目录 {Maps.Count} 张（含预览 {CountPreviews()} ）" +
                $"，玩家槽容量 {SkirmishLimits.SlotCapacity}");
        }

        private int CountPreviews()
        {
            var n = 0;
            for (var i = 0; i < Maps.Count; i++) if (Maps[i].Preview != null) n++;
            return n;
        }

        /// <summary>按文件名取松散地图的 [PreviewPack] 缩略图。</summary>
        private static Texture2D LoadPreview(string fileName)
        {
            byte[] data;
            string origin;
            if (!VirtualFileSystem.Instance.TryResolve(fileName, out data, out origin) || data == null)
                return null;
            return MapPreview.ToTexture(MapPreview.Decode(
                MapIniDocument.Parse(MapIniDocument.DecodeText(data))));
        }

        private static SkirmishMapInfo ToMapInfo(string fileName, ScenarioDocument doc)
        {
            var info = new SkirmishMapInfo
            {
                Id = System.IO.Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Theater = doc.Theater,
                Width = doc.Bounds.Width,
                Height = doc.Bounds.Height,
                MinPlayers = doc.MinPlayers > 0 ? doc.MinPlayers : 1,
            };

            // 起始点数量 = 地图里的「出生 waypoint」个数，不是 [Waypoints] 总数：
            // 战役图同样有大量航路点（触发器/巡逻用），全算进去会把 4 人的遭遇战图撑成 20 席。
            // 口径：[Header] NumberStartingPoints 是权威声明；RA2 旧格式没有 [Header]，
            // 起始点按多人图约定取「索引 0..7 且坐标有效」的 waypoint（最多 8 个）。
            var startPoints = doc.NumberStartingPoints;
            if (startPoints <= 0)
            {
                for (var i = 0; i < doc.Waypoints.Count; i++)
                {
                    var wp = doc.Waypoints[i];
                    if (!wp.Valid) continue;
                    int index;
                    if (!int.TryParse(wp.Id, out index)) continue;
                    if (index < 0 || index > 7) continue;
                    startPoints++;
                }
                if (startPoints < 1) startPoints = 1;
            }

            info.StartPointCount = startPoints;
            info.MaxPlayers = SkirmishLimits.ClampMapSlots(doc.MaxPlayers > 0 ? doc.MaxPlayers : startPoints);
            // 显示名只允许来自 [Basic] Name / CSF；缺省时回落可读的文件名词干，
            // 绝不把归档 CRC 键（id:XXXXXXXX@mix）当成名字上屏——原版选图页根本没有这种串。
            info.DisplayName = !string.IsNullOrEmpty(doc.DisplayName)
                ? doc.DisplayName
                : PrettyMapName(fileName);
            info.Modes.AddRange(doc.GameModes);
            return info;
        }

        /// <summary>文件名 → 可读词干：去扩展名、去前导 X、去掉 id: 前缀与归档后缀，保留字母数字。</summary>
        private static string PrettyMapName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "---";
            var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var at = stem.IndexOf('@');
            if (at > 0) stem = stem.Substring(0, at);
            if (stem.StartsWith("id:", System.StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(3);
            if (stem.Length > 1 && stem[0] == 'X' && char.IsLetter(stem[1])) stem = stem.Substring(1);
            // 纯十六进制 CRC（8 位 hex 且不是合法名字）不展示，避免回退成哈希串
            if (stem.Length == 8 && uint.TryParse(stem, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) return "---";
            return stem;
        }
    }
}



