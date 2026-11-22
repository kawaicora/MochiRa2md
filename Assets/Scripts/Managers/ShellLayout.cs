// ShellLayout.cs —— 外壳（主菜单 / 单人 / 遭遇战对话框）布局几何，对照原版 src/ui/shell/geom.rs 口径移植。
//
// 口径（与参考引擎一致，不另起一套）：
//   · 逻辑对话框按 800×600  authored；窗口更大时整框等比缩放、居中，余量留给窗口黑底；
//   · DLU（MS Sans Serif 8pt）→ 像素：x 基宽 6/4，y 基高 13/8；
//   · 右侧栏 168 宽：顶帽 199、瓦片行 42、按钮格 156×42；
//   · 所有矩形先在 800×600  authored 空间算好，再由 ShellScale 一次性映射到窗口；
//     绘制与拾取走同一个矩形，不会出现"画的地方和点的地方差一个缩放系数"。
//
// 这里只放纯几何与常量；控件怎么画、事件怎么绑归 UIManager / MenuManager。
using System;
using UnityEngine;

namespace Ra2md.Managers
{
    /// <summary>窗口空间像素矩形（与 geom.rs RectPx 同语义）。</summary>
    public struct RectPx
    {
        public int X;
        public int Y;
        public int W;
        public int H;

        public RectPx(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }

        public RectPx Translate(int dx, int dy) => new RectPx(X + dx, Y + dy, W, H);

        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;

        public override string ToString() => $"RectPx({X},{Y},{W},{H})";
    }

    /// <summary>右侧栏三段矩形（顶帽 / 瓦片列 / 底帽）。</summary>
    public struct RightPanelRects
    {
        public RectPx Top;
        public RectPx Tile;
        public int TileCount;
        public RectPx Bottom;
        public RectPx AuthoredTile;
        public ShellScale Scale;

        /// <summary>第 row 行瓦片矩形（按 authored 行高缩放，不逐行累加已缩放高度）。</summary>
        public RectPx TileRow(int row) => RowCell(row, AuthoredTile.W, AuthoredTile.H);

        /// <summary>靠右对齐的 w×h 控件格，落在瓦片列第 row 行。</summary>
        public RectPx RowCell(int row, int w, int h)
        {
            var tile = AuthoredTile;
            return Scale.ScaleRect(new RectPx(tile.X + tile.W - w, tile.Y + row * tile.H, w, h));
        }

        public RightPanelRects Translate(int dx, int dy)
        {
            var copy = this;
            copy.Top = Top.Translate(dx, dy);
            copy.Tile = Tile.Translate(dx, dy);
            copy.Bottom = Bottom.Translate(dx, dy);
            copy.AuthoredTile = AuthoredTile.Translate(dx, dy);
            return copy;
        }

        /// <summary>面板相对 168×199 原稿的拉伸比（两侧钳到 1，不缩小原稿美术）。</summary>
        public Vector2 Stretch()
        {
            return new Vector2(
                Mathf.Max(1f, Tile.W / (float)ShellLayout.RightPanelWidth),
                Mathf.Max(1f, Top.H / (float)ShellLayout.RightPanelTopH));
        }
    }

    /// <summary>800×600 逻辑外壳 → 实窗口的统一映射（等比、居中）。</summary>
    public struct ShellScale
    {
        public int ScreenW;
        public int ScreenH;
        public bool Right;

        public static ShellScale Retail => new ShellScale { ScreenW = ShellLayout.BaseW, ScreenH = ShellLayout.BaseH, Right = false };

        public static ShellScale FromScreen(int w, int h) => new ShellScale { ScreenW = w, ScreenH = h, Right = false };

        public ShellScale AnchoredRight() => new ShellScale { ScreenW = ScreenW, ScreenH = ScreenH, Right = true };
        public ShellScale Centred() => new ShellScale { ScreenW = ScreenW, ScreenH = ScreenH, Right = false };

        public bool IsRetail => ScreenW == ShellLayout.BaseW && ScreenH == ShellLayout.BaseH;

