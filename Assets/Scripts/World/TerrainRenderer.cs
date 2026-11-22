// TerrainRenderer.cs —— 地形层：IsoMapPack5 格 + theater TMP（多 slot 合成）+ PAL → Surface16
//
// 口径：
//  · 格数据：IsoTileRecord{ X, Y, TileIndex(全局低16), SubTile, Height, Terrain }
//  · theater：TheaterData.Resolve(全局 tileIndex) → 文件名（词干+两位序号+.tmp）
//  · TMP：TmpReader 解出多 slot，按 anchor 合成到 blockWidth×blockHeight 画布
//  · 等距投影：screenX=(x-y)*halfW，screenY=(x+y)*halfH - Height*step，相机格居中
//  · 深度：按 (x+y) 升序铺，后铺的压前面
//  · 输出：1:1 写 Surface16 的 view 区（右侧栏区域留黑，由 HUD 层覆盖）
// 未做的层：extra 层（树/桥叠图）、overlay SHP、单位/建筑、HUD —— 不在这里伪造。
using System;
using System.Collections.Generic;
using Ra2md.Assets;
using Ra2md.Core;
using Ra2md.Gfx;
using Ra2md.Menu;

namespace Ra2md.World
{
    public static class TerrainRenderer
    {
        private const int HeightStep = 15;
        private const int CellW = 60;
        private const int CellH = 30;
        /// <summary>格高 30（对象层用它把 SHP 物件从格子上顶点下移到底角）。</summary>
        public const int CellHeight = CellH;
        private const int FallbackHalfW = 30;
        private const int FallbackHalfH = 15;

        private static TheaterData _theater;
        private static byte[] _palette;
        private static readonly Dictionary<string, byte[]> _unitPaletteCache =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string, byte[]> _namedPaletteCache =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static byte[] _objectPalette;
        private static readonly List<IsoTileRecord> _order = new List<IsoTileRecord>();
        private static int _cameraX, _cameraY;

        public static string Status = "未初始化";
        public static int DrawnTiles { get; private set; }
        public static int MissedTiles { get; private set; }
        public static int ClampedTiles { get; private set; }
        public static int LastWidth { get; private set; }
        public static int LastHeight { get; private set; }

        /// <summary>当前局内调色板（对象层 SHP 也用这张，保证颜色和地形一致）。</summary>
        public static byte[] ScenePalette => _palette;

        /// <summary>已装载的 theater（MapCanvas 共用，不再各写一套表）。</summary>
        public static TheaterData Theater => _theater;

        /// <summary>
        /// 按名字取任意一张 pal（art(md).ini 的 Palette= 字段就是这么用的：覆盖物逐个读 art 字段，
        /// 谁写了 Palette= 就用那张表，不写才走类别默认）。
        /// </summary>
        public static byte[] PaletteByName(string name)
        {
            var clean = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (clean.Length == 0) return null;
            if (_namedPaletteCache.TryGetValue(clean, out var cached)) return cached;

            var candidates = new[] { clean, clean + ".pal" };
            if (MenuResourceLibrary.TryLoadFirst(candidates, out var bytes, out var origin)
                && bytes != null && PalReader.TryDecode(bytes, out var rgba, out _))
            {
                _namedPaletteCache[clean] = rgba;
                BootLog.Log("[对象层] art Palette=" + name + " 源=" + origin);
                return rgba;
            }

            BootLog.Log("[对象层] art Palette=" + name + " 取不到，回落类别默认表");
            return null;
        }

        /// <summary>全局 tile 序号 + 子图号 → 索引位图。地形层与整图光栅共用这一个入口。</summary>
        public static bool TryGetTile(int globalIndex, int subTile, out IndexedImage image)
        {
            image = null;
            if (_theater == null) return false;
            var reference = _theater.Resolve(globalIndex);
            if (reference == null) return false;
            image = LoadTileImage(reference.FileName, subTile);
            if (!image.Valid && subTile != 0) image = LoadTileImage(reference.FileName, 0);
            return image.Valid;
        }

        /// <summary>对象（SHP）调色板：theater 的 unit 表，不是地形 iso 表。</summary>
        public static byte[] ObjectPalette => _objectPalette ?? _palette;

        /// <summary>
        /// unit SHP 的阵营重染表：unit pal 的 16..31 是 remap 区（占位色），必须按所有者阵营颜色替换。
        /// 无主/中立（r,g,b 全 0 或未给）走中性灰，和原版未占领单位一致。
        /// </summary>
        public static byte[] UnitPalette(int r, int g, int b) => RemapPalette(_objectPalette, r, g, b);

