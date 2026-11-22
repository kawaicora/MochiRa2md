// UiTick.cs —— 勾选框 / 单选钮句柄：状态 + 变更回调。
//
// 对照原版勾选：CheckboxFace：Unchecked / Checked / CheckedAlone / AnsweredAlone；
// 单选与勾选共用 Toggle，是否互斥由同一组 Toggle 管理（group），句柄本身不记互斥。
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers.Ui
{
    public sealed class UiTick : UiWidget
    {
        public Toggle Toggle { get; }
        public Text Label { get; }

        public bool IsOn
        {
            get => Toggle != null && Toggle.isOn;
            set { if (Toggle != null) Toggle.isOn = value; }
        }

        public event Action<bool> OnChanged;

        internal UiTick(GameObject go, Toggle toggle, Text label) : base(go)
        {
            Toggle = toggle;
            Label = label;
            if (toggle != null)
            {
                toggle.onValueChanged.AddListener(v => OnChanged?.Invoke(v));
            }
        }
    }
}
