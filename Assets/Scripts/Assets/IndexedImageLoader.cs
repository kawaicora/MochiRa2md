// IndexedImageLoader.cs —— 唯一的“名字 → 索引色像素”入口（地形 tile / 树 / overlay / 建筑 全走这里）。
//
// 为什么集中：RA2/YR 的 .sno/.tem/.urb 文件既可能是 TMP，也可能是 SHP（TREE15.sno、TIBTRE01.sno、
// GAWALL 之类），而 .shp 永远是 SHP。以前 TerrainRenderer 和 MapObjectLayer 各写了一套查找与解码，
// 一处改对另一处没改 —— 上下颠倒、尺寸不对、调色板不对都由此而来。现在只有这一个解码器。
//
// TMP 的“子图”语义（IsoMapPack5 每格带 SubTileIndex）：
//   一个 TMP 可以是 2×2、3×3 模板，地图每一格引用同一个 TileIndex + 不同的 SubTileIndex，
//   取的就是模板里的那一张子图。以前把整个模板合到一张画布再逐格贴，等于每格都贴了整块水/整座悬崖，
//   海岸线与悬崖因此糊成一片。现在 LoadTile(file, subTile) 只取该子图，并带回它相对格子的偏移。
using System;
using System.Collections.Generic;
using Ra2md.Menu;

namespace Ra2md.Assets
{
    /// <summary>解码后的索引色图像（行序：第 0 行 = 图像最上一行；0 = 透明）。</summary>
    public sealed class IndexedImage
    {
        public int Width;
        public int Height;
        public byte[] Pixels;
        public bool IsSprite;          // 来自 SHP（画布中心锚点）；false = TMP（格子左上角锚点 + Offset）
        public string Origin;          // 命中来源，便于 bootlog 取证
        public string Error;
        public bool Valid => Error == null && Pixels != null && Width > 0 && Height > 0;

        /// <summary>TMP 子图相对“格子左上角”的偏移（含 extra 图形；可为负，悬崖壁即向上伸）。</summary>
        public int OffsetX;
        public int OffsetY;

        /// <summary>TMP 模板的格宽/格高与子图总数（供地形层判断 tile 跨格）。</summary>
        public int CellWidth;
        public int CellHeight;
        public int SubTileCount;

        /// <summary>脚点锚点规则（唯一口径）：TMP 用格子上顶点，SHP 用画布中心。</summary>
        public bool TopAnchored => !IsSprite;
    }

    public static class IndexedImageLoader
    {
        private static readonly Dictionary<string, IndexedImage> Cache =
            new Dictionary<string, IndexedImage>(StringComparer.OrdinalIgnoreCase);

        public static void Clear() => Cache.Clear();

        /// <summary>按完整文件名取 SHP 帧 / TMP 的某个子图（frame 即 subTile）。</summary>
        public static IndexedImage LoadFile(string fileName, int frame)
            => LoadInternal("f:" + fileName, fileName, frame, false, true);

        /// <summary>
        /// 地形专用：按 (tile 文件名, 子图索引) 取图。这是地形层唯一的取图入口，
        /// 子图索引就是 IsoMapPack5 的 SubTileIndex。
        /// </summary>
        public static IndexedImage LoadTile(string fileName, int subTile)
            => LoadInternal("t:" + fileName, fileName, subTile, false, true);

        /// <summary>
        /// 覆盖物专用：art 段写 Theater=yes 的（栅栏、桥片段这类）真实图在 theater 的 tile 里，
        /// 先按 theater 后缀取 TMP 子图，取不到才回落 SHP。以前 SHP 优先会命中同名占位资产
        /// （红底白字 replace fence），画出来就是一排占位小牌。
        /// </summary>
        public static IndexedImage LoadOverlay(string imageName, int frame, string theaterExtension)
        {
            if (string.IsNullOrEmpty(imageName)) return Error("名字为空");
            var key = "v:" + imageName + "#" + frame + (theaterExtension ?? string.Empty);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var ext = string.IsNullOrEmpty(theaterExtension) ? ".tem" : theaterExtension;
            IndexedImage image = null;
            if (!imageName.EndsWith(".shp", StringComparison.OrdinalIgnoreCase)
                && !imageName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                image = DecodeFile(imageName + ext, frame, true);
            }
            if (image == null || !image.Valid)
                image = DecodeFile(imageName + ".shp", frame, true);
            if (image == null || !image.Valid)
                image = DecodeFile(imageName, frame, true);

            Cache[key] = image ?? Error("贴图缺失 " + imageName);
            return Cache[key];
        }