        /// <summary>按 art(md).ini 的 Remapable=yes 才重染 16..31；表可以是 unit 表也可以是 building 表。</summary>
        public static byte[] RemapPalette(byte[] baseTable, int r, int g, int b)
        {
            if (baseTable == null) return _palette;
            var hasColor = r > 0 || g > 0 || b > 0;
            if (!hasColor) { r = 176; g = 176; b = 176; }
            var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(baseTable) + "|" + r + "," + g + "," + b;
            if (_unitPaletteCache.TryGetValue(key, out var existing)) return existing;

            var table = (byte[])baseTable.Clone();
            for (var i = 0; i < 16; i++)
            {
                // 由暗到亮的一条 16 级渐变：末尾一级就是阵营本色的最高亮阶
                var t = (i + 1) / 16f;
                var bright = 0.35f + 0.65f * t;
                var p = (16 + i) * 4;
                if (p + 3 >= table.Length) break;
                table[p] = (byte)Math.Min(255, (int)(r * bright));
                table[p + 1] = (byte)Math.Min(255, (int)(g * bright));
                table[p + 2] = (byte)Math.Min(255, (int)(b * bright));
                table[p + 3] = 255;
            }

            _unitPaletteCache[key] = table;
            return table;
        }

        /// <summary>
        /// ART.INI 的 building 调色板：snow.pal / temperat.pal / urban.pal。
        /// TerrainPalette=no（默认）的建筑走这张，不是 iso 地表表。
        /// </summary>
        public static byte[] BuildingPalette
        {
            get
            {
                var theater = _theater != null ? _theater.Theater : "TEMPERATE";
                var name = (theater ?? "TEMPERATE").Trim().ToLowerInvariant();
                if (name.StartsWith("temperat")) name = "temperat";
                else if (name.StartsWith("snow")) name = "snow";
                else if (name.StartsWith("urban")) name = "urban";
                var pal = PaletteByName(name);
                return pal ?? _objectPalette ?? _palette;
            }
        }

        /// <summary>当前 theater 的 tile 扩展名（.sno / .tem …），对象层找不到 SHP 时按它回退 TMP。</summary>
        public static string TileExtension => _theater != null ? _theater.TileExtension : ".tem";

        /// <summary>
        /// 按 theater 扩展名取一张 TMP（树 TIBTRE01.sno、矿 tib01.sno 这类对象就是 tile 而不是 SHP）。
        /// 复用地形那套多 slot 合成，输出是索引像素（用 ScenePalette 上色）。
        /// </summary>
        public static bool TryLoadTheaterTile(string imageName, out byte[] pixels, out int width, out int height,
            out bool isSprite, out string error)
        {
            pixels = null;
            width = 0;
            height = 0;
            isSprite = false;
            error = null;
            if (string.IsNullOrEmpty(imageName)) { error = "名字为空"; return false; }

            var ext = TileExtension;
            var name = imageName;
            if (name.EndsWith(".shp", StringComparison.OrdinalIgnoreCase)) return false;
            if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name += ext;

            var image = LoadTileImage(name, 0);
            if (image == null || image.Error != null || image.Pixels == null)
            {
                error = image != null ? image.Error : "tile 未加载";
                return false;
            }
            pixels = image.Pixels;
            width = image.Width;
            height = image.Height;
            isSprite = image.IsSprite;
            return true;
        }

        public static int CameraX => _cameraX;
        public static int CameraY => _cameraY;

        /// <summary>
        /// 格坐标 → 屏幕像素（与地形铺图同一套投影，返回格子菱形的**上顶点**，和 Draw 里 sx/sy 同一基准）：
        ///   x = viewW/2 + (dx-dy-(camX-camY))*30
        ///   y =(面高)/2 + (dx+dy-(camX+camY))*15 - level*15
        /// 再换算成 UGUI 坐标（左下原点），供 ScreenSpaceOverlay 使用。
        /// 对象层自己决定锚点：TMP tile 用上顶点对齐；SHP 物件用格心（上顶点 - CellH/2）。
        /// </summary>
        public static bool TryCellToUi(int cellX, int cellY, int level, int screenW, int screenH,
            out float uiX, out float uiY)
        {
            uiX = 0f;
            uiY = 0f;
            var surface = Instance != null ? Instance.Surface : null;
            if (surface == null) return false;

            var viewW = Math.Max(1, (int)RenderResolution.ViewRect().width);
            var surfaceH = surface.Height;
            var surfaceW = surface.Width;

            IsoProjection.CellToSurface(cellX, cellY, level, viewW, surfaceH, _cameraX, _cameraY,
                out var px, out var py);

            // 呈现面在屏幕里居中放置（1:1），换算到左下原点的 UGUI 坐标；上顶点 y 取反
            uiX = (screenW - surfaceW) / 2f + px;
            uiY = (screenH + surfaceH) / 2f - py;
            return true;
        }

