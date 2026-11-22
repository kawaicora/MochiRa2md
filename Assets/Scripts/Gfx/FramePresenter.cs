// FramePresenter.cs —— Unity 侧的"呈现后端"：把 16bpp 后备面整块拷到 Texture2D 上屏。
// 契约（对应原版）：游戏画面只允许经 Surface16 一条路进出，不用 Sprite / Tilemap / UGUI 承载游戏内容；
// 这里的 Canvas + RawImage 只做"最后一层显示器"，不做任何缩放/插值（Point 过滤、1:1 像素）。
using Ra2md.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Gfx
{
    public sealed class FramePresenter : MonoBehaviour
    {
        private static FramePresenter _instance;
        private Texture2D _texture;
        private Color32[] _pixels;
        private Surface16 _surface;
        private RawImage _image;

        public Surface16 Surface => _surface;

        public static FramePresenter Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Ra2md.FramePresenter");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<FramePresenter>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            var canvasGo = new GameObject("Ra2md.Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var imageGo = new GameObject("Ra2md.Screen");
            imageGo.transform.SetParent(canvasGo.transform, false);
            _image = imageGo.AddComponent<RawImage>();
            _image.raycastTarget = false;
            var rt = _image.rectTransform;
            // 1:1 直显：不给画布做任何伸缩。锚点居中、尺寸 = 面尺寸（面尺寸就是窗口客户区），
            // 每个像素对屏幕一个像素；窗口变了由 RenderResolution 重建面后调用 SetPixelSize 同步。
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>等价 sub_4A42F0(hWnd,w,h,bpp)：建后备面 + 建呈现目标。</summary>
        public bool CreateSurface(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            _surface = new Surface16(width, height);

            if (_texture == null || _texture.width != width || _texture.height != height)
            {
                if (_texture != null) Destroy(_texture);
                _texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                _texture.filterMode = FilterMode.Point;
                _texture.wrapMode = TextureWrapMode.Clamp;
                _pixels = new Color32[width * height];
                _image.texture = _texture;
            }

            // 面尺寸变了就同步 RawImage 的像素尺寸：1:1 直显，不做缩放/插值。
            if (_image != null)
            {
                var rt = _image.rectTransform;
                rt.sizeDelta = new Vector2(width, height);
                rt.anchoredPosition = Vector2.zero;
            }

            BootLog.Log("[present] surface %dx%d created", width, height);
            return true;
        }

        /// <summary>整图画布接管游戏画面后，屏幕呈现层退成“空显示器”，不再参与遮挡。</summary>
        public void SetVisible(bool visible)
        {
            if (_image != null) _image.enabled = visible;
        }

        /// <summary>gfx 处一次性上屏（每帧最多一次；原版 55E160 内部可多次 Render，我们合并到这一次 Present）。</summary>
        public void Present()
        {
            if (_surface == null || _texture == null)
            {
                return;
            }

            var src = _surface.Pixels;
            var dst = _pixels;
            for (int i = 0; i < src.Length; i++)
            {
                ushort c = src[i];
                byte r = (byte)((c >> 11) & 0x1F);
                byte g = (byte)((c >> 5) & 0x3F);
                byte b = (byte)(c & 0x1F);
                dst[i].r = (byte)((r << 3) | (r >> 2));
                dst[i].g = (byte)((g << 2) | (g >> 4));
                dst[i].b = (byte)((b << 3) | (b >> 2));
                dst[i].a = 255;
            }

            _texture.SetPixels32(dst);
            _texture.Apply(false);
        }
    }
}
