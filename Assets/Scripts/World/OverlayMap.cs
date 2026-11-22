// OverlayMap.cs —— 地图 overlay（树/岩石/矿石/桥等非建筑贴图）解码。
//
// 对照 ConcordRTS MapDatabase.cpp parseOverlayCells：
//   · [OverlayPack]：Format5 压缩的**按单元格索引**的 overlay 类型序号数组；
//       1 字节/格（0xFF = 空）或 2 字节/格（0xFFFF = 空，整表 512×512×2 时）。
//   · [OverlayDataPack]：同长度，1 字节/格 = 该 overlay 的帧号（缺省 0）。
//   · 序号 → 名字：rules 的 [OverlayTypes] 段**数字键 = 序号**（实测 1=GASAND、26=BRIDGE2、39 后跳到 42，键不连续；
//       早期误按出现顺序零基会导致整体错位一格，已改正；键非数字的改版 INI 才退回出现顺序）。
//   · 名字 → 图：rules 同名段里的 Image（缺失则用名字本身）。
//   · 格坐标：cellIndex 按 x 最快排布，x = left + i % width，y = top + i / width。
using System;
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    public sealed class OverlayCell
    {
        public int X;
        public int Y;
        public int OverlayIndex;
        public int Frame;
        public string TypeName;
        public string ImageName;
    }

    public static class OverlayMap
    {
        public static List<OverlayCell> Decode(MapIniDocument ini, RulesDatabase rules, out string note)
        {
            var cells = new List<OverlayCell>();
            note = null;
            if (ini == null) { note = "无 INI"; return cells; }

            // 口径（OpenRA ImportGen2MapCommand::ReadOverlay）：[OverlayPack] 解压后是 **512×512 的整格阵**，
            // 数组下标 = rx + 512 * ry，rx/ry 就是 IsoMapPack5 用的那套 iso 格坐标（和地形 tile.X/tile.Y 同一空间），
            // 不是 [Map] Size 的矩形、也不是逐行铺。1 字节/格（0xFF 空）；YR 的 overlay 种类 < 256，故为 1 字节表。
            byte[] pack;
            int packWritten;
            string error;
            if (!MapPreview.UnpackSectionLcw(ini, "OverlayPack", 0, out pack, out packWritten, out error))
            {
                note = "[OverlayPack] " + error;
                return cells;
            }
            if (packWritten <= 0) { note = "[OverlayPack] 空"; return cells; }

            byte[] dataPack;
            int dataWritten;
            if (!MapPreview.UnpackSectionLcw(ini, "OverlayDataPack", 0, out dataPack, out dataWritten, out error))
            {
                dataPack = null;
                dataWritten = 0;
            }

            const int gridW = 512;
            const int gridH = 512;
            var bytesPerCell = packWritten >= gridW * gridH * 2 ? 2 : 1;
            var cellCount = Math.Min(packWritten / bytesPerCell, gridW * gridH);

            var typeNames = new List<string>();
            if (rules != null)
            {
                // 口径：rules 的 [OverlayTypes] 是 **数字键 = overlay 序号**（实测 rulesmd.ini 为
                // 1=GASAND … 26=BRIDGE2 … 39 之后直接跳到 42，键不连续），所以按真键查，不能用出现顺序；
                // 键不是数字的改版 INI 才退回出现顺序零基。
                var keyed = rules.GetIndexMap("OverlayTypes");
                if (keyed.Count > 0)
                {
                    var max = 0;
                    foreach (var pair in keyed) if (pair.Key > max) max = pair.Key;
                    typeNames.Capacity = max + 1;
                    for (var i = 0; i <= max; i++)
                        typeNames.Add(keyed.TryGetValue(i, out var n) ? n : string.Empty);
                }
                else
                {
                    typeNames = rules.GetValueList("OverlayTypes");
                }
            }
            var nameToImage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < cellCount; i++)
            {
                var raw = bytesPerCell == 2
                    ? (int)((ushort)(pack[i * 2] | (pack[i * 2 + 1] << 8)))
                    : (int)pack[i];
                if (raw == (bytesPerCell == 2 ? 0xFFFF : 0xFF)) continue;

                var x = i % gridW;
                var y = i / gridW;
                if (y >= gridH) continue;

                string typeName = raw >= 0 && raw < typeNames.Count ? (typeNames[raw] ?? string.Empty).Trim() : string.Empty;
                // 序号 0 与键表里的空号（39→42 之间的 40/41）在原版里就是“空覆盖物”，不建对象。
                if (typeName.Length == 0) continue;
                string imageName = typeName;
                if (!nameToImage.TryGetValue(typeName, out imageName))
                {
                    imageName = (rules != null ? rules.GetString(typeName, "Image", string.Empty) : string.Empty).Trim();
                    if (imageName.Length == 0) imageName = typeName;
                    nameToImage[typeName] = imageName;
                }

                cells.Add(new OverlayCell
                {
                    X = x,
                    Y = y,
                    OverlayIndex = raw,
                    Frame = i < dataWritten ? dataPack[i] : 0,
                    TypeName = typeName,
                    ImageName = imageName,
                });
            }

            note = $"overlay={cells.Count} 格阵={gridW}×{gridH} 字节/格={bytesPerCell} 类型名={typeNames.Count}";
            return cells;
        }
    }
}
