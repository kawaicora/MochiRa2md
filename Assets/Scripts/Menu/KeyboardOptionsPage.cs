// KeyboardOptionsPage.cs —— 键盘选项页，按原版「自訂鍵盤功能鍵」对话框逐块拼：
//
//   左列：類別（下拉）+ 描述框      右列：命令列表框（可点选、滚轮滚动）
//   左下：目前快捷鍵 / 輸入新的快捷鍵 / 目前此快捷鍵指定為
//   右下：指定、全部重設 —— 两个按钮本体是 mnbttn.shp 三帧（MenuShpButton），不再程序自绘
//
// 命令对象全部来自 CommandClass.GameCreate<T>()（CommandRegistry.InstallAll），
// 文案一律按 CSF key 取，改键结果由 InputManager 落盘。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ra2md.Input;

namespace Ra2md.Menu
{
    public sealed class KeyboardOptionsPage : MonoBehaviour
    {
        // ---- 几何：按 802×632 原版截图扣掉外壳偏移量出的 632 宽内容区坐标 ----
        private const int TitleTop = 62;
        private const int TitleHeight = 24;

        private const int CategoryLabelLeft = 84;
        private const int CategoryLabelTop = 100;
        private const int ComboLeft = 84;
        private const int ComboTop = 130;
        private const int ComboWidth = 216;
        private const int ComboHeight = 26;

        private const int DescLabelTop = 166;
        private const int DescBoxLeft = 84;
        private const int DescBoxTop = 182;
        private const int DescBoxWidth = 214;
        private const int DescBoxHeight = 124;

        private const int ListLabelLeft = 336;
        private const int ListBoxLeft = 336;
        private const int ListBoxTop = 128;
        private const int ListBoxWidth = 198;
        private const int ListBoxHeight = 174;
        private const int RowLeft = 342;
        private const int RowTop = 131;
        private const int RowHeight = 19;
        private const int RowCount = 9;
        private const int RowNameWidth = 150;

        private const int CurKeyTop = 320;
        private const int PendingLabelTop = 372;
        private const int PendingBoxLeft = 84;
        private const int PendingBoxTop = 392;
        private const int PendingBoxWidth = 193;
        private const int PendingBoxHeight = 24;
        private const int AssignLabelTop = 420;

        private const int AssignButtonLeft = 331;
        private const int AssignButtonTop = 390;
        private const int ResetButtonLeft = 331;
        private const int ResetButtonTop = 444;

        private static readonly Color32 TextYellow = new Color32(255, 222, 25, 255);
        private static readonly Color32 TextDim = new Color32(150, 150, 150, 255);
        private static readonly Color32 SelectedFill = new Color32(96, 8, 6, 255);
        private static readonly Color32 SelectedEdge = new Color32(220, 60, 40, 255);
        private static readonly Color32 BoxFill = new Color32(0, 0, 0, 255);

        private readonly List<Text> _rowNames = new List<Text>();
        private readonly List<Text> _rowKeys = new List<Text>();
        private readonly List<GameObject> _rowHits = new List<GameObject>();
        private readonly List<GameObject> _rowBars = new List<GameObject>();

        private RectTransform _canvas;
        private GameObject _cartRoot;

        private Text _descText;
        private Text _currentKeyValue;
        private Text _pendingValue;
        private Text _assignedValue;
        private Text _categoryValue;

        private MenuShpButton _assignButton;
        private MenuShpButton _resetButton;
        private MenuListScroll _scrollbar;

        private GameObject _dropdown;
        private Text _dropdownText;

        private int _selected = -1;
        private int _scroll;
        private bool _dropdownOpen;
        private Hotkey _pending;

        private IList<CommandClass> Commands { get { return InputManager.Instance.Commands; } }

        // ---- 显示名：原版对话框走 Cmnd: 族（與 KeyboardMD.ini 段名不完全同名，这里做映射） ----
        private static readonly Dictionary<string, string> NameKeyAlias = new Dictionary<string, string>
        {
            { "CombatantSelect", "Cmnd:CombatantSelect" },
            { "TypeSelect", "Cmnd:TypeSelect" },
            { "VeterancyNav", "Cmnd:VetNavigation" },
            { "PlanningMode", "Cmnd:PlanningMode" },
            { "Delete", "Cmnd:Delete" },
            { "PlaceBeacon", "Cmnd:Beacon" },
        };

