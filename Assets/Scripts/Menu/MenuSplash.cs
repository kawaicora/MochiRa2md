// MenuSplash.cs —— 进程启动信息画面（logo 影片之后、主菜单之前的那一屏）。
//
// 启动链条口径：
//   1) 挂载归档；2) 取 CSF 字符串表与字体；3) 上屏启动画面（调色板 + SHP 图，居中），
//      画面上叠 Loading / 版权 / 品牌三行文字；4) 首帧上屏后保留至少 6 秒，
//      规则/类型初始化在这段停留时间内完成；5) 之后才进主菜单外壳。
// 缺图降级：SHP 或调色板取不到时仍画黑底 + 文字，不阻断启动。
//
// 资源名按原版两种家族候选（尤复后缀档 / 原版无后缀档）依次取第一个命中；
// 640 宽窗口取小图，其余宽度取大图。
using UnityEngine;
using UnityEngine.UI;
using Ra2md.Assets;

namespace Ra2md.Menu
{
    public sealed class MenuSplash
    {
        /// <summary>首帧上屏后的最短停留时长（毫秒）；计时从第一次上屏起，不每帧重置。</summary>
        public const int MinimumVisibleMs = 6000;

        private const int PlaneW = 800;
        private const int PlaneH = 600;

        private readonly GameObject _root;
        private readonly float _startSeconds;

        /// <summary>停留时长已到，可以收起进主菜单。</summary>
        public bool Done => Time.unscaledTime - _startSeconds >= MinimumVisibleMs / 1000f;

        private MenuSplash(GameObject root)
        {
            _root = root;
            _startSeconds = Time.unscaledTime;
        }

        public static MenuSplash Show(Transform parent)
        {
            var go = new GameObject("MenuSplash");
            go.transform.SetParent(parent, false);

            // 黑底：缺图时画面不空成纯白。
            var black = MenuResourceLibrary.CreateSolidTexture(Color.black);
            MenuSceneObjects.Quad(go.transform, "Backdrop", black, 0, 0, PlaneW, PlaneH, 5, false);

            var art = LoadArt();
            if (art != null)
            {
                int x = (PlaneW - art.width) / 2;
                int y = (PlaneH - art.height) / 2;
                MenuSceneObjects.Quad(go.transform, "Art", art, x, y, art.width, art.height, 6, false);
            }

            var canvas = MenuSceneObjects.AttachLabelCanvas(go, PlaneW, PlaneH, 50);

            // Loading… 左上；版权与品牌右下，距右边 10px。
            AddText(canvas, MenuStrings.Get("GUI:LoadingEx", "Loading..."),
                10, 10, 320, 16, TextAnchor.MiddleLeft, Color.white);

            var copyright = MenuStrings.Get("TXT_COPYRIGHT", "© 2000, 2001 ELECTRONIC ARTS INC. ALL RIGHTS RESERVED");
            var brand = MenuStrings.Get("GUI:WWBrand", "WESTWOOD STUDIOS® IS AN ELECTRONIC ARTS® BRAND");
            const int row = 3 + 13;
            AddText(canvas, copyright, PlaneW - 10, PlaneH - 40, 380, 16, TextAnchor.MiddleRight, Color.white);
            AddText(canvas, brand, PlaneW - 10, PlaneH - 40 + row, 380, 16, TextAnchor.MiddleRight, Color.white);

            return new MenuSplash(go);
        }

        private static Text AddText(RectTransform canvas, string text, int x, int yTop, int w, int h,
            TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w, h);
            // 右对齐时矩形右端压在 x 上；左对齐时左端压在 x 上。
            rect.anchoredPosition = anchor == TextAnchor.MiddleRight
                ? new Vector2(x - w, PlaneH - yTop)
                : new Vector2(x, PlaneH - yTop);

            var label = go.AddComponent<Text>();
            label.text = text;
            label.font = MenuFonts.Chinese();
            label.fontSize = 13;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private static Texture2D LoadArt()
        {
            var palBytes = TryFirst(new[] { "GLSMD.PAL", "GLS.PAL" });
            var shpNames = Screen.width == 640
                ? new[] { "GLSSMD.SHP", "GLSS.SHP" }
                : new[] { "GLSLMD.SHP", "GLSL.SHP" };
            var shpBytes = TryFirst(shpNames);
            if (shpBytes == null)
            {
                Debug.LogWarning("[启动] 找不到启动画面 SHP，降级黑底 + 文字");
                return null;
            }

            if (!ShpReader.TryParse(shpBytes, out var image, out _))
            {
                Debug.LogWarning("[启动] 启动画面 SHP 解析失败，降级黑底 + 文字");
                return null;
            }

            byte[] palette;
            if (palBytes == null || !PalReader.TryDecode(palBytes, out palette, out _))
            {
                palette = MenuResourceLibrary.BuildGrayscalePalette();
            }

            return ShpTextureFactory.FromFrame(image, 0, palette, "启动画面", true);
        }

        private static byte[] TryFirst(string[] names)
        {
            foreach (var name in names)
            {
                var bytes = MenuResourceLibrary.LoadRawByName(name, out _);
                if (bytes != null) return bytes;
            }
            return null;
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }
}
