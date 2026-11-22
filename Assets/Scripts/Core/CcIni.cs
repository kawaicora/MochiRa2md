// CcIni.cs —— 原版 CCINIClass + RawFileClass(RA2MD.INI) 的最小等价实现
// 对应 WinMain 374-380：new(0x24) RawFileClass("RA2MD.INI") -> sub_4741F0(&dword_8870C0, ini,0,0)
// 读取语义对齐 sub_5276D0(ini, section, key, defaultValue)：
//   - 键不存在 / 解析失败 -> 返回 defaultValue（原版就是把旧值原样传进去再写回）
//   - [Video] ScreenWidth/ScreenHeight 是 qword 的低/高 dword，按 DWORD 读
// 写回语义对齐 sub_529560(ini, section, key, value)：[Intro] Play=0 必须落盘，否则每局都播片。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ra2md.Core
{
    public sealed class CcIni
    {
        private readonly string _path;
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _sectionOrder = new List<string>();

        public string Path => _path;

        public CcIni(string path)
        {
            _path = path;
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_path))
            {
                return;
            }

            foreach (var rawLine in File.ReadAllLines(_path, Encoding.Default))
            {
                var line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close > 1)
                    {
                        var name = line.Substring(1, close - 1).Trim();
                        if (!_sections.ContainsKey(name))
                        {
                            _sections[name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            _sectionOrder.Add(name);
                        }
                        _current = _sections[name];
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0 || _current == null) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                _current[key] = value;
            }
        }

        private Dictionary<string, string> _current = null;

        // 原版 INI 注释只有 ';'（Theater/地图文件里另有 '#' 的习惯，但 CCINI 只认 ';'）。
        private static string StripComment(string line)
        {
            int semi = line.IndexOf(';');
            return semi >= 0 ? line.Substring(0, semi) : line;
        }

        public bool HasSection(string section) => _sections.ContainsKey(section);

        public bool HasKey(string section, string key)
        {
            return _sections.TryGetValue(section, out var sec) && sec.ContainsKey(key);
        }

        public string GetString(string section, string key, string defaultValue = "")
        {
            if (_sections.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var v))
            {
                return v;
            }
            return defaultValue;
        }

        /// <summary>等价 sub_5276D0：读 DWORD，失败返回 defaultValue。</summary>
        public int GetInt(string section, string key, int defaultValue = 0)
        {
            var text = GetString(section, key, null);
            if (text == null) return defaultValue;
            return TryParseInt(text, out var value) ? value : defaultValue;
        }

        /// <summary>等价 sub_5295F0：读 bool（原版把任意非 0 当 true，缺省返回 defaultValue）。</summary>
        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            var text = GetString(section, key, null);
            if (text == null) return defaultValue;
            return ParseBool(text, defaultValue);
        }

        /// <summary>等价 sub_529560：写回并立即落盘（[Intro] Play=0 就靠这条）。</summary>
        public bool SetString(string section, string key, string value)
        {
            if (!_sections.TryGetValue(section, out var sec))
            {
                sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[section] = sec;
                _sectionOrder.Add(section);
            }
            sec[key] = value;
            return Save();
        }

        public bool SetInt(string section, string key, int value) => SetString(section, key, value.ToString());

        private bool Save()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var name in _sectionOrder)
                {
                    sb.Append('[').Append(name).Append(']').Append('\n');
                    foreach (var kv in _sections[name])
                    {
                        sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                    }
                    sb.Append('\n');
                }
                var dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, sb.ToString(), Encoding.Default);
                return true;
            }
            catch (Exception e)
            {
                BootLog.Log("[ini] save failed: " + e.Message);
                return false;
            }
        }

        // ---- 原版数值/布尔文本解析（CCINIClass 语义）----
        private static bool TryParseInt(string text, out int value)
        {
            text = text.Trim();
            if (text.Length == 0) { value = 0; return false; }

            // 支持 0x 前缀与十进制；原版还接受末尾带 'h' 的写法（少数 mod INI 这么写）。
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
            }
            if (text.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(text.Substring(0, text.Length - 1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
            }
            return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static readonly HashSet<string> TrueWords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "yes", "true", "on", "1", "y", "t" };
        private static readonly HashSet<string> FalseWords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "no", "false", "off", "0", "n", "f" };

        private static bool ParseBool(string text, bool defaultValue)
        {
            text = text.Trim();
            if (TrueWords.Contains(text)) return true;
            if (FalseWords.Contains(text)) return false;
            if (int.TryParse(text, out var number)) return number != 0;
            return defaultValue;
        }
    }
}