        private static FramePresenter Instance => FramePresenter.Instance;

        public static void Init(SkirmishMatch match)
        {
            IndexedImageLoader.Clear();
            if (match == null) { Status = "无对局"; return; }
            if (!match.Terrain.Valid)
            {
                Status = "地形无效: " + match.Terrain.Error;
                BootLog.Log("[地形渲染] " + Status);
                return;
            }

            var theater = string.IsNullOrEmpty(match.Document.Theater) ? "TEMPERATE" : match.Document.Theater;
            _theater = TheaterData.Load(theater);
            _palette = LoadPalette(theater);
            _objectPalette = LoadObjectPalette(theater);

            _order.Clear();
            _order.AddRange(match.Terrain.Tiles);
            _order.Sort((a, b) =>
            {
                // 等距画家算法：先按 x+y（远近），同一列时低处先画，高处（悬崖顶/壁）后画压住低处
                var d = (a.CellX + a.CellY).CompareTo(b.CellX + b.CellY);
                if (d != 0) return d;
                return a.Height.CompareTo(b.Height);
            });

            var house = match.PlayerHouseClass;
            _cameraX = house != null && house.StartingCellX >= 0 ? house.StartingCellX : (match.Terrain.MinX + match.Terrain.MaxX) / 2;
            _cameraY = house != null && house.StartingCellY >= 0 ? house.StartingCellY : (match.Terrain.MinY + match.Terrain.MaxY) / 2;

            Status = $"theater={_theater.Theater} set={_theater.SetCount} tile={_theater.TotalTiles} "
                + $"pal={(_palette != null ? "OK" : "缺失")} tiles={_order.Count} 相机=({_cameraX},{_cameraY})";
            BootLog.Log("[地形渲染] " + Status);
        }

        /// <summary>
        /// 对象调色板：SHP 物体（树/overlay/建筑/单位）走 theater 的 unit 调色板
        /// （snow→unitsno.pal、temperate→unittem.pal、urban→uniturb.pal），不是地形那张 iso 表。
        /// 把 iso 表套在对象上就是“颜色偏/发灰”的根因。
        /// </summary>
        private static byte[] LoadObjectPalette(string theater)
        {
            var sfx = TileExtensionFor(theater);           // ".sno" / ".tem" / ".urb" …
            var name = (theater ?? "temperate").Trim().ToLowerInvariant();
            if (name.StartsWith("temperat")) name = "temperat";

            // 名字表不可靠：实测 "snow.pal" 与 "temperat.pal" 会解析到同一份 768 字节内容
            // （都出 khaki 土黄），所以 unit 表一律按 CRC 直取，名字只作兜底日志。
            var idByTheater = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "snow", 0x26D08FB1u },      // unitsno.pal
                { "temperat", 0x63DA7359u },  // unittem.pal
                { "urban", 0xFC5F6352u },     // uniturb.pal
            };
            byte[] bytes;
            string origin;
            var id = idByTheater.TryGetValue(name, out var mapped) ? mapped : 0u;
            if (id != 0 && VirtualFileSystem.Instance.TryResolveById(id, out bytes, out origin) && bytes != null
                && PalReader.TryDecode(bytes, out var rgbaById, out _))
            {
                BootLog.Log("[对象层] 调色板 id:" + id.ToString("X8") + " 源=" + origin);
                return rgbaById;
            }

            var candidates = new[]
            {
                "unit" + sfx.TrimStart('.') + ".pal",
                "unit" + name + ".pal",
                "unitsno.pal", "unittem.pal", "uniturb.pal",
            };

