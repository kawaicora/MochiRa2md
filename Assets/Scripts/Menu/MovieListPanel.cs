// MovieListPanel.cs —— 「選擇影片」列表（对话框 0x129）：左侧影片清单 + 滚动条，右侧按钮列由 MenuShell 管。
//
// 几何与条目口径对照原版：
//   · 列表框 0x744：DLU(80,79,266,187) → 800×600 下 (120,128,399,304)；
//   · 提示行 0x40C：DLU(80,45,266,24) → (120,73,399,39)，居中顶对齐；
//   · 行高 19，滚动条宽 20，箭头高 22；一屏 15 行；
//   · 条目顺序：开场动画常驻，其后按战役进度放苏军 0..=soviet、盟军 0..=allied；
//     新档进度 -1，列表只有开场动画；选中态初始为无（-1）。
// 条目标题走 CSF 的 "Name:<Id>"；播放源按片名字段 + .bik，VFS 命中才返回。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ra2md.Assets;
using Ra2md.Managers;

namespace Ra2md.Menu
{
    /// <summary>一条影片。</summary>
    public struct MovieItem
    {
        public string Id;      // CSF Name: 后缀，例如 Sov01MD / IntroMovie
        public string Title;   // CSF 文案
        public string Clip;    // 播放源文件名；null = 未配置
    }

    /// <summary>战役影片解锁进度（对应 OptionsClass 苏军/盟军字段；-1 = 未解锁）。</summary>
    public struct MovieProgress
    {
        public int Soviet;
        public int Allied;

        public static MovieProgress Default => new MovieProgress { Soviet = -1, Allied = -1 };
    }

    public sealed class MovieListPanel : MonoBehaviour
    {
        // ---- 800×600 原版几何 ----
        private const int BoxLeft = 120;
        private const int BoxTop = 128;
        private const int BoxWidth = 399;
        private const int BoxHeight = 304;

        private const int PromptLeft = 120;
        private const int PromptTop = 73;
        private const int PromptWidth = 399;
        private const int PromptHeight = 39;

        private const int RowLeft = 121;
        private const int RowWidth = BoxWidth - 2 - 20;
        private const int RowHeight = 19;
        private const int RowFirstTop = BoxTop + 1;
        private const int VisibleRows = 15;

        private const int ScrollLeft = BoxLeft + BoxWidth - 20 - 1;
        private const int ScrollWidth = 20;

        // 列表框线条色调（原版 owner-draw 边框：亮 / 暗 / 角部平均）。
        private static readonly Color32 FrameLight = new Color32(167, 190, 197, 255);
        private static readonly Color32 FrameDark = new Color32(104, 122, 128, 255);

        private static readonly Color32 RowColor = new Color32(250, 214, 60, 255);
        private static readonly Color32 RowSelected = new Color32(255, 255, 255, 255);
        private static readonly Color32 RowHighlight = new Color32(96, 16, 12, 255);

        private Transform _root;
        private readonly List<MovieItem> _items = new List<MovieItem>();
        private readonly List<Text> _rowTexts = new List<Text>();
        private readonly List<GameObject> _rowHighlight = new List<GameObject>();
        private MenuListScroll _scrollbar;
        private int _scroll;
        private int _selected = -1;

        public int SelectedIndex { get { return _selected; } }
        public MovieItem? SelectedItem
        {
            get { return _selected >= 0 && _selected < _items.Count ? _items[_selected] : (MovieItem?)null; }
        }

        public static MovieListPanel Create(Transform plane, Canvas labelCanvas)
        {
            var go = new GameObject("MovieListPanel");
            go.transform.SetParent(plane, false);
            var panel = go.AddComponent<MovieListPanel>();
            panel._root = go.transform;
            panel.Build(labelCanvas);
            return panel;
        }

        // ------------------------------------------------------------ 数据

        /// <summary>读取菜单影片清单（目录表与进度取自全局配置管理器）。</summary>
        public static List<MovieItem> LoadItems()
        {
            var cfg = GlobalConfigManager.Instance;
            return LoadItems(new MovieProgress
            {
                Soviet = cfg.SovietMovieProgress,
                Allied = cfg.AlliedMovieProgress,
            });
        }

        /// <summary>按解锁进度组装条目。</summary>
        public static List<MovieItem> LoadItems(MovieProgress progress)
        {
            var cfg = GlobalConfigManager.Instance;
            var items = new List<MovieItem>();
            items.Add(MakeItem(GlobalConfigManager.IntroMovie));

            int sovTake = Math.Max(0, progress.Soviet + 1);
            int allTake = Math.Max(0, progress.Allied + 1);
            for (var i = 0; i < GlobalConfigManager.SovietMovies.Length && i < sovTake; i++)
                items.Add(MakeItem(GlobalConfigManager.SovietMovies[i]));
            for (var i = 0; i < GlobalConfigManager.AlliedMovies.Length && i < allTake; i++)
                items.Add(MakeItem(GlobalConfigManager.AlliedMovies[i]));
            return items;
        }

        private static MovieItem MakeItem(GlobalConfigManager.MovieCatalogEntry entry)
        {
            return new MovieItem
            {
                Id = entry.Id,
                Title = MenuStrings.Get("Name:" + entry.Id, entry.Id),
                Clip = ResolveClip(entry.Stem, entry.Id),
            };
        }

