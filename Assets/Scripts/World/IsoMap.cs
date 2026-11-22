// IsoMap.cs —— [IsoMapPack5] 地形块解码（RA2/YR 的等距地图数据）。
//
// 容器：与 [PreviewPack] 同一套 Format5 —— 段内所有键值拼接 → base64 → (u16 comp, u16 uncomp, LZO1X)*n。
// 记录：每条 11 字节（原版 TIsometricTile）：
//     int16 X       —— 等距格 X（不是屏幕像素，是地图格坐标）
//     int16 Y       —— 等距格 Y
//     int32 TileIndex —— theater 全局 tile 序号（跨 tile set 累加；对照 WorldAlteringEditor
//                        TheaterTileData.ReadTileData 的 currentTileIndex++，不是“低16位号+高16位set”）
//     byte  SubTile —— 该格在这个模板里取第几个子图（2×2 水/海岸、2×3 悬崖都靠它）
//     byte  Height  —— 高度层级（0 平地，1.. 每级 +? 像素，供 TMP/地块绘制用）
//     byte  Terrain —— 地形类型（0 普通，1 水/岸等，按 theater 的 [Terrain] 解释）
//
// 本类只做“解到数据”：不解 theater ini、不贴图、不做 iso 投影。错误一律带回原因，不吞。
using System;
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    public sealed class IsoTileRecord
    {
        public short X;          // 记录里的原始 rx（IsoMapPack5 的“半格”坐标，与 [Structures]/[Waypoints] 同一空间）
        public short Y;          // 记录里的原始 ry
        public int CellX;        // 换算后的真格坐标（OpenRA ImportGen2MapCommand.ToMPos）
        public int CellY;
        public int TileIndex;
        public byte SubTile;
        public byte Height;
        public byte Terrain;

        public int TileSet => (TileIndex >> 16) & 0xFFFF;
        public int TileNumber => TileIndex & 0xFFFF;
    }

    public sealed class IsoMapData
    {
        public readonly List<IsoTileRecord> Tiles = new List<IsoTileRecord>();
        public int MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue;
        public int DuplicateSlots;      // 同一真格被两条记录覆盖的次数（IsoMapPack5 的半格重复）
        public int OutOfBounds;         // 落在 [Map] Size 之外的记录数
        public readonly List<string> OutOfBoundsSamples = new List<string>();
        public string Error;

        public int Width => MaxX >= MinX ? MaxX - MinX + 1 : 0;
        public int Height => MaxY >= MinY ? MaxY - MinY + 1 : 0;
        public bool Valid => Error == null && Tiles.Count > 0;

        private Dictionary<int, IsoTileRecord> _index;

        /// <summary>
        /// 该格的地形抬升（elevation）。对象层/单位层摆位必须调这里：
        /// 地面上有高台时，建筑/树要跟地形同一个 level，否则会浮空或陷进去。
        /// </summary>
        public int HeightAt(int x, int y)
        {
            if (_index == null) _index = IsoMap.IndexByCell(this);
            return _index.TryGetValue(IsoMap.CellKey(x, y), out var tile) ? tile.Height : 0;
        }

        public string Summary()
        {
            if (!Valid) return "无效(" + (Error ?? "空") + ")";
            return $"tiles={Tiles.Count} 范围=({MinX},{MinY})-({MaxX},{MaxY}) 尺寸={Width}×{Height}"
                + $" 去重={DuplicateSlots} 越界={OutOfBounds}";
        }
    }

    public static class IsoMap
    {
        public const int RecordSize = 11;

        /// <summary>解码地图的 [IsoMapPack5]（不依赖 theater，只给格数据）。</summary>
        public static IsoMapData Decode(MapIniDocument ini)
        {
            var result = new IsoMapData();
            if (ini == null) { result.Error = "无 INI"; return result; }

            var sizeSec = ini.GetSection("Map");
            var size = MapIniDocument.SplitComma(sizeSec != null ? sizeSec.GetString("Size", string.Empty) : string.Empty);
            int w = 0, h = 0;
            if (size.Length >= 4)
            {
                w = MapIniDocument.ParseInt(size[2]) - MapIniDocument.ParseInt(size[0]);
                h = MapIniDocument.ParseInt(size[3]) - MapIniDocument.ParseInt(size[1]);
            }

            // 上限按 [Map] Size；没有就按记录长度上限先给个够用的缓冲（解完按 written 截断）
            var cap = (w > 0 && h > 0) ? w * h * RecordSize : 0;

            byte[] data;
            int written;
            string error;
            // maxBytes=0 时 UnpackSection 用 packed*16 兜底；这里优先用 Size 推断
            // cap 只在 Size 可信时作为预算提示；真实 tile 数常多于 Size 猜测（水面/岸边格额外），
            // 因此按 0 交给 UnpackSection 用打包长度的保守上限分配，解完以 written 截断。
            if (!MapPreview.UnpackSection(ini, "IsoMapPack5", 0, out data, out written, out error))
            {
                result.Error = error;
                BootLog.Log("[地形] IsoMapPack5 解码失败：" + error);
                return result;
            }

            // ConcordRTS MapDatabase.cpp：合法载荷末尾会留 4 字节（最后一块的块头），
            // 记录区 = written - 4；其余余数才是真错。
            var remainder = written % RecordSize;
            var recordBytes = remainder == 4 ? written - 4 : written - remainder;
            if (remainder != 0 && remainder != 4)
            {
                result.Error = $"记录长度 {written} 余 {remainder}，既不是 {RecordSize} 整数倍也不是 4 字节尾块头";
                BootLog.Log("[地形] " + result.Error);
                return result;
            }

            var count = recordBytes / RecordSize;
            for (var i = 0; i < count; i++)
            {
                var o = i * RecordSize;
                // IsoMapPack5 的一条记录就是一个 60×30 地形片，rx/ry 就是它的落位坐标：
                // 包大小恒为 (2*w-1)*h，横向相邻记录差 1 个 rx（屏幕上正好半格 = 30px），
                // 相邻两条是两块不同的子图（water01s3 / water03s2 这种），靠画家顺序拼成连续水面。
                // 因此这里不能按“同格重复”去重，也不要换算成 w×h 的格号——那样会丢一半地形，
                // 也正好会和 Object 层的 rx/ry 错开。
                var tile = new IsoTileRecord
                {
                    X = BitConverter.ToInt16(data, o),
                    Y = BitConverter.ToInt16(data, o + 2),
                    TileIndex = BitConverter.ToInt32(data, o + 4),
                    SubTile = data[o + 8],
                    Height = data[o + 9],
                    Terrain = data[o + 10],
                };
                tile.CellX = tile.X;
                tile.CellY = tile.Y;
                result.Tiles.Add(tile);
                if (tile.X < result.MinX) result.MinX = tile.X;
                if (tile.X > result.MaxX) result.MaxX = tile.X;
                if (tile.Y < result.MinY) result.MinY = tile.Y;
                if (tile.Y > result.MaxY) result.MaxY = tile.Y;
            }

            BootLog.Log("[地形] " + result.Summary());
            return result;
        }

        /// <summary>按 (x,y) 建索引：地形渲染按格查，避免线性扫。</summary>
        public static Dictionary<int, IsoTileRecord> IndexByCell(IsoMapData map)
        {
            var dict = new Dictionary<int, IsoTileRecord>(map != null ? map.Tiles.Count : 0);
            if (map == null) return dict;
            for (var i = 0; i < map.Tiles.Count; i++)
            {
                var t = map.Tiles[i];
                dict[CellKey(t.X, t.Y)] = t;
            }
            return dict;
        }

        public static int CellKey(int x, int y) => (y << 16) ^ (x & 0xFFFF);
    }
}
