// MenuInteractive.cs —— 几何控件的鼠标拾取：按下/拖动把屏幕点换算回 800×600 平面坐标（左上原点）。
// 原版这些滑条、滚动条是引擎直接处理鼠标的，这里用不可见 quad + 拖拽组件还原同样的手感。
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ra2md.Menu
{
    /// <summary>一块矩形拖拽区：横向返回 0..1 的 X 比例，纵向返回 0..1 的 Y 比例（顶端为 1）。</summary>
    public sealed class MenuDragArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerClickHandler
    {
        public int AreaLeft;
        public int AreaTop;
        public int AreaWidth = 1;
        public int AreaHeight = 1;
        public bool Vertical;

        /// <summary>值变化回调（0..1）。</summary>
        public Action<float> OnValue;

        /// <summary>单击回调（拖动也会先触发按下，这里只给不需要拖动的控件用）。</summary>
        public Action OnClicked;

        public void Configure(int left, int top, int width, int height, bool vertical)
        {
            AreaLeft = left;
            AreaTop = top;
            AreaWidth = Mathf.Max(1, width);
            AreaHeight = Mathf.Max(1, height);
            Vertical = vertical;
        }

        public void OnPointerDown(PointerEventData eventData) { Report(eventData); }

        public void OnDrag(PointerEventData eventData) { Report(eventData); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (OnClicked != null) OnClicked();
        }

        private void Report(PointerEventData eventData)
        {
            if (OnValue == null) return;

            var camera = eventData.pressEventCamera ?? eventData.enterEventCamera ?? Camera.main;
            if (camera == null) return;

            var parent = transform.parent != null ? transform.parent : transform;
            var ray = camera.ScreenPointToRay(eventData.position);
            var planeZ = parent.position.z;
            if (Mathf.Abs(ray.direction.z) < 1e-5f) return;
            var t = (planeZ - ray.origin.z) / ray.direction.z;
            if (t < 0f) return;

            var world = ray.origin + ray.direction * t;
            var local = parent.InverseTransformPoint(world);

            var px = (local.x - AreaLeft) / AreaWidth;
            var py = ((MenuSceneObjects.PlaneHeight - local.y) - AreaTop) / AreaHeight;
            OnValue(Mathf.Clamp01(Vertical ? py : px));
        }
    }
}
