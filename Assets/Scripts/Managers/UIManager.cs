// UIManager.cs —— 统一 UI 管理器：所有界面控件的唯一构造入口。
//
// 职责边界：
//   · 这里建 Canvas / 面板 / 文本 / 图 / 小地图图 / 九宫格竖条；
//   · UI 自身按固定像素尺寸摆放，不随窗口拉伸（对话/侧栏在原版里就是 DLU 定位）；
//   · 世界画面与视频才按窗口等比适配，适配公式放 FitProportional，不反过来拉伸 UI；
//   · 设置参数不在这里存，归 GlobalConfigManager；页面路由归 MenuManager。
using System.Collections.Generic;
using Ra2md.Managers.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers
{
    public sealed class UIManager : MonoBehaviour
    {
        private static UIManager _instance;

        private Canvas _canvas;
        private RectTransform _root;
        private readonly List<RectTransform> _raycastTargets = new List<RectTransform>();

        public static UIManager Instance
        {
            get
            {
                if (_instance == null) Ensure();
                return _instance;
            }
        }

        public static UIManager Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Ra2md.UIManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UIManager>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            BuildRoot();
        }

        public Canvas RootCanvas => _canvas;
        public RectTransform Root => _root;

        private void BuildRoot()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            canvasGo.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("Root");
            rootGo.transform.SetParent(canvasGo.transform, false);
            _root = rootGo.AddComponent<RectTransform>();
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
        }

        /// <summary>内置字体（LegacyRuntime 优先，Arial 兜底）；各文本控件统一从这里取。</summary>
        public static Font DefaultFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        // ---- 基础控件工厂 ----

        public RectTransform CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, Color? background = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;

            if (background.HasValue)
            {
                var img = go.AddComponent<Image>();
                img.color = background.Value;
                img.raycastTarget = true;
            }
            RegisterRaycast(rt);
            return rt;
        }

        public Text CreateText(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 sizeDelta,
            TextAnchor anchor, int fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;

            var text = go.AddComponent<Text>();
            text.font = DefaultFont();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        public Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public RawImage CreateRawImage(RectTransform parent, string name, Texture texture)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var raw = go.AddComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;
            return raw;
        }

        // ---- 常用控件工具箱（句柄工厂：菜单绘制 = 挂组件 + 绑事件）----

        private static readonly Color ButtonNormalTint = new Color(0.32f, 0.32f, 0.38f, 1f);
        private static readonly Color TrackbarTint = new Color(0.12f, 0.12f, 0.15f, 1f);

        private Text MakeLabel(Transform parent, string text, TextAnchor anchor, int fontSize, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 0f);
            rt.offsetMax = new Vector2(-6f, 0f);
            var label = go.AddComponent<Text>();
            label.font = DefaultFont();
            label.fontSize = fontSize;
            label.alignment = anchor;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>静态文本控件。</summary>
        public UiLabel CreateLabel(RectTransform parent, string name, float x, float y, float w, float h,
            string text, TextAnchor anchor, int fontSize, Color color)
        {
            var go = new GameObject(name);
            UiWidget.AttachRect(go, parent, x, y, w, h);
            var label = MakeLabel(go.transform, text, anchor, fontSize, color);
            return new UiLabel(go, label);
        }

        /// <summary>静态图：sprite 走 Image；texture 走 RawImage（视频帧/纹理直贴）。</summary>
        public UiImageWidget CreateImageWidget(RectTransform parent, string name, float x, float y, float w, float h,
            Sprite sprite, Texture texture)
        {
            var go = new GameObject(name);
            UiWidget.AttachRect(go, parent, x, y, w, h);
            Image img = null;
            RawImage raw = null;
            if (sprite != null)
            {
                img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.color = Color.white;
            }
            else
            {
                raw = go.AddComponent<RawImage>();
                raw.texture = texture;
                raw.color = Color.white;
            }
            return new UiImageWidget(go, img, raw);
        }

        /// <summary>按钮：三态帧可空（空帧回退纯色），onClick 经句柄 BindOnClick / OnClick 绑定。</summary>
        public UiButton CreateButton(RectTransform parent, string name, float x, float y, float w, float h,
            string label, Sprite normal = null, Sprite hover = null, Sprite pressed = null, int fontSize = 13)
        {
            var go = new GameObject(name);
            UiWidget.AttachRect(go, parent, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.color = ButtonNormalTint;
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            Text text = null;
            if (!string.IsNullOrEmpty(label))
            {
                text = MakeLabel(go.transform, label, TextAnchor.MiddleCenter, fontSize, Color.white);
            }
            var handle = new UiButton(go, btn, img, text);
            handle.SetFrames(normal, hover, pressed);
            return handle;
        }

        /// <summary>滑动条：水平方向，min/max/初值；变更回调挂句柄 OnValueChanged。</summary>
        public UiTrackbar CreateTrackbar(RectTransform parent, string name, float x, float y, float w, float h,
            float min, float max, float value)
        {
            var go = new GameObject(name);
            UiWidget.AttachRect(go, parent, x, y, w, h);
            var bg = go.AddComponent<Image>();
            bg.color = TrackbarTint;
            bg.raycastTarget = true;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.5f);
            fillRt.anchorMax = new Vector2(1f, 0.5f);
            fillRt.sizeDelta = new Vector2(0f, h * 0.4f);
            var fill = fillGo.AddComponent<Image>();
            fill.color = new Color(0.55f, 0.55f, 0.62f, 1f);

            var slider = go.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.targetGraphic = bg;
            slider.fillRect = fillRt;
            slider.interactable = true;
            return new UiTrackbar(go, slider);
        }

        /// <summary>列表框：纵向行 + 垂直滚动；选中回调挂句柄 OnPick。</summary>
        public UiListBox CreateListBox(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var root = new GameObject(name);
            UiWidget.AttachRect(root, parent, x, y, w, h);
            var rootBg = root.AddComponent<Image>();
            rootBg.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(root.transform, false);
            var viewRt = viewportGo.GetComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = Vector2.zero;
            viewRt.offsetMax = new Vector2(-UiListBox.ScrollbarWidth, 0f);
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var viewImg = viewportGo.AddComponent<Image>();
            viewImg.color = new Color(0.1f, 0.1f, 0.12f, 1f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var scroll = root.AddComponent<ScrollRect>();
            scroll.viewport = viewRt;
            scroll.content = contentRt;
            scroll.vertical = true;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = UiListBox.RowHeight;

            return new UiListBox(root, scroll, contentRt);
        }

        /// <summary>下拉框：收起态面钮 + 点开列项；选中回调挂句柄 OnPick。</summary>
        public UiComboBox CreateComboBox(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var faceGo = new GameObject(name);
            UiWidget.AttachRect(faceGo, parent, x, y, w, h);
            var faceImg = faceGo.AddComponent<Image>();
            faceImg.color = new Color(0.22f, 0.22f, 0.28f, 1f);
            faceImg.raycastTarget = true;
            var faceBtn = faceGo.AddComponent<Button>();
            faceBtn.targetGraphic = faceImg;
            var faceLabel = MakeLabel(faceGo.transform, string.Empty, TextAnchor.MiddleLeft, 13, Color.white);

            var dropGo = new GameObject("Dropdown");
            UiWidget.AttachRect(dropGo, parent, x, y + h, w, 0f);
            var dropBg = dropGo.AddComponent<Image>();
            dropBg.color = new Color(0.12f, 0.12f, 0.15f, 0.98f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(dropGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 0f);

            return new UiComboBox(faceGo, faceBtn, faceLabel, dropGo.GetComponent<RectTransform>(), contentRt);
        }

        /// <summary>勾选框 / 单选钮：状态回调挂句柄 OnChanged。</summary>
        public UiTick CreateTick(RectTransform parent, string name, float x, float y, float w, float h,
            string label, bool isOn)
        {
            var go = new GameObject(name);
            UiWidget.AttachRect(go, parent, x, y, w, h);

            var boxGo = new GameObject("Box", typeof(RectTransform));
            boxGo.transform.SetParent(go.transform, false);
            var boxRt = boxGo.GetComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0f, 0.5f);
            boxRt.anchorMax = new Vector2(0f, 0.5f);
            boxRt.pivot = new Vector2(0f, 0.5f);
            boxRt.sizeDelta = new Vector2(h, h);
            var boxImg = boxGo.AddComponent<Image>();
            boxImg.color = new Color(0.2f, 0.2f, 0.24f, 1f);

            var checkGo = new GameObject("Check", typeof(RectTransform));
            checkGo.transform.SetParent(boxGo.transform, false);
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(3f, 3f);
            checkRt.offsetMax = new Vector2(-3f, -3f);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = new Color(0.85f, 0.85f, 0.9f, 1f);

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = isOn;

            var text = MakeLabel(go.transform, label, TextAnchor.MiddleLeft, 13, Color.white);
            text.rectTransform.offsetMin = new Vector2(h + 8f, 0f);
            return new UiTick(go, toggle, text);
        }

        // ---- 九宫格 / 瓦片条 ----

        /// <summary>
        /// 一条竖边框：顶部一段、底部一段是固定帽，中间小块纵向铺满（瓦片条口径）。
        /// topSprite/bottomSprite 为空时对应帽高度取 capHeight；middleSprite 为空只留两端帽。
        /// </summary>
        public RectTransform CreateVerticalStrip(RectTransform parent, string name,
            Sprite topSprite, Sprite middleSprite, Sprite bottomSprite,
            float width, float height, float capHeight)
        {
            var strip = CreatePanel(name, parent, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height), null);

            if (topSprite != null)
            {
                var top = CreateImage(strip, "Top", topSprite, Color.white).rectTransform;
                top.anchorMin = new Vector2(0f, 1f);
                top.anchorMax = new Vector2(1f, 1f);
                top.pivot = new Vector2(0.5f, 1f);
                top.sizeDelta = new Vector2(width, capHeight);
            }

            if (bottomSprite != null)
            {
                var bottom = CreateImage(strip, "Bottom", bottomSprite, Color.white).rectTransform;
                bottom.anchorMin = new Vector2(0f, 0f);
                bottom.anchorMax = new Vector2(1f, 0f);
                bottom.pivot = new Vector2(0.5f, 0f);
                bottom.sizeDelta = new Vector2(width, capHeight);
            }

            if (middleSprite != null)
            {
                var middle = CreateImage(strip, "Middle", middleSprite, Color.white);
                var rt = middle.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = new Vector2(0f, capHeight);
                rt.offsetMax = new Vector2(0f, -capHeight);
                middle.type = Image.Type.Sliced;
                middle.fillCenter = true;
            }

            return strip;
        }

        // ---- 等比适配（世界画面 / 视频用；UI 不走这里）----

        /// <summary>把一个纹理按原始宽高比塞进给定框，返回适配后的尺寸（居中由锚点决定）。</summary>
        public static Vector2 FitProportional(int texW, int texH, float boxW, float boxH)
        {
            if (texW <= 0 || texH <= 0 || boxW <= 0f || boxH <= 0f) return new Vector2(boxW, boxH);
            var scale = Mathf.Min(boxW / texW, boxH / texH);
            return new Vector2(texW * scale, texH * scale);
        }

        // ---- 拾取 ----

        public void RegisterRaycast(RectTransform rt)
        {
            if (rt != null && !_raycastTargets.Contains(rt)) _raycastTargets.Add(rt);
        }

        /// <summary>屏幕点是否落在任意已注册 UI 上；世界拾取前先问这里。</summary>
        public bool IsPointerOverUI(Vector2 screen)
        {
            for (var i = 0; i < _raycastTargets.Count; i++)
            {
                var rt = _raycastTargets[i];
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen)) return true;
            }
            return false;
        }

        // ---- 分辨率守卫 ----

        /// <summary>客户区小于 800×600：整块黑屏 + 居中提示，不再进菜单/战场。</summary>
        public void ShowResolutionTooSmall(int width, int height)
        {
            var guardGo = new GameObject("ResolutionGuard");
            guardGo.transform.SetParent(transform, false);
            var guardCanvas = guardGo.AddComponent<Canvas>();
            guardCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            guardCanvas.sortingOrder = 100;

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(guardGo.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bg = bgGo.AddComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = true;

            var textGo = new GameObject("Message");
            textGo.transform.SetParent(guardGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.5f, 0.5f);
            textRt.anchorMax = new Vector2(0.5f, 0.5f);
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.sizeDelta = new Vector2(700f, 60f);
            var text = textGo.AddComponent<Text>();
            text.font = DefaultFont();
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.95f, 0.9f, 0.7f, 1f);
            text.raycastTarget = false;
            text.text = $"分辨率过小（当前 {width}×{height}，需要 ≥ 800×600），无法显示。";
        }
    }
}
