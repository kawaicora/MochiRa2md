// MapCanvas.cs —— 整张地图一次性画成一张大纹理，之后相机只平移“画布”，不再重绘。
//
// 口径（与 TerrainRenderer / IsoProjection 同一套）：
//   · 画布像素 = 地图像素：1 格 = 60×30 菱形，相邻格屏幕步进 (30,15)，高程 1 级 = 15px。
//   · 画布坐标原点在左下（UGUI / Texture2D 一致），y 向上；地表数据是 y 向下，只在 Blit 处翻一次。
//   · IsoMapPack5 的一条记录 = 一块 60×30 地形片，rx/ry 即落位坐标（横向半格步进），
//     用画家顺序（rx+ry 升序、同序按 height 升序）叠加，和原版 MapClass::DrawCell 顺序一致。
//   · 相机（视口）只改 MapCanvasView 容器的 anchoredPosition：地形纹理、对象层一起平移，零重绘。
using System;
using System.Collections.Generic;
using Ra2md.Assets;
using Ra2md.Core;
using Ra2md.Gfx;
using UnityEngine.UI;
using UnityEngine;

namespace Ra2md.World
{
    public sealed class MapCanvasView : MonoBehaviour
    {
        private static MapCanvasView _instance;

        private Canvas _canvas;
        private RawImage _image;

        private RectTransform _root;          // 可平移的画布容器（地形 + 对象都在它下面）
        private RectTransform _objects;       // 对象容器（永远盖在地形之上）
        private Vector2 _pan = Vector2.zero;
        private Vector2 _dragFrom;
        private bool _dragging;