        /// <summary>两轴同一系数：能塞进窗口的最大等比。</summary>
        public float Factor() => Mathf.Min(ScreenW / (float)ShellLayout.BaseW, ScreenH / (float)ShellLayout.BaseH);

        /// <summary>缩放后外壳在窗口里的矩形（居中；Right 时贴右边）。</summary>
        public RectPx DialogRect()
        {
            var f = Factor();
            var w = (int)Math.Round(ShellLayout.BaseW * f, MidpointRounding.AwayFromZero);
            var h = (int)Math.Round(ShellLayout.BaseH * f, MidpointRounding.AwayFromZero);
            var spare = ScreenW - w;
            var x = Right ? spare : spare / 2;
            return new RectPx(x, (ScreenH - h) / 2, w, h);
        }

        public RectPx Window() => new RectPx(0, 0, ScreenW, ScreenH);

        /// <summary>原稿矩形 → 窗口矩形（逐边取整，和参考引擎 scale_rect 同口径）。</summary>
        public RectPx ScaleRect(RectPx rect)
        {
            var f = Factor();
            var origin = DialogRect();
            int Edge(int authored) => (int)Math.Round(authored * f, MidpointRounding.AwayFromZero);
            var x0 = origin.X + Edge(rect.X);
            var y0 = origin.Y + Edge(rect.Y);
            var x1 = origin.X + Edge(rect.X + rect.W);
            var y1 = origin.Y + Edge(rect.Y + rect.H);
            return new RectPx(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
        }

        /// <summary>窗口点 → 原稿点（拾取/派生控件子矩形前先回原稿空间）。</summary>
        public Vector2Int BasePoint(int x, int y)
        {
            var origin = DialogRect();
            var f = Factor();
            var bx = (int)Math.Round((x - origin.X) / f, MidpointRounding.AwayFromZero);
            var by = (int)Math.Round((y - origin.Y) / f, MidpointRounding.AwayFromZero);
            return new Vector2Int(bx, by);
        }

        public RectPx BaseRect(RectPx rect)
        {
            var a = BasePoint(rect.X, rect.Y);
            var b = BasePoint(rect.X + rect.W, rect.Y + rect.H);
            return new RectPx(a.x, a.y, b.x - a.x, b.y - a.y);
        }

        /// <summary>先回原稿空间派生子矩形，再映射回窗口（绘制与拾取同一矩形）。</summary>
        public RectPx DeriveRect(RectPx rect, Func<RectPx, RectPx> derive)
        {
            if (IsRetail) return derive(rect);
            return ScaleRect(derive(BaseRect(rect)));
        }

        public RectPx ArtSize(float w, float h)
        {
            var f = Factor();
            return new RectPx(0, 0, (int)(w * f), (int)(h * f));
        }
    }

    public static class ShellLayout
    {
        public const int BaseW = 800;
        public const int BaseH = 600;

        // DLU 基准（MS Sans Serif 8pt）
        private const int DluBaseX = 6;
        private const int DluBaseY = 13;

        // 右侧栏 chrome
        public const int RightPanelWidth = 168;
        public const int RightPanelTopH = 199;
        public const int RightPanelTileH = 42;
        public const int RightPanelTileCountCap = 9;
        public const int SdbtnanmCellH = 42;
        public const int LowerStripH = 32;
        public const int SdbtnanmCellWNarrow = 156;
        public const int RightPanelFaceW = 110;

        /// <summary>Round-half-up MulDiv（符号安全）。</summary>
        public static int MulDivRound(int n, int numer, int denom)
        {
            var value = n * numer;
            return value >= 0 ? (value + denom / 2) / denom : (value - denom / 2) / denom;
        }

        /// <summary>DLU 矩形 → 像素矩形。</summary>
        public static RectPx DluRect(int x, int y, int w, int h)
        {
            return new RectPx(
                MulDivRound(x, DluBaseX, 4),
                MulDivRound(y, DluBaseY, 8),
                MulDivRound(w, DluBaseX, 4),
                MulDivRound(h, DluBaseY, 8));
        }

