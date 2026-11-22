// UiComboBox.cs —— 下拉框句柄：收起态显示当前项，点开列项，选中回填并收起。
//
// 对照原版下拉框：ComboPress = Scroll / Pick(idx) / Dismiss / Face / Open；
// 箭头区点开列表，选行或点别处关闭；Open/Pick/Dismiss 的音效 cue 由页面回调挂。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers.Ui
{
    public sealed class UiComboBox : UiWidget
    {
        public Button Face { get; }
        public Text FaceLabel { get; }
        public RectTransform Dropdown { get; }

        public int SelectedIndex { get; private set; } = -1;
        public bool IsOpen { get; private set; }

        public event Action<int> OnPick;

        private readonly List<string> _items = new List<string>();
        private readonly List<Button> _rows = new List<Button>();
        private readonly RectTransform _content;

        internal UiComboBox(GameObject go, Button face, Text faceLabel, RectTransform dropdown, RectTransform content)
            : base(go)
        {
            Face = face;
            FaceLabel = faceLabel;
            Dropdown = dropdown;
            _content = content;
            if (face != null) face.onClick.AddListener(Toggle);
            if (dropdown != null) dropdown.gameObject.SetActive(false);
        }

        public void SetItems(IList<string> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            SelectedIndex = -1;
            if (FaceLabel != null) FaceLabel.text = string.Empty;
            RebuildRows();
        }

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (Dropdown != null) Dropdown.gameObject.SetActive(open);
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            SelectedIndex = index;
            if (FaceLabel != null) FaceLabel.text = _items[index];
            SetOpen(false);
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
                rt.sizeDelta = new Vector2(0f, UiListBox.RowHeight);
                rt.anchoredPosition = new Vector2(0f, -i * UiListBox.RowHeight);

                var bg = rowGo.AddComponent<Image>();
                bg.color = new Color(0.18f, 0.18f, 0.22f, 0.96f);
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
                _content.sizeDelta = new Vector2(0f, _items.Count * UiListBox.RowHeight);
            }
        }
    }
}