        public static MapCanvasView Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Ra2md.MapCanvas");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MapCanvasView>();
                    _instance.CreateHierarchy();
                }
                return _instance;
            }
        }

        public RectTransform Root
        {
            get
            {
                if (_instance == null) { var _ = Instance; }
                return _root;
            }
        }

        public Texture2D Texture => MapCanvas.Texture;
        public Vector2 Pan => _pan;

        private void CreateHierarchy()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 0;
            canvasGo.AddComponent<GraphicRaycaster>();

            // MapRoot 是可平移的整张地图容器；Terrain 永远在 sibling 0，Objects 在它之上。
            var rootGo = new GameObject("MapRoot");
            rootGo.transform.SetParent(canvasGo.transform, false);
            _root = rootGo.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0f, 0f);
            _root.anchoredPosition = Vector2.zero;

            var terrainGo = new GameObject("Terrain");
            terrainGo.transform.SetParent(_root, false);
            _image = terrainGo.AddComponent<RawImage>();
            _image.raycastTarget = false;
            var irt = _image.rectTransform;
            irt.anchorMin = new Vector2(0f, 0f);
            irt.anchorMax = new Vector2(0f, 0f);
            irt.pivot = new Vector2(0f, 0f);
            irt.anchoredPosition = Vector2.zero;

            var objectsGo = new GameObject("Objects");
            objectsGo.transform.SetParent(_root, false);
            _objects = objectsGo.AddComponent<RectTransform>();
            _objects.anchorMin = new Vector2(0f, 0f);
            _objects.anchorMax = new Vector2(0f, 0f);
            _objects.pivot = new Vector2(0f, 0f);
            _objects.anchoredPosition = Vector2.zero;
        }

        /// <summary>把地形纹理铺进画布容器（尺寸 = 整图尺寸，1:1 像素）。</summary>
        public void SetTexture(Texture2D texture, int width, int height)
        {
            if (_image == null) CreateHierarchy();
            _image.texture = texture;
            _image.enabled = texture != null;
            var irt = _image.rectTransform;
            irt.sizeDelta = new Vector2(width, height);
            irt.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            _objects.sizeDelta = new Vector2(width, height);
        }

        /// <summary>把视口对准画布上的一个点（像素，左下原点）。对齐目标中心只需改 pan，不重绘。</summary>
        public void CenterOn(Vector2 canvasPoint)
        {
            _pan = new Vector2(-canvasPoint.x, -canvasPoint.y);
            ApplyPan();
        }

        /// <summary>把对象层容器接进画布（它就成为 Objects 容器，位置用画布像素坐标）。</summary>
        public void Attach(RectTransform child)
        {
            if (child == null) return;
            if (_objects == null) CreateHierarchy();
            child.SetParent(_objects, false);
            child.anchorMin = new Vector2(0f, 0f);
            child.anchorMax = new Vector2(0f, 0f);
            child.pivot = new Vector2(0f, 0f);
            child.sizeDelta = _objects.sizeDelta;
            child.anchoredPosition = Vector2.zero;
        }

        private void ApplyPan()
        {
            if (_root == null) return;
            ClampPan();
            _root.anchoredPosition = _pan;
        }

        /// <summary>
        /// 视口不许滑出地图可视边界：画布外（margin 之外）可能挂着维持运行的第三方对象/空面，
        /// 滑出去就是一整块黑底。local 中心 = -pan；当画布比视口大时，local 中心必须落在
        /// [sw/2, W-sw/2] 内；画布比视口小则居中。
        /// </summary>
        private void ClampPan()
        {
            if (MapCanvas.Texture == null) return;
            var sw = Screen.width;
            var sh = Screen.height;
            var w = (float)MapCanvas.Width;
            var h = (float)MapCanvas.Height;

            float loX, hiX;
            if (w <= sw) { loX = hiX = -w * 0.5f; }
            else { loX = sw * 0.5f - w; hiX = -sw * 0.5f; }

            float loY, hiY;
            if (h <= sh) { loY = hiY = -h * 0.5f; }
            else { loY = sh * 0.5f - h; hiY = -sh * 0.5f; }

            _pan.x = Mathf.Clamp(_pan.x, loX, hiX);
            _pan.y = Mathf.Clamp(_pan.y, loY, hiY);
        }

        private void Update()
        {
            // 原版视口拖动：按住鼠标左键（或中键）平移画布；只改 transform，不重画地形。
            if (_canvas == null) return;
            var held = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(2);
            if (held && !_dragging)
            {
                _dragging = true;
                _dragFrom = UnityEngine.Input.mousePosition;
            }
            else if (!held && _dragging)
            {
                _dragging = false;
            }
            else if (_dragging)
            {
                var now = (Vector2)UnityEngine.Input.mousePosition;
                _pan += now - _dragFrom;
                _dragFrom = now;
                ApplyPan();
            }
        }
    }

    /// <summary>整张地图的离线光栅化结果（一次性建成，之后只被平移）。</summary>
    public static class MapCanvas
    {
        private const int CellStepX = 30;      // 相邻格屏幕 x 步进（菱形宽 60 的一半）
        private const int CellStepY = 15;      // 相邻格屏幕 y 步进（菱形高 30 的一半）
        private const int HeightStep = 15;     // 每级高程的屏幕抬升
        private const int MarginX = 64;
        private const int MarginTop = 336;     // 14 级高程 210 + 悬崖壁外扩
        private const int MarginBottom = 96;

        public static Texture2D Texture { get; private set; }
        public static int Width { get; private set; }
        public static int Height { get; private set; }
        public static int MinCellX { get; private set; }
        public static int MinCellY { get; private set; }
        public static int MaxCellX { get; private set; }
        public static int MaxCellY { get; private set; }
        public static string Status { get; private set; }

        private static int _u0, _t0;
        private static int _drawn, _clamped, _missed;
        // 每格高程（MinCell..MaxCell），几何取格 / 拾取都用它；Build 时一次填满。
        private static int[] _levels;
        private static int _levelW, _levelH;

        /// <summary>画布像素 x：格 (cx,cy) 的菱形左顶点所在列。</summary>
        public static int CanvasX(int cellX, int cellY) => (cellX - cellY - _u0) * CellStepX + MarginX;

        /// <summary>画布像素 y（自下而上，y 向上）。level 越高画得越高。</summary>
        public static int CanvasY(int cellX, int cellY, int level)
        {
            var top = (cellX + cellY - _t0) * CellStepY + MarginTop - level * HeightStep;
            return Height - top;
        }

        /// <summary>格 → 画布点（UGUI 坐标，左下原点）。对象层摆位只允许走这里。</summary>
        public static bool TryCellToCanvas(int cellX, int cellY, int level, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (Texture == null) return false;
            x = CanvasX(cellX, cellY);
            y = CanvasY(cellX, cellY, level);
            return true;
        }

        /// <summary>某格的高程（Build 时缓存；越界归 0）。</summary>
        public static int LevelAt(int cellX, int cellY)
        {
            if (_levels == null) return 0;
            var ix = cellX - MinCellX;
            var iy = cellY - MinCellY;
            if (ix < 0 || iy < 0 || ix >= _levelW || iy >= _levelH) return 0;
            return _levels[iy * _levelW + ix];
        }

        /// <summary>
        /// 画布点 → 格（拾取用）。逐格做菱形判定：格上顶点 (CanvasX, CanvasY)，
        /// 菱形范围 x∈[cx-30,cx+30]、y∈[cy-30,cy]，判据 |dx|/30 + |dy|/15 ≤ 1。
        /// 复杂度 O(格数)，一次点击扫一遍完全够用。
        /// </summary>
        public static bool TryCanvasToCell(float canvasX, float canvasY, out int cellX, out int cellY)
        {
            cellX = -1;
            cellY = -1;
            if (Texture == null) return false;
            for (var cy = MinCellY; cy <= MaxCellY; cy++)
            {
                for (var cx = MinCellX; cx <= MaxCellX; cx++)
                {
                    var left = CanvasX(cx, cy);
                    var top = CanvasY(cx, cy, LevelAt(cx, cy));
                    var dx = (canvasX - (left + CellStepX)) / CellStepX;
                    var dy = (canvasY - (top - CellStepY)) / CellStepY;
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) <= 1f)
                    {
                        cellX = cx;
                        cellY = cy;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>画布点 → 格（入参是 UGUI 屏幕坐标，自动叠加当前 pan）。</summary>
        public static bool TryScreenToCell(Vector2 screenPosition, out int cellX, out int cellY)
        {
            var view = MapCanvasView.Instance;
            var pan = view.Pan;
            var local = new Vector2(screenPosition.x - pan.x - (Screen.width * 0.5f),
                screenPosition.y - pan.y - (Screen.height * 0.5f));
            return TryCanvasToCell(local.x, local.y, out cellX, out cellY);
        }

        /// <summary>地图中心格对应的画布点（开局视口对准这里）。</summary>
        public static Vector2 CenterPoint()
        {
            if (Texture == null) return Vector2.zero;
            var cx = (MinCellX + MaxCellX) / 2;
            var cy = (MinCellY + MaxCellY) / 2;
            return new Vector2(CanvasX(cx, cy), CanvasY(cx, cy, 0));
        }

        public static bool Build(SkirmishMatch match)
        {
            if (match == null || !match.Terrain.Valid)
            {
                Status = "地形无效";
                return false;
            }

            var tiles = match.Terrain.Tiles;
            if (tiles.Count == 0)
            {
                Status = "无 tile";
                return false;
            }

            MinCellX = MaxCellX = tiles[0].X;
            MinCellY = MaxCellY = tiles[0].Y;
            for (var i = 1; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (t.X < MinCellX) MinCellX = t.X;
                if (t.X > MaxCellX) MaxCellX = t.X;
                if (t.Y < MinCellY) MinCellY = t.Y;
                if (t.Y > MaxCellY) MaxCellY = t.Y;
            }

            _u0 = MinCellX - MaxCellY;
            _t0 = MinCellX + MinCellY;
            var uMax = MaxCellX - MinCellY;
            var tMax = MaxCellX + MaxCellY;

            // 每格高程表（拾取与视口对位都用它）
            _levelW = MaxCellX - MinCellX + 1;
            _levelH = MaxCellY - MinCellY + 1;
            _levels = new int[_levelW * _levelH];
            for (var i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                var lx = t.X - MinCellX;
                var ly = t.Y - MinCellY;
                if (lx < 0 || ly < 0 || lx >= _levelW || ly >= _levelH) continue;
                if (t.Height > _levels[ly * _levelW + lx]) _levels[ly * _levelW + lx] = t.Height;
            }

            Width = (uMax - _u0) * CellStepX + 30 + MarginX * 2;
            Height = (tMax - _t0) * CellStepY + 30 + MarginTop + MarginBottom;

            var surface = new Surface16(Width, Height);
            surface.Clear(0);

            var palette = TerrainRenderer.ScenePalette;
            if (palette == null)
            {
                Status = "调色板缺失";
                return false;
            }

            // 画家顺序：与 TerrainRenderer.Draw 一致（rx+ry → height）
            var order = new List<IsoTileRecord>(tiles);
            order.Sort((a, b) =>
            {
                var d = (a.X + a.Y).CompareTo(b.X + b.Y);
                return d != 0 ? d : a.Height.CompareTo(b.Height);
            });

            _drawn = 0;
            _clamped = 0;
            _missed = 0;

            // 第一遍：铺底瓦。原版 MapClass 先把整片地图铺成 theater 的 ClearTile（[General] ClearTile 那个
            // tile set 的第 0 张），再往上叠 IsoMapPack5 的 tile。
            // 注意：底瓦要**从 level 0 一级一级铺到该格自身 level**。相邻格高度差一级就是 15px 的竖向带，
            // 只铺顶层那一层的话，这条带没有任何像素、直接露出画布底色 —— 就是那些黑三角。
            IndexedImage baseTile;
            var clearIndex = TerrainRenderer.Theater != null ? TerrainRenderer.Theater.ClearTileIndex : 0;
            if (TerrainRenderer.TryGetTile(clearIndex, 0, out baseTile))
            {
                for (var i = 0; i < order.Count; i++)
                {
                    var t = order[i];
                    for (var lvl = 0; lvl <= t.Height; lvl++)
                    {
                        BlitIndexed(surface, baseTile.Pixels, baseTile.Width, baseTile.Height, palette,
                            CanvasX(t.X, t.Y) - CellStepX + baseTile.OffsetX,
                            Height - CanvasY(t.X, t.Y, lvl) + baseTile.OffsetY);
                    }
                }
            }

            for (var i = 0; i < order.Count; i++)
            {
                var tile = order[i];
                var index = tile.TileIndex;
                if (index < 0 || index == 0xFFFF || index >= TerrainRenderer.Theater.TotalTiles)
                {
                    index = 0;
                    _clamped++;
                }

                IndexedImage image;
                if (!TerrainRenderer.TryGetTile(index, tile.SubTile, out image))
                {
                    _missed++;
                    continue;
                }

                var left = CanvasX(tile.X, tile.Y) - CellStepX + image.OffsetX;
                var top = Height - CanvasY(tile.X, tile.Y, tile.Height) + image.OffsetY;
                BlitIndexed(surface, image.Pixels, image.Width, image.Height, palette, left, top);
                _drawn++;
            }

            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            Texture = ToTexture(surface);
            Status = $"画布={Width}×{Height} 画={_drawn}/{order.Count} 缺={_missed} 归零={_clamped} "
                + $"格=({MinCellX},{MinCellY})-({MaxCellX},{MaxCellY})";
            BootLog.Log("[地图画布] " + Status);
            return Texture != null;
        }

        /// <summary>索引位图 → surface（0 透明跳过，调色板 RGBA 4 字节步进，和 TerrainRenderer.Blit 同一份口径）。</summary>
        private static void BlitIndexed(Surface16 surface, byte[] pixels, int w, int h, byte[] palette, int left, int top)
        {
            for (var y = 0; y < h; y++)
            {
                var py = top + y;
                if ((uint)py >= (uint)surface.Height) continue;
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    var px = left + x;
                    if ((uint)px >= (uint)surface.Width) continue;
                    var index = pixels[row + x];
                    if (index == 0) continue;
                    var p = index * 4;
                    var r = palette[p];
                    var g = palette[p + 1];
                    var b = palette[p + 2];
                    surface.Pixels[py * surface.Width + px] = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
                }
            }
        }

        private static Texture2D ToTexture(Surface16 surface)
        {
            var texture = new Texture2D(surface.Width, surface.Height, TextureFormat.RGBA32, false, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            var colors = new Color32[surface.Width * surface.Height];
            var src = surface.Pixels;
            for (var y = 0; y < surface.Height; y++)
            {
                // Surface16 行序自上而下，Texture2D 自下而上：只在这一次翻转
                var srcRow = y * surface.Width;
                var dstRow = (surface.Height - y - 1) * surface.Width;
                for (var x = 0; x < surface.Width; x++)
                {
                    var c = src[srcRow + x];
                    var r = (byte)((c >> 11) & 0x1F);
                    var g = (byte)((c >> 5) & 0x3F);
                    var b = (byte)(c & 0x1F);
                    colors[dstRow + x] = new Color32((byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)),
                        (byte)((b << 3) | (b >> 2)), 255);
                }
            }

            texture.SetPixels32(colors);
            texture.Apply(false, false);
            return texture;
        }
    }
}
