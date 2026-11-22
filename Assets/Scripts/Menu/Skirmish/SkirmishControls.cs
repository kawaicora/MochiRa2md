// SkirmishControls.cs —— 遭遇战对话框（资源 258 / 0x102）的可复用控件组件。
//
// 为什么做成 Component：原版 ownerdraw 的每个控件（编辑框 / 组合框 / 滑条 / 复选框）
// 都是同一套绘制指令的实例（code\sub_\sub_60F9A0.c 的子类化 → sub_61D950 等 WM_PAINT 分支），
// 换句话说"外观"是控件自带的，不是页面贴上去的。这里照同样的结构：每个控件一个
// MonoBehaviour，自己建子 Quad、自己管状态，页面只负责摆放和读数。
//
// 贴图来源（全部从 ra2.mix\local.mix 按名字取，见 sub_61F210 的 ForceLoadFile 清单）：
//   arrow_dd.pcx / arrow_du.pcx   下拉箭头（28×24）
//   trofl/trofm/trofr.pcx         滑条数值板左端头 / 中间 / 右端头
//   其余（编辑框/组合框边框、复选框、把手）按实机截图逐像素取样构造
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ra2md.Core;

namespace Ra2md.Menu
{
    /// <summary>控件公共工具：在控件宿主下按 800×600 左上原点摆一块 Quad。</summary>
    internal static class SkinQuad
    {
        public static GameObject Add(Transform host, string name, Texture texture, int x, int y,
            int w, int h, int order, bool interactive = false)
        {
            return MenuSceneObjects.QuadLocal(host, name, texture, w, h, order, interactive,
                x + w * 0.5f, MenuSceneObjects.PlaneHeight - y - h * 0.5f);
        }

        public static Transform Host(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }
    }

    /// <summary>
    /// 2px 双色边框 + 50% 黑点阵内部。编辑框 / 组合框 / 滑条轨道 / 右列金属框都是这一套，
    /// 只换两级边框色（实机截图取样：组合框 #A5BEC6/#6B7984，金属框 #848684/#525152）。
    /// </summary>
    public sealed class SkinCell : MonoBehaviour
    {
        private GameObject _edge, _inner, _fill;
        private int _w, _h;

        public static SkinCell Create(Transform parent, string name, int x, int y, int w, int h, int order,
            Color32 hi, Color32 lo)
        {
            var host = SkinQuad.Host(parent, name);
            var cell = host.gameObject.AddComponent<SkinCell>();
            cell.Build(x, y, w, h, order, hi, lo);
            return cell;
        }

        private void Build(int x, int y, int w, int h, int order, Color32 hi, Color32 lo)
        {
            _w = w;
            _h = h;
            _edge = SkinQuad.Add(transform, "Edge", MenuWidgets.Solid(hi), x, y, w, h, order);
            _inner = SkinQuad.Add(transform, "Inner", MenuWidgets.Solid(lo), x + 1, y + 1, w - 2, h - 2,
                order + 1);
            // 原版 ownerdraw 的 EDIT/COMBOBOX 内部是「透空」的：只有 2px 双色边框 + 1px 内部描边，
            // 中间不铺点刷，直接透出对话框背景图。这里不再建 Dither 面片（否则会透出下面那张
            // 主题红纹理，看上去一片脏红）。
            _fill = null;
        }

        public void SetVisible(bool visible)
        {
            MenuSceneObjects.SetVisible(_edge, visible);
            MenuSceneObjects.SetVisible(_inner, visible);
            MenuSceneObjects.SetVisible(_fill, visible);
        }

        public int Width { get { return _w; } }
        public int Height { get { return _h; } }
    }

    /// <summary>按名字从 ra2.mix\local.mix 取一张 PCX 并铺成 Quad（自带缓存，可多处复用）。</summary>
    public sealed class SkinPcx : MonoBehaviour
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private GameObject _quad;
        private string _pcxName;
        private int _x, _y, _w, _h, _order;

        public static SkinPcx Create(Transform parent, string name, string pcxName, int x, int y,
            int w, int h, int order)
        {
            var host = SkinQuad.Host(parent, name);
            var pcx = host.gameObject.AddComponent<SkinPcx>();
            pcx._pcxName = pcxName;
            pcx._x = x; pcx._y = y; pcx._w = w; pcx._h = h; pcx._order = order;
            pcx.Rebuild();
            return pcx;
        }