        /// <summary>原版「選項」类第一屏命令次序（截图上那 9 条，缺的条目自动跳过）。</summary>
        private static readonly string[] FirstScreenIds =
        {
            "CombatantSelect", "TypeSelect", "NextObject", "CenterBase",
            "HealthNavigation", "VeterancyNav", "PreviousObject", "NextIdleHarvester", "CenterView",
        };

        private List<CommandClass> _view;
        private int _viewStamp = -1;

        /// <summary>命令显示次序：原版首屏 9 条优先，其余按注册顺序补在后面。</summary>
        private List<CommandClass> View
        {
            get
            {
                var commands = Commands;
                if (_view != null && _viewStamp == commands.Count) return _view;

                var ordered = new List<CommandClass>(commands.Count);
                foreach (var id in FirstScreenIds)
                {
                    var command = InputManager.Instance.Find(id);
                    if (command != null) ordered.Add(command);
                }
                foreach (var command in commands)
                    if (!ordered.Contains(command)) ordered.Add(command);

                _view = ordered;
                _viewStamp = commands.Count;
                return _view;
            }
        }

        private static string NameKeyOf(CommandClass command)
        {
            if (command == null) return null;
            if (!string.IsNullOrEmpty(command.Id) && NameKeyAlias.TryGetValue(command.Id, out var key)) return key;
            return command.NameKey;
        }

