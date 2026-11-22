// UiTrackbar.cs —— 滑动条句柄：取值区间 + 当前值 + 变更回调。
//
// 算术口径对照原版滑动条：thumb_left = 1 + position*usableSpan/range.max(1)，
// 按压条带 y > height-18；这里用 Unity Slider 承载拖拽/跳转语义，工厂按宽度留拇指区。
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers.Ui
{
    public sealed class UiTrackbar : UiWidget
    {
        public Slider Slider { get; }

        public float MinValue
        {
            get => Slider != null ? Slider.minValue : 0f;
            set { if (Slider != null) Slider.minValue = value; }
        }

        public float MaxValue
        {
            get => Slider != null ? Slider.maxValue : 1f;
            set { if (Slider != null) Slider.maxValue = value; }
        }

        public float Value
        {
            get => Slider != null ? Slider.value : 0f;
            set { if (Slider != null) Slider.value = value; }
        }

        public event Action<float> OnValueChanged;

        internal UiTrackbar(GameObject go, Slider slider) : base(go)
        {
            Slider = slider;
            if (slider != null)
            {
                slider.onValueChanged.AddListener(v => OnValueChanged?.Invoke(v));
            }
        }
    }
}