        /// <summary>模板子窗口：DLU 换算后宽高各 +1（原版 775BC0 的外延修正）。</summary>
        public static RectPx ChildWindow(int x, int y, int w, int h)
        {
            var r = DluRect(x, y, w, h);
            return new RectPx(r.X, r.Y, r.W + 1, r.H + 1);
        }

        /// <summary>对话框原点：仅当窗口大于 1024×767 才居中留边。</summary>
        public static Vector2Int DialogOrigin(int screenW, int screenH)
        {
            return new Vector2Int(
                screenW > 1023 ? (screenW - 800) / 2 : 0,
                screenH > 767 ? (screenH - 600) / 2 : 0);
        }

        public static int CenterOffset(int screen, int baseSize) => Math.Max(0, (screen - baseSize) / 2);

        /// <summary>右侧栏三段矩形（三枚外壳 0xE2/0x100/0x102 共用）。</summary>
        public static RightPanelRects RightPanelRects(int screenW, int screenH)
        {
            var leftMargin = screenW > 1023 ? (screenW - 800) / 2 : 0;
            var topMargin = screenH > 767 ? (screenH - 600) / 2 : 0;
            var effectiveRight = screenW - leftMargin;

            var top = new RectPx(effectiveRight - RightPanelWidth, topMargin, RightPanelWidth, RightPanelTopH);
            var tile = new RectPx(top.X, top.Y + top.H, RightPanelWidth, RightPanelTileH);

            var effectiveH = screenH > 767 ? screenH - topMargin * 2 : screenH;
            var remaining = Math.Max(0, effectiveH - top.H);
            var tileCount = Math.Min(remaining / RightPanelTileH, RightPanelTileCountCap);
            var bottomY = tile.Y + tileCount * RightPanelTileH;
            var bottomH = Math.Max(0, screenH - topMargin - bottomY);

            return new RightPanelRects
            {
                Top = top,
                Tile = tile,
                TileCount = tileCount,
                Bottom = new RectPx(top.X, bottomY, RightPanelWidth, bottomH),
                AuthoredTile = tile,
                Scale = ShellScale.Retail,
            };
        }

        /// <summary>底部横条（LWSCRN 帽），主菜单 / 单人页有，遭遇战页没有。</summary>
        public static RectPx LowerStripRect(int screenW, int screenH)
        {
            var leftMargin = screenW > 1023 ? (screenW - 800) / 2 : 0;
            var topMargin = screenH > 767 ? (screenH - 600) / 2 : 0;
            var shellH = screenH > 767 ? 600 : screenH;
            var w = screenW == 640 ? 472 : 632;
            return new RectPx(leftMargin, topMargin + shellH - LowerStripH, w, LowerStripH);
        }

        /// <summary>按钮吸附（四舍五入行距，主菜单 0xE2 堆叠按钮用）。</summary>
        public static RectPx SnapButtonRoundHalfUp(int dluY, RightPanelRects panel, int cellW)
        {
            var dluTop = MulDivRound(dluY, DluBaseY, 8) + panel.Top.Y;
            var panelY = panel.Tile.Y;
            var rowH = RightPanelTileH;
            var delta = Math.Max(0, dluTop - panelY);
            var q = delta / rowH;
            var rem = delta % rowH;
            if (rowH - rem <= rem) q += 1;
            var y = q * rowH + panelY;
            var x = panel.Top.X + (RightPanelWidth - cellW);
            return new RectPx(x, y, cellW, SdbtnanmCellH);
        }

        /// <summary>按钮吸附（偏置截断，单人 0x100 / 遭遇战 0x102 用）。</summary>
        public static RectPx SnapButtonBiasedTruncate(int screenW, int screenH, RectPx source, RightPanelRects panel, int cellW)
        {
            var offsetX = CenterOffset(screenW, 800);
            var sourceY = source.Y + CenterOffset(screenH, 600);
            var tileH = Math.Max(1, panel.Tile.H);
            var tileIndex = Math.Max(0, (sourceY - panel.Tile.Y + tileH / 2) / tileH);
            return new RectPx(
                screenW - offsetX - cellW,
                panel.Tile.Y + tileIndex * tileH,
                cellW,
                SdbtnanmCellH);
        }
    }
}
