// Surface16.cs —— 原版 16bpp 后备面（DSurface）的最小等价
// 原版约束（WinMain 1169-1178）：Primary 面的位深必须 == 2（16bpp），全部绘制/调色板展开都在 565 空间完成。
// 复刻策略：所有游戏绘制都进这里（uint16 565），最后由 FramePresenter 一次性拷到 Texture2D；
// 不使用 Unity 的 Sprite/UGUI/Mesh 承载游戏画面，否则像素级 1:1 无从谈起。
using System;

namespace Ra2md.Gfx
{
    public struct RectI
    {
        public int X, Y, W, H;
        public RectI(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
        public override string ToString() => $"({X},{Y},{W},{H})";
    }

    public sealed class Surface16
    {
        // WinMain 1205-1217 的三矩形（整屏 / 主视图 / 侧栏），单位是逻辑分辨率像素。
        public static RectI WindowBounds;
        public static RectI ViewBounds;
        public static RectI SidebarBounds;
        public static int ZBufferHeight;   // 480 - dword_886FA4

        public readonly ushort[] Pixels;   // RGB565
        public readonly int Width;
        public readonly int Height;
        public readonly int Pitch;         // = Width（原版 DSurface 也按 width 行宽）

        public Surface16(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("surface size");
            Width = width;
            Height = height;
            Pitch = width;
            Pixels = new ushort[width * height];
        }

        public void Clear(ushort color = 0)
        {
            var span = Pixels.AsSpan();
            span.Fill(color);
        }

        public void SetPixel(int x, int y, ushort color)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            Pixels[y * Pitch + x] = color;
        }

        public ushort GetPixel(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
            return Pixels[y * Pitch + x];
        }

        public void FillRect(int x, int y, int w, int h, ushort color)
        {
            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min(Width, x + w), y1 = Math.Min(Height, y + h);
            if (x1 <= x0 || y1 <= y0) return;

            for (int row = y0; row < y1; row++)
            {
                Pixels.AsSpan(row * Pitch + x0, x1 - x0).Fill(color);
            }
        }

        /// <summary>原版 GSurface 的 Blit：带裁剪的整行拷贝（不做缩放、不做 alpha）。</summary>
        public void Blit(Surface16 src, int dstX, int dstY)
        {
            if (src == null) return;

            int x0 = Math.Max(0, dstX), y0 = Math.Max(0, dstY);
            int x1 = Math.Min(Width, dstX + src.Width), y1 = Math.Min(Height, dstY + src.Height);
            if (x1 <= x0 || y1 <= y0) return;

            int srcOffsetX = x0 - dstX;
            int copyWidth = x1 - x0;

            for (int row = y0; row < y1; row++)
            {
                int srcOffsetY = row - dstY;
                var srcSpan = src.Pixels.AsSpan(srcOffsetY * src.Pitch + srcOffsetX, copyWidth);
                srcSpan.CopyTo(Pixels.AsSpan(row * Pitch + x0, copyWidth));
            }
        }

        /// <summary>调色板索引位图 → 565（等价原版 sub_6B9D00 mode=2 的逐像素索引→RGB 组装）。</summary>
        public void BlitIndexed(byte[] indices, int srcW, int srcH, byte[] paletteRgb, int dstX, int dstY,
            ushort transparentIndex = 0xFFFF)
        {
            if (indices == null || paletteRgb == null) return;

            for (int y = 0; y < srcH; y++)
            {
                int dy = dstY + y;
                if ((uint)dy >= (uint)Height) continue;
                for (int x = 0; x < srcW; x++)
                {
                    int dx = dstX + x;
                    if ((uint)dx >= (uint)Width) continue;
                    byte index = indices[y * srcW + x];
                    if (index == transparentIndex) continue;
                    Pixels[dy * Pitch + dx] = PaletteTo565(paletteRgb, index);
                }
            }
        }

        /// <summary>等价原版位移掩码 dword_8a0dd0/…/de4 的 565 组装（这里用标准 5-6-5）。</summary>
        public static ushort PaletteTo565(byte[] paletteRgb, int index)
        {
            int o = index * 3;
            ushort r = (ushort)(paletteRgb[o] & 0xF8);
            ushort g = (ushort)(paletteRgb[o + 1] & 0xFC);
            ushort b = (ushort)(paletteRgb[o + 2] & 0xF8);
            return (ushort)((r << 8) | (g << 3) | (b >> 3));
        }
    }
}
