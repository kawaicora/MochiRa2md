// PalReader.cs —— Westwood PAL 调色板（256 色）。
// 常见形态：768 字节原始 RGB（6 位分量，需 ×4 扩展）；部分带 4 字节 0x00 0x00 0x02 0x00 头。
// 若最大值超过 63 则按 8 位处理。
using System;

namespace Ra2md.Assets
{
    public static class PalReader
    {
        public static bool TryDecode(byte[] data, out byte[] rgba, out string error)
        {
            rgba = null;
            error = null;

            if (data == null || data.Length < 768)
            {
                error = "PAL 数据不足 768 字节";
                return false;
            }

            int start = 0;
            if (data.Length >= 772 && data[0] == 0 && data[1] == 0 && data[2] == 0x02 && data[3] == 0)
            {
                start = 4;
            }

            bool sixBit = true;
            for (int i = start; i < start + 768; i++)
            {
                if (data[i] > 63)
                {
                    sixBit = false;
                    break;
                }
            }

            rgba = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                int src = start + i * 3;
                int scale = sixBit ? 4 : 1;
                byte r = (byte)Math.Min(255, data[src] * scale);
                byte g = (byte)Math.Min(255, data[src + 1] * scale);
                byte b = (byte)Math.Min(255, data[src + 2] * scale);
                rgba[i * 4] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }

            // 索引 0 约定为透明
            rgba[3] = 0;
            return true;
        }

        /// <summary>把索引像素合成到 RGBA32 画布；画布按帧偏移放置。
        /// transparentZero=true 时索引 0 作透明跳过；false 时索引 0 按 palette[0] 实色绘制
        /// （对齐参考 MenuImage.cpp:30：alpha = transparentZero && index==0 ? 0 : 255）。</summary>
        public static void BlitIndexed(byte[] indices, int width, int height, byte[] palette,
            byte[] canvas, int canvasWidth, int canvasHeight, int offsetX, int offsetY,
            bool transparentZero = true)
        {
            for (int y = 0; y < height; y++)
            {
                int canvasY = offsetY + y;
                if (canvasY < 0 || canvasY >= canvasHeight) continue;
                int srcRow = y * width;
                int dstRow = canvasY * canvasWidth;

                for (int x = 0; x < width; x++)
                {
                    int canvasX = offsetX + x;
                    if (canvasX < 0 || canvasX >= canvasWidth) continue;

                    byte index = indices[srcRow + x];
                    if (index == 0 && transparentZero) continue;   // 透明

                    int dst = (dstRow + canvasX) * 4;
                    int src = index * 4;
                    canvas[dst] = palette[src];
                    canvas[dst + 1] = palette[src + 1];
                    canvas[dst + 2] = palette[src + 2];
                    canvas[dst + 3] = 255;
                }
            }
        }
    }
}
