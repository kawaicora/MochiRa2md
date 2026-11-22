// MenuListScroll.cs —— 原版 ListClass 滚动条的 Unity 复刻。
//
// 对照 YRpp ListClass：
//   UpButton / DownButton（ShapeButtonClass 上下箭头）+ Scroller（SliderClass 滑块）
//   + SelectedIndex / CurrentTopIndex；SetViewIndex 把顶行号夹在 [0, Count-LineCount]。
// 几何对照参考实现 drawMapList：箭头各占顶端/底端 14px，轨道在中间；
// 滑块高度 = 轨道高 × 可见行数 / 总行数，滑块顶按 Top/maxScroll 比例落位。
// 三条滚动路径都要通：上下箭头点击、轨道/滑块拖拽、鼠标滚轮（指针落在列表框内才响应）。
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ra2md.Menu
{
    /// <summary>一条垂直滚动条：箭头 + 轨道 + 比例滑块，对外只暴露顶行号。</summary>
    public sealed class MenuListScroll : MonoBehaviour
    {
        private const int ArrowSize = 22;
        private const int ThumbMin = 14;

        private static readonly Color32 TrackFill = new Color32(12, 17, 15, 255);
        private static readonly Color32 ArrowFill = new Color32(28, 34, 29, 255);
        private static readonly Color32 ThumbFill = new Color32(113, 98, 56, 255);
        private static readonly Color32 ThumbEdge = new Color32(198, 178, 120, 255);
        private static readonly Color32 ArrowGold = new Color32(206, 196, 120, 255);
        private static readonly Color32 ArrowDim = new Color32(110, 104, 80, 255);

        private int _left, _top, _width, _height;
        private int _order;
        // 滚轮命中区：默认就是滚动条列；列表框可以把整格矩形设进来，指针落在条目区也能滚。
        private int _hitLeft, _hitTop, _hitWidth, _hitHeight;

        private GameObject _upArrow;
        private GameObject _downArrow;
        private GameObject _thumb;

        /// <summary>总行数。</summary>
        public int Total { get; private set; }
        /// <summary>一屏可见行数。</summary>
        public int Visible { get; private set; }
        /// <summary>当前顶行号（CurrentTopIndex）。</summary>
        public int Top { get; private set; }

        /// <summary>顶行号变化回调。</summary>
        public Action<int> TopChanged;

        public static MenuListScroll Create(Transform parent, string name,
            int left, int top, int width, int height, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var scroll = go.AddComponent<MenuListScroll>();
            scroll._left = left;
            scroll._top = top;
            scroll._width = Mathf.Max(8, width);
            scroll._height = Mathf.Max(ArrowSize * 2 + ThumbMin, height);
            scroll._order = order;
            scroll._hitLeft = left;
            scroll._hitTop = top;
            scroll._hitWidth = scroll._width;
            scroll._hitHeight = scroll._height;
            scroll.Build();
            return scroll;
        }

        private void Build()
        {
            // 轨道（箭头之间的中段）
            int trackTop = _top + ArrowSize;
            int trackH = _height - ArrowSize * 2;
            MenuWidgets.Panel(transform, "Track", _left, trackTop, _width, trackH,
                MenuWidgets.FrameDim, TrackFill, _order);

            // 上箭头
            _upArrow = MenuSceneObjects.Quad(transform, "UpArrow", MenuWidgets.Arrow(),
                _left + 3, _top + 2, 9, 9, _order + 3, true);
            _upArrow.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            var up = _upArrow.AddComponent<ScrollArrow>();
            up.Configure(this, -1);

            // 下箭头
            _downArrow = MenuSceneObjects.Quad(transform, "DownArrow", MenuWidgets.Arrow(),
                _left + 3, _top + _height - ArrowSize + 2, 9, 9, _order + 3, true);
            var down = _downArrow.AddComponent<ScrollArrow>();
            down.Configure(this, +1);

            // 滑块
            _thumb = MenuSceneObjects.Quad(transform, "Thumb",
                MenuWidgets.Solid(ThumbFill),
                _left + 2, trackTop, _width - 4, ThumbMin, _order + 2, false);

            // 轨道拖拽区：点轨道或拖滑块都按比例换顶行
            var hit = MenuSceneObjects.Quad(transform, "TrackHit",
                MenuSceneObjects.TransparentTexture,
                _left, trackTop, _width, trackH, _order + 4, true);
            var drag = hit.AddComponent<MenuDragArea>();
            drag.Configure(_left, trackTop, _width, trackH, true);
            drag.OnValue = ratio =>
            {
                var max = MaxScroll;
                SetTop(Mathf.RoundToInt(max * Mathf.Clamp01(ratio)));
            };
        }

        private int MaxScroll { get { return Mathf.Max(0, Total - Visible); } }

        public void SetCount(int total, int visible)
        {
            Total = Mathf.Max(0, total);
            Visible = Mathf.Max(1, visible);
            Refresh();
        }

        /// <summary>扩大滚轮命中区（整格列表矩形），条目区上滚轮同样能滚。</summary>
        public void SetHitRect(int left, int top, int width, int height)
        {
            _hitLeft = left;
            _hitTop = top;
            _hitWidth = Mathf.Max(1, width);
            _hitHeight = Mathf.Max(1, height);
        }

        public void SetTop(int top)
        {
            var clamped = Mathf.Clamp(top, 0, MaxScroll);
            if (clamped == Top) return;
            Top = clamped;
            Refresh();
            if (TopChanged != null) TopChanged(Top);
        }

        public void ScrollBy(int delta)
        {
            SetTop(Top + delta);
        }

        private void Refresh()
        {
            int trackTop = _top + ArrowSize;
            int trackH = _height - ArrowSize * 2;

            // 滑块高度按可见比例；内容一屏放得下时整条轨道都交给滑块。
            int thumbH = Total <= Visible
                ? trackH
                : Mathf.Max(ThumbMin, Mathf.RoundToInt(trackH * (float)Visible / Total));
            int travel = Mathf.Max(0, trackH - thumbH);
            int thumbTop = MaxScroll <= 0
                ? trackTop
                : trackTop + Mathf.RoundToInt(travel * (Top / (float)MaxScroll));

            if (_thumb != null)
            {
                _thumb.transform.localPosition = new Vector3(
                    _left + _width * 0.5f,
                    MenuSceneObjects.PlaneHeight - thumbTop - thumbH * 0.5f,
                    _thumb.transform.localPosition.z);
                var filter = _thumb.GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = MenuSceneObjects.CreateQuadMesh(_width - 4, thumbH);
                MenuSceneObjects.SetTexture(_thumb, MenuWidgets.Solid(ThumbFill));
            }

            // 箭头可用态：顶到最上 / 最下时变暗
            DimArrow(_upArrow, Top <= 0);
            DimArrow(_downArrow, Top >= MaxScroll);
        }

        private void DimArrow(GameObject arrow, bool dim)
        {
            if (arrow == null) return;
            var renderer = arrow.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                renderer.sharedMaterial.SetColor("_Color", dim ? (Color)ArrowDim : (Color)ArrowGold);
        }

        private void Update()
        {
            var delta = UnityEngine.Input.mouseScrollDelta.y;
            if (Mathf.Abs(delta) < 0.01f) return;
            if (!PointerInside()) return;
            ScrollBy(delta > 0f ? -1 : 1);
        }

        private bool PointerInside()
        {
            var camera = Camera.main;
            if (camera == null) return false;

            var ray = camera.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (Mathf.Abs(ray.direction.z) < 1e-5f) return false;
            var t = -ray.origin.z / ray.direction.z;
            if (t < 0f) return false;

            var world = ray.origin + ray.direction * t;
            var local = transform.parent != null
                ? transform.parent.InverseTransformPoint(world)
                : transform.InverseTransformPoint(world);
            var x = local.x;
            var yTop = MenuSceneObjects.PlaneHeight - local.y;

            return x >= _hitLeft && x <= _hitLeft + _hitWidth
                   && yTop >= _hitTop && yTop <= _hitTop + _hitHeight;
        }

        /// <summary>滚动条箭头点击。</summary>
        public sealed class ScrollArrow : MonoBehaviour, IPointerClickHandler
        {
            private MenuListScroll _scroll;
            private int _delta;

            public void Configure(MenuListScroll scroll, int delta)
            {
                _scroll = scroll;
                _delta = delta;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (_scroll != null) _scroll.ScrollBy(_delta);
            }
        }
    }
}
