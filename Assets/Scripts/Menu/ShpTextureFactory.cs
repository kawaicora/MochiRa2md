// ShpTextureFactory.cs —— SHP 索引帧 + PAL 调色板 → Unity Texture2D。
// 全部按原版点采样（FilterMode.Point），不做任何平滑，保证像素级还原。
using System;
using UnityEngine;
using Ra2md.Assets;
namespace Ra2md.Menu
{
    public static class ShpTextureFactory
    {
        /// <summary>把 SHP 的第 frame 帧解成 RGBA32 纹理；空帧或解码失败返回 null。</summary>
        public static Texture2D FromFrame(ShpImage image, int frame, byte[] palette, string name,
            bool transparentZero = true)
        {
            if (image == null || palette == null) return null;
            if (frame < 0 || frame >= image.FrameCount) return null;

            if (!image.TryDecodeFrame(frame, out var pixels, out int width, out int height, out string error))
            {
                Debug.LogWarning("[SHP] 帧 " + frame + " 解码失败：" + error);
                return null;
            }

            if (width <= 0 || height <= 0 || pixels == null) return null;

            var canvas = new byte[width * height * 4];
            PalReader.BlitIndexed(pixels, width, height, palette, canvas, width, height, 0, 0, transparentZero);
            // SHP 第 0 行是顶部，Unity 纹理原点在左下：转正后再上传
            FlipVertical(canvas, width, height);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = string.IsNullOrEmpty(name) ? "SHP" : name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            texture.SetPixels32(ToColors(canvas));
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>把 SHP 画布级的整帧（按帧偏移摆到 canvasWidth×canvasHeight 上）解成纹理。</summary>
        public static Texture2D FromCanvasFrame(ShpImage image, int frame, byte[] palette, string name,
            bool transparentZero = true)
        {
            if (image == null || palette == null) return null;
            if (frame < 0 || frame >= image.FrameCount) return null;

            if (!image.TryDecodeFrame(frame, out var pixels, out int width, out int height, out string error))
            {
                Debug.LogWarning("[SHP] 帧 " + frame + " 解码失败：" + error);
                return null;
            }
            if (width <= 0 || height <= 0 || pixels == null) return null;

            int canvasWidth = Math.Max(1, image.CanvasWidth);
            int canvasHeight = Math.Max(1, image.CanvasHeight);
            var canvas = new byte[canvasWidth * canvasHeight * 4];
            int offsetX = image.Frames[frame].OffsetX;
            int offsetY = image.Frames[frame].OffsetY;
            PalReader.BlitIndexed(pixels, width, height, palette, canvas, canvasWidth, canvasHeight, offsetX, offsetY, transparentZero);
            FlipVertical(canvas, canvasWidth, canvasHeight);

            var texture = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false)
            {
                name = string.IsNullOrEmpty(name) ? "SHP" : name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            texture.SetPixels32(ToColors(canvas));
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>8 位索引 + 调色板 → 纯色块纹理（用于 PAL 预览）。</summary>
        public static Texture2D FromPalette(byte[] palette, int cellSize)
        {
            if (palette == null) return null;
            int cell = Math.Max(1, cellSize);
            const int columns = 16;
            const int rows = 16;
            int width = columns * cell;
            int height = rows * cell;
            var canvas = new byte[width * height * 4];

            for (int index = 0; index < 256; index++)
            {
                int cx = (index % columns) * cell;
                int cy = (index / columns) * cell;
                for (int y = 0; y < cell; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        int dst = ((cy + y) * width + cx + x) * 4;
                        int src = index * 4;
                        canvas[dst] = palette[src];
                        canvas[dst + 1] = palette[src + 1];
                        canvas[dst + 2] = palette[src + 2];
                        canvas[dst + 3] = 255;
                    }
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "PAL",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(ToColors(canvas));
            texture.Apply(false, false);
            return texture;
        }

        private static void FlipVertical(byte[] rgba, int width, int height)
        {
            var stride = width * 4;
            var tmp = new byte[stride];
            for (var y = 0; y < height / 2; y++)
            {
                var top = y * stride;
                var bottom = (height - 1 - y) * stride;
                Buffer.BlockCopy(rgba, top, tmp, 0, stride);
                Buffer.BlockCopy(rgba, bottom, rgba, top, stride);
                Buffer.BlockCopy(tmp, 0, rgba, bottom, stride);
            }
        }

        private static Color32[] ToColors(byte[] rgba)
        {
            int count = rgba.Length / 4;
            var colors = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                colors[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            }
            return colors;
        }
    }
}