        /// <summary>按资源名取图（对象层用）：SHP → 原名 + theater 后缀（TMP/SHP 自适应）。</summary>
        public static IndexedImage LoadObject(string imageName, int frame, string theaterExtension)
        {
            if (string.IsNullOrEmpty(imageName)) return Error("名字为空");
            var key = "o:" + imageName + "#" + frame + (theaterExtension ?? string.Empty);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            IndexedImage image = null;
            if (!imageName.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
            {
                image = DecodeFile(imageName + ".shp", frame, true);
            }
            if (image == null || !image.Valid)
            {
                var ext = string.IsNullOrEmpty(theaterExtension) ? ".tem" : theaterExtension;
                image = DecodeFile(imageName + ext, frame, true);
            }
            if (image == null || !image.Valid)
            {
                image = DecodeFile(imageName, frame, true);   // 名字本身已带后缀
            }

            Cache[key] = image ?? Error("贴图缺失 " + imageName);
            return Cache[key];
        }

        private static IndexedImage LoadInternal(string keyPrefix, string fileName, int subTile, bool quiet, bool cache)
        {
            if (string.IsNullOrEmpty(fileName)) return Error("名字为空");
            var key = keyPrefix + "#" + subTile;
            if (cache && Cache.TryGetValue(key, out var cached)) return cached;

            var image = DecodeFile(fileName, subTile, quiet);
            if (cache) Cache[key] = image;
            return image;
        }

        private static IndexedImage Error(string message) => new IndexedImage { Error = message };

        private static IndexedImage DecodeFile(string fileName, int frame, bool quiet = false)
        {
            IndexedImage image;
            byte[] bytes;
            string origin;
            if (!MenuResourceLibrary.TryLoadFirst(new[] { fileName, fileName.ToUpperInvariant() }, out bytes, out origin)
                || bytes == null)
            {
                if (!quiet) Core.BootLog.Log("[贴图] 资源缺失 " + fileName);
                return Error("资源缺失 " + fileName);
            }

            // 1) SHP 优先判定：头是 0/0 + 画布宽高 + 帧数，TMP 头对不上
            if (ShpReader.TryParse(bytes, out var shp, out _))
            {
                var index = frame;
                if (index < 0 || index >= shp.FrameCount) index = 0;
                if (shp.TryDecodeCanvas(index, out var pixels, out var w, out var h, out var err))
                {
                    return new IndexedImage
                    {
                        Width = w, Height = h, Pixels = pixels, IsSprite = true,
                        Origin = origin + "#" + index,
                    };
                }
                image = Error("SHP 帧解码失败 " + fileName + "#" + index + "：" + err);
                return image;
            }

            // 2) TMP：只取指定的子图，绝不合并模板
            var tmp = TmpReader.Parse(bytes);
            if (!tmp.Valid)
            {
                image = Error("TMP/SHP 均失败 " + fileName + "：" + tmp.Error);
                if (!quiet) Core.BootLog.Log("[贴图] " + image.Error);
                return image;
            }

            var index2 = frame;
            if (index2 < 0 || index2 >= tmp.Count || tmp.Get(index2) == null)
            {
                // SubTileIndex 越界（编辑器里会报 InvalidSubTileIndex 并归 0）→ 退第一个有效子图
                index2 = 0;
                while (index2 < tmp.Count && tmp.Get(index2) == null) index2++;
            }
            var sub = tmp.Get(index2);
            if (sub == null) return Error("TMP 子图全为空 " + fileName);

            return new IndexedImage
            {
                Width = sub.Width,
                Height = sub.Height,
                Pixels = sub.Pixels,
                IsSprite = false,
                Origin = origin + "#" + index2,
                OffsetX = sub.OffsetX,
                OffsetY = sub.OffsetY,
                CellWidth = tmp.TileWidth,
                CellHeight = tmp.TileHeight,
                SubTileCount = tmp.Count,
            };
        }
    }
}
