// MixFileSystem.cs —— 像文件系统一样访问 MIX（对齐 XCC Mixer 的工作方式）。
//
// 核心规则：
//   * 挂载只读「头部 + 目录」，几十 MB / 几百 MB 的档不会整档进内存
//   * 打开某个文件 = 在所属档里 seek 到该条目，只读它自己的字节
//   * 嵌套档 = 父档上的切片源（SliceMixSource），可继续向下嵌套：mix → mix → 文件
//   * 嵌套档按需打开（按名字判定候选），不做「把每个条目都读出来试一遍」的全量探测
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ra2md.Assets
{
    public sealed class MountedArchive
    {
        public MixArchive Archive;
        public string Name;                // 顶层为文件名，嵌套为 "父/子"
        public int Priority;
        public string FullPath;            // 顶层磁盘路径；嵌套为 null
        public MountedArchive Parent;      // 非空表示这是父归档里的嵌套子 MIX
        public int Sequence;               // 同优先级时的稳定顺序
        public string VirtualPath => Name;

        public IEnumerable<MixEntry> Entries => Archive.Entries;
    }

    /// <summary>目录列表项：可能是普通条目，也可能是一个可以继续打开的嵌套 MIX。</summary>
    public sealed class MixDirItem
    {
        public string Name;
        public uint Id;
        public long Size;
        public bool IsArchive;
        public MixEntry Entry;
        public MountedArchive Owner;
    }

    public sealed class VirtualFileSystem
    {
        private static VirtualFileSystem _instance;

        public static VirtualFileSystem Instance
        {
            get
            {
                if (_instance == null) _instance = new VirtualFileSystem();
                return _instance;
            }
        }

        public static void Reset() { _instance = new VirtualFileSystem(); }

        private readonly List<MountedArchive> _archives = new List<MountedArchive>();
        private readonly List<string> _looseRoots = new List<string>();
        private readonly List<string> _nestedWarnings = new List<string>();

        /// <summary>已按需打开过的嵌套档，键为 "父/子" 的虚拟路径（小写）。</summary>
        private readonly Dictionary<string, MountedArchive> _nestedCache = new Dictionary<string, MountedArchive>();

        private int _sequenceCounter;
        private bool _nestedSweepDone;

        public IReadOnlyList<MountedArchive> Archives => _archives;
        public IReadOnlyList<string> LooseRoots => _looseRoots;
        public int ArchiveCount => _archives.Count;
        public int EntryCount => _archives.Sum(a => a.Archive.Entries.Count);
        public bool Loaded { get; private set; }

        /// <summary>嵌套探测过程中被跳过的条目（诊断用）。</summary>
        public IReadOnlyList<string> NestedWarnings => _nestedWarnings;

        // 已知的内层归档名（RA2/YR 的固定结构）。名字表没回填时用 CRC 兜底判定。
        private static readonly string[] KnownNestedNames =
            Ra2md.Managers.GlobalConfigManager.Resources.KnownNestedArchiveNames;

        private static readonly HashSet<uint> KnownNestedIds =
            new HashSet<uint>(KnownNestedNames.Select(MixArchive.FileIdFromName));

        // ---- 挂载 ----

        public void AddLooseRoot(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            if (!Directory.Exists(directory)) return;
            if (_looseRoots.Contains(directory)) return;
            _looseRoots.Add(directory);
            _looseIndex = null;
            Loaded = true;
        }

        /// <summary>挂载一个顶层 MIX：只解析目录，不读取任何条目字节。</summary>
        public MountedArchive Mount(string path, int priority)
        {
            var archive = new MixArchive();
            if (!archive.Load(path))
            {
                UnityEngine.Debug.LogWarning("[MIX] 装载失败 " + path + " : " + archive.Error);
                return null;
            }

            var mounted = new MountedArchive
            {
                Archive = archive,
                Name = archive.DisplayName,
                Priority = priority,
                FullPath = path,
                Sequence = ++_sequenceCounter,
            };

            _archives.Add(mounted);
            SortArchives();
            Loaded = true;

            FillEntryNames(archive);
            return mounted;
        }

        public MountedArchive MountBytes(byte[] data, string displayName, int priority)
        {
            var archive = new MixArchive();
            if (!archive.LoadFromBytes(data, displayName)) return null;

            var mounted = new MountedArchive
            {
                Archive = archive,
                Name = displayName,
                Priority = priority,
                Sequence = ++_sequenceCounter,
            };

            _archives.Add(mounted);
            SortArchives();
            Loaded = true;

            FillEntryNames(archive);
            return mounted;
        }

        public void Unmount(MountedArchive mounted)
        {
            if (mounted == null) return;
            _archives.Remove(mounted);
            foreach (var key in _nestedCache.Where(kv => kv.Value == mounted).Select(kv => kv.Key).ToList())
            {
                _nestedCache.Remove(key);
            }
        }

        public void Clear()
        {
            _archives.Clear();
            _looseRoots.Clear();
            _nestedCache.Clear();
            _nestedWarnings.Clear();
            _sequenceCounter = 0;
            _nestedSweepDone = false;
            _looseIndex = null;
            Loaded = false;
        }

        private void SortArchives()
        {
            _archives.Sort((a, b) =>
            {
                if (a.Priority != b.Priority) return b.Priority.CompareTo(a.Priority);
                return a.Sequence.CompareTo(b.Sequence);
            });
        }

        private static void FillEntryNames(MixArchive archive)
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Name != null) continue;
                entry.Name = MixNameTable.LookupName(entry.Id);
            }
        }

        private int _openChildCount;

        // ---- 默认扫描 ----

        /// <summary>按优先级扫描目录内全部 *.mix 并挂载（只读目录，不读数据）。</summary>
        public void ScanDirectory(string directory)
        {
            if (!Directory.Exists(directory)) return;

            AddLooseRoot(directory);

            var files = Directory.GetFiles(directory)
                .Where(f => f.EndsWith(".mix", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                Mount(file, PriorityFor(Path.GetFileName(file)));
            }
        }

        /// <summary>MIX 优先级：数值越大越优先。</summary>
        public static int PriorityFor(string fileName)
        {
            string lower = fileName.ToLowerInvariant();

            if (lower.StartsWith("expand")) return 900;      // 扩展包最高
            if (lower == "ra2md.mix") return 800;
            if (lower == "ra2.mix") return 700;
            if (lower == "langmd.mix") return 650;
            if (lower == "language.mix") return 640;
            if (lower.StartsWith("multimd")) return 600;
            if (lower.StartsWith("multi.")) return 590;
            if (lower.StartsWith("thememd")) return 560;
            if (lower.StartsWith("theme.")) return 550;
            if (lower.StartsWith("maps")) return 500;
            if (lower.StartsWith("movies") || lower.StartsWith("movmd")) return 400;
            if (lower.StartsWith("wdt")) return 300;
            if (lower.StartsWith("sounds")) return 350;
            return 100;
        }

        // ---- 目录浏览 / 按路径打开 ----

        /// <summary>
        /// 列出某个虚拟目录。path 为空 → 顶层已挂载的档；path = "ra2.mix" → 该档的条目；
        /// "ra2.mix/audiomd.mix" → 内层档的条目（自动按需打开内层档）。
        /// </summary>
        public IReadOnlyList<MixDirItem> List(string virtualPath)
        {
            var items = new List<MixDirItem>();

            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
            {
                foreach (var mounted in _archives.Where(a => a.Parent == null))
                {
                    items.Add(new MixDirItem
                    {
                        Name = mounted.Name,
                        Id = 0,
                        Size = mounted.Archive.Length,
                        IsArchive = true,
                        Owner = mounted,
                    });
                }
                return items;
            }

            var owner = OpenArchive(virtualPath);
            if (owner == null) return items;

            foreach (var entry in owner.Archive.Entries)
            {
                string name = entry.Name ?? MixNameTable.LookupName(entry.Id);
                items.Add(new MixDirItem
                {
                    Name = name ?? ("0x" + entry.Id.ToString("X8")),
                    Id = entry.Id,
                    Size = entry.Size,
                    IsArchive = LooksLikeArchiveEntry(owner, entry),
                    Entry = entry,
                    Owner = owner,
                });
            }
            return items;
        }

        /// <summary>
        /// 打开一个归档（顶层或任意深度嵌套），例如 "ra2md.mix"、"langmd.mix/audiomd.mix"。
        /// 只解析目录；同一路径只解析一次，之后走缓存。
        /// </summary>
        public MountedArchive OpenArchive(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath)) return null;

            var segments = SplitPath(virtualPath);
            if (segments.Count == 0) return null;

            MountedArchive current = FindTopLevel(segments[0]);
            if (current == null) return null;
            if (segments.Count == 1) return current;

            for (int i = 1; i < segments.Count; i++)
            {
                current = OpenChild(current, segments[i]);
                if (current == null) return null;
            }
            return current;
        }

        /// <summary>按虚拟路径读取单个文件，例如 "langmd.mix/audiomd.mix/umenucl1.aud"。</summary>
        public bool OpenFile(string virtualPath, out byte[] data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(virtualPath)) return false;

            var segments = SplitPath(virtualPath);
            if (segments.Count == 0) return false;

            if (segments.Count == 1)
            {
                // 没有指定档：走全库查找（含松散目录与已打开/可打开的内层档）
                if (TryResolve(segments[0], out data, out _)) return true;
                return false;
            }

            var owner = OpenArchive(string.Join("/", segments.Take(segments.Count - 1)));
            if (owner == null) return false;

            var entry = ResolveEntry(owner.Archive, segments[segments.Count - 1]);
            if (entry == null) return false;
            return owner.Archive.TryReadEntry(entry, out data);
        }

        private MountedArchive FindTopLevel(string name)
        {
            string lower = Path.GetFileName(name).ToLowerInvariant();
            foreach (var mounted in _archives)
            {
                if (mounted.Parent != null) continue;
                if (mounted.Name.ToLowerInvariant() == lower) return mounted;
                if (mounted.FullPath != null &&
                    Path.GetFileName(mounted.FullPath).ToLowerInvariant() == lower) return mounted;
            }
            return null;
        }

        /// <summary>在 parent 里按名字打开一个嵌套档（切片源 + 只解析目录）。</summary>
        private MountedArchive OpenChild(MountedArchive parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName)) return null;

            string key = (parent.VirtualPath + "/" + childName).ToLowerInvariant();
            var cached = _nestedCache.TryGetValue(key, out var hit) ? hit : null;
            if (cached != null) return cached;

            var entry = ResolveEntry(parent.Archive, childName);
            if (entry == null || entry.Size <= 0) return null;

            var child = parent.Archive.OpenChild(entry, childName);
            if (child == null || !IsPlausibleArchive(child, entry.Size))
            {
                _nestedWarnings.Add(parent.VirtualPath + "/" + childName + " : 不是可解析的 MIX");
                return null;
            }

            FillEntryNames(child);

            var mounted = new MountedArchive
            {
                Archive = child,
                Name = parent.VirtualPath + "/" + (child.DisplayName ?? childName),
                Priority = parent.Priority,
                FullPath = null,
                Parent = parent,
                Sequence = ++_sequenceCounter,
            };

            _archives.Add(mounted);
            _nestedCache[key] = mounted;
            SortArchives();
            _openChildCount++;
            return mounted;
        }

        /// <summary>支持 "audio.mix" 这种真名，也支持列表里回落的 "0xFE9DF3BE" 这种 ID 写法。</summary>
        private static MixEntry ResolveEntry(MixArchive archive, string token)
        {
            if (archive == null || string.IsNullOrEmpty(token)) return null;

            if (token.Length > 2 && (token[0] == '0') && (token[1] == 'x' || token[1] == 'X'))
            {
                if (uint.TryParse(token.Substring(2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out uint id))
                {
                    return archive.FindById(id);
                }
                return null;
            }

            return archive.FindByName(token);
        }

        /// <summary>
        /// 该条目是不是嵌套档：名字命中已知内层档名就直接认；否则用 10 字节头嗅探
        /// （存档里很多内层档没有名字表条目，只靠名字判定会整体漏读，菜单 SHP 就全丢了）。
        /// </summary>
        private static bool LooksLikeArchiveEntry(MountedArchive owner, MixEntry entry)
        {
            if (entry == null || entry.Size < 4096) return false;

            string name = entry.Name ?? MixNameTable.LookupName(entry.Id);
            if (name != null && name.EndsWith(".mix", StringComparison.OrdinalIgnoreCase)) return true;
            if (KnownNestedIds.Contains(entry.Id)) return true;

            return owner != null && owner.Archive.LooksLikeChildArchive(entry);
        }

        // ---- 查找 ----

        // 与参考实现一致：裸文件名（无扩展名）在 MIX 里可能带这些后缀之一，逐个兜底。
        private static readonly string[] ExtensionCandidates =
            Ra2md.Managers.GlobalConfigManager.Resources.ExtensionCandidates;

        /// <summary>按文件名查找，返回命中的归档与条目；松散文件优先。</summary>
        public bool TryResolve(string fileName, out byte[] data, out string origin)
        {
            data = null;
            origin = null;
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            string baseName = PathOf(fileName);

            foreach (var root in _looseRoots)
            {
                var path = Path.Combine(root, baseName);
                if (!File.Exists(path)) continue;

                data = File.ReadAllBytes(path);
                origin = "松散:" + baseName;
                return true;
            }

            foreach (var candidate in CandidateNames(baseName))
            {
                if (TryResolveInOpened(candidate, out data, out origin)) return true;
            }

            // 全库未命中：按需打开顶层档里的内层归档，再试一次（仍只读目录）。
            if (TryOpenNestedCandidates())
            {
                foreach (var candidate in CandidateNames(baseName))
                {
                    if (TryResolveInOpened(candidate, out data, out origin)) return true;
                }
            }

            return false;
        }

        private bool TryResolveInOpened(string fileName, out byte[] data, out string origin)
        {
            data = null;
            origin = null;
            uint id = MixArchive.FileIdFromName(fileName);

            foreach (var mounted in _archives)
            {
                var entry = mounted.Archive.FindById(id);
                if (entry == null) continue;
                if (!mounted.Archive.TryReadEntry(entry, out data)) continue;
                origin = mounted.VirtualPath + ":0x" + entry.Id.ToString("X8");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 按一组候选文件名查找，返回第一个命中的：先扫根目录（松散文件），再扫归档。
        /// 文件选择只在这里做——上层只管给候选名，不判断谁优先。
        /// </summary>
        public bool TryResolveFirst(string[] candidateNames, out byte[] data, out string origin)
        {
            data = null;
            origin = null;
            if (candidateNames == null || candidateNames.Length == 0) return false;

            // 1) 根目录优先
            foreach (var root in _looseRoots)
            {
                foreach (var name in candidateNames)
                {
                    var baseName = PathOf(name);
                    var path = Path.Combine(root, baseName);
                    if (!File.Exists(path)) continue;

                    data = File.ReadAllBytes(path);
                    origin = "松散:" + baseName;
                    return true;
                }
            }

            // 2) 归档
            foreach (var name in candidateNames)
            {
                if (TryResolveInOpened(PathOf(name), out data, out origin)) return true;
            }

            // 3) 按需打开内层归档再试
            if (TryOpenNestedCandidates())
            {
                foreach (var name in candidateNames)
                {
                    if (TryResolveInOpened(PathOf(name), out data, out origin)) return true;
                }
            }

            return false;
        }

        public bool TryResolveById(uint id, out byte[] data, out string origin)
        {
            data = null;
            origin = null;

            // MOD 规则：根目录里的同名文件优先于 MIX（xxx.shp / rulemd.ini / ra2.csf 都按这条覆盖）。
            if (TryResolveLooseById(id, out data, out origin)) return true;

            if (TryResolveByIdInOpened(id, out data, out origin)) return true;

            if (TryOpenNestedCandidates())
            {
                return TryResolveByIdInOpened(id, out data, out origin);
            }
            return false;
        }

        // 根目录松散文件的 id 索引（按需建立；新增/删除文件后调 RefreshLooseIndex 重建）。
        private Dictionary<uint, string> _looseIndex;

        /// <summary>丢弃根目录索引，下次查找时重建。</summary>
        public void RefreshLooseIndex() { _looseIndex = null; }

        private bool TryResolveLooseById(uint id, out byte[] data, out string origin)
        {
            data = null;
            origin = null;
            if (_looseRoots.Count == 0) return false;

            if (_looseIndex == null) BuildLooseIndex();

            var path = _looseIndex.TryGetValue(id, out var p) ? p : null;
            if (path == null || !File.Exists(path)) return false;

            data = File.ReadAllBytes(path);
            origin = "松散:" + Path.GetFileName(path);
            return true;
        }

        /// <summary>只索引根目录一级文件（原版 MOD 就是把文件丢在安装目录根下）。</summary>
        private void BuildLooseIndex()
        {
            _looseIndex = new Dictionary<uint, string>();
            foreach (var root in _looseRoots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(root))
                    {
                        var name = Path.GetFileName(file);
                        var id = MixArchive.FileIdFromName(name);
                        if (!_looseIndex.ContainsKey(id)) _looseIndex[id] = file;
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[MIX] 根目录索引失败 " + root + " : " + e.Message);
                }
            }
        }

        private bool TryResolveByIdInOpened(uint id, out byte[] data, out string origin)
        {
            data = null;
            origin = null;

            foreach (var mounted in _archives)
            {
                var entry = mounted.Archive.FindById(id);
                if (entry == null) continue;
                if (!mounted.Archive.TryReadEntry(entry, out data)) continue;
                origin = mounted.VirtualPath + ":0x" + id.ToString("X8");
                return true;
            }
            return false;
        }

        public bool Exists(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            string baseName = PathOf(fileName);

            foreach (var root in _looseRoots)
            {
                if (File.Exists(Path.Combine(root, baseName))) return true;
            }

            uint id = MixArchive.FileIdFromName(baseName);
            if (_archives.Any(m => m.Archive.FindById(id) != null)) return true;

            if (TryOpenNestedCandidates())
            {
                return _archives.Any(m => m.Archive.FindById(id) != null);
            }
            return false;
        }

        /// <summary>
        /// 打开顶层档里「名字像归档」的内层档（如 ra2md.mix 里的 audiomd.mix）。
        /// 只读目录，不读条目字节；一次性做完，之后由 _nestedCache 命中。
        /// </summary>
        private bool TryOpenNestedCandidates()
        {
            if (_nestedSweepDone) return false;
            _nestedSweepDone = true;

            bool opened = false;
            var tops = _archives.Where(a => a.Parent == null).ToList();

            foreach (var top in tops)
            {
                foreach (var entry in top.Archive.Entries)
                {
                    if (!LooksLikeArchiveEntry(top, entry)) continue;

                    string name = entry.Name ?? MixNameTable.LookupName(entry.Id);
                    string childName = name ?? ("0x" + entry.Id.ToString("X8"));
                    string key = (top.VirtualPath + "/" + childName).ToLowerInvariant();
                    if (_nestedCache.ContainsKey(key)) continue;

                    var child = OpenChild(top, childName);
                    if (child != null) opened = true;
                }
            }

            return opened;
        }

        /// <summary>全库检索名字中包含关键字的条目（依赖名字表回填）。</summary>
        public IEnumerable<(MountedArchive, MixEntry)> Search(string keyword)
        {
            string lower = (keyword ?? string.Empty).ToLowerInvariant();
            foreach (var mounted in _archives)
            {
                foreach (var entry in mounted.Archive.Entries)
                {
                    string name = entry.Name ?? MixNameTable.LookupName(entry.Id);
                    if (name == null)
                    {
                        if (lower.Length == 0) yield return (mounted, entry);
                        else if (("0x" + entry.Id.ToString("X8")).ToLowerInvariant().Contains(lower))
                            yield return (mounted, entry);
                        continue;
                    }
                    if (lower.Length == 0 || name.ToLowerInvariant().Contains(lower))
                    {
                        yield return (mounted, entry);
                    }
                }
            }
        }

        public string NameFor(uint id) => MixNameTable.LookupName(id);

        // ---- 工具 ----

        private static List<string> SplitPath(string path)
        {
            return path.Trim().Trim('"')
                .Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static string PathOf(string name)
        {
            var trimmed = name.Trim();
            int sep = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
            return sep >= 0 ? trimmed.Substring(sep + 1) : trimmed;
        }

        private static IEnumerable<string> CandidateNames(string baseName)
        {
            yield return baseName;
            bool hasExt = baseName.Contains('.');
            if (hasExt) yield break;
            foreach (var ext in ExtensionCandidates) yield return baseName + ext;
        }

        /// <summary>
        /// 判定一个切片是否真是 MIX：不做头部猜格式，而是真解析一遍，
        /// 再要求 ≥95% 的目录项落在这个切片范围内。
        /// </summary>
        private static bool IsPlausibleArchive(MixArchive archive, long length)
        {
            if (archive == null || !archive.IndexReadable || archive.FileCount == 0) return false;
            if (archive.FileCount > 20000) return false;

            int total = 0, good = 0;
            foreach (var entry in archive.Entries)
            {
                total++;
                if (entry.Size > 0 && entry.AbsoluteOffset >= 0 && entry.AbsoluteOffset + entry.Size <= length)
                {
                    good++;
                }
            }

            return total > 0 && good >= total * 0.95;
        }
    }
}
