// MixBrowserWindow.cs —— 工具/Mix浏览器
// 浏览、预览、导出 MIX 归档内容；支持添加 MIX 文件（按钮或拖拽）、添加目录、整档导出。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ra2md.Assets;
using UnityEditor;
using UnityEngine;

namespace Ra2md.EditorTools
{
    public sealed class MixBrowserWindow : EditorWindow
    {
        private const float ArchiveWidth = 260f;
        private const float EntryWidth = 340f;
        private const int MaxPreviewBytes = 64 * 1024 * 1024;

        private VirtualFileSystem _vfs = new VirtualFileSystem();
        private readonly List<MountedArchive> _visibleArchives = new List<MountedArchive>();
        private readonly List<EntryRef> _filtered = new List<EntryRef>();
        private readonly HashSet<uint> _archivesChecked = new HashSet<uint>();

        private int _selectedArchive = -1;
        private EntryRef _selectedEntry;
        private string _search = string.Empty;
        private string _lastSearch = null;
        private Vector2 _archiveScroll;
        private Vector2 _entryScroll;
        private Vector2 _previewScroll;
        private bool _onlyNamed;

        private Texture2D _previewTexture;
        private string _previewInfo = string.Empty;
        private string _previewText = string.Empty;
        private byte[] _previewBytes;
        private string _previewOrigin = string.Empty;

        private int _shpFrame;
        private int _shpFrameCount = 1;

        private byte[] _paletteRgba;
        private string _paletteName = string.Empty;

        private struct EntryRef
        {
            public MountedArchive Archive;
            public MixEntry Entry;
        }

        [MenuItem("工具/Mix浏览器")]
        public static void Open()
        {
            var window = GetWindow<MixBrowserWindow>("MIX 浏览器");
            window.minSize = new Vector2(900, 520);
            window.Show();
        }

        private void OnEnable()
        {
            if (_vfs.ArchiveCount == 0)
            {
                ScanStreamingAssets();
            }
        }

        private void OnDestroy()
        {
            DestroyPreviewTexture();
        }

        private void ScanStreamingAssets()
        {
            string root = Application.streamingAssetsPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            MixNameTable.RegisterDirectory(root);
            _vfs.AddLooseRoot(root);
            foreach (var file in Directory.GetFiles(root)
                         .Where(f => f.EndsWith(".mix", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                _vfs.Mount(file, VirtualFileSystem.PriorityFor(Path.GetFileName(file)));
            }
            RebuildArchiveList();
            RebuildEntries();
        }

        private void RebuildArchiveList()
        {
            _visibleArchives.Clear();
            _visibleArchives.AddRange(_vfs.Archives);
            if (_selectedArchive >= _visibleArchives.Count) _selectedArchive = -1;
        }

        private void RebuildEntries()
        {
            _filtered.Clear();

            IEnumerable<MountedArchive> scope;
            if (_selectedArchive >= 0 && _selectedArchive < _visibleArchives.Count)
                scope = new[] { _visibleArchives[_selectedArchive] };
            else
                scope = _visibleArchives;

            string key = (_search ?? string.Empty).Trim().ToLowerInvariant();
            foreach (var archive in scope)
            {
                foreach (var entry in archive.Archive.Entries)
                {
                    string name = entry.Name ?? MixNameTable.LookupName(entry.Id);
                    if (_onlyNamed && name == null) continue;

                    if (key.Length > 0)
                    {
                        string idText = "0x" + entry.Id.ToString("X8").ToLowerInvariant();
                        bool hit = (name != null && name.ToLowerInvariant().Contains(key))
                                   || idText.Contains(key)
                                   || archive.Name.ToLowerInvariant().Contains(key);
                        if (!hit) continue;
                    }

                    _filtered.Add(new EntryRef { Archive = archive, Entry = entry });
                }
            }

            _lastSearch = _search;
        }

        // ---- GUI ----

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawArchivePane();
            DrawEntryPane();
            DrawPreviewPane();
            EditorGUILayout.EndHorizontal();

            HandleDragAndDrop();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("添加 MIX", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                string start = Application.streamingAssetsPath;
                string path = EditorUtility.OpenFilePanelWithFilters("添加 MIX 归档", start,
                    new[] { "MIX 归档", "mix", "所有文件", "*" });
                if (!string.IsNullOrEmpty(path)) MountPath(path);
            }

            if (GUILayout.Button("添加目录", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                string dir = EditorUtility.OpenFolderPanel("添加 MIX 目录", Application.streamingAssetsPath, "");
                if (!string.IsNullOrEmpty(dir))
                {
                    MixNameTable.RegisterDirectory(dir);
                    foreach (var file in Directory.GetFiles(dir)
                                 .Where(f => f.EndsWith(".mix", StringComparison.OrdinalIgnoreCase)))
                    {
                        MountPath(file);
                    }
                    _vfs.AddLooseRoot(dir);
                    RebuildArchiveList();
                    RebuildEntries();
                }
            }

            if (GUILayout.Button("重扫 StreamingAssets", EditorStyles.toolbarButton, GUILayout.Width(150)))
            {
                _vfs.Clear();
                _selectedArchive = -1;
                _selectedEntry = default;
                _previewInfo = string.Empty;
                _previewText = string.Empty;
                _previewBytes = null;
                DestroyPreviewTexture();
                ScanStreamingAssets();
            }

            GUILayout.Space(8);
            _search = GUILayout.TextField(_search ?? string.Empty, EditorStyles.toolbarTextField, GUILayout.Width(200));
            if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                _search = string.Empty;
                RebuildEntries();
            }

            _onlyNamed = GUILayout.Toggle(_onlyNamed, "仅有名条目", EditorStyles.toolbarButton, GUILayout.Width(90));

            if (!string.IsNullOrEmpty(_search) && _search != _lastSearch) RebuildEntries();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_visibleArchives.Count} 归档 / {_filtered.Count} 条", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void MountPath(string path)
        {
            var mounted = _vfs.Mount(path, VirtualFileSystem.PriorityFor(Path.GetFileName(path)));
            if (mounted == null) return;
            RebuildArchiveList();
            RebuildEntries();
        }

        private void DrawArchivePane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ArchiveWidth));
            EditorGUILayout.LabelField("归档", EditorStyles.boldLabel);

