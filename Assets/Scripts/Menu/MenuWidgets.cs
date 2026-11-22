// MenuWidgets.cs —— 选项页的纯几何控件（滑条 / 下拉框 / 复选框 / 数字微调框）。
// 原版这些控件不是 SHP，是引擎用线条和色块画出来的；这里同样用 1×1 纯色贴图拼矩形，
// 坐标全部按 800×600 像素平面（左上原点、y 向下），与 AddPageLabel 同一套坐标系。
using System.Collections.Generic;
using UnityEngine;

namespace Ra2md.Menu
{
    public static class MenuWidgets
    {
        // 取色对照 802×632 原版选项页截图
        public static readonly Color32 FrameLight = new Color32(198, 198, 198, 255);
        public static readonly Color32 FrameDim = new Color32(110, 110, 110, 255);
        public static readonly Color32 TrackFill = new Color32(64, 64, 64, 255);
        public static readonly Color32 HandleFill = new Color32(178, 178, 178, 255);
        public static readonly Color32 HandleLine = new Color32(250, 250, 250, 255);
        public static readonly Color32 BoxFill = new Color32(12, 12, 12, 255);
        public static readonly Color32 BoxCheckOn = new Color32(226, 122, 22, 255);
        public static readonly Color32 BoxCheckEdge = new Color32(180, 150, 90, 255);
        public static readonly Color32 RedPanel = new Color32(120, 12, 8, 255);
        public static readonly Color32 RedPanelEdge = new Color32(236, 210, 160, 255);
        public static readonly Color32 ArrowFill = new Color32(196, 196, 196, 255);
        // 横向推子（对照原版 GaugeClass 横向仪表 / drawLobbyStepper）：暗红轨道、红握把金边。
        public static readonly Color32 TrackFrameRed = new Color32(117, 17, 11, 255);
        public static readonly Color32 TrackFillDark = new Color32(8, 3, 3, 255);
        public static readonly Color32 GripFill = new Color32(126, 22, 14, 255);
        public static readonly Color32 GripEdge = new Color32(255, 177, 38, 255);

        private static readonly Dictionary<Color32, Texture2D> _solid =
            new Dictionary<Color32, Texture2D>();
        private static Texture2D _arrow;

