// SHPManager.cs —— SHP / TMP 索引图像管理器：对象层、按钮、cameo 出图的统一入口。
//
// 职责边界：
//   · 解码（SHP/TMP 自适应、子图、调色板索引像素）委托给 IndexedImageLoader；
//   · 索引像素 → Sprite 委托给 SpriteFactory（行序翻转只在那一处）；
//   · 调色板选择（地表表 / unit 表 / 阵营重染）仍由世界层按类别解析后传进来，
//     本管理器不认识地形/单位/建筑的类别口径。
using Ra2md.Assets;
using Ra2md.Gfx;
using UnityEngine;

namespace Ra2md.Managers
{
    public sealed class SHPManager
    {
        private static SHPManager _instance;

        public static SHPManager Instance
        {
            get
            {
                if (_instance == null) _instance = new SHPManager();
                return _instance;
            }
        }

        /// <summary>对象层取图：SHP → 剧场地表后缀自适应。</summary>
        public IndexedImage LoadObject(string imageName, int frame, string theaterExtension)
            => IndexedImageLoader.LoadObject(imageName, frame, theaterExtension);

        /// <summary>地形 tile 取图（带子图索引）。</summary>
        public IndexedImage LoadTile(string fileName, int subTile)
            => IndexedImageLoader.LoadTile(fileName, subTile);

        /// <summary>覆盖物取图：先 theater 后缀 TMP，回落 SHP。</summary>
        public IndexedImage LoadOverlay(string imageName, int frame, string theaterExtension)
            => IndexedImageLoader.LoadOverlay(imageName, frame, theaterExtension);

        /// <summary>索引像素 → Sprite（调色板由调用方按类别解析好传入）。</summary>
        public Sprite MakeSprite(IndexedImage image, byte[] palette, string name)
        {
            if (image == null || !image.Valid) return null;
            return SpriteFactory.Make(image.Pixels, image.Width, image.Height, palette, name);
        }

        /// <summary>清缓存（换图/重开本局时调用）。</summary>
        public void Clear() => IndexedImageLoader.Clear();
    }
}
