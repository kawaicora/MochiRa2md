// VoxelLighting.cs —— VXL 体素漫反射亮度页计算。
//
// 口径对照参考项目 vxl_normals.rs（diffuse_pages）：
//   · 世界光源在模型空间按绘制旋转的逆变换折算；
//   · 每条法线与光源点积，非负时 page = floor(16 * dot)，负方向置 0；
//   · 法线表外的索引兜底 0；253/254/255 为环境常量页 16。
// 页面再交给 VPL 把原色索引映射到光照后的调色板索引。
using System;

namespace Ra2md.Assets
{
    internal static class VoxelLighting
    {
        // YR 世界光源（参考 vxl_normals.rs YR_WORLD_LIGHT，位模式解码值）。
        private static readonly float[] WorldLight = { -0.4999994f, -0.707106f, 0.4999994f };

        private const int AmbientPage = 16;
        private const int PageScale = 16;

        /// <summary>
        /// 计算某朝向、某法线模式下的 256 项亮度页表。
        /// </summary>
        /// <param name="mode">VXL 肢体法线表模式（2=TS，4=RA2）。</param>
        /// <param name="cos">绘制旋转余弦。</param>
        /// <param name="sin">绘制旋转正弦。</param>
        public static byte[] ComputePages(byte mode, float cos, float sin)
        {
            // 世界光折算到模型空间（逆旋转）。
            float lx = cos * WorldLight[0] + sin * WorldLight[1];
            float ly = -sin * WorldLight[0] + cos * WorldLight[1];
            float lz = WorldLight[2];

            var pages = new byte[256];
            var table = VoxelNormals.TableFor(mode);
            for (var i = 0; i < table.Length; i++)
            {
                var n = table[i];
                var dot = n[0] * lx + n[1] * ly + n[2] * lz;
                if (dot < 0f) { pages[i] = 0; continue; }
                var page = (int)Math.Floor(PageScale * dot);
                pages[i] = (byte)(page < 0 ? 0 : (page > AmbientPage ? AmbientPage : page));
            }

            // 环境尾部：参考引擎照明预算把 253/254/255 固定写环境页 16。
            pages[253] = AmbientPage;
            pages[254] = AmbientPage;
            pages[255] = AmbientPage;
            return pages;
        }
    }
}
