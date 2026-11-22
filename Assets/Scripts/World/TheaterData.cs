// TheaterData.cs —— theater ini 的 tile set 表（对照 ConcordRTS TheaterDatabase.cpp 一比一）。
//
// 关键口径（不是目测）：
//   * 段名是**连续**的 TileSet0000、TileSet0001…，读到第一个缺号为止；
//   * 每段读 SetName / FileName / TilesInSet / Morphable；
//   * tile 编号是**全局累加序号**：TileSet0000 从 0 开始吃 TilesInSet 个，下一个 set 接着编号；
//     所以 IsoMapPack5 里的 tileIndex（低 16 位）直接用全局序号落在哪个区间来定位 set，
//     不是“高 16 位当 set”。
//   * 具体文件 = FileName 去扩展后的词干 + 两位序号(N+1)，扩展名按 .tmp 兜底
//     （temperat/snow/urban 实际都是 .tmp）。
using System;
using System.Collections.Generic;
using Ra2md.Core;
using Ra2md.Menu;

namespace Ra2md.World
{
    public sealed class TheaterTileSet
    {
        public int Index;
        public string SetName;
        public string FileName;
        public int TilesInSet;
        public bool Morphable;
        public int StartTileIndex;
    }

    public sealed class TheaterTileRef
    {
        public int TileIndex;        // 全局 tile 序号
        public int TileSetIndex;
        public string SetName;
        public int IndexInSet;
        public string FileName;
    }

    public sealed class TheaterData
    {
        public string Theater;
        public string IniSource;
        public string Error;
        public readonly List<TheaterTileSet> Sets = new List<TheaterTileSet>();
        public int TotalTiles { get; private set; }

        /// <summary>[General] ClearTile = tile set 序号；该 set 的第 0 张就是“空地/水面”底瓦（MapClass 先铺它）。</summary>
        public int ClearTileIndex { get; private set; }
        public string TileExtension = ".tem";
        public int SetCount => Sets.Count;

        public static TheaterData Load(string theater)
        {
            var data = new TheaterData { Theater = theater ?? Ra2md.Managers.GlobalConfigManager.Resources.DefaultTheater };
            var name = (theater ?? Ra2md.Managers.GlobalConfigManager.Resources.DefaultTheater).Trim().ToLowerInvariant();
            if (name.StartsWith("temperat") || name == "tem") name = "temperat";
            if (name.StartsWith("snow")) name = "snow";
            if (name.StartsWith("urban")) name = "urban";
            var candidates = new[]
            {
                name + Ra2md.Managers.GlobalConfigManager.Resources.TheaterIniSuffix,
                name + Ra2md.Managers.GlobalConfigManager.Resources.TheaterMdIniSuffix,
                Ra2md.Managers.GlobalConfigManager.Resources.TheaterFallbackIni,
            };

            byte[] bytes;
            string origin;
            if (!MenuResourceLibrary.TryLoadFirst(candidates, out bytes, out origin) || bytes == null)
            {
                data.Error = "theater ini 缺失（试过 " + string.Join("/", candidates) + "）";
                BootLog.Log("[地形] " + data.Error);
                return data;
            }
            data.IniSource = origin;

            var ini = MapIniDocument.Parse(MapIniDocument.DecodeText(bytes));
            var cursor = 0;
            for (var i = 0; ; i++)
            {
                var sectionName = "TileSet" + i.ToString("D4");
                var sec = ini.GetSection(sectionName);
                if (sec == null) break;

                var set = new TheaterTileSet
                {
                    Index = i,
                    SetName = (sec.GetString("SetName", string.Empty) ?? string.Empty).Trim(),
                    FileName = (sec.GetString("FileName", string.Empty) ?? string.Empty).Trim(),
                    TilesInSet = MapIniDocument.ParseInt(sec.GetString("TilesInSet", "0")),
                    Morphable = (sec.GetString("Morphable", "no") ?? string.Empty).Trim().ToLowerInvariant() == "yes",
                    StartTileIndex = cursor,
                };
                if (set.TilesInSet < 0) set.TilesInSet = 0;
                if (set.TilesInSet > 0 && set.FileName.Length == 0) set.TilesInSet = 0;
                cursor += set.TilesInSet;
                data.Sets.Add(set);
            }

            // 扩展名口径（inferTileExtensionFromPath）：temperat→.tem / snow→.sno / urban→.urb / 新城市→.ubn。
            data.TileExtension = TileExtensionFor(data.Theater);
            data.TotalTiles = cursor;

            // [General] ClearTile = 用于“空地/水”的 tile set 序号；底瓦 = 该 set 起始全局序号。
            var clearSet = MapIniDocument.ParseInt(ini.GetSection("General") != null
                ? ini.GetSection("General").GetString("ClearTile", "0") : "0");
            data.ClearTileIndex = clearSet >= 0 && clearSet < data.Sets.Count ? data.Sets[clearSet].StartTileIndex : 0;
            if (data.SetCount == 0) data.Error = "theater ini 没有连续的 TileSet0000 段";
            BootLog.Log($"[地形] theater={data.Theater} 源={origin} tileSet={data.SetCount} tile={data.TotalTiles}"
                + (data.Error != null ? " 错误=" + data.Error : string.Empty));
            return data;
        }

        /// <summary>全局 tile 序号 → set + 文件名（对照 resolveTileIndex）。</summary>
        public TheaterTileRef Resolve(int tileIndex)
        {
            if (tileIndex < 0) return null;
            for (var i = 0; i < Sets.Count; i++)
            {
                var set = Sets[i];
                if (tileIndex >= set.StartTileIndex && tileIndex < set.StartTileIndex + set.TilesInSet)
                {
                    var indexInSet = tileIndex - set.StartTileIndex;
                    var _ext = TileExtension;
                    var stem = Stem(set.FileName);
                    var twoDigit = (indexInSet + 1) < 10 ? "0" + (indexInSet + 1) : (indexInSet + 1).ToString();
                    return new TheaterTileRef
                    {
                        TileIndex = tileIndex,
                        TileSetIndex = set.Index,
                        SetName = set.SetName,
                        IndexInSet = indexInSet,
                        FileName = (stem + twoDigit + _ext).ToLowerInvariant(),
                    };
                }
            }
            return null;
        }

        private static string TileExtensionFor(string theater)
        {
            var name = (theater ?? string.Empty).Trim().ToLowerInvariant();
            if (name.StartsWith("temperat")) return ".tem";
            if (name.StartsWith("snow")) return ".sno";
            if (name.StartsWith("urbann") || name.StartsWith("newurban")) return ".ubn";
            if (name.StartsWith("urban")) return ".urb";
            if (name.StartsWith("desert")) return ".des";
            if (name.StartsWith("lunar")) return ".lun";
            return ".tem";
        }

        private static string Stem(string fileName)
        {
            var value = (fileName ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            var slash = value.LastIndexOf('\\');
            if (slash >= 0) value = value.Substring(slash + 1);
            var dot = value.LastIndexOf('.');
            return dot > 0 ? value.Substring(0, dot) : value;
        }
    }
}