        private void Rebuild()
        {
            Texture2D tex;
            if (!Cache.TryGetValue(_pcxName, out tex) || tex == null)
            {
                tex = SkirmishSkin.LoadPcxFromLocalMix(_pcxName);
                Cache[_pcxName] = tex;
            }
            if (tex == null) return;
            var w = _w > 0 ? _w : tex.width;
            var h = _h > 0 ? _h : tex.height;
            if (_quad == null)
                _quad = SkinQuad.Add(transform, "Pcx", tex, _x, _y, w, h, _order);
            else
                MenuSceneObjects.SetTexture(_quad, tex);
        }

        public GameObject Quad { get { return _quad; } }
    }

    /// <summary>
    /// 滑条数值板（trofl | trofm | trofr）。
    /// 依据 code\sub_\sub_61D950.c 的 WM_PAINT：trofm 先铺满 50px 宽、24 高，
    /// trofl 贴左端 8px、trofr 贴右端 10px，数值文字画在右侧 49px 内（右对齐、字号 12）。
    /// </summary>
    public sealed class SkinValuePlate : MonoBehaviour
    {
        private GameObject _left, _mid, _right;

        public static SkinValuePlate Create(Transform parent, string name, int x, int y, int w, int h,
            int order)
        {
            var host = SkinQuad.Host(parent, name);
            var plate = host.gameObject.AddComponent<SkinValuePlate>();
            plate.Build(x, y, w, h, order);
            return plate;
        }

        private void Build(int x, int y, int w, int h, int order)
        {
            var mid = SkirmishSkin.Tex("trofm.pcx");
            var left = SkirmishSkin.Tex("trofl.pcx");
            var right = SkirmishSkin.Tex("trofr.pcx");

            if (mid != null)
                _mid = SkinQuad.Add(transform, "Mid", mid, x, y, w, h, order);
            else
                _mid = SkinQuad.Add(transform, "Face", SkirmishSkin.ValueFace(), x, y, w, h, order);

            if (left != null)
                _left = SkinQuad.Add(transform, "L", left, x, y, left.width, h, order + 1);
            if (right != null)
                _right = SkinQuad.Add(transform, "R", right, x + w - right.width, y, right.width, h,
                    order + 1);
        }
    }

    /// <summary>滑条把手（12×22 竖向渐变条，取样见 SkirmishSkin.GripTexture）。</summary>
    public sealed class SkinGrip : MonoBehaviour
    {
        private GameObject _quad;
        private int _left, _top, _range;

        public static SkinGrip Create(Transform parent, string name, int left, int top, int range, int order)
        {
            var host = SkinQuad.Host(parent, name);
            var grip = host.gameObject.AddComponent<SkinGrip>();
            grip._left = left;
            grip._top = top;
            grip._range = range;
            grip.SetRatio(0f);
            return grip;
        }

        public void SetRatio(float ratio)
        {
            var x = _left + Mathf.RoundToInt(Mathf.Clamp01(ratio) * _range);
            if (_quad == null)
                _quad = SkinQuad.Add(transform, "Grip", SkirmishSkin.GripTexture(), x, _top, 12, 22, 64);
            else
                _quad.transform.localPosition = new Vector3(x + 6f,
                    MenuSceneObjects.PlaneHeight - (_top + 11f), 0f);
        }
    }

    /// <summary>
    /// 复选框（原版 BS_AUTOCHECKBOX → sub_6163A0 的按钮绘制）：紫灰 2px 框 + 深红内槽 + 红球。
    /// 自带点击区域，点方框或整行文字都能切。
    /// </summary>
    public sealed class SkinToggle : MonoBehaviour
    {
        private GameObject _orb;
        private Action<bool> _onChange;
        private bool _state;

        public bool IsOn { get { return _state; } }

        public static SkinToggle Create(Transform parent, string name, int x, int y, int size,
            int labelWidth, bool initial, Action<bool> onChange)
        {
            var host = SkinQuad.Host(parent, name);
            var toggle = host.gameObject.AddComponent<SkinToggle>();
            toggle.Build(x, y, size, labelWidth, initial, onChange);
            return toggle;
        }

        private void Build(int x, int y, int size, int labelWidth, bool initial, Action<bool> onChange)
        {
            _state = initial;
            _onChange = onChange;

            SkinQuad.Add(transform, "Edge", MenuWidgets.Solid(SkirmishSkin.ToggleEdgeHi), x, y, size, size, 60);
            SkinQuad.Add(transform, "Inner", MenuWidgets.Solid(SkirmishSkin.ToggleEdgeLo), x + 1, y + 1,
                size - 2, size - 2, 61);
            SkinQuad.Add(transform, "Well", MenuWidgets.Solid(new Color32(0x42, 0x00, 0x00, 255)),
                x + 2, y + 2, size - 4, size - 4, 62);
            _orb = SkinQuad.Add(transform, "Orb", SkirmishSkin.OrbTexture(), x + 4, y + 4, size - 8,
                size - 8, 63);
            MenuSceneObjects.SetVisible(_orb, _state);

            var hit = SkinQuad.Add(transform, "Hit", MenuSceneObjects.TransparentTexture, x, y,
                size + labelWidth, size + 2, 66, true);
            var click = hit.AddComponent<MenuDragArea>();
            click.Configure(x, y, size + labelWidth, size + 2, false);
            click.OnClicked = () => SetState(!_state, true);
        }

        public void SetState(bool on, bool notify)
        {
            _state = on;
            MenuSceneObjects.SetVisible(_orb, on);
            if (notify && _onChange != null) _onChange(on);
        }
    }

    /// <summary>
    /// 整条滑条 = 轨道（SkinCell）+ 把手（SkinGrip）+ 数值板（SkinValuePlate）+ 数值文字 + 拖动命中区。
    /// 几何照 sub_61D950：数值板固定 50px 贴右，把手可移动里程 = 控件宽 - 50 - 13，
    /// 把手 12px，最小位置 +1px。不再复用 MenuWidgets.SliderLive（它会另外画一套轨道和把手）。
    /// </summary>
    public sealed class SkinStepper : MonoBehaviour
    {
        private SkinCell _track;
        private SkinGrip _grip;
        private SkinValuePlate _plate;
        private Text _text;
        private float _value01;

        public event Action<float> ValueChanged;

        public float Value01 { get { return _value01; } }

        public static SkinStepper Create(Transform pageRoot, Transform canvas, string name,
            int x, int y, int width, int height, float initial01, int order)
        {
            var host = SkinQuad.Host(pageRoot, name);
            var stepper = host.gameObject.AddComponent<SkinStepper>();
            stepper.Build(canvas, x, y, width, height, initial01, order);
            return stepper;
        }

        private void Build(Transform canvas, int x, int y, int width, int height, float initial01, int order)
        {
            const int plateWidth = 50;
            var trackWidth = width - plateWidth;
            var range = width - plateWidth - 13;

            _track = SkinCell.Create(transform, "Track", x, y, trackWidth, height, order,
                SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);

            _plate = SkinValuePlate.Create(transform, "Value", x + trackWidth, y, plateWidth, 24,
                order);

            _grip = SkinGrip.Create(transform, "Grip", x + 2, y + 1, range, order + 3);

            _text = AddValueText(canvas, x + width - 49, y, 49, height);

            var hit = SkinQuad.Add(transform, "Hit", MenuSceneObjects.TransparentTexture, x, y,
                trackWidth, height, 66, true);
            var drag = hit.AddComponent<MenuDragArea>();
            drag.Configure(x, y, trackWidth, height, false);
            drag.OnValue = v =>
            {
                _grip.SetRatio(v);
                SetValue01(v, true);
            };

            SetValue01(initial01, false);
        }

        private Text AddValueText(Transform canvas, int x, int y, int w, int h)
        {
            var go = new GameObject("Value", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(
                x + w * 0.5f - MenuSceneObjects.PlaneWidth * 0.5f,
                MenuSceneObjects.PlaneHeight * 0.5f - y - h * 0.5f);

            var text = go.AddComponent<Text>();
            text.font = MenuFonts.Chinese();
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color32(0xFF, 0xE8, 0x00, 255);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        public void SetValue01(float ratio, bool notify)
        {
            _value01 = Mathf.Clamp01(ratio);
            _grip.SetRatio(_value01);
            if (notify && ValueChanged != null) ValueChanged(_value01);
        }

        public void SetLabel(string label)
        {
            if (_text != null) _text.text = label;
        }
    }
}

