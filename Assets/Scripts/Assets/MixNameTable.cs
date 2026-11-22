// MixNameTable.cs —— MIX 条目名回填表。
// MIX 目录只存 CRC32 名字哈希，没有名字本体；这里维护 名字 ↔ 哈希 双向表，
// 并允许把松散目录里的真实文件名批量注入（命中率随目录内容提升）。
using System;
using System.Collections.Generic;
using System.IO;

namespace Ra2md.Assets
{
    public static class MixNameTable
    {
        private static readonly Dictionary<uint, string> IdToName = new Dictionary<uint, string>();
        private static readonly Dictionary<string, uint> NameToId = new Dictionary<string, uint>();
        private static bool _seeded;

        /// <summary>高置信度名字（已在本机真实归档上核对命中）。表归口全局配置管理器。</summary>
        private static readonly string[] KnownNames = Combine(
            Ra2md.Managers.GlobalConfigManager.Resources.KnownArchiveNames,
            Ra2md.Managers.GlobalConfigManager.Resources.LooseResourceNames);

        private static string[] Combine(string[] a, string[] b)
        {
            var result = new string[a.Length + b.Length];
            Array.Copy(a, 0, result, 0, a.Length);
            Array.Copy(b, 0, result, a.Length, b.Length);
            return result;
        }

        public static void EnsureSeeded()
        {
            if (_seeded) return;
            _seeded = true;
            foreach (var name in KnownNames) Register(name);
        }

        public static void Register(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;

            string baseName = fileName;
            int sep = Math.Max(baseName.LastIndexOf('/'), baseName.LastIndexOf('\\'));
            if (sep >= 0) baseName = baseName.Substring(sep + 1);
            if (baseName.Length == 0) return;

            uint id = MixArchive.FileIdFromName(baseName);
            NameToId[baseName.ToLowerInvariant()] = id;
            if (!IdToName.ContainsKey(id)) IdToName[id] = baseName;
        }

        /// <summary>
        /// 给一个已知 ID 绑一个名字（名字的 CRC 与 entry.Id 不一致时用）。
        /// 典型场景：MIX 条目真实 ID 是 0x31301AE2，但文件本名算出来的 CRC 对不上，
        /// 只能按「实测 ID + 逻辑名」登记，才能让 LoadRawByName 命中。
        /// </summary>
        public static void RegisterAlias(string fileName, uint id)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;
            string baseName = fileName;
            int sep = Math.Max(baseName.LastIndexOf('/'), baseName.LastIndexOf('\\'));
            if (sep >= 0) baseName = baseName.Substring(sep + 1);
            if (baseName.Length == 0) return;

            NameToId[baseName.ToLowerInvariant()] = id;
            if (!IdToName.ContainsKey(id)) IdToName[id] = baseName;

            // 同时登记无扩展名的短名，方便 INI 只写 SHP 名时也能反查
            string stem = Path.GetFileNameWithoutExtension(baseName);
            if (stem.Length > 0 && !NameToId.ContainsKey(stem.ToLowerInvariant()))
                NameToId[stem.ToLowerInvariant()] = id;
        }

        /// <summary>把目录里所有文件名注入名字表（提升回填命中率）。</summary>
        public static int RegisterDirectory(string directory, bool recursive = true)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return 0;

            EnsureSeeded();
            int added = 0;
            var files = Directory.GetFiles(directory, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (NameToId.ContainsKey(name.ToLowerInvariant())) continue;
                Register(name);
                added++;
            }
            return added;
        }

        /// <summary>
        /// 从 INI 文本里收集文件名：art(md).ini 的段名就是 SHP 名（[SDBTNANM] → sdbtnanm.shp），
        /// Image= / Cameo= 等键直接给出文件名。MIX 目录只有 CRC，靠这些名字才能反查条目。
        /// </summary>
        public static int RegisterIni(string iniText)
        {
            if (string.IsNullOrEmpty(iniText)) return 0;

            EnsureSeeded();
            int added = 0;
            var lines = iniText.Split('\n');
            string section = null;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (section.Length > 0 && !section.Contains(" ") && !IsSpecialSection(section))
                    {
                        added += RegisterName(section);
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (value.Length == 0 || value.IndexOf(' ') >= 0) continue;

                // 只取文件名类键，避免把数字/路径塞进名字表
                if (!IsFileNameKey(key)) continue;
                added += RegisterName(value);
            }

            return added;
        }

        private static bool IsSpecialSection(string section)
        {
            switch (section.ToLowerInvariant())
            {
                case "header":
                case "basic":
                case "audiovisual":
                case "colorscheme":
                case "cameos":
                case "animations":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsFileNameKey(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "image":
                case "cameo":
                case "altcameo":
                case "newtheater":
                case "filename":
                case "name":
                    return true;
                default:
                    return false;
            }
        }

        private static int RegisterName(string baseName)
        {
            int added = 0;
            if (!NameToId.ContainsKey(baseName.ToLowerInvariant())) { Register(baseName); added++; }
            // 游戏侧引用 SHP 时会补 .shp 后缀，两条都要登记
            string withShp = baseName + ".shp";
            if (!NameToId.ContainsKey(withShp.ToLowerInvariant())) { Register(withShp); added++; }
            return added;
        }

        public static string LookupName(uint id)
        {
            EnsureSeeded();
            return IdToName.TryGetValue(id, out var name) ? name : null;
        }

        public static uint? LookupId(string fileName)
        {
            EnsureSeeded();
            if (NameToId.TryGetValue(fileName.ToLowerInvariant(), out uint id)) return id;
            return null;
        }

        public static int Count
        {
            get { EnsureSeeded(); return IdToName.Count; }
        }
    }
}
