// UiLabel.cs —— 静态文本句柄（SHP/PNG 之外的文字，字号/颜色/对齐由工厂给定）。
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers.Ui
{
    public sealed class UiLabel : UiWidget
    {
        public Text Text { get; }

        public string Value
        {
            get => Text != null ? Text.text : string.Empty;
            set { if (Text != null) Text.text = value; }
        }

        internal UiLabel(GameObject go, Text text) : base(go)
        {
            Text = text;
        }
    }
}
