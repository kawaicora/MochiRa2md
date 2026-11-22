// IsoProjection.cs —— 等距格坐标 → 屏幕像素 → UGUI 坐标的唯一定义。
//
// 地形层（Surface16 绘制）、对象层（ScreenSpaceOverlay GameObject）、
// 以后的地图编辑器放置、单位移动，全部只允许调用这里的公式，
// 不许再各自抄一遍 (x-y)*30 / (x+y)*15 —— 每次抄都会抄错一次。
namespace Ra2md.World
{
    public static class IsoProjection
    {
        public const int CellW = 60;         // 菱形宽
        public const int CellH = 30;         // 菱形高
        public const int HalfW = CellW / 2;
        public const int HalfH = CellH / 2;
        public const int HeightStep = 15;    // 每级 elevation 的屏幕抬高

        /// <summary>格坐标 → 呈现面像素（cellXpx 是菱形水平中心，cellYpx 是菱形**上顶点**）。</summary>
        public static void CellToSurface(int cellX, int cellY, int level, int viewWidth, int viewHeight,
            int cameraX, int cameraY, out int cellXpx, out int cellYpx)
        {
            var originX = viewWidth / 2;
            var originY = viewHeight / 2;
            cellXpx = originX + (cellX - cellY - (cameraX - cameraY)) * HalfW;
            cellYpx = originY + (cellX + cellY - (cameraX + cameraY)) * HalfH - level * HeightStep;
        }

        /// <summary>Sprite 左上角 X：画布水平中心对菱形水平中心。</summary>
        public static int SpriteLeft(int cellXpx, int spriteWidth) => cellXpx - spriteWidth / 2;

        /// <summary>
        /// Sprite 左上角 Y：TMP 用菱形上顶点；SHP 用画布中心对菱形中心（上顶点 + CellH/2 - h/2）。
        /// </summary>
        public static int SpriteTop(int cellYpx, int spriteHeight, bool topAnchored)
            => topAnchored ? cellYpx : cellYpx + HalfH - spriteHeight / 2;
    }
}