            _archiveScroll = EditorGUILayout.BeginScrollView(_archiveScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _visibleArchives.Count; i++)
            {
                var archive = _visibleArchives[i];
                bool selected = i == _selectedArchive;

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Toggle(selected, archive.Name, EditorStyles.label) && !selected)
                {
                    _selectedArchive = i;
                    _selectedEntry = default;
                    RebuildEntries();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(archive.Archive.IndexReadable
                        ? $"{archive.Archive.Entries.Count}"
                        : "读失败", EditorStyles.miniLabel, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("显示全部归档", EditorStyles.miniButton))
            {
                _selectedArchive = -1;
                RebuildEntries();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEntryPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(EntryWidth));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("条目", EditorStyles.boldLabel, GUILayout.Width(60));
            if (GUILayout.Button("导出整档", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                ExportArchive();
            }
            EditorGUILayout.EndHorizontal();

            _entryScroll = EditorGUILayout.BeginScrollView(_entryScroll, GUILayout.ExpandHeight(true));
            int shown = 0;
            foreach (var item in _filtered)
            {
                if (shown >= 4000) break;
                shown++;

                string name = item.Entry.Name ?? MixNameTable.LookupName(item.Entry.Id);
                string label = (name ?? "0x" + item.Entry.Id.ToString("X8"))
                               + "   " + FormatSize(item.Entry.Size);

                bool isSelected = _selectedEntry.Entry != null
                                  && _selectedEntry.Entry.Id == item.Entry.Id
                                  && _selectedEntry.Archive == item.Archive;

                if (GUILayout.Toggle(isSelected, label, EditorStyles.label) && !isSelected)
                {
                    _selectedEntry = item;
                    _shpFrame = 0;
                    LoadPreview();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            if (_selectedEntry.Entry == null)
            {
                EditorGUILayout.HelpBox("选择左侧条目以预览", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var entry = _selectedEntry.Entry;
            var archive = _selectedEntry.Archive;

            EditorGUILayout.LabelField((entry.Name ?? "0x" + entry.Id.ToString("X8")) +
                                       "   " + FormatSize(entry.Size), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("来源：" + archive.Name + "   偏移 " + entry.AbsoluteOffset, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(_previewOrigin))
                EditorGUILayout.LabelField(_previewOrigin, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (_shpFrameCount > 1)
            {
                EditorGUILayout.LabelField("帧", GUILayout.Width(24));
                int frame = EditorGUILayout.IntSlider(_shpFrame, 0, _shpFrameCount - 1);
                if (frame != _shpFrame)
                {
                    _shpFrame = frame;
                    RenderShpFrame();
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("导出此文件", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                ExportEntry();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_previewInfo))
            {
                EditorGUILayout.HelpBox(_previewInfo, MessageType.None);
            }

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandHeight(true));

            if (_previewTexture != null)
            {
                float maxWidth = position.width - ArchiveWidth - EntryWidth - 60f;
                float scale = Mathf.Min(1f, maxWidth / _previewTexture.width);
                var rect = GUILayoutUtility.GetRect(_previewTexture.width * scale, _previewTexture.height * scale);
                GUI.DrawTexture(rect, _previewTexture, ScaleMode.ScaleToFit, true);
            }

            if (!string.IsNullOrEmpty(_previewText))
            {
                EditorGUILayout.TextArea(_previewText, GUILayout.ExpandHeight(true));
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ---- 预览 ----

        private void DestroyPreviewTexture()
        {
            if (_previewTexture == null) return;
            DestroyImmediate(_previewTexture);
            _previewTexture = null;
        }

        private void LoadPreview()
        {
            DestroyPreviewTexture();
            _previewText = string.Empty;
            _previewInfo = string.Empty;
            _previewBytes = null;
            _previewOrigin = string.Empty;
            _shpFrameCount = 1;
            _shpFrame = 0;

            if (_selectedEntry.Entry == null) return;

            var entry = _selectedEntry.Entry;
            var archive = _selectedEntry.Archive;

            if (entry.Size > MaxPreviewBytes)
            {
                _previewInfo = $"文件 {FormatSize(entry.Size)} 超过预览上限，请直接导出。";
                return;
            }

            if (!archive.Archive.TryReadEntry(entry, out var data) || data == null)
            {
                _previewInfo = "读取失败：" + archive.Archive.Error;
                return;
            }

            _previewBytes = data;
            _previewOrigin = DescribeFormat(data, entry.Name);

            var ext = ExtensionOf(entry.Name);

            if (ext == ".shp" || LooksLikeShp(data))
            {
                ShowShp(data);
                return;
            }

            if (ext == ".pal" || LooksLikePal(data))
            {
                ShowPal(data);
                return;
            }

            if (IsTextual(data))
            {
                _previewText = DecodeText(data);
                _previewInfo = "文本预览：" + FormatSize(data.Length);
                return;
            }

            _previewText = HexDump(data, 400);
            _previewInfo = "二进制预览（前 400 字节）";
        }

        private void ShowShp(byte[] data)
        {
            if (!ShpReader.TryParse(data, out var image, out string error))
            {
                _previewInfo = "SHP 解析失败：" + error;
                _previewText = HexDump(data, 200);
                return;
            }

            _shpFrameCount = Math.Max(1, image.FrameCount);
            _previewInfo = $"SHP 画布 {image.CanvasWidth}×{image.CanvasHeight}，{image.FrameCount} 帧";
            RenderShpFrame();
        }

        private void RenderShpFrame()
        {
            DestroyPreviewTexture();
            if (_previewBytes == null) return;

            if (!ShpReader.TryParse(_previewBytes, out var image, out string error))
            {
                _previewInfo = "SHP 解析失败：" + error;
                return;
            }

            if (!image.TryDecodeFrame(_shpFrame, out var pixels, out int width, out int height, out error))
            {
                _previewInfo = "帧解码失败：" + error;
                return;
            }

            if (width <= 0 || height <= 0)
            {
                _previewInfo = "空帧（占位帧）";
                return;
            }

            var frameInfo = image.Frames[_shpFrame];
            EnsurePalette();

            var canvas = new byte[width * height * 4];
            var palette = _paletteRgba ?? BuildGrayscalePalette();

            PalReader.BlitIndexed(pixels, width, height, palette, canvas, width, height, 0, 0);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "MIX预览",
            };
            texture.SetPixels32(ToColor32(canvas));
            texture.Apply(false, false);

            _previewTexture = texture;
            _previewInfo = $"SHP 第 {_shpFrame + 1}/{image.FrameCount} 帧 {width}×{height}"
                           + $"（偏移 {frameInfo.OffsetX},{frameInfo.OffsetY}） 调色板：{_paletteName}";
        }

        private void ShowPal(byte[] data)
        {
            if (!PalReader.TryDecode(data, out var rgba, out string error))
            {
                _previewInfo = "PAL 解析失败：" + error;
                return;
            }

            const int cell = 16;
            int textureWidth = cell * 16;
            int textureHeight = cell * 16;
            var canvas = new byte[textureWidth * textureHeight * 4];

            for (int index = 0; index < 256; index++)
            {
                int cx = (index % 16) * cell;
                int cy = (index / 16) * cell;
                for (int y = 0; y < cell; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        int dst = ((cy + y) * textureWidth + cx + x) * 4;
                        int src = index * 4;
                        canvas[dst] = rgba[src];
                        canvas[dst + 1] = rgba[src + 1];
                        canvas[dst + 2] = rgba[src + 2];
                        canvas[dst + 3] = 255;
                    }
                }
            }

            var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                name = "PAL预览",
            };
            texture.SetPixels32(ToColor32(canvas));
            texture.Apply(false, false);
            _previewTexture = texture;
            _previewInfo = "PAL 调色板 256 色";
        }

        private void EnsurePalette()
        {
            if (_paletteRgba != null) return;

            string[] candidates =
            {
                "unittem.pal", "temperat.pal", "unitsno.pal", "snow.pal",
                "uniturb.pal", "urban.pal", "isopalg.pal", "anim.pal",
            };

            foreach (var name in candidates)
            {
                if (!_vfs.TryResolve(name, out var data, out _)) continue;
                if (!PalReader.TryDecode(data, out _paletteRgba, out _)) continue;
                _paletteName = name;
                return;
            }

            _paletteName = "灰度兜底";
        }

        private static byte[] BuildGrayscalePalette()
        {
            var palette = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                byte v = (byte)i;
                palette[i * 4] = v;
                palette[i * 4 + 1] = v;
                palette[i * 4 + 2] = v;
                palette[i * 4 + 3] = i == 0 ? (byte)0 : (byte)255;
            }
            return palette;
        }

        private static Color32[] ToColor32(byte[] rgba)
        {
            var colors = new Color32[rgba.Length / 4];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            }
            return colors;
        }

        // ---- 导出 ----

        private void ExportEntry()
        {
            if (_previewBytes == null || _selectedEntry.Entry == null) return;

            string name = _selectedEntry.Entry.Name ?? ("0x" + _selectedEntry.Entry.Id.ToString("X8"));
            string path = EditorUtility.SaveFilePanel("导出条目", Application.streamingAssetsPath,
                name, ExtensionTrimmed(name));
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllBytes(path, _previewBytes);
            EditorUtility.RevealInFinder(path);
        }

        private void ExportArchive()
        {
            var target = _selectedArchive >= 0 && _selectedArchive < _visibleArchives.Count
                ? _visibleArchives[_selectedArchive]
                : null;
            if (target == null)
            {
                EditorUtility.DisplayDialog("导出整档", "请先在左侧选择一个归档。", "好");
                return;
            }

            string dir = EditorUtility.OpenFolderPanel("导出归档到目录", Application.streamingAssetsPath, target.Name);
            if (string.IsNullOrEmpty(dir)) return;

            int ok = 0;
            int failed = 0;
            foreach (var entry in target.Archive.Entries)
            {
                string name = entry.Name ?? ("0x" + entry.Id.ToString("X8"));
                name = SanitizeFileName(name);
                if (!target.Archive.TryReadEntry(entry, out var data)) { failed++; continue; }
                File.WriteAllBytes(Path.Combine(dir, name), data);
                ok++;
            }

            EditorUtility.DisplayDialog("导出整档",
                $"{target.Name}\n成功 {ok} 个，失败 {failed} 个\n目录：{dir}", "好");
            EditorUtility.RevealInFinder(dir);
        }

        // ---- 工具 ----

        private static string ExtensionOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return Path.GetExtension(name).ToLowerInvariant();
        }

        private static string ExtensionTrimmed(string name)
        {
            var ext = ExtensionOf(name);
            return ext.Length > 1 ? ext.Substring(1) : "bin";
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.#") + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.##") + " MB";
        }

        private static bool LooksLikeShp(byte[] data)
        {
            if (data == null || data.Length < 32) return false;
            int zero = data[0] | (data[1] << 8);
            int w = data[2] | (data[3] << 8);
            int h = data[4] | (data[5] << 8);
            int frames = data[6] | (data[7] << 8);
            if (zero != 0) return false;
            if (w <= 0 || w > 4096 || h <= 0 || h > 4096) return false;
            if (frames <= 0 || frames > 1000) return false;
            return data.Length >= 8 + frames * 24;
        }

        private static bool LooksLikePal(byte[] data)
        {
            if (data == null) return false;
            return data.Length == 768 || data.Length == 772;
        }

        private static bool IsTextual(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            int limit = Math.Min(data.Length, 2048);
            int printable = 0;
            for (int i = 0; i < limit; i++)
            {
                byte b = data[i];
                if (b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127)) printable++;
            }
            return printable >= limit * 0.92f;
        }

        private static string DecodeText(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 32 * 1024));
            if (text.Length > 6000) text = text.Substring(0, 6000) + "\n... (已截断)";
            return text;
        }

