// VXLManager.cs —— VXL 体素模型管理器：单位（MCV/矿车/坦克）体素解析与光栅化的统一入口。
//
// 职责边界：
//   · 体素文件解析、柱状 RLE、法线表口径委托给 VxlReader；
//   · 软件等距光栅（朝向、深度缓冲、2×2 覆盖）委托给 VoxelImage；
//   · 出 Sprite 仍走 SHPManager / SpriteFactory，本管理器只给索引色图像。
using Ra2md.Assets;

namespace Ra2md.Managers
{
    public sealed class VXLManager
    {
        private static VXLManager _instance;

        public static VXLManager Instance
        {
            get
            {
                if (_instance == null) _instance = new VXLManager();
                return _instance;
            }
        }

        /// <summary>取解析后的体素模型（带缓存；缺失返回 null 并在 bootlog 留痕）。</summary>
        public VxlModel GetModel(string name) => VoxelImage.GetModel(name);

        /// <summary>光栅一个朝向的索引色图（facing 0..255，0 = 正北）。</summary>
        public IndexedImage Render(string name, int facing) => VoxelImage.Render(name, facing);

        public void Clear() => VoxelImage.Clear();
    }
}
