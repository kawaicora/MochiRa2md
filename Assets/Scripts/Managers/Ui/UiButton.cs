// UiButton.cs —— 按钮句柄：常态 / 高亮 / 按下三帧 + onClick 绑定。
//
// 对照原版按钮：owner_button_frame(pressed, highlighted) → 按下=1、高亮=2、常态=0；
// 三帧精灵由工厂按 SHP/PNG 资源填入，缺帧时回退纯色底。点击事件在这里只做转发，
// 具体音效/页面跳转由 LogicManager 挂的回调决定。
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers.Ui
{
    public sealed class UiButton : UiWidget
    {
        public Button Button { get; }
        public Image Image { get; }
        public Text Label { get; }

        public Sprite FrameNormal { get; private set; }
        public Sprite FrameHover { get; private set; }
        public Sprite FramePressed { get; private set; }

        public event Action OnClick;

        internal UiButton(GameObject go, Button button, Image image, Text label) : base(go)
        {
            Button = button;
            Image = image;
            Label = label;
            if (button != null)
            {
                button.onClick.AddListener(() => OnClick?.Invoke());
            }
        }

        /// <summary>绑定点击回调（可多次追加；与 OnClick 事件等价）。</summary>
        public void BindOnClick(Action action)
        {
            if (action != null) OnClick += action;
        }

        /// <summary>设置三态帧：normal=常态，hover=高亮，pressed=按下。</summary>
        public void SetFrames(Sprite normal, Sprite hover, Sprite pressed)
        {
            FrameNormal = normal;
            FrameHover = hover;
            FramePressed = pressed;
            ApplyFrame(false, false);
        }

        public bool Interactable
        {
            get => Button != null && Button.interactable;
            set { if (Button != null) Button.interactable = value; }
        }

        internal void ApplyFrame(bool pressed, bool hovered)
        {
            if (Image == null) return;
            Image.sprite = pressed ? FramePressed : hovered ? FrameHover : FrameNormal;
        }
    }
}