        public static Texture2D Solid(Color32 color)
        {
            if (_solid.TryGetValue(color, out var cached) && cached != null) return cached;

            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixels32(new[] { color });
            t.Apply(false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Point;
            _solid[color] = t;
            return t;
        }

        /// <summary>下拉箭头：8×8 白色倒三角。</summary>
        public static Texture2D Arrow()
        {
            if (_arrow != null) return _arrow;
            var t = new Texture2D(9, 9, TextureFormat.RGBA32, false);
            var px = new Color32[81];
            for (var y = 0; y < 9; y++)
                for (var x = 0; x < 9; x++)
                {
                    var inside = y >= 2 && y <= 6 && x >= (y - 2) && x <= 8 - (y - 2);
                    px[y * 9 + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(0, 0, 0, 0);
                }
            t.SetPixels32(px);
            t.Apply(false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Point;
            _arrow = t;
            return t;
        }

        private static GameObject Rect(Transform parent, string name, int x, int y, int w, int h,
            Color32 color, int order)
        {
            return MenuSceneObjects.Quad(parent, name, Solid(color), x, y, w, h, order, false);
        }

        /// <summary>带 1px 外框、内填色的矩形（滑条轨道 / 输入框）。</summary>
        public static void Panel(Transform parent, string name, int x, int y, int w, int h,
            Color32 border, Color32 fill, int order)
        {
            Rect(parent, name + "_Border", x, y, w, h, border, order);
            Rect(parent, name + "_Fill", x + 1, y + 1, w - 2, h - 2, fill, order + 1);
        }

        /// <summary>横向滑条：轨道 + 白色中线 + 方块滑块（滑块中心由 0..1 的 value 决定）。</summary>
        public static void Slider(Transform parent, string name, int x, int y, int w, int h, float value, int order)
        {
            Panel(parent, name, x, y, w, h, FrameLight, TrackFill, order);
            Rect(parent, name + "_CenterLine", x + 1, y + h / 2 - 1, w - 2, 1, FrameDim, order + 2);

            int knobW = 12, knobH = h;
            int travel = Mathf.Max(0, w - knobW);
            int knobX = x + Mathf.RoundToInt(travel * Mathf.Clamp01(value));
            Rect(parent, name + "_Knob", knobX, y, knobW, knobH, HandleFill, order + 3);
            Rect(parent, name + "_KnobEdge", knobX, y, 1, knobH, FrameLight, order + 4);
            Rect(parent, name + "_KnobEdge2", knobX + knobW - 1, y, 1, knobH, FrameDim, order + 4);
            Rect(parent, name + "_KnobLine", knobX + knobW / 2 - 1, y + 2, 1, knobH - 4, HandleLine, order + 5);
        }

        /// <summary>复选方块：14×14，勾选时内填橙块。</summary>
        public static void CheckBox(Transform parent, string name, int x, int y, bool on, int order)
        {
            Rect(parent, name, x, y, 14, 14, BoxCheckEdge, order);
            Rect(parent, name + "_Fill", x + 2, y + 2, 10, 10, BoxFill, order + 1);
            if (on) Rect(parent, name + "_On", x + 3, y + 3, 8, 8, BoxCheckOn, order + 2);
        }

        /// <summary>下拉框：黑底 + 右侧箭头钮（原版「800 x 600 x 16」那一格的外观）。</summary>
        public static void Combo(Transform parent, string name, int x, int y, int w, int h, int order)
        {
            Panel(parent, name, x, y, w, h, FrameLight, BoxFill, order);
            Rect(parent, name + "_ArrowBack", x + w - 22, y + 2, 20, h - 4, ArrowFill, order + 2);
            MenuSceneObjects.Quad(parent, name + "_Arrow", Arrow(), x + w - 17, y + (h - 9) / 2, 9, 9, order + 3, false);
        }

        // ------------------------------------------------------------ 可拖动版本

        /// <summary>横向滑条（可拖动）：返回句柄，拖到哪就设哪，滑块跟着移。</summary>
        public static SliderWidget SliderLive(Transform parent, string name, int x, int y, int w, int h,
            float value, int order, System.Action<float> onValue)
        {
            var widget = DrawSlider(parent, name, x, y, w, h, value, order);
            var hit = MenuSceneObjects.Quad(parent, name + "_Hit", MenuSceneObjects.TransparentTexture,
                x, y, w, h, order + 6, true);
            var drag = hit.AddComponent<MenuDragArea>();
            drag.Configure(x, y, w, h, false);
            drag.OnValue = v =>
            {
                widget.SetValue(v);
                if (onValue != null) onValue(widget.Value);
            };
            return widget;
        }

        /// <summary>音量推子（可拖动）：横向细轨道 + 竖向握把 + 右侧红底数值格。左右方向，对齐原版横向仪表。</summary>
        public static SpinnerWidget SpinnerLive(Transform parent, string name, int x, int y, int w, int h,
            float value, int order, System.Action<float> onValue)
        {
            var widget = DrawSpinner(parent, name, x, y, w, h, value, order);
            var trackW = w - SpinnerWidget.CellWidth - 4;
            var hit = MenuSceneObjects.Quad(parent, name + "_Hit", MenuSceneObjects.TransparentTexture,
                x + 2, y, trackW, h, order + 6, true);
            var drag = hit.AddComponent<MenuDragArea>();
            drag.Configure(x + 2, y, trackW, h, false);
            drag.OnValue = v =>
            {
                widget.SetValue(v);
                if (onValue != null) onValue(widget.Value);
            };
            return widget;
        }

        /// <summary>可点击复选块：点一下原地切换勾选外观。</summary>
        public static CheckBoxWidget CheckBoxLive(Transform parent, string name, int x, int y, bool on,
            int order, System.Action<bool> onToggle)
        {
            var widget = new CheckBoxWidget();
            widget.Border = Rect(parent, name, x, y, 14, 14, BoxCheckEdge, order);
            widget.Fill = Rect(parent, name + "_Fill", x + 2, y + 2, 10, 10, BoxFill, order + 1);
            widget.On = Rect(parent, name + "_On", x + 3, y + 3, 8, 8, BoxCheckOn, order + 2);
            widget._on = on;
            widget._onRect = widget.On;
            MenuSceneObjects.SetVisible(widget.On, on);

            var hit = MenuSceneObjects.Quad(parent, name + "_Hit", MenuSceneObjects.TransparentTexture,
                x, y, 14, 14, order + 3, true);
            var drag = hit.AddComponent<MenuDragArea>();
            drag.Configure(x, y, 14, 14, false);
            drag.OnClicked = () =>
            {
                widget.Toggle();
                if (onToggle != null) onToggle(widget.IsOn);
            };
            return widget;
        }

        private static SliderWidget DrawSlider(Transform parent, string name, int x, int y, int w, int h,
            float value, int order)
        {
            var widget = new SliderWidget
            {
                _x = x, _y = y, _w = w, _h = h, _knobWidth = 12,
            };

            // 横向细轨道：红框 + 暗底（对照原版横向仪表 / drawLobbyStepper）
            Rect(parent, name + "_Track", x + 1, y + h / 2 - 2, w - 2, 4, TrackFrameRed, order + 2);
            Rect(parent, name + "_TrackFill", x + 2, y + h / 2 - 1, w - 4, 2, TrackFillDark, order + 3);

            widget._knob = Rect(parent, name + "_Knob", x, y, 12, h, GripFill, order + 4);
            widget._edgeLeft = Rect(parent, name + "_KnobEdge", x, y, 1, h, GripEdge, order + 5);
            widget._edgeRight = Rect(parent, name + "_KnobEdge2", x, y, 1, h, GripEdge, order + 5);
            widget._line = Rect(parent, name + "_KnobLine", x + 5, y + 2, 1, h - 4, GripEdge, order + 5);
            widget.SetValue(value);
            return widget;
        }

        private static SpinnerWidget DrawSpinner(Transform parent, string name, int x, int y, int w, int h,
            float value, int order)
        {
            var widget = new SpinnerWidget { _x = x, _y = y, _w = w, _h = h, _gripWidth = 12 };

            int trackW = w - SpinnerWidget.CellWidth - 4;
            int trackTop = y + h / 2 - 2;
            const int trackH = 4;

            // 横向轨道：红框 + 暗底
            Rect(parent, name + "_Track", x + 2, trackTop, trackW, trackH, TrackFrameRed, order + 2);
            Rect(parent, name + "_TrackFill", x + 3, trackTop + 1, trackW - 2, trackH - 2, TrackFillDark, order + 3);

            // 竖向握把：红填 + 金边
            int gripH = h - 6;
            widget._grip = Rect(parent, name + "_Knob", x, trackTop - 3, 12, gripH, GripFill, order + 4);
            widget._edgeLeft = Rect(parent, name + "_KnobEdge", x, trackTop - 3, 1, gripH, GripEdge, order + 5);
            widget._edgeRight = Rect(parent, name + "_KnobEdge2", x, trackTop - 3, 1, gripH, GripEdge, order + 5);

            // 右侧红底数值格
            int cellX = x + w - SpinnerWidget.CellWidth;
            Rect(parent, name + "_Cell", cellX, y + 4, SpinnerWidget.CellWidth - 2, h - 8, RedPanelEdge, order + 2);
            Rect(parent, name + "_CellFill", cellX + 1, y + 5, SpinnerWidget.CellWidth - 4, h - 10, RedPanel, order + 3);

            widget.SetValue(value);
            return widget;
        }
    }

    /// <summary>横向滑条的滑块句柄。</summary>
    public sealed class SliderWidget
    {
        public int _x, _y, _w, _h, _knobWidth;
        public GameObject _knob, _edgeLeft, _edgeRight, _line;

        public float Value { get; private set; }

        public void SetValue(float value)
        {
            Value = Mathf.Clamp01(value);
            var travel = Mathf.Max(0, _w - _knobWidth);
            var knobX = _x + Mathf.RoundToInt(travel * Value);
            Move(_knob, knobX, _y, _knobWidth, _h);
            Move(_edgeLeft, knobX, _y, 1, _h);
            Move(_edgeRight, knobX + _knobWidth - 1, _y, 1, _h);
            Move(_line, knobX + _knobWidth / 2 - 1, _y + 2, 1, _h - 4);
        }

        private static void Move(GameObject go, int x, int y, int w, int h)
        {
            if (go == null) return;
            go.transform.localPosition = new Vector3(
                x + w * 0.5f,
                MenuSceneObjects.PlaneHeight - y - h * 0.5f,
                go.transform.localPosition.z);
        }
    }

    /// <summary>横向音量推子的握把句柄。</summary>
    public sealed class SpinnerWidget
    {
        /// <summary>右侧预留数值格宽度。</summary>
        public const int CellWidth = 26;

        public int _x, _y, _w, _h, _gripWidth;
        public GameObject _grip, _edgeLeft, _edgeRight;

        public float Value { get; private set; }

        public void SetValue(float value)
        {
            Value = Mathf.Clamp01(value);
            int trackW = _w - CellWidth - 4;
            var travel = Mathf.Max(0, trackW - _gripWidth);
            var knobX = _x + 2 + Mathf.RoundToInt(travel * Value);
            var gripH = _h - 6;
            var gripTop = _y + _h / 2 - 2 - 3;
            Move(_grip, knobX, gripTop, _gripWidth, gripH);
            Move(_edgeLeft, knobX, gripTop, 1, gripH);
            Move(_edgeRight, knobX + _gripWidth - 1, gripTop, 1, gripH);
        }

        private void Move(GameObject go, int x, int y, int w, int h)
        {
            if (go == null) return;
            go.transform.localPosition = new Vector3(
                x + w * 0.5f,
                MenuSceneObjects.PlaneHeight - y - h * 0.5f,
                go.transform.localPosition.z);
        }
    }

    /// <summary>复选块句柄。</summary>
    public sealed class CheckBoxWidget
    {
        public GameObject Border, Fill, On;
        public GameObject _onRect;
        public bool _on;

        public bool IsOn { get { return _on; } }

        public void Toggle()
        {
            _on = !_on;
            MenuSceneObjects.SetVisible(_onRect, _on);
        }
    }
}

