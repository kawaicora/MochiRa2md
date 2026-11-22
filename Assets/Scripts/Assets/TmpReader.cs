// TmpReader.cs —— RA2/TS 等距地块 TMP 解码，逐字段对照 OpenRA TmpTSLoader.cs：
//
//   模板头 16B：u32 gridW / u32 gridH / i32 tileW / i32 tileH
//   偏移表：u32 offsets[gridW*gridH]（每个子图在文件里的绝对偏移，0 = 空）
//   子图头（52B 中前 40B 有用，其余是 WW 垃圾字节）：
//     +0  i32 x            （基图自身偏移，RA2 里恒为 0）
//     +4  i32 y
//     +8  u32 extraDataOffset
//     +12 u32 zDataOffset
//     +16 u32 extraZDataOffset
//     +20 i32 extraX       （extra 图形在原模板坐标系里的位置）
//     +24 i32 extraY
//     +28 u32 extraWidth
//     +32 u32 extraHeight
//     +36 u32 flags        （bit0 = 有 extra 图形：悬崖壁/悬崖顶/坡道都是这条）
//   然后跳过 12B，接像素数据。
//   像素数据是“每行从 4 列起、每行 ±4 列”的菱形行序列；先基图，再同结构的高度(z)图。
//   flags bit0 时，再读 extraHeight 行 × extraWidth 字节的 extra 图形（照样一段 z 图）。
//
// 以前只读基图、把整个模板的所有子图合到一张画布，两处都错：
//   · 悬崖/坡道的“壁面”全在 extra 段里 → 悬崖只剩一条平顶，其余是黑块；
//   · 2×2 水/海岸模板每个地图格只引用其中一个子图（IsoMapPack5 的 SubTileIndex），
//     合并整模板会把四个子图一起画到同一个格子 → 海岸糊成一片。
using System;
using System.Collections.Generic;

namespace Ra2md.Assets
{
    /// <summary>一个子图（对应模板中的一个格）。OffsetX/OffsetY 是相对“本格格子左上角”的偏移。</summary>
    public sealed class TmpSubImage
    {
        public int Width;
        public int Height;
        public byte[] Pixels;     // 索引色，0 = 透明
        public int OffsetX;       // 含 extra 图形后的包围盒左边界（相对格子左上角，可为负）
        public int OffsetY;       // 同上，可为负（悬崖壁向上伸）
        public byte Height0;      // 子图自带的 elevation（仅供参考，渲染用地图 Level）
    }

    public sealed class TmpFile
    {
        public int CellsX;
        public int CellsY;
        public int TileWidth = 60;
        public int TileHeight = 30;
        public readonly List<TmpSubImage> Subtiles = new List<TmpSubImage>();
        public string Error;

        public bool Valid => Error == null && Subtiles.Count > 0;
        public int Count => Subtiles.Count;

        public TmpSubImage Get(int index)
        {
            if (index < 0 || index >= Subtiles.Count) return null;
            return Subtiles[index];
        }
    }

    public static class TmpReader
    {
        private const int TemplateHeaderBytes = 16;
        private const int SubImageHeaderBytes = 52;

