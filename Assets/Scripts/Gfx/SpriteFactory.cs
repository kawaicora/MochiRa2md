// SpriteFactory.cs —— 索引色像素 + PAL → Unity Sprite 的唯一出口。
//
// 行序约定只在这里出现一次：解码器给的 Pixels 第 0 行是图像最上一行，
// 而 Texture2D.SetPixels32 的第 0 行在图像**底部**，所以这里统一翻转。
// 任何新图层（单位、动画、HUD 图标）都必须调这里，不许再自己拼 Color32[]。
using UnityEngine;

namespace Ra2md.Gfx
{
    public static class SpriteFactory
    {
        /// <summary>
        /// 生成 Sprite。pivot=(0.5,0) 即画布水平中心 + 画布底边；
        /// 落位由 IsoProjection 换算，不在这里做。
        /// </summary>
        public static Sprite Make(byte[] indexed, int width, int height, byte[] palette, string name = null)
        {
            if (indexed == null || width <= 0 || height <= 0) return null;
            var texture = MakeTexture(indexed, width, height, palette, name);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 1f,
                0, SpriteMeshType.FullRect);
        }

        public static Texture2D MakeTexture(byte[] indexed, int width, int height, byte[] palette, string name = null)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name ?? "indexed",
            };

            var colors = new Color32[width * height];
            var usePalette = palette != null && palette.Length >= 1024;
            for (var y = 0; y < height; y++)
            {
                var srcRow = y * width;
                var dstRow = (height - 1 - y) * width;     // ← 唯一的翻转点
                for (var x = 0; x < width; x++)
                {
                    var i = srcRow + x;
                    if (i >= indexed.Length) continue;
                    var index = indexed[i];
                    if (index == 0) { colors[dstRow + x] = new Color32(0, 0, 0, 0); continue; }
                    if (!usePalette) { colors[dstRow + x] = new Color32(index, index, index, 255); continue; }
                    var p = index * 4;
                    colors[dstRow + x] = new Color32(palette[p], palette[p + 1], palette[p + 2], 255);
                }
            }

            texture.SetPixels32(colors);
            texture.Apply(false);
            return texture;
        }
    }
}
