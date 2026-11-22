// UiWidget.cs —— UIManager 控件工具箱的句柄基类。
//
// 口径：菜单绘制 = 给游戏对象挂组件；每个控件由 UIManager 工厂创建，返回句柄供绑定事件/取组件。
// 坐标系与既有菜单一致：800×600 逻辑平面，左上原点、y 向下；锚点固定左上。
using System;
using UnityEngine;

namespace Ra2md.Managers.Ui
{
    public abstract class UiWidget
    {
        public GameObject GameObject { get; }
        public RectTransform Rect { get; }

        public bool Visible
        {
            get => GameObject != null && GameObject.activeSelf;
            set { if (GameObject != null) GameObject.SetActive(value); }
        }

        protected UiWidget(GameObject go)
        {
            GameObject = go;
            Rect = go != null ? go.GetComponent<RectTransform>() : null;
        }

        /// <summary>在父节点下挂一个左上锚定的矩形。</summary>
        public static RectTransform AttachRect(GameObject go, Transform parent, float x, float y, float w, float h)
        {
            go.transform.SetParent(parent != null ? parent : null, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public virtual void Dispose()
        {
            if (GameObject != null) UnityEngine.Object.Destroy(GameObject);
        }
    }
}
