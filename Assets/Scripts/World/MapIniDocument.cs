// MapIniDocument.cs —— 场景文件（.yro/.yrm/.map/.mpr）的最小 INI 解析器。
//
// 对照 CCINIClass：
//   * 段头 `[Section]`，键值 `Key=Value`，注释 `;` 截断；
//   * 同段允许重复键（[Actions] 一个触发器挂多组动作，按顺序保留）；
//   * 场景文件开头可能带 IsoMapPack5/OverlayPack 的二进制块，解析器按行扫描，
//     不匹配段头/键值的原始字节行直接跳过，不抛异常。
using System;
using System.Collections.Generic;
using System.Text;

namespace Ra2md.World
{
    /// <summary>一个 INI 段：有序键值对（重复键保留）。</summary>
    public sealed class IniSection
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Entries = new List<KeyValuePair<string, string>>();

        public string GetString(string key, string defaultValue = "")
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return Entries[i].Value;
                }
            }
            return defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            var text = GetString(key, null);
            if (string.IsNullOrWhiteSpace(text)) return defaultValue;
            return int.TryParse(text.Trim(), out var v) ? v : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            var text = GetString(key, null);
            if (string.IsNullOrWhiteSpace(text)) return defaultValue;
            return IsOneValue(text);
        }

        public static bool IsOneValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim().ToLowerInvariant();
            return t == "yes" || t == "true" || t == "on" || t == "1" || t == "y" || t == "t";
        }
    }

    /// <summary>解析后的整份 INI；段名大小写不敏感。</summary>
    public sealed class MapIniDocument
    {
        private readonly Dictionary<string, IniSection> _sections =
            new Dictionary<string, IniSection>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IniSection> _order = new List<IniSection>();

        public IReadOnlyList<IniSection> Sections => _order;

        public IniSection GetSection(string name)
        {
            return _sections.TryGetValue(name, out var sec) ? sec : null;
        }

        public bool HasSection(string name) => _sections.ContainsKey(name);

        public static MapIniDocument Parse(string text)
        {
            var doc = new MapIniDocument();
            if (string.IsNullOrEmpty(text)) return doc;

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            IniSection current = null;

            foreach (var raw in lines)
            {
                var line = StripComment(raw).Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close > 1)
                    {
                        var name = line.Substring(1, close - 1).Trim();
                        if (!doc._sections.TryGetValue(name, out current))
                        {
                            current = new IniSection { Name = name };
                            doc._sections[name] = current;
                            doc._order.Add(current);
                        }
                    }
                    continue;
                }

                if (current == null) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = Unquote(line.Substring(eq + 1).Trim());
                current.Entries.Add(new KeyValuePair<string, string>(key, value));
            }

            return doc;
        }

        private static string StripComment(string line)
        {
            int semi = line.IndexOf(';');
            return semi >= 0 ? line.Substring(0, semi) : line;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        /// <summary>逗号分隔表，逐项去空白；空字段保留为空串。</summary>
        public static string[] SplitComma(string value)
        {
            if (string.IsNullOrEmpty(value)) return Array.Empty<string>();
            var parts = value.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        /// <summary>宽松整数解析（支持 0x 前缀），失败返回 defaultValue。</summary>
        public static int ParseInt(string text, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(text)) return defaultValue;
            var t = text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var hex) ? hex : defaultValue;
            }
            return int.TryParse(t, out var v) ? v : defaultValue;
        }

        /// <summary>把字节按 Latin1 解码为文本（场景 INI 是 ANSI，二进制块不会形成可解析行）。</summary>
        public static string DecodeText(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            return Encoding.GetEncoding("ISO-8859-1").GetString(data);
        }

        /// <summary>是/否取值判定（委托到 IniSection.IsOneValue），供解析器按字段位使用。</summary>
        public static bool IsOneValue(string text) => IniSection.IsOneValue(text);
    }
}