        private static string HexDump(byte[] data, int maxBytes)
        {
            int count = Math.Min(data.Length, maxBytes);
            var builder = new StringBuilder(count * 4);
            for (int i = 0; i < count; i += 16)
            {
                builder.Append(i.ToString("X6")).Append(": ");
                for (int j = 0; j < 16 && i + j < count; j++)
                {
                    builder.Append(data[i + j].ToString("X2")).Append(' ');
                }
                builder.Append('\n');
            }
            if (count < data.Length) builder.Append("... (共 ").Append(data.Length).Append(" 字节)");
            return builder.ToString();
        }

        private static string DescribeFormat(byte[] data, string name)
        {
            if (data == null || data.Length < 8) return string.Empty;

            if (data.Length >= 4 && data[0] == 0x20 && data[1] == 0x46 && data[2] == 0x53 && data[3] == 0x43)
                return "格式：CSF 字符串表";
            if (LooksLikeShp(data)) return "格式：SHP 索引图像";
            if (LooksLikePal(data)) return "格式：PAL 调色板";
            if (data.Length >= 4 && data[0] == 'B' && data[1] == 'I' && data[2] == 'K')
                return "格式：Bink 视频";
            if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return "格式：BMP 位图";
            if (LooksLikeMixContainer(data)) return "格式：嵌套 MIX 归档";
            return "格式：原始字节";
        }

