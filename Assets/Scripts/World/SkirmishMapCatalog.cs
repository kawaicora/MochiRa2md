// SkirmishMapCatalog.cs —— 遭遇战“游戏地图”列表的唯一来源：missionsmd.pkt。
//
// 原版选图页的名单不是扫 MIX 扫出来的，而是读 missionsmd.pkt 的 [MultiMaps]：
//   1=XMP29U2  …  16=XBarrel  … 31=XArena
// 每个值既是小节名，也是地图文件名（<值>.map，在 multimd.mix / MULTI.MIX 里），
// 小节里的 Description=NAME:xxx / DESC:xxx 就是 CSF 的显示名键 —— 这就是“地图名字取得出来”的链路。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ra2md.Assets;
using Ra2md.Core;
using Ra2md.Menu;

namespace Ra2md.World
{
    public static class SkirmishMapCatalog
    {
        public sealed class Entry
        {
            public string Section;
            public string FileName;        // 解析到的场景文件名
            public string DescriptionKey;  // CSF 标签（Description 字段，可能为 null）
            public string DisplayName;     // 最终显示名
            public int MinPlayers = 2;
            public int MaxPlayers = 4;
            public List<string> GameModes = new List<string>();
        }

        private static List<Entry> _entries;
        private static string _source;

        public static string Source => _source;
        public static int Count => Entries.Count;

        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                if (_entries == null) _entries = Load();
                return _entries;
            }
        }

        public static void Reload() => _entries = null;

        private static List<Entry> Load()
        {
            var result = new List<Entry>();
            MenuResourceLibrary.EnsureMounted();

            byte[] bytes;
            string origin;
            var candidates = new[] { "missionsmd.pkt", "MISSIONSMD.PKT", "missions.pkt", "MISSIONS.PKT", "missionsmd.ini" };
            if (!MenuResourceLibrary.TryLoadFirst(candidates, out bytes, out origin) || bytes == null)
            {
                _source = "缺 missionsmd.pkt";
                BootLog.Log("[选图] " + _source);
                return result;
            }
            _source = origin;

            var text = MapIniDocument.DecodeText(bytes);
            if (string.IsNullOrEmpty(text) || text.IndexOf('=') < 0)
            {
                // 兜底：pkt 是纯 ASCII，DecodeText 的编码嗅探可能判错
                text = Encoding.ASCII.GetString(bytes);
            }

            var ini = MapIniDocument.Parse(text);
            var multi = ini.GetSection("MultiMaps");
            if (multi == null)
            {
                BootLog.Log("[选图] missionsmd.pkt 没有 [MultiMaps]");
                return result;
            }

            var entries = multi.Entries;
            var skippedMissing = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var section = (entries[i].Value ?? string.Empty).Trim();
                if (section.Length == 0) continue;

                // 名册只收 VFS 里确实能解析到场景的条目；取不到地图包/场景的条目不进列表。
                if (!ScenarioLoader.TryResolve(section, out var resolvedFile))
                {
                    skippedMissing++;
                    continue;
                }

                var sec = ini.GetSection(section);

                // 口径对照参考 pkt_display_name：DescriptionText 是直写文案；
                // Description 是 CSF 标签（剥掉 NAME:/DESC: 前缀后按 key 取）；都没有才用文件名兜底。
                var literal = sec != null ? sec.GetString("DescriptionText", null) : null;
                var labelRaw = sec != null ? sec.GetString("Description", null) : null;
                var label = StripLabelPrefix(labelRaw);
                var display = !string.IsNullOrWhiteSpace(literal)
                    ? literal.Trim()
                    : (!string.IsNullOrEmpty(label) ? MenuStrings.Get(label, null) : null);
                if (string.IsNullOrEmpty(display)) display = PrettyName(section);

                var modes = sec != null
                    ? MapIniDocument.SplitComma(sec.GetString("GameMode", string.Empty))
                    : Array.Empty<string>();

                result.Add(new Entry
                {
                    Section = section,
                    FileName = resolvedFile ?? section + ".map",
                    DescriptionKey = label,
                    DisplayName = display,
                    MinPlayers = sec != null ? sec.GetInt("MinPlayers", 2) : 2,
                    MaxPlayers = sec != null ? sec.GetInt("MaxPlayers", 4) : 4,
                    GameModes = new List<string>(modes.Where(m => !string.IsNullOrWhiteSpace(m))),
                });
            }

            BootLog.Log($"[选图] missionsmd.pkt {_source} 官方地图 {result.Count} 张" +
                (skippedMissing > 0 ? $"，缺场景跳过 {skippedMissing} 张" : string.Empty) +
                (result.Count > 0 ? "，首项=" + result[0].Section + "/" + result[0].DisplayName +
                    "（" + (result[0].DescriptionKey ?? "无key") + "）" : string.Empty));
            return result;
        }

        /// <summary>剥掉 CSF 标签前的 NAME:/DESC: 前缀（部分改版 pkt 会带）。</summary>
        private static string StripLabelPrefix(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var value = raw.Trim();
            var colon = value.IndexOf(':');
            if (colon > 0)
            {
                var prefix = value.Substring(0, colon).Trim();
                if (string.Equals(prefix, "NAME", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(prefix, "DESC", StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(colon + 1).Trim();
                }
            }
            return value.Length > 0 ? value : null;
        }

        /// <summary>没有 CSF 时的可读名：XBarrel → Barrel，XMP29U2 → MP29U2。</summary>
        private static string PrettyName(string section)
        {
            if (string.IsNullOrEmpty(section)) return section;
            var s = section;
            if (s.Length > 1 && (s[0] == 'X' || s[0] == 'x') && char.IsLetter(s[1])) s = s.Substring(1);
            return s;
        }
    }
}
