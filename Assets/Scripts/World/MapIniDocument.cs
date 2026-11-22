// MapIniDocument.cs —— 场景文件（.yro/.yrm/.map/.mpr）的最小 INI 解析器。
//
// 对照 CCINIClass：
//   * 段头 `[Section]`，键值 `Key=Value`，注释 `;` 截断；
//   * 同段允许重复键（[Actions] 一个触发器挂多组动作，按顺序保留）；
//   * 场景文件开头可能带 IsoMapPack5/OverlayPack 的二进制块，解析器按行扫描，
//     不匹配段头/键值的原始字节行直接跳过，不抛异常。
using System;
using System.Collections.Generic;
using System.Linq;
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

        public bool HasKey(string key)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Entries[i].Key, key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>把另一段的条目追加到本段末尾；后写入的键取值优先（GetString 取最后命中）。</summary>
        public void AppendFrom(IniSection child)
        {
            if (child == null) return;
            Entries.AddRange(child.Entries);
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

        public int SectionIndex(string name)
        {
            for (int i = 0; i < _order.Count; i++)
            {
                if (string.Equals(_order[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>把另一份 INI 合并进来：同名段追加条目，新段补齐；后合并的键取值优先。</summary>
        public void MergeFrom(MapIniDocument child)
        {
            if (child == null) return;
            foreach (var childSec in child._order)
            {
                if (!_sections.TryGetValue(childSec.Name, out var sec))
                {
                    sec = new IniSection { Name = childSec.Name };
                    _sections[childSec.Name] = sec;
                    _order.Add(sec);
                }
                sec.AppendFrom(childSec);
            }
        }

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

        // ---------------------------------------------------------------- 扩展指令
        //
        // 口径对照参考项目：
        //   * Ares `[#include]`：段内每个键的值是要并入的 INI 文件名；缺文件跳过；
        //     每个根文件加载周期内同一文件只读一次；后读覆盖先写。
        //   * Phobos `[$Include]`：段内条目值是文件名；Phobos 段优先并让 Ares 段失活。
        //   * `$Inherits`：段内键，逗号分隔父段名；父段条目回填子段未自定义的键，
        //     子段自有键永远优先；继承在 include 展开之后进行。

        private sealed class IncludeState
        {
            public readonly HashSet<string> Visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Path = new List<string>();
            public int References;
        }

        /// <summary>归一化包含身份：反斜杠转正斜杠、去 `.`/`..`、小写，与宿主路径无关。</summary>
        private static string IncludeIdentity(string name)
        {
            var parts = new List<string>();
            foreach (var raw in name.Replace('\\', '/').Split('/'))
            {
                if (raw.Length == 0 || raw == ".") continue;
                if (raw == "..") { if (parts.Count > 0 && parts[parts.Count - 1] != "..") parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(raw);
            }
            return string.Join("/", parts).ToLowerInvariant();
        }

        /// <summary>
        /// 展开 include / inherits 指令。<paramref name="load"/> 按文件名取字节，取不到返回 null。
        /// </summary>
        public static MapIniDocument ExpandDirectives(MapIniDocument root, string rootName,
            Func<string, byte[]> load, bool aresEnabled, bool phobosEnabled)
        {
            if (root == null) return new MapIniDocument();

            var aresSection = Ra2md.Managers.GlobalConfigManager.Resources.AresIncludeSection;
            var phobosSection = Ra2md.Managers.GlobalConfigManager.Resources.PhobosIncludeSection;
            var inheritsKey = Ra2md.Managers.GlobalConfigManager.Resources.InheritsKey;

            var dialect = string.Empty;
            if (phobosEnabled && root.HasSection(phobosSection)) dialect = phobosSection;
            else if (aresEnabled && root.HasSection(aresSection)) dialect = aresSection;

            if (dialect.Length > 0)
            {
                var state = new IncludeState();
                state.Visited.Add(IncludeIdentity(rootName));
                state.Path.Add(rootName);
                WalkIncludes(root, dialect, load, state);
                state.Path.RemoveAt(state.Path.Count - 1);
            }

            var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in root._order.ToArray())
            {
                Inherit(root, section.Name, inheritsKey, completed, new List<string>());
            }
            return root;
        }

        private static void WalkIncludes(MapIniDocument doc, string sectionName,
            Func<string, byte[]> load, IncludeState state)
        {
            var section = doc.GetSection(sectionName);
            if (section == null) return;

            var names = new List<string>();
            foreach (var entry in section.Entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Value)) names.Add(entry.Value.Trim());
            }

            var maxDepth = Ra2md.Managers.GlobalConfigManager.Resources.IncludeMaxDepth;
            var maxRefs = Ra2md.Managers.GlobalConfigManager.Resources.IncludeMaxReferences;

            foreach (var name in names)
            {
                var identity = IncludeIdentity(name);
                if (state.Visited.Contains(identity)) continue;
                if (state.References >= maxRefs)
                {
                    Ra2md.Core.BootLog.Log("[ini] include 引用数超限，跳过 " + name);
                    continue;
                }
                if (state.Path.Count > maxDepth)
                {
                    Ra2md.Core.BootLog.Log("[ini] include 深度超限，跳过 " + name);
                    continue;
                }

                byte[] bytes;
                try { bytes = load != null ? load(name) : null; }
                catch (Exception e)
                {
                    Ra2md.Core.BootLog.Log("[ini] include 读取失败 " + name + "：" + e.Message);
                    continue;
                }
                if (bytes == null)
                {
                    Ra2md.Core.BootLog.Log("[ini] include 缺失（跳过）：" + name);
                    continue;
                }

                state.Visited.Add(identity);
                state.References++;
                state.Path.Add(name);
                try
                {
                    var child = Parse(DecodeText(bytes));
                    doc.MergeFrom(child);
                    WalkIncludes(child, sectionName, load, state);
                }
                finally
                {
                    state.Path.RemoveAt(state.Path.Count - 1);
                }
            }
        }

        private static void Inherit(MapIniDocument doc, string name, string inheritsKey,
            HashSet<string> completed, List<string> chain)
        {
            if (completed.Contains(name)) return;
            var index = doc.SectionIndex(name);
            if (index < 0) { completed.Add(name); return; }

            if (chain.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                Ra2md.Core.BootLog.Log("[ini] $Inherits 循环引用跳过：" + name);
                return;
            }

            var section = doc._order[index];
            var parents = SplitComma(section.GetString(inheritsKey, string.Empty));
            chain.Add(name);
            foreach (var parent in parents)
            {
                if (string.IsNullOrWhiteSpace(parent)) continue;
                var parentIndex = doc.SectionIndex(parent);
                if (parentIndex < 0)
                {
                    Ra2md.Core.BootLog.Log("[ini] $Inherits 父段缺失：" + parent);
                    continue;
                }
                Inherit(doc, parent, inheritsKey, completed, chain);
                var parentSec = doc._order[parentIndex];
                foreach (var entry in parentSec.Entries)
                {
                    if (string.Equals(entry.Key, inheritsKey, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.Key, Ra2md.Managers.GlobalConfigManager.Resources.AresIncludeSection, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.Key, Ra2md.Managers.GlobalConfigManager.Resources.PhobosIncludeSection, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (section.HasKey(entry.Key)) continue; // 子段自有键优先
                    section.Entries.Add(entry);
                }
            }
            chain.RemoveAt(chain.Count - 1);
            completed.Add(name);
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
