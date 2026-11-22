// SkirmishMapCatalog.cs —— 遭遇战“游戏地图”列表的唯一来源：missionsmd.pkt。
//
// 原版选图页的名单不是扫 MIX 扫出来的，而是读 missionsmd.pkt 的 [MultiMaps]：
//   1=XMP29U2  …  16=XBarrel  … 31=XArena
// 每个值既是小节名，也是地图文件名（<值>.map，在 multimd.mix / MULTI.MIX 里），
// 小节里的 Description=NAME:xxx / DESC:xxx 就是 CSF 的显示名键 —— 这就是“地图名字取得出来”的链路。
using System;
using System.Collections.Generic;
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
            public string FileName;        // 值 + ".map"
            public string DescriptionKey;  // NAME:xxx / DESC:xxx
            public string DisplayName;     // CSF 解出的显示名
            public int MinPlayers = 1;
            public int MaxPlayers;
            public string GameMode;
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
            for (var i = 0; i < entries.Count; i++)
            {
                var section = (entries[i].Value ?? string.Empty).Trim();
                if (section.Length == 0) continue;

                var sec = ini.GetSection(section);
                var key = sec != null ? sec.GetString("Description", null) : null;
                var display = !string.IsNullOrEmpty(key) ? MenuStrings.Get(key, null) : null;
                if (string.IsNullOrEmpty(display)) display = PrettyName(section);

                result.Add(new Entry
                {
                    Section = section,
                    FileName = section + ".map",
                    DescriptionKey = key,
                    DisplayName = display,
                    MinPlayers = sec != null ? sec.GetInt("MinPlayers", 1) : 1,
                    MaxPlayers = sec != null ? sec.GetInt("MaxPlayers", 0) : 0,
                    GameMode = sec != null ? sec.GetString("GameMode", null) : null,
                });
            }

            BootLog.Log($"[选图] missionsmd.pkt {_source} 官方地图 {result.Count} 张" +
                (result.Count > 0 ? "，首项=" + result[0].Section + "/" + result[0].DisplayName +
                    "（" + (result[0].DescriptionKey ?? "无key") + "）" : string.Empty));
            return result;
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
