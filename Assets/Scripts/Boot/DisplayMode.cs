// DisplayMode.cs —— WinMain 1080-1178 行的显示模式链 + 1169-1178 行的位深硬校验
// 顺序（不可颠倒）：
//   1) [Video] ScreenWidth / ScreenHeight（qword_A8EB84 的低/高 dword）→ BackBuffer
//   2) 失败 → 回退 640×480（0x280/0x1E0）再试一次
//   3) 再失败 → 文本 2159/2160 + exit(1)
//   4) 建 DSurface::Primary 后取 vtable+112 的位深，必须 == 2（16bpp），否则 文本 2245 "Invalid mode"
// Unity 侧：这里的"建面/位深"落到 Gfx.Surface16 与 Gfx.FramePresenter；失败判定保留。
using Ra2md.Core;
using Ra2md.Gfx;

namespace Ra2md.Boot
{
    public static class DisplayMode
    {
        public const int FallbackWidth = 640;   // 0x280
        public const int FallbackHeight = 480;  // 0x1E0

        public sealed class Result
        {
            public int Width;
            public int Height;
            public int BitDepth;
            public bool FullScreen;
            public bool Ok;
            public int TextId;      // 失败时的文本资源 ID（2159/2160/2245），0 = 无
        }

        /// <summary>读 [Video] 段并落地到 GameStatics + 创建后备面。</summary>
        public static Result Apply(CcIni ini, bool forceWindowed)
        {
            var r = new Result();

            int width = ini.GetInt("Video", "ScreenWidth", 800);
            int height = ini.GetInt("Video", "ScreenHeight", 600);
            GameStatics.VideoBackBuffer = ini.GetBool("Video", "BackBuffer", true);      // Game::bVideoBackBuffer
            GameStatics.AllowModeToggle = ini.GetBool("Video", "AllowModeToggle", false); // byte_A8ED63
            GameStatics.IsFullScreen = !forceWindowed;                                    // byte_89F978

            if (!TryCreate(width, height))
            {
                BootLog.Log("[boot] display mode %dx%d failed, retry %dx%d (WinMain 1133-1155)",
                    width, height, FallbackWidth, FallbackHeight);
                width = FallbackWidth;
                height = FallbackHeight;
                if (!TryCreate(width, height))
                {
                    r.TextId = 2160;   // aTxtVideoError/2159 + aTxtShortTitle/2160
                    BootLog.Log("[boot] video error (text 2160) -> exit(1)");
                    return r;
                }
            }

            // 位深：原版 Primary->vtable+112 必须返回 2（=16bpp）。
            // 复刻侧固定 16bpp RGB565 后备面，所以这里只在日志里确认契约。
            if (GameStatics.BitDepth != 16)
            {
                r.TextId = 2245;       // aTxtInvalidmode
                BootLog.Log("[boot] invalid mode (text 2245)");
                return r;
            }

            GameStatics.ScreenWidth = width;
            GameStatics.ScreenHeight = height;
            r.Width = width;
            r.Height = height;
            r.BitDepth = GameStatics.BitDepth;
            r.FullScreen = GameStatics.IsFullScreen;
            r.Ok = true;

            BootLog.Log("[boot] display %dx%d %dbpp backbuffer=%s fullscreen=%s",
                width, height, GameStatics.BitDepth, GameStatics.VideoBackBuffer, GameStatics.IsFullScreen);
            return r;
        }

        private static bool TryCreate(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            // 原版 sub_4A42F0(hWnd, w, h, bpp)：建窗口 + 建 Primary 面。
            bool ok = FramePresenter.Instance.CreateSurface(width, height);
            if (!ok)
            {
                return false;
            }

            // qword_886FB8 记录实际模式；视图矩形在这里首次成形（见 ApplyViewRects）。
            GameStatics.ScreenWidth = width;
            GameStatics.ScreenHeight = height;
            ApplyViewRects(width, height);
            return true;
        }

        /// <summary>WinMain 1205-1217：三矩形 + Z 面高度（480 - dword_886FA4）。</summary>
        public static void ApplyViewRects(int width, int height)
        {
            int sidebar = GameStatics.SidebarWidth;      // v221[2] = 168
            var full = new RectI(0, 0, width, height);                 // v221
            var view = new RectI(0, 0, width - sidebar, height);        // v220 = v219
            var sidebarRect = new RectI(0, 0, sidebar, height);         // 原版侧栏矩形

            Surface16.WindowBounds = full;
            Surface16.ViewBounds = view;
            Surface16.SidebarBounds = sidebarRect;

            int zbufferHeight = GameStatics.LogicalViewHeight - GameStatics.ViewReservedTop; // 480 - dword_886FA4
            Surface16.ZBufferHeight = zbufferHeight > 0 ? zbufferHeight : GameStatics.LogicalViewHeight;

            BootLog.Log("[boot] rects full=%s view=%s sidebar=%s zbuf=%d",
                full, view, sidebarRect, Surface16.ZBufferHeight);
        }
    }
}