        public static TmpFile Parse(byte[] data)
        {
            var file = new TmpFile();
            if (data == null || data.Length < TemplateHeaderBytes + 4)
            {
                file.Error = "TMP 数据不足";
                return file;
            }

            var gridW = (int)ReadUInt32(data, 0);
            var gridH = (int)ReadUInt32(data, 4);
            var tileW = ReadInt32(data, 8);
            var tileH = ReadInt32(data, 12);

            if (gridW <= 0 || gridH <= 0 || gridW > 64 || gridH > 64
                || tileW <= 0 || tileH <= 0 || tileW > 512 || tileH > 512)
            {
                file.Error = "TMP 头不是模板结构";
                return file;
            }

            file.CellsX = gridW;
            file.CellsY = gridH;
            file.TileWidth = tileW;
            file.TileHeight = tileH;

            var count = gridW * gridH;
            var tableEnd = TemplateHeaderBytes + count * 4;
            if (tableEnd > data.Length)
            {
                file.Error = "TMP 偏移表越过文件";
                return file;
            }

            for (var i = 0; i < count; i++)
            {
                var offset = (int)ReadUInt32(data, TemplateHeaderBytes + i * 4);
                if (offset == 0)
                {
                    file.Subtiles.Add(null);
                    continue;
                }
                if (offset < tableEnd || offset + SubImageHeaderBytes > data.Length)
                {
                    file.Error = $"TMP 子图偏移越界 [{i}] = {offset}";
                    return file;
                }

                // 口径对照参考 tmp_decode.rs：基图菱形恒占 [0,tileW)×[0,tileH)；
                // extra 图形相对 stored 原点偏移 = rawExtra - stored。包围盒取基图矩形 ∪ extra 矩形，
                // 不再按模板格原点折算 stored_x/stored_y（老算法对非零 stored 坐标会把锚点整体推偏，
                // 正是悬崖/水岸边缘错位三角的来源）。
                var subX = ReadInt32(data, offset);
                var subY = ReadInt32(data, offset + 4);
                var extraX = ReadInt32(data, offset + 20) - subX;
                var extraY = ReadInt32(data, offset + 24) - subY;
                var extraW = (int)ReadUInt32(data, offset + 28);
                var extraH = (int)ReadUInt32(data, offset + 32);
                var flags = ReadUInt32(data, offset + 36);
                var hasExtra = (flags & 0x01) != 0 && extraW > 0 && extraH > 0;

                // 包围盒 = 基图格矩形 ∪ extra 矩形
                var minX = hasExtra ? Math.Min(0, extraX) : 0;
                var minY = hasExtra ? Math.Min(0, extraY) : 0;
                var maxX = hasExtra ? Math.Max(tileW, extraX + extraW) : tileW;
                var maxY = hasExtra ? Math.Max(tileH, extraY + extraH) : tileH;

                var boundsW = maxX - minX;
                var boundsH = maxY - minY;
                if (boundsW <= 0 || boundsH <= 0 || boundsW > 2048 || boundsH > 2048)
                {
                    file.Error = $"TMP 子图包围盒非法 [{i}] = {boundsW}×{boundsH}";
                    return file;
                }

                var cursor = offset + SubImageHeaderBytes;
                var pixels = new byte[boundsW * boundsH];
                var hasZ = (flags & 0x02) != 0;
                var extraZOffset = ReadUInt32(data, offset + 16);

                // 基图：菱形行序列，按格子矩形在包围盒里的位置落进去
                if (!UnpackDiamond(data, ref cursor, pixels, boundsW, boundsH, tileW, tileH, -minX, -minY))
                {
                    file.Error = $"TMP 子图 [{i}] 基图数据不足";
                    return file;
                }

                // 高度(z)图：仅 flags bit1 存在（TmpImage 构造同此判定），跳过
                if (hasZ && !SkipDiamond(data, ref cursor, tileW, tileH))
                {
                    file.Error = $"TMP 子图 [{i}] z 数据不足";
                    return file;
                }

                if (hasExtra)
                {
                    // extra 图形（悬崖壁等）：直接是矩形块，0 = 透明
                    for (var y = 0; y < extraH; y++)
                    {
                        var dstRow = (extraY - minY + y) * boundsW + (extraX - minX);
                        if (cursor + extraW > data.Length)
                        {
                            file.Error = $"TMP 子图 [{i}] extra 数据不足";
                            return file;
                        }
                        Array.Copy(data, cursor, pixels, dstRow, extraW);
                        cursor += extraW;
                    }

                    // extra z 图：仅同时存在 z 且 extraZDataOffset>0 时才有
                    if (hasZ && extraZOffset > 0)
                    {
                        var extraZ = extraW * extraH;
                        if (cursor + extraZ > data.Length)
                        {
                            file.Error = $"TMP 子图 [{i}] extra z 数据不足";
                            return file;
                        }
                        cursor += extraZ;
                    }
                }

                file.Subtiles.Add(new TmpSubImage
                {
                    Width = boundsW,
                    Height = boundsH,
                    Pixels = pixels,
                    OffsetX = minX,
                    OffsetY = minY,
                    Height0 = data[offset + 40],
                });
            }

            if (file.Subtiles.Count == 0)
            {
                file.Error = "TMP 没有可解码的子图";
                return file;
            }
            return file;
        }

        /// <summary>菱形行走：行宽从 4 开始，前半每行 +4，后半每行 -4。</summary>
        private static bool UnpackDiamond(byte[] data, ref int cursor, byte[] dest, int destW, int destH,
            int imgW, int imgH, int baseX, int baseY)
        {
            var rowWidth = 4;
            for (var y = 0; y < imgH; y++)
            {
                var startX = (imgW - rowWidth) / 2;
                if (cursor + rowWidth > data.Length) return false;
                var dstY = y + baseY;
                if (dstY >= 0 && dstY < destH)
                {
                    var dstX = startX + baseX;
                    if (dstX >= 0 && dstX + rowWidth <= destW)
                        Array.Copy(data, cursor, dest, dstY * destW + dstX, rowWidth);
                }
                cursor += rowWidth;
                rowWidth += (y < imgH / 2 - 1 ? 4 : -4);
            }
            return true;
        }

        private static bool SkipDiamond(byte[] data, ref int cursor, int imgW, int imgH)
        {
            var rowWidth = 4;
            for (var y = 0; y < imgH; y++)
            {
                if (cursor + rowWidth > data.Length) return false;
                cursor += rowWidth;
                rowWidth += (y < imgH / 2 - 1 ? 4 : -4);
            }
            return true;
        }

        private static uint ReadUInt32(byte[] data, int offset)
            => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

        private static int ReadInt32(byte[] data, int offset) => (int)ReadUInt32(data, offset);
    }
}
