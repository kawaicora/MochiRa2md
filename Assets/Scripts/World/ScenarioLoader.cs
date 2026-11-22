// ScenarioLoader.cs —— 按名字从 VFS 取场景字节并解析为 ScenarioDocument。
//
// VFS 顺序与全局约束一致：StreamingAssets 松散文件 → MIX → 嵌套 MIX。
// 场景候选后缀：.yro（YR 战役）/ .yrm（YR 多人）/ .map / .mpr / .mmx。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ra2md.Assets;
using Ra2md.Core;
using Ra2md.Menu;
using UnityEngine;

namespace Ra2md.World
{
    public static class ScenarioLoader
    {
        private static readonly string[] Extensions = { ".yro", ".yrm", ".map", ".mpr", ".mmx" };
        private static readonly Dictionary<string, ScenarioDocument> Cache =
            new Dictionary<string, ScenarioDocument>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>归档里的一张地图条目。</summary>
        public sealed class MapSource
        {
            public string Name;
            public string Key;
            public uint Id;
            public string Archive;
            public byte[] Data;
        }

        private static readonly string[] MapArchives =
            Ra2md.Managers.GlobalConfigManager.Resources.MapArchiveNames;

        private static bool IsPackage(string fileName)
        {
            var lower = (fileName ?? string.Empty).ToLowerInvariant();
            foreach (var ext in Ra2md.Managers.GlobalConfigManager.Resources.MapPackageExtensions)
            {
                if (lower.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 松散 .mmx/.yro 都是 MIX 包：真正的 .map 场景在里面（Barrel.mmx → xbarrel.map，
        /// [Basic] Name=Yuri Submarine Pen）。直接按 INI 解析包体只会得到二进制垃圾，
        /// 这就是选图页显示“无 / 1240FA73 / 1-2 玩家”的根因。
        /// 这里把包体拆开，嗅探出里面那份场景。
        /// </summary>
        public static bool TryReadScenarioBytes(string fileName, out byte[] mapData, out string innerName)
        {
            mapData = null;
            innerName = null;
            if (string.IsNullOrEmpty(fileName)) return false;

            MenuResourceLibrary.EnsureMounted();
            byte[] bytes;
            string origin;
            if (!VirtualFileSystem.Instance.TryResolve(fileName, out bytes, out origin) || bytes == null) return false;

            if (!IsPackage(fileName))
            {
                mapData = bytes;
                innerName = fileName;
                return true;
            }

            var archive = new MixArchive();
            if (!archive.LoadFromBytes(bytes, System.IO.Path.GetFileName(fileName))) return false;

            MixEntry best = null;
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                var entry = archive.Entries[i];
                if (entry.Size < 1024) continue;
                if (!archive.TryReadEntry(entry, out var candidate) || candidate == null) continue;
                var headLen = Mathf.Min(candidate.Length, 4096);
                var head = System.Text.Encoding.ASCII.GetString(candidate, 0, headLen);
                if (head.IndexOf("[Basic]", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                best = entry;
                mapData = candidate;
                innerName = entry.Name ?? System.IO.Path.GetFileName(fileName);
                break;
            }

            if (mapData == null)
            {
                BootLog.Log($"[map] 包内无场景 {fileName}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 扫描归档里的地图：MULTI.MIX / MAPS01 / MAPS02 / mapsmd03。
        /// MIX 只有 CRC 没有文件名（name table 未覆盖这些条目），所以按内容嗅探：
        /// 头 4KB 内出现 [Basic] 且出现 [PreviewPack] 才当作地图，名字取 [Basic] Name。
        /// </summary>
        public static IReadOnlyList<MapSource> EnumerateArchiveMaps()
        {
            MenuResourceLibrary.EnsureMounted();
            var vfs = VirtualFileSystem.Instance;
            var result = new List<MapSource>();

            foreach (var archive in vfs.Archives)
            {
                var archiveName = archive.Name ?? string.Empty;
                var lower = archiveName.ToLowerInvariant();
                if (!MapArchives.Any(x => lower == x || lower.EndsWith("/" + x))) continue;

                foreach (var entry in archive.Entries)
                {
                    if (entry.Size < 4096 || entry.Size > 4 * 1024 * 1024) continue;
                    byte[] data;
                    string origin;
                    if (!vfs.TryResolveById(entry.Id, out data, out origin) || data == null) continue;

                    var headLen = Mathf.Min(data.Length, 4096);
                    var head = System.Text.Encoding.ASCII.GetString(data, 0, headLen);
                    if (head.IndexOf("[Basic]", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (head.IndexOf("[PreviewPack]", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var ini = MapIniDocument.Parse(MapIniDocument.DecodeText(data));
                    var basic = ini.GetSection("Basic");
                    var display = basic != null ? basic.GetString("Name", null) : null;
                    result.Add(new MapSource
                    {
                        Name = string.IsNullOrEmpty(display) ? entry.Id.ToString("X8") : display,
                        Key = "id:" + entry.Id.ToString("X8") + "@" + archiveName,
                        Id = entry.Id,
                        Archive = archiveName,
                        Data = data,
                    });
                }
            }

            BootLog.Log($"[map] 归档地图 {result.Count} 张（MULTI/MAPS01/MAPS02/mapsmd03）");
            return result;
        }

        /// <summary>按 "id:XXXXXXXX@归档" 键装载归档内地图。</summary>
        public static ScenarioDocument LoadByKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith("id:", System.StringComparison.Ordinal))
                return null;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var body = key.Substring(3);
            var at = body.IndexOf('@');
            if (at <= 0) return null;
            if (!uint.TryParse(body.Substring(0, at), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var id))
                return null;

            MenuResourceLibrary.EnsureMounted();
            byte[] data;
            string origin;
            if (!VirtualFileSystem.Instance.TryResolveById(id, out data, out origin) || data == null)
            {
                BootLog.Log($"[map] 未找到归档条目 {key}");
                return null;
            }

            var doc = LoadFromBytes(data, key);
            Cache[key] = doc;
            return doc;
        }

        /// <summary>按文件名（可带或不带后缀）装载场景；重复装载走缓存。</summary>
        public static ScenarioDocument LoadByName(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName)) return null;
            if (Cache.TryGetValue(mapName, out var cached)) return cached;

            MenuResourceLibrary.EnsureMounted();
            var vfs = VirtualFileSystem.Instance;

            // 候选名：原名 + 各场景后缀（调用方可传不带后缀的地图名）。
            var candidates = new List<string> { mapName };
            foreach (var ext in Extensions)
            {
                if (!mapName.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(mapName + ext);
                }
            }

            byte[] data = null;
             string resolvedName = null;
            foreach (var candidate in candidates)
            {
                byte[] mapBytes;
                string innerName;
                if (TryReadScenarioBytes(candidate, out mapBytes, out innerName) && mapBytes != null && mapBytes.Length > 0)
                {
                    data = mapBytes;
                    resolvedName = innerName ?? candidate;
                    break;
                }
            }

            if (data != null && data.Length > 0)
            {
                var doc = LoadFromBytes(data, Path.GetFileName(resolvedName));
                Cache[mapName] = doc;
                return doc;
            }

            BootLog.Log($"[map] 未找到场景 {mapName}");
            return null;
        }

        /// <summary>只探测场景是否在 VFS 可解析（不缓存、不解析），供名册过滤使用。</summary>
        public static bool TryResolve(string mapName, out string resolvedName)
        {
            resolvedName = null;
            if (string.IsNullOrWhiteSpace(mapName)) return false;
            MenuResourceLibrary.EnsureMounted();

            var candidates = new List<string> { mapName };
            foreach (var ext in Extensions)
            {
                if (!mapName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(mapName + ext);
                }
            }

            foreach (var candidate in candidates)
            {
                if (TryReadScenarioBytes(candidate, out var mapBytes, out var innerName)
                    && mapBytes != null && mapBytes.Length > 0)
                {
                    resolvedName = innerName ?? candidate;
                    return true;
                }
            }
            return false;
        }

        public static ScenarioDocument LoadFromBytes(byte[] data, string sourceName)
        {
            string text = MapIniDocument.DecodeText(data);
            var ini = MapIniDocument.Parse(text);
            var doc = ScenarioParser.Parse(ini, sourceName);

            BootLog.Log($"[map] {sourceName} theater={doc.Theater} size={doc.Bounds.Width}x{doc.Bounds.Height} " +
                $"houses={doc.Houses.Count} waypoints={doc.Waypoints.Count} " +
                $"units={doc.Units.Count} infantry={doc.Infantry.Count} structures={doc.Structures.Count} " +
                $"terrain={doc.Terrain.Count} teamTypes={doc.TeamTypes.Count} triggers={doc.Triggers.Count} " +
                $"events={doc.Events.Count} actions={doc.Actions.Count} warnings={doc.Warnings.Count}");

            foreach (var warning in doc.Warnings)
            {
                BootLog.Log("[map]   ! " + warning);
            }

            return doc;
        }

        /// <summary>枚举松散目录下可见的场景文件名（VFS 内归档场景待资源层补目录索引后再纳入）。</summary>
        public static IReadOnlyList<string> EnumerateLooseMaps()
        {
            var result = new List<string>();
            var root = Application.streamingAssetsPath;
            if (!Directory.Exists(root)) return result;

            foreach (var ext in Extensions)
            {
                foreach (var file in Directory.EnumerateFiles(root, "*" + ext))
                {
                    result.Add(Path.GetFileName(file));
                }
            }
            return result.Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void ClearCache() => Cache.Clear();
    }
}