            byte[] nameBytes;
            string nameOrigin;
            if (MenuResourceLibrary.TryLoadFirst(candidates, out nameBytes, out nameOrigin) && nameBytes != null
                && PalReader.TryDecode(nameBytes, out var rgba, out _))
            {
                BootLog.Log("[对象层] 调色板 " + nameOrigin + "（" + candidates[0] + "）");
                return rgba;
            }
            BootLog.Log("[对象层] 对象调色板缺失，回落地形调色板");
            return _palette;
        }

        private static string TileExtensionFor(string theater)
        {
            var name = (theater ?? "temperate").Trim().ToLowerInvariant();
            if (name.StartsWith("temperat")) return ".tem";
            if (name.StartsWith("snow")) return ".sno";
            if (name.StartsWith("newurban")) return ".ubn";
            if (name.StartsWith("urban")) return ".urb";
            if (name.StartsWith("desert")) return ".des";
            if (name.StartsWith("lunar")) return ".lun";
            return ".tem";
        }

        private static byte[] LoadPalette(string theater)
        {
            var name = (theater ?? "temperate").Trim().ToLowerInvariant();
            if (name.StartsWith("temperat")) name = "temperat";
            if (name.StartsWith("snow")) name = "snow";
            if (name.StartsWith("urban")) name = "urban";
            var candidates = new[]
            {
                name + ".pal",              // unitsno.pal / unittem.pal 这类命名先试
                "unit" + name + ".pal",
                "iso" + name.Substring(0, 3) + ".pal",   // isosno.pal / istem? 兜底拼法
                "isotem.pal", "isosno.pal", "isourb.pal",
                "temperat.pal", "snow.pal", "urban.pal",
                "unittem.pal", "unitsno.pal", "uniturb.pal",
            };
            // RA2 的 theater 调色板是 iso 前缀那三张，在 ra2.mix 的 0x3B5A96DE（palette pack，42 条）里，
            // 名字表没覆盖，只能按 id 直取。实测 clear01.sno：isosno 出白雪、isotem 出橘土、isourb 出深色。
            var idByTheater = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                { "snow", 0xF109F70Fu },      // isosno.pal
                { "temperat", 0x5F9D97B9u },  // isotem.pal
                { "urban", 0xC8D0CB21u },     // isourb.pal
            };

            byte[] bytes;
            string origin;

            // 先按 theater 的 id 直取。名字表里 "snow.pal"(64DB0B48) 确实存在，但用 clear01.sno
            // 实渲染出来是土黄+绿斑点（terrain 该是灰白雪），所以以实测为准，不走名字优先。
            var id = idByTheater.TryGetValue(name, out var mapped) ? mapped : 0x21437A9Fu;
            if (VirtualFileSystem.Instance.TryResolveById(id, out bytes, out origin) && bytes != null
                && PalReader.TryDecode(bytes, out var rgba, out _))
            {
                BootLog.Log("[地形渲染] 调色板 id:" + id.ToString("X8") + " 源=" + origin);
                return rgba;
            }

            if (MenuResourceLibrary.TryLoadFirst(candidates, out bytes, out origin) && bytes != null
                && PalReader.TryDecode(bytes, out rgba, out var err))
            {
                BootLog.Log("[地形渲染] 调色板(名字) " + origin);
                return rgba;
            }

            BootLog.Log("[地形渲染] 调色板缺失: " + string.Join("/", candidates) + " 及 id:" + id.ToString("X8"));
            return null;
        }

        /// <summary>地形 TMP 取图：统一走 IndexedImageLoader，并**按 SubTileIndex 取该格的子图**。
        /// 一个 2×2/3×3 模板（水、海岸、悬崖）每格只画自己那一块，取子图 0 才是“黑块”的来源。</summary>
        private static IndexedImage LoadTileImage(string fileName, int subTile)
            => IndexedImageLoader.LoadTile(fileName, subTile);

        /// <summary>把地形画到 16bpp 面（1:1；面尺寸 = 窗口客户区）。</summary>
        public static void Draw(Surface16 surface, SkirmishMatch match)
        {
            if (surface == null || match == null || _theater == null || _palette == null) return;

            var viewW = Math.Max(1, (int)RenderResolution.ViewRect().width);
            var viewH = surface.Height;
            var originX = viewW / 2;
            var originY = viewH / 2;
            LastWidth = surface.Width;
            LastHeight = surface.Height;

            surface.Clear(0);
            DrawnTiles = 0;
            MissedTiles = 0;
            ClampedTiles = 0;
            var missing = new List<string>();
            var unresolved = new List<string>();
            var unresolvedTop = -1;
            var unresolvedBottom = -1;

            for (var i = 0; i < _order.Count; i++)
            {
                var tile = _order[i];
                // TileIndex 是 theater 全局序号。越界值（含地图里用 0xFFFF 标记的空单元）按
                // WorldAlteringEditor/MapLoader.PostCheckMap 的口径归 0：原版这些格子不是洞，
                // 而是画 theater 的 clear 底图（tile 0）；跳过不画就等于把地面挖成黑菱形。
                var index = tile.TileIndex;
                if (index < 0 || index >= _theater.TotalTiles)
                {
                    ClampedTiles++;
                    var raw = index;
                    if (unresolvedTop < 0) unresolvedTop = raw;
                    unresolvedBottom = raw;
                    if (unresolved.Count < 4) unresolved.Add("idx=" + raw + "→0");
                    index = 0;
                }
                var reference = _theater.Resolve(index);
                if (reference == null)
                {
                    MissedTiles++;
                    if (unresolved.Count < 4) unresolved.Add("idx=" + index + " 无 set");
                    continue;
                }

                // 子图号越界同样归 0（PostCheckMap: SubTileIndex > maxSub → 0；maxSub<0 → TileIndex=0）
                var subTile = tile.SubTile;
                var image = LoadTileImage(reference.FileName, subTile);
                if (!image.Valid && subTile != 0)
                {
                    subTile = 0;
                    image = LoadTileImage(reference.FileName, 0);
                }
                if (!image.Valid)
                {
                    MissedTiles++;
                    if (missing.Count < 8 && !missing.Contains(image.Error)) missing.Add(image.Error);
                    continue;
                }

                // 等距投影与锚点规则只在 IsoProjection 定义一次，对象层/单位层共用同一份
                IsoProjection.CellToSurface(tile.CellX, tile.CellY, tile.Height, viewW, surface.Height,
                    _cameraX, _cameraY, out var cellX, out var cellY);

                int sx, sy;
                if (image.IsSprite)
                {
                    sx = IsoProjection.SpriteLeft(cellX, image.Width);
                    sy = IsoProjection.SpriteTop(cellY, image.Height, true);
                }
                else
                {
                    // TMP 子图：格子左上角为 (cellX-HalfW, cellY)，再叠加子图自身偏移
                    // （extra 图形的包围盒可能为负，悬崖壁正是向上/向两侧伸出）
                    sx = cellX - IsoProjection.HalfW + image.OffsetX;
                    sy = cellY + image.OffsetY;
                }

                if (sx + image.Width <= 0 || sy + image.Height <= 0 || sx >= viewW || sy >= viewH) continue;

                Blit(surface, image, sx, sy, viewW);
                DrawnTiles++;
            }

            Status = $"theater={_theater.Theater} 画={DrawnTiles}/{_order.Count} 缺={MissedTiles} 归零={ClampedTiles} "
                + $"tile总={_theater.TotalTiles} 面={surface.Width}×{surface.Height}"
                + (unresolvedTop >= 0 ? $" 越界tile={unresolvedTop}..{unresolvedBottom}" : string.Empty)
                + (missing.Count > 0 ? " 首缺=" + missing[0] : string.Empty);
            BootLog.Log("[地形渲染] " + Status);
            for (var i = 0; i < missing.Count; i++) BootLog.Log("[地形渲染] 缺资源: " + missing[i]);
            for (var i = 0; i < unresolved.Count; i++) BootLog.Log("[地形渲染] 越界 tile: " + unresolved[i]);
        }

        private static void Blit(Surface16 surface, IndexedImage image, int dx, int dy, int clipWidth)
        {
            var sw = surface.Width;
            var sh = surface.Height;
            // SHP 形态的 tile（树/灯）是“对象”，走 unit 调色板；TMP 地形仍走 iso 表
            var palette = image.IsSprite ? ObjectPalette : _palette;
            for (var y = 0; y < image.Height; y++)
            {
                var py = dy + y;
                if (py < 0 || py >= sh) continue;
                var row = y * image.Width;
                for (var x = 0; x < image.Width; x++)
                {
                    var px = dx + x;
                    if (px < 0 || px >= sw || px >= clipWidth) continue;
                    var index = image.Pixels[row + x];
                    if (index == 0) continue;
                    var p = index * 4;
                    var r = palette[p];
                    var g = palette[p + 1];
                    var b = palette[p + 2];
                    surface.Pixels[py * sw + px] = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
                }
            }
        }
    }
}