        private static string NameOf(CommandClass command)
        {
            if (command == null) return string.Empty;
            var key = NameKeyOf(command);
            if (!string.IsNullOrEmpty(key))
            {
                var value = MenuStrings.Get(key, string.Empty);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return command.FallbackName;
        }

        private static string DescOf(CommandClass command)
        {
            if (command == null) return string.Empty;

            var key = NameKeyOf(command);
            if (string.IsNullOrEmpty(key)) return string.Empty;

            var candidates = new[] { key + "Desc", key + "_DESC", key + "_Desc" };
            foreach (var candidate in candidates)
            {
                var value = MenuStrings.Get(candidate, string.Empty);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return MenuStrings.Get("DESC:NoDescription", string.Empty);
        }

        public static KeyboardOptionsPage Create(Transform parent)
        {
            var go = new GameObject("KeyboardOptionsPage");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var page = go.AddComponent<KeyboardOptionsPage>();
            page.Build();
            return page;
        }

        private void OnDestroy()
        {
            var manager = InputManager.Instance;
            if (manager != null)
            {
                manager.BindingChanged -= OnBindingChanged;
                manager.CancelCapture();
            }
        }

        // ------------------------------------------------------------ 拼装

        private void Build()
        {
            _canvas = MenuSceneObjects.AttachLabelCanvas(gameObject,
                MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight, 70);

            var cart = new GameObject("KeyboardCart");
            cart.transform.SetParent(transform, false);
            cart.transform.localPosition = Vector3.zero;
            _cartRoot = cart;
            var t = cart.transform;

            // 标题（内容区正中）
            AddText("Title", MenuStrings.Get("GUI:CustomizeKeyboard", "Customize Keyboard"),
                0, TitleTop, 632, TitleHeight, 15, TextYellow, TextAnchor.MiddleCenter);

            // ---- 左列：類別 ----
            AddText("CategoryLabel", MenuStrings.Get("GUI:Category", "Category"),
                CategoryLabelLeft, CategoryLabelTop, 90, 22, 14, TextYellow, TextAnchor.MiddleLeft);

            MenuWidgets.Combo(t, "CategoryCombo", ComboLeft, ComboTop, ComboWidth, ComboHeight, 60);
            _categoryValue = AddText("CategoryValue", CategoryLabel(),
                ComboLeft + 6, ComboTop + 2, ComboWidth - 30, ComboHeight - 4, 14, TextYellow,
                TextAnchor.MiddleLeft);

            var comboHit = MenuSceneObjects.Quad(t, "CategoryHit", MenuSceneObjects.TransparentTexture,
                ComboLeft, ComboTop, ComboWidth, ComboHeight, 68, true);
            var comboDrag = comboHit.AddComponent<MenuDragArea>();
            comboDrag.Configure(ComboLeft, ComboTop, ComboWidth, ComboHeight, false);
            comboDrag.OnClicked = ToggleDropdown;

            BuildDropdown(t);

            // ---- 左列：描述 ----
            AddText("DescLabel", MenuStrings.Get("GUI:Description", "Description"),
                CategoryLabelLeft, DescLabelTop, 90, 22, 14, TextYellow, TextAnchor.MiddleLeft);

            MenuWidgets.Panel(t, "DescBox", DescBoxLeft, DescBoxTop, DescBoxWidth, DescBoxHeight,
                MenuWidgets.FrameLight, BoxFill, 60);

            _descText = AddText("DescText", string.Empty, DescBoxLeft + 6, DescBoxTop + 4,
                DescBoxWidth - 12, DescBoxHeight - 8, 13, TextYellow, TextAnchor.UpperLeft);
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _descText.verticalOverflow = VerticalWrapMode.Overflow;

            // ---- 右列：命令列表 ----
            AddText("ListLabel", MenuStrings.Get("GUI:Commands", "Commands"),
                ListLabelLeft, CategoryLabelTop, 90, 22, 14, TextYellow, TextAnchor.MiddleLeft);

            MenuWidgets.Panel(t, "ListBox", ListBoxLeft, ListBoxTop, ListBoxWidth, ListBoxHeight,
                MenuWidgets.FrameLight, BoxFill, 60);

            // 右侧滚动条：上下箭头 + 滑块（对齐 ListClass 的 UpButton/DownButton/Scroller）
            _scrollbar = MenuListScroll.Create(t, "ListScroll",
                ListBoxLeft + ListBoxWidth - 16, ListBoxTop + 2, 15, ListBoxHeight - 4, 62);
            _scrollbar.SetHitRect(ListBoxLeft, ListBoxTop, ListBoxWidth, ListBoxHeight);
            _scrollbar.TopChanged = top =>
            {
                _scroll = top;
                Refresh();
            };

            for (var i = 0; i < RowCount; i++)
            {
                var rowY = RowTop + i * RowHeight;
                var rowWidth = ListBoxWidth - 20;

                var bar = MenuSceneObjects.Quad(t, "RowBar" + i, MenuWidgets.Solid(SelectedFill),
                    ListBoxLeft + 2, rowY - 1, rowWidth, RowHeight, 63, false);
                MenuSceneObjects.SetVisible(bar, false);
                _rowBars.Add(bar);

                var hit = MenuSceneObjects.Quad(t, "RowHit" + i, MenuSceneObjects.TransparentTexture,
                    ListBoxLeft + 2, rowY, rowWidth, RowHeight, 67, true);
                var drag = hit.AddComponent<MenuDragArea>();
                var rowIndex = i;
                drag.Configure(ListBoxLeft + 2, rowY, rowWidth, RowHeight, false);
                drag.OnClicked = () => SelectRow(rowIndex);
                _rowHits.Add(hit);

                var name = AddText("RowName" + i, string.Empty, RowLeft, rowY, RowNameWidth, RowHeight,
                    13, TextYellow, TextAnchor.MiddleLeft);
                _rowNames.Add(name);

                var key = AddText("RowKey" + i, string.Empty,
                    ListBoxLeft + ListBoxWidth - 64, rowY, 56, RowHeight, 13, TextYellow, TextAnchor.MiddleRight);
                _rowKeys.Add(key);
            }

            // ---- 左下：目前快捷鍵 ----
            AddText("CurKeyLabel", MenuStrings.Get("GUI:CurrentShortcut", "Current Shortcut"),
                CategoryLabelLeft, CurKeyTop, 130, 22, 14, TextYellow, TextAnchor.MiddleLeft);
            _currentKeyValue = AddText("CurKeyValue", string.Empty,
                CategoryLabelLeft + 108, CurKeyTop, 220, 22, 14, TextYellow, TextAnchor.MiddleLeft);

            // ---- 左下：輸入新的快捷鍵 ----
            AddText("PendingLabel", MenuStrings.Get("GUI:PressShortcut", "Press Shortcut"),
                CategoryLabelLeft, PendingLabelTop, 200, 22, 14, TextYellow, TextAnchor.MiddleLeft);

            MenuWidgets.Panel(t, "PendingBox", PendingBoxLeft, PendingBoxTop, PendingBoxWidth, PendingBoxHeight,
                MenuWidgets.FrameLight, BoxFill, 60);
            _pendingValue = AddText("PendingValue", string.Empty, PendingBoxLeft + 4, PendingBoxTop + 2,
                PendingBoxWidth - 8, PendingBoxHeight - 4, 13, TextYellow, TextAnchor.MiddleLeft);

            var pendingHit = MenuSceneObjects.Quad(t, "PendingHit", MenuSceneObjects.TransparentTexture,
                PendingBoxLeft, PendingBoxTop, PendingBoxWidth, PendingBoxHeight, 68, true);
            var pendingDrag = pendingHit.AddComponent<MenuDragArea>();
            pendingDrag.Configure(PendingBoxLeft, PendingBoxTop, PendingBoxWidth, PendingBoxHeight, false);
            pendingDrag.OnClicked = BeginCapture;

            // ---- 左下：目前此快捷鍵指定為 ----
            AddText("AssignedLabel", MenuStrings.Get("GUI:CurAssignedTo", "Currently Assigned To"),
                CategoryLabelLeft, AssignLabelTop, 200, 22, 14, TextYellow, TextAnchor.MiddleLeft);
            _assignedValue = AddText("AssignedValue", string.Empty,
                CategoryLabelLeft + 168, AssignLabelTop, 220, 22, 13, TextDim, TextAnchor.MiddleLeft);

            // ---- 按钮：mnbttn.shp 三帧 ----
            _assignButton = MenuShpButton.Create(t, "AssignButton", "GUI:Assign", "Assign",
                AssignButtonLeft, AssignButtonTop, Assign, 64);

            _resetButton = MenuShpButton.Create(t, "ResetButton", "GUI:ResetAll", "Reset All",
                ResetButtonLeft, ResetButtonTop, ResetAll, 64);

            InputManager.Instance.BindingChanged += OnBindingChanged;

            if (Commands.Count > 0) _selected = 0;
            Refresh();
        }

        private void BuildDropdown(Transform t)
        {
            _dropdown = new GameObject("CategoryDropdown");
            _dropdown.transform.SetParent(t, false);
            _dropdown.transform.localPosition = Vector3.zero;

            MenuSceneObjects.Quad(_dropdown.transform, "Back", MenuWidgets.Solid(MenuWidgets.FrameLight),
                ComboLeft, ComboTop + ComboHeight, ComboWidth, 22, 70, true);

            var fill = MenuSceneObjects.Quad(_dropdown.transform, "Fill", MenuWidgets.Solid(BoxFill),
                ComboLeft + 1, ComboTop + ComboHeight + 1, ComboWidth - 2, 20, 71, false);
            fill.name = "Fill";

            _dropdownText = AddText("DropdownText", CategoryLabel(),
                ComboLeft + 6, ComboTop + ComboHeight + 1, ComboWidth - 12, 20, 14, TextYellow,
                TextAnchor.MiddleLeft);

            var hit = MenuSceneObjects.Quad(_dropdown.transform, "Hit", MenuSceneObjects.TransparentTexture,
                ComboLeft, ComboTop + ComboHeight, ComboWidth, 22, 73, true);
            var drag = hit.AddComponent<MenuDragArea>();
            drag.Configure(ComboLeft, ComboTop + ComboHeight, ComboWidth, 22, false);
            drag.OnClicked = () =>
            {
                _dropdownOpen = false;
                _dropdown.SetActive(false);
            };

            _dropdown.SetActive(false);
            _dropdownOpen = false;
        }

        /// <summary>
        /// 文字按 Quad 的同一套像素算法摆位（中心锚点、尺寸即宽高），
        /// 不走 AddCenteredTextAt 的「底左锚点 + PlaneHeight-yTop」那套，避免与 Mesh 坐标错位半屏。
        /// </summary>
        private Text AddText(string name, string content, int x, int yTop, int width, int height,
            int size, Color32 color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_canvas, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(
                x + width * 0.5f - MenuSceneObjects.PlaneWidth * 0.5f,
                MenuSceneObjects.PlaneHeight * 0.5f - yTop - height * 0.5f);

            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = MenuFonts.Chinese();
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        // ------------------------------------------------------------ 数据

        /// <summary>当前类别名（原版第一类为「選項」）。</summary>
        private static string CategoryLabel()
        {
            return MenuStrings.Get("GUI:Options", "Options");
        }

        private void ToggleDropdown()
        {
            _dropdownOpen = !_dropdownOpen;
            _dropdown.SetActive(_dropdownOpen);
        }

        private void SelectRow(int row)
        {
            var index = _scroll + row;
            if (index < 0 || index >= View.Count) return;
            _selected = index;
            Refresh();
        }

        // ------------------------------------------------------------ 操作

        private void BeginCapture()
        {
            if (_dropdownOpen) ToggleDropdown();

            InputManager.Instance.BeginCapture(key =>
            {
                _pending = key;
                Refresh();
            });
            _pending = Hotkey.None;
            Refresh();
        }

        private void Assign()
        {
            var view = View;
            if (_selected < 0 || _selected >= view.Count) return;
            if (!_pending.IsBound)
            {
                Debug.Log("[输入] 还没有捕获到新键，先点「輸入新的快捷鍵」再按一个键");
                return;
            }

            InputManager.Instance.Bind(view[_selected], _pending);
            InputManager.Instance.SaveBindings();
            _pending = Hotkey.None;
            Refresh();
        }

        private void ResetAll()
        {
            InputManager.Instance.ResetToDefaults();
            _pending = Hotkey.None;
            Refresh();
        }

        private void OnBindingChanged(CommandClass command)
        {
            Refresh();
        }

        // ------------------------------------------------------------ 刷新

        private void Refresh()
        {
            var view = View;

            if (_selected >= view.Count) _selected = view.Count - 1;
            if (_scrollbar != null) _scrollbar.SetCount(view.Count, RowCount);

            for (var i = 0; i < RowCount; i++)
            {
                var index = _scroll + i;
                var has = index >= 0 && index < view.Count;

                MenuSceneObjects.SetVisible(_rowHits[i], has);
                MenuSceneObjects.SetVisible(_rowNames[i].gameObject, has);
                MenuSceneObjects.SetVisible(_rowKeys[i].gameObject, has);

                var highlight = has && index == _selected;
                MenuSceneObjects.SetVisible(_rowBars[i], highlight);

                if (!has) continue;

                var command = view[index];
                _rowNames[i].text = NameOf(command);
                _rowKeys[i].text = command.Bound.IsBound ? command.Bound.Label : string.Empty;
            }

            var selected = _selected >= 0 && _selected < view.Count ? view[_selected] : null;

            if (_descText != null) _descText.text = DescOf(selected);

            if (_currentKeyValue != null)
            {
                _currentKeyValue.text = selected == null
                    ? string.Empty
                    : (selected.Bound.IsBound ? selected.Bound.Label : string.Empty);
            }

            if (_pendingValue != null)
            {
                _pendingValue.text = _pending.IsBound ? _pending.Label : string.Empty;
            }

            if (_assignedValue != null)
            {
                var owner = _pending.IsBound ? InputManager.Instance.FindByBinding(_pending) : null;
                _assignedValue.text = owner != null ? NameOf(owner) : string.Empty;
            }

            if (_assignButton != null) _assignButton.SetEnabled(selected != null);
            if (_resetButton != null) _resetButton.SetEnabled(true);

            if (_categoryValue != null) _categoryValue.text = CategoryLabel();
            if (_dropdownText != null) _dropdownText.text = CategoryLabel();
        }
    }
}
