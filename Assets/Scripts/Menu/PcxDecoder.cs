// PcxDecoder.cs —— 8 位 PCX → Unity Texture2D。
// 原版 shell 的控件贴图（ra2.mix\local.mix\arrow_dd.pcx 等）是 8bpp PCX（RLE 压缩）。
// 取色用 PCX 尾部自带的 0x0C+768 字节调色板；显式传入的 dialog.pal 仅作兜底（6 位分量需 ×4）。
using System;
using UnityEngine;
using Ra2md.Assets;

namespace Ra2md.Menu
{
    public static class PcxDecoder
    {
        public static Texture2D ToTexture(byte[] pcx, byte[] palette, out byte[] usedPalette)
        {
            usedPalette = null;
            if (pcx == null || pcx.Length < 128) return null;
            if (pcx[0] != 0x0A) return null;

            int bpp = pcx[3];
            int xmin = BitConverter.ToUInt16(pcx, 4);
            int ymin = BitConverter.ToUInt16(pcx, 6);
            int xmax = BitConverter.ToUInt16(pcx, 8);
            int ymax = BitConverter.ToUInt16(pcx, 10);
            int width = xmax - xmin + 1;
            int height = ymax - ymin + 1;
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

            int planes = pcx[65];
            int bytesPerLine = BitConverter.ToUInt16(pcx, 66);

            if (bpp != 8) return null;

            // 尾部调色板（0x0C marker）
            byte[] tailPal = null;
            for (var i = pcx.Length - 769; i >= 128 && i >= pcx.Length - 1024; i--)
            {
                if (pcx[i] == 0x0C)
                {
                    tailPal = new byte[768];
                    Buffer.BlockCopy(pcx, i + 1, tailPal, 0, 768);
                    break;
                }
            }
            // 调色板：local.mix 里的 PCX 尾部自带 0x0C+768 字节调色板，实测它就是原版取色来源
            // （trofm 的索引 198 → #5B0000，与实机截图数值格底色 #5A0000 一致）；
            // 显式传入的 dialog.pal 只作兜底，且它是 6 位分量，需要 ×4。
            var pal = tailPal;
            if (pal == null && palette != null && palette.Length >= 768)
            {
                var needScale = true;
                for (var i = 0; i < 768; i++)
                {
                    if (palette[i] > 63) { needScale = false; break; }
                }
                var scaled = new byte[768];
                for (var i = 0; i < 768; i++)
                    scaled[i] = needScale ? (byte)Math.Min(255, palette[i] * 4) : palette[i];
                pal = scaled;
            }
            if (pal == null) return null;
            usedPalette = pal;

            // RLE 解压
            var raw = new byte[bytesPerLine * height * planes];
            int src = 128;
            int dst = 0;
            int lineTotal = bytesPerLine * planes;
            while (dst < raw.Length && src < pcx.Length)
            {
                var b = pcx[src++];
                if ((b & 0xC0) == 0xC0)
                {
                    int count = b & 0x3F;
                    if (src >= pcx.Length) break;
                    var value = pcx[src++];
                    for (var i = 0; i < count && dst < raw.Length; i++) raw[dst++] = value;
                }
                else raw[dst++] = b;
                if (dst % lineTotal == 0) { /* 行对齐，PCX 每行独立压缩 */ }
            }

            // 索引缓冲（单 plane / 多 plane 合并）
            var indexed = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                var lineBase = y * lineTotal;
                for (var x = 0; x < width; x++)
                {
                    byte v = 0;
                    for (var p = 0; p < planes; p++)
                    {
                        int off = lineBase + p * bytesPerLine + x;
                        if (off < raw.Length)
                        {
                            v = raw[off];
                            break;
                        }
                    }
                    indexed[y * width + x] = v;
                }
            }

            var rgba = new byte[width * height * 4];
            // PalReader.BlitIndexed 取的是 RGBA 调色板（index*4），这里把 PCX 的 RGB 768 字节扩成 1024，
            // 否则索引 ≥192 时会越界（arrow_dd.pcx 用到 229）。
            var palRgba = new byte[1024];
            for (var i = 0; i < 256; i++)
            {
                palRgba[i * 4] = pal[i * 3];
                palRgba[i * 4 + 1] = pal[i * 3 + 1];
                palRgba[i * 4 + 2] = pal[i * 3 + 2];
                palRgba[i * 4 + 3] = 255;
            }
            PalReader.BlitIndexed(indexed, width, height, palRgba, rgba, width, height, 0, 0, true);
            usedPalette = palRgba;

            // PCX 第 0 行在顶部，Unity 纹理原点左下：翻转
            var stride = width * 4;
            var tmp = new byte[stride];
            for (var y = 0; y < height / 2; y++)
            {
                int top = y * stride;
                int bottom = (height - 1 - y) * stride;
                Buffer.BlockCopy(rgba, top, tmp, 0, stride);
                Buffer.BlockCopy(rgba, bottom, rgba, top, stride);
                Buffer.BlockCopy(tmp, 0, rgba, bottom, stride);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "PCX",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            var colors = new Color32[width * height];
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            }
            texture.SetPixels32(colors);
            texture.Apply(false, false);
            return texture;
        }
    }
}