        private static bool LooksLikeMixContainer(byte[] data)
        {
            if (data == null || data.Length < 12) return false;
            uint flags = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
            const uint known = 0x00010000u | 0x00020000u;
            if ((flags & ~known) != 0) return false;
            int count = data[4] | (data[5] << 8);
            if (count <= 0) return false;
            return data.Length > 6 + count * 12;
        }

        // ---- 拖拽 ----

        private void HandleDragAndDrop()
        {
            var current = Event.current;
            if (current == null) return;

            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform) return;

            var paths = DragAndDrop.paths;
            bool hasMix = paths != null && paths.Any(p =>
                p.EndsWith(".mix", StringComparison.OrdinalIgnoreCase) || Directory.Exists(p));
            if (!hasMix) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (current.type != EventType.DragPerform) return;

            DragAndDrop.AcceptDrag();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    MixNameTable.RegisterDirectory(path);
                    _vfs.AddLooseRoot(path);
                    foreach (var file in Directory.GetFiles(path)
                                 .Where(f => f.EndsWith(".mix", StringComparison.OrdinalIgnoreCase)))
                    {
                        MountPath(file);
                    }
                }
                else if (path.EndsWith(".mix", StringComparison.OrdinalIgnoreCase))
                {
                    MountPath(path);
                }
            }
            RebuildArchiveList();
            RebuildEntries();
            current.Use();
        }
    }
}