        /// <summary>按片名字段解析影片归档；VFS 不命中返回 null，不瞎猜。</summary>
        public static string AutoResolveClip(string id, int listIndex)
        {
            var cfg = GlobalConfigManager.Instance;
            if (string.Equals(id, GlobalConfigManager.IntroMovie.Id, StringComparison.OrdinalIgnoreCase))
                return ResolveClip(GlobalConfigManager.IntroMovie.Stem, id);

            var stem = LookupStem(id);
            return ResolveClip(stem, id);
        }

        private static string LookupStem(string id)
        {
            var cfg = GlobalConfigManager.Instance;
            for (var i = 0; i < GlobalConfigManager.SovietMovies.Length; i++)
                if (string.Equals(GlobalConfigManager.SovietMovies[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return GlobalConfigManager.SovietMovies[i].Stem;
            for (var i = 0; i < GlobalConfigManager.AlliedMovies.Length; i++)
                if (string.Equals(GlobalConfigManager.AlliedMovies[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return GlobalConfigManager.AlliedMovies[i].Stem;
            return null;
        }

        private static string ResolveClip(string stem, string id)
        {
            var vfs = VirtualFileSystem.Instance;
            var ext = GlobalConfigManager.MovieExtension;
            if (!string.IsNullOrEmpty(stem))
            {
                var candidate = stem + ext;
                if (vfs.Exists(candidate)) return candidate;
            }
            if (!string.IsNullOrEmpty(id))
            {
                if (vfs.Exists(id + ext)) return id + ext;
                if (vfs.Exists(id.ToLowerInvariant() + ext)) return id.ToLowerInvariant() + ext;
            }
            Debug.LogWarning("[影片] " + id + " 没有解析到影片文件（片名字段 " + stem + "）");
            return null;
        }

        // ------------------------------------------------------------ 构建

        private void Build(Canvas labelCanvas)
        {
            _items.Clear();
            _items.AddRange(LoadItems());

            // 提示行（0x40C）：居中顶对齐。
            MenuSceneObjects.AddCenteredTextAt(labelCanvas.transform,
                MenuStrings.Get("GUI:SelectMovie", "Select Movie"),
                PromptLeft, PromptTop, PromptWidth, PromptHeight, 18, RowColor);

            // 列表框外框 + 内底。
            MenuWidgets.Panel(_root, "ListBox", BoxLeft, BoxTop, BoxWidth, BoxHeight,
                FrameLight, new Color32(0, 0, 0, 255), 70);

            BuildScrollbar();

            // 新档进入列表：无选中项。
            _selected = -1;

            for (var i = 0; i < VisibleRows; i++)
            {
                int y = RowFirstTop + i * RowHeight;
                var hl = MenuSceneObjects.Quad(_root, "RowSel" + i,
                    MenuWidgets.Solid(RowHighlight),
                    RowLeft, y, RowWidth, RowHeight - 1, 74, false);
                MenuSceneObjects.SetVisible(hl, false);
                _rowHighlight.Add(hl);

                var text = MenuSceneObjects.AddCenteredTextAt(labelCanvas.transform, string.Empty,
                    RowLeft, y, RowWidth, RowHeight, 14, RowColor);
                text.alignment = TextAnchor.MiddleLeft;
                _rowTexts.Add(text);

                var hit = MenuSceneObjects.Quad(_root, "RowHit" + i,
                    MenuSceneObjects.TransparentTexture,
                    RowLeft, y, RowWidth, RowHeight, 76, true);
                var row = hit.AddComponent<MovieListRow>();
                row.Configure(this, i);
            }

            RefreshRows();
        }

        private void BuildScrollbar()
        {
            _scrollbar = MenuListScroll.Create(_root, "Scrollbar",
                ScrollLeft, BoxTop + 2, ScrollWidth, BoxHeight - 4, 72);
            _scrollbar.SetHitRect(BoxLeft, BoxTop, BoxWidth, BoxHeight);
            _scrollbar.TopChanged = top =>
            {
                _scroll = top;
                RefreshRows();
            };
        }

        private void RefreshRows()
        {
            if (_scrollbar != null) _scrollbar.SetCount(_items.Count, VisibleRows);

            for (var i = 0; i < _rowTexts.Count; i++)
            {
                var index = _scroll + i;
                var has = index >= 0 && index < _items.Count;
                _rowTexts[i].text = has ? _items[index].Title : string.Empty;
                _rowTexts[i].color = has && index == _selected ? RowSelected : RowColor;
                _rowTexts[i].gameObject.SetActive(has);
                MenuSceneObjects.SetVisible(_rowHighlight[i], has && index == _selected);
            }
        }

        // ------------------------------------------------------------ 交互

        public void Select(int rowIndex)
        {
            var index = _scroll + rowIndex;
            if (index < 0 || index >= _items.Count) return;
            _selected = index;
            RefreshRows();
        }

        public void ScrollBy(int delta)
        {
            if (_scrollbar != null) _scrollbar.ScrollBy(delta);
        }
    }

    /// <summary>列表行拾取。</summary>
    public sealed class MovieListRow : MonoBehaviour, IPointerClickHandler
    {
        private MovieListPanel _panel;
        private int _row;

        public void Configure(MovieListPanel panel, int row)
        {
            _panel = panel;
            _row = row;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_panel != null) _panel.Select(_row);
        }
    }
}
