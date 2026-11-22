// MenuShpButton.cs —— 原版对话框按钮本体：mnbttn.shp（126×25，3 帧）+ mainbttn.pal。
//
// 帧序：0 暗红常態 / 1 橙色悬停 / 2 亮黄按下（离线逐像素核对过 0x1BB65278 + 0xC087631E）。
// 键盘选项页的「指定」「全部重設」等按钮全部由本组件拼出来，不再用程序画的矩形冒充按钮。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ra2md.Menu
{
    public sealed class MenuShpButton : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public const string ShpFileName = Ra2md.Managers.GlobalConfigManager.Resources.ButtonShp;
        public const uint ShpId = Ra2md.Managers.GlobalConfigManager.Resources.ButtonShpId;
        public const uint PaletteId = Ra2md.Managers.GlobalConfigManager.Resources.ButtonPaletteId;
        public const int Width = 126;
        public const int Height = 25;

        private static List<Texture2D> _frames;

        /// <summary>mnbttn.shp 的三帧贴图（首次访问时从 VFS 取）。</summary>
        public static List<Texture2D> Frames
        {
            get
            {
                if (_frames != null && _frames.Count > 0) return _frames;

                _frames = new List<Texture2D>(3);
                for (var i = 0; i < 3; i++)
                {
                    var tex = MenuResourceLibrary.LoadShpFrameTexture(
                        ShpId, PaletteId, i, ShpFileName + " 帧" + i, true);
                    if (tex != null) _frames.Add(tex);
                }
                if (_frames.Count == 0) Debug.LogWarning("[菜单] mnbttn.shp 三帧没取到，按钮会没有本体");
                return _frames;
            }
        }

        public static readonly Color32 LabelColor = new Color32(255, 222, 25, 255);
        public static readonly Color32 LabelDimColor = new Color32(150, 130, 40, 255);

        public string Label;
        /// <summary>文案的 CSF key（按钮点击按 key 派发，换语言不影响逻辑）。</summary>
        public string Key;
        public Action Clicked;

        public GameObject Face;
        public Text Text;

        public bool Interactive { get; set; }

        private Texture2D[] _stateTex = new Texture2D[3];
        private int _state = -1;

        /// <summary>
        /// 在 800×600 平面（左上原点）的 left/top 处拼一个原版按钮。
        /// labelKey 走 CSF；fallback 只在 CSF 缺条时用。
        /// </summary>
        public static MenuShpButton Create(Transform parent, string name, string labelKey, string fallback,
            int left, int top, Action clicked, int order = 64)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var button = go.AddComponent<MenuShpButton>();
            button.Key = labelKey;
            button.Label = MenuStrings.Get(labelKey, fallback);
            button.Clicked = clicked;

            var frames = Frames;
            for (var i = 0; i < 3; i++)
                button._stateTex[i] = i < frames.Count ? frames[i] : (frames.Count > 0 ? frames[0] : null);

            button.Face = MenuSceneObjects.Quad(go.transform, "Face", button._stateTex[0],
                left, top, Width, Height, order, true);

            // 文字画布：跟随按钮，按 800×600 平面坐标摆，保证和按钮同层不会被别的内容盖住。
            var canvas = MenuSceneObjects.AttachLabelCanvas(go, Width, Height, order + 2);
            canvas.localPosition = new Vector3(left + Width * 0.5f,
                MenuSceneObjects.PlaneHeight - top - Height * 0.5f, -0.05f);

            button.Text = MenuSceneObjects.AddCenteredText(canvas, button.Label, Width, Height, 14, LabelColor);

            button.SetEnabled(true);
            return button;
        }

        public void SetLabel(string text)
        {
            Label = text;
            if (Text != null) Text.text = text;
        }

        public void SetEnabled(bool enabled)
        {
            Interactive = enabled;
            SetState(enabled ? 0 : 0, enabled);
        }

        private void SetState(int frame, bool lit = true)
        {
            if (frame < 0) frame = 0;
            if (frame > 2) frame = 2;
            if (_state == frame && Text != null && Text.color == (lit ? (Color)LabelColor : (Color)LabelDimColor))
                return;

            _state = frame;
            var tex = _stateTex[frame];
            MenuSceneObjects.SetTexture(Face, tex);
            if (Text != null) Text.color = lit ? (Color)LabelColor : (Color)LabelDimColor;
        }

        // ---- 拾取 ----

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetState(1);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetState(0);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetState(2);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!Interactive) return;
            SetState(1);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactive) return;
            if (Clicked != null) Clicked();
        }
    }
}
