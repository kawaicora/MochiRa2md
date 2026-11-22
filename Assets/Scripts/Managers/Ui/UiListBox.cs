// UiListBox.cs —— 列表框句柄：纵向行 + 滚动条 + 选中回调。
//
// 几何口径对照原版列表：ROW_HEIGHT=19、SCROLLBAR_WIDTH=20、ARROW_HEIGHT=22、
// MIN_THUMB_HEIGHT=14；滚动部件 Up/Down/Thumb/Track 的交互由 ScrollRect 承载，
// 行高与右栏宽度在工厂里按常量落位。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers.Ui
{
    public sealed class UiListBox : UiWidget
    {
        public const float RowHeight = 19f;
        public const float ScrollbarWidth = 20f;

        public ScrollRect Scroll { get; }
        public IReadOnlyList<string> Items => _items;
        public int SelectedIndex { get; private set; } = -1;

        public event Action<int> OnPick;

        private readonly List<string> _items = new List<string>();
        private readonly List<Button> _rows = new List<Button>();
        private readonly RectTransform _content;

        internal UiListBox(GameObject go, ScrollRect scroll, RectTransform content) : base(go)
        {
            Scroll = scroll;
            _content = content;
        }

        public void SetItems(IList<string> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            SelectedIndex = -1;
            RebuildRows();
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            SelectedIndex = index;
            OnPick?.Invoke(index);
        }

        private void RebuildRows()
        {
            foreach (var row in _rows)
            {
                if (row != null) UnityEngine.Object.Destroy(row.gameObject);
            }
            _rows.Clear();

            for (var i = 0; i < _items.Count; i++)
            {
                var rowGo = new GameObject($"Row{i}", typeof(RectTransform));
                rowGo.transform.SetParent(_content, false);
                var rt = rowGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(0f, RowHeight);
                rt.anchoredPosition = new Vector2(0f, -i * RowHeight);

                var bg = rowGo.AddComponent<Image>();
                bg.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
                var btn = rowGo.AddComponent<Button>();
                var index = i;
                btn.onClick.AddListener(() => Select(index));

                var labelGo = new GameObject("Label", typeof(RectTransform));
                labelGo.transform.SetParent(rowGo.transform, false);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = new Vector2(0f, 0f);
                labelRt.anchorMax = new Vector2(1f, 1f);
                labelRt.offsetMin = new Vector2(6f, 0f);
                labelRt.offsetMax = new Vector2(-6f, 0f);
                var label = labelGo.AddComponent<Text>();
                label.font = UIManager.DefaultFont();
                label.fontSize = 13;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = Color.white;
                label.text = _items[i];

                _rows.Add(btn);
            }

            if (_content != null)
            {
                _content.sizeDelta = new Vector2(0f, _items.Count * RowHeight);
            }
        }
    }
}
