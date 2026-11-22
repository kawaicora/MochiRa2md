// MapManager.cs —— 地图世界管理器：对世界层单例的统一门面（画布 / 对象层 / 视口）。
//
// 职责边界：
//   · 这里只做世界层能力的转发与坐标落位；地形光栅、对象生成、选中移动仍在 World 层实现；
//   · 不存规则数据（归 GlobalDataManager / RulesDatabase），不存设置（归 GlobalConfigManager）；
//   · 后续编辑器拖拽、小地图跳转、视野跟随都从这里走，不再直接抓 MapCanvasView.Instance。
using Ra2md.World;
using UnityEngine;

namespace Ra2md.Managers
{
    public sealed class MapManager
    {
        private static MapManager _instance;

        public static MapManager Instance
        {
            get
            {
                if (_instance == null) _instance = new MapManager();
                return _instance;
            }
        }

        public SkirmishMatch Match => SkirmishMatch.Current;
        public MapObjectLayer Objects => MapObjectLayer.Instance;
        public MapCanvasView View => MapCanvasView.Instance;

        public int Width => MapCanvas.Width;
        public int Height => MapCanvas.Height;

        /// <summary>把视口对准某一格（自动取该格高程；视口边界由 MapCanvasView 钳制）。</summary>
        public void CenterOnCell(int cellX, int cellY)
        {
            var level = MapCanvas.LevelAt(cellX, cellY);
            if (!MapCanvas.TryCellToCanvas(cellX, cellY, level, out var x, out var y)) return;
            MapCanvasView.Instance.CenterOn(new Vector2(x, y));
        }
    }
}
