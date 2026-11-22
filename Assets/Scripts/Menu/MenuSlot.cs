// MenuSlot.cs —— 右侧按钮本体（Face + 文字）。
// 底板 / 扫光帧由 MenuButtonPanel 按槽位常驻管理；本组件只负责按钮本体三态与拾取。
// 对照参考：按钮本体仅在 Idle 相位绘制；帧序 0=常态 1=悬停/聚焦 2=按下。
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ra2md.Menu
{
    public class MenuSlot : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public string Label;
        /// <summary>文案的 CSF key；点击派发按 key 走，换语言不影响逻辑。</summary>
        public string Key;
        public int Index;
        /// <summary>普通槽位 0..7；-1 表示 SDBTM 内的底部按钮（Exit/Back）。</summary>
        public int Slot;

        public GameObject Face;
        public Text Text;

        private Texture _normal;
        private Texture _pressed;
        private Texture _hover;
        private Action<MenuSlot> _clicked;

        /// <summary>非 Idle 相位不响应拾取（参考 hitButton：phase != Idle 返回 -1）。</summary>
        public bool Interactive { get; set; }

        /// <summary>当前本体状态：0 常态 1 悬停 2 按下（底板按状态换帧）。</summary>
        public int FaceState { get; private set; }

        /// <summary>状态变化时通知面板换底板帧（0 常态 / 1 悬停 / 2 按下）。</summary>
        public Action<MenuSlot> StateChanged;

        public void Configure(int index, int slot, string key, string label, GameObject face, Text text,
            Texture normal, Texture hover, Texture pressed, Action<MenuSlot> onClicked)
        {
            Index = index;
            Slot = slot;
            Key = key;
            Label = label;
            Face = face;
            Text = text;
            _normal = normal;
            _hover = hover;
            _pressed = pressed;
            _clicked = onClicked;
            Interactive = false;

            SetFaceState(0);
        }

        // ---- 本体可见性 / 三态（面板相位驱动） ----

        public void SetFaceVisible(bool visible)
        {
            if (Face != null) Face.SetActive(visible);
            if (Text != null) Text.gameObject.SetActive(visible);
        }

        /// <summary>帧序：0 常态，1 悬停/键盘聚焦，2 按下。</summary>
        public void SetFaceState(int frame)
        {
            Texture tex;
            switch (frame)
            {
                case 1: tex = _hover != null ? _hover : _normal; break;
                case 2: tex = _pressed != null ? _pressed : _normal; break;
                default: tex = _normal; break;
            }
            MenuSceneObjects.SetTexture(Face, tex);
            if (FaceState != frame)
            {
                FaceState = frame;
                if (StateChanged != null) StateChanged(this);
            }
        }

        // ---- 拾取 ----

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetFaceState(1);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetFaceState(0);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetFaceState(2);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetFaceState(1);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactive) return;
            if (_clicked != null) _clicked(this);
        }
    }
}
