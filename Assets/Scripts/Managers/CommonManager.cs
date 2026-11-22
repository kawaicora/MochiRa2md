// CommonManager.cs —— 通用管理器：跨 UI / 单位 / 模型共用的坐标口径、锚点与分辨率守卫。
//
// 职责边界（谁也别越界）：
//   · 这里只放"画什么都要用"的工具：两套零位换算、锚点口径、分辨率门槛；
//   · 具体 UI 构造归 UIManager；SHP 解码/出图归 SHPManager；体素归 VXLManager；
//     FBX 模型归 ModelManager；地图世界归 MapManager；触发器归 TriggerManager；
//     设置参数归 GlobalConfigManager。
//
// 两套零位（历史上"画出来上下颠倒"就出在这两套坐标被混用）：
//   · 画布 / UGUI：原点在左下，y 向上；
//   · 索引位图（SHP / TMP / VXL 光栅）：原点在左上，行序自上而下，y 向下。
// 像素行序的翻转只允许发生在 SpriteFactory 一处；这里只做锚点与坐标换算，不再自己翻像素。
using UnityEngine;

namespace Ra2md.Managers
{
    public sealed class CommonManager : MonoBehaviour
    {
        /// <summary>低于这个客户区尺寸直接黑屏提示，不进入菜单/战场。</summary>
        public const int MinWidth = 800;
        public const int MinHeight = 600;

        private static CommonManager _instance;

        public static CommonManager Instance
        {
            get
            {
                if (_instance == null) Ensure();
                return _instance;
            }
        }

        public static bool IsResolutionOk
            => Screen.width >= MinWidth && Screen.height >= MinHeight;

        public static CommonManager Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Ra2md.CommonManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CommonManager>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// 物件锚点（UGUI pivot）：TMP 地形片锚在格子上顶点 (0.5,1)；
        /// SHP 物件 / 单位 / 体素锚在画布中心 (0.5,0.5)。
        /// </summary>
        public static Vector2 PivotFor(bool topAnchored)
            => topAnchored ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0.5f);

        /// <summary>画布坐标（左下原点，y 向上）→ 位图坐标（左上原点，y 向下）。</summary>
        public static Vector2 CanvasToImage(float canvasX, float canvasY, int imageHeight)
            => new Vector2(canvasX, imageHeight - canvasY);
    }
}
