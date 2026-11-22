// VoxelReader.cs —— VXL 体素模型的解析与“软件等距光栅化”。
//
// 为什么需要它：RA2/YR 的单位里，MCV（AMCV/SMCV/PCV）、矿车、坦克全是 Voxel=yes，
// 磁盘上只有 .vxl（+ .hva 动作矩阵），没有 .shp。对象层原来只会找 SHP，所以基地车
// 建出来了却画不出来 —— 这里的口径是 OpenRA OpenRA.Mods.Cnc\FileFormats\VxlReader.cs
// （头部 802 字节 + 每肢体 28 字节头 + bodySize + 每肢体 92 字节 footer + 柱状 RLE 体素）。
//
// 光栅化口径（TS/RA2 的等距屏幕）：一格的屏幕步进是 (30,15)，一格的体素网格是 24×24，
// 所以 1 体素 = (30/24, 15/24) 屏幕像素；z 轴 24 体素 = 一个高程级 = 15px。
// 每体素画成 2×2 方块（相邻体素间距 1.25px，1px 会露出孔洞），深度按 x+y+z 从后往前。
using System;
using System.Collections.Generic;
using Ra2md.Menu;
using UnityEngine;

namespace Ra2md.Assets
{
    /// <summary>一个 VXL 肢体（正常单位只有 1 个，矿车 2 个、MCV 1 个）。</summary>
    public sealed class VxlLimb
    {
        public string Name;
        public float Scale = 1f;
        public float[] Bounds = new float[6];   // x0,y0,z0, x1,y1,z1
        public byte[] Size = new byte[3];
        public byte Type;                       // 2 = TS 法线表，4 = RA2 法线表
        public byte[] Color;                    // [x,y,z] 索引色，0 = 空
        public byte[] Normal;                   // [x,y,z] 法线
    }

    public sealed class VxlModel
    {
        public VxlLimb[] Limbs;
        public string Origin;

        /// <summary>所有肢体合并后的包围盒（体素坐标，未旋转）。</summary>
        public void AggregateBounds(out float x0, out float y0, out float z0, out float x1, out float y1, out float z1)
        {
            x0 = y0 = z0 = float.MaxValue;
            x1 = y1 = z1 = float.MinValue;
            for (var i = 0; i < Limbs.Length; i++)
            {
                var l = Limbs[i];
                if (l == null) continue;
                if (l.Bounds[0] < x0) x0 = l.Bounds[0];
                if (l.Bounds[1] < y0) y0 = l.Bounds[1];
                if (l.Bounds[2] < z0) z0 = l.Bounds[2];
                if (l.Bounds[3] > x1) x1 = l.Bounds[3];
                if (l.Bounds[4] > y1) y1 = l.Bounds[4];
                if (l.Bounds[5] > z1) z1 = l.Bounds[5];
            }
            if (x1 < x0) { x0 = y0 = z0 = 0f; x1 = y1 = z1 = 1f; }
        }
    }

    public static class VxlReader
    {
        private static uint ReadU32(byte[] d, int p)
            => (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));

        private static float ReadF32(byte[] d, int p) => BitConverter.ToSingle(d, p);

        public static VxlModel Load(string name, out string error)
        {
            error = null;
            var candidates = new[] { name + ".vxl", name.ToLowerInvariant() + ".vxl" };
            byte[] data = null;
            string origin = null;
            for (var i = 0; i < candidates.Length && data == null; i++)
                data = MenuResourceLibrary.LoadRawByName(candidates[i], out origin);
            if (data == null)
            {
                error = "缺少 " + name + ".vxl";
                return null;
            }

            try
            {
                var model = Parse(data);
                model.Origin = origin;
                return model;
            }
            catch (Exception e)
            {
                error = "VXL 解析失败 " + name + "：" + e.Message;
                return null;
            }
        }

        public static VxlModel Parse(byte[] d)
        {
            var magic = System.Text.Encoding.ASCII.GetString(d, 0, Math.Min(16, d.Length));
            if (!magic.StartsWith("Voxel Animation", StringComparison.Ordinal))
                throw new InvalidOperationException("VXL 头不是 Voxel Animation");
            if (d.Length < 802) throw new InvalidOperationException("VXL 太短");

            var limbCount = (int)ReadU32(d, 20);
            if (limbCount <= 0 || limbCount > 64) throw new InvalidOperationException("肢体数异常 " + limbCount);
            var bodySize = (int)ReadU32(d, 28);

            const int headerEnd = 802;
            var footers = headerEnd + 28 * limbCount + bodySize;
            if (footers < 0 || footers + 92 * limbCount > d.Length)
                throw new InvalidOperationException("VXL footer 越界");

            var model = new VxlModel { Limbs = new VxlLimb[limbCount] };
            var offsets = new int[limbCount];
            for (var i = 0; i < limbCount; i++)
            {
                var p = footers + 92 * i;
                offsets[i] = (int)ReadU32(d, p);
                var scale = ReadF32(d, p + 12);
                var limb = new VxlLimb
                {
                    Name = System.Text.Encoding.ASCII.GetString(d, headerEnd + 28 * i, 16).TrimEnd('\0'),
                    Scale = scale > 0f ? scale : 1f,
                };
                for (var j = 0; j < 6; j++) limb.Bounds[j] = ReadF32(d, p + 64 + 4 * j);
                limb.Size[0] = d[p + 88];
                limb.Size[1] = d[p + 89];
                limb.Size[2] = d[p + 90];
                limb.Type = d[p + 91];
                model.Limbs[i] = limb;
            }

            for (var i = 0; i < limbCount; i++)
            {
                var limb = model.Limbs[i];
                var sx = Math.Max(1, (int)limb.Size[0]);
                var sy = Math.Max(1, (int)limb.Size[1]);
                var sz = Math.Max(1, (int)limb.Size[2]);
                limb.Color = new byte[sx * sy * sz];
                limb.Normal = new byte[sx * sy * sz];

                var baseOffset = headerEnd + 28 * limbCount;
                var columnTable = baseOffset + offsets[i];
                if (columnTable + 4 * sx * sy > d.Length) throw new InvalidOperationException("VXL 柱表越界");
                var dataStart = columnTable + 4 * sx * sy + 4 * sx * sy;   // 列偏移 + 4 字节空表

                for (var col = 0; col < sx * sy; col++)
                {
                    var start = (int)ReadU32(d, columnTable + 4 * col);
                    if (start == -1) continue;
                    var p = dataStart + start;
                    var x = col % sx;
                    var y = col / sx;
                    var z = 0;
                    do
                    {
                        if (p >= d.Length) break;
                        z += d[p++];
                        var count = d[p++];
                        for (var k = 0; k < count; k++)
                        {
                            if (p + 1 >= d.Length) break;
                            var ci = d[p++];
                            var ni = d[p++];
                            if (z + k < sz && z + k >= 0)
                            {
                                var idx = (z + k) * sx * sy + y * sx + x;
                                limb.Color[idx] = ci;
                                limb.Normal[idx] = ni;
                            }
                        }
                        if (p < d.Length) p++;   // 重复 count 字节
                        z += count;
                    }
                    while (z < sz);
                }
            }
            return model;
        }
    }

    /// <summary>体素模型 → 索引色图像（8 朝向，软件光栅，深度缓冲）。</summary>
    public static class VoxelImage
    {
        private static readonly Dictionary<string, VxlModel> ModelCache =
            new Dictionary<string, VxlModel>(StringComparer.OrdinalIgnoreCase);

        private static VplFile _vpl;
        private static bool _vplTried;

        private static VplFile GetVpl()
        {
            if (_vplTried) return _vpl;
            _vplTried = true;
            var bytes = MenuResourceLibrary.LoadRawByName(
                Ra2md.Managers.GlobalConfigManager.Resources.VplName, out _);
            _vpl = VplFile.Parse(bytes);
            Ra2md.Core.BootLog.Log(_vpl != null
                ? "[体素] 光照表 " + Ra2md.Managers.GlobalConfigManager.Resources.VplName + " 已载入"
                : "[体素] 光照表缺失，体素按原色索引绘制");
            return _vpl;
        }

        public static VxlModel GetModel(string name)
        {
            if (ModelCache.TryGetValue(name, out var m)) return m;
            m = VxlReader.Load(name, out var error);
            if (m == null) Ra2md.Core.BootLog.Log("[体素] " + error);
            else Ra2md.Core.BootLog.Log($"[体素] {name} 肢体={m.Limbs.Length} 源={m.Origin}");
            ModelCache[name] = m;
            return m;
        }

        public static void Clear() => ModelCache.Clear();

        /// <summary>
        /// 生成一个朝向的索引色图（IsSprite=true，画布中心锚点）。
        /// facing 0..255（RA2 的 FacingClass），0 = 正北（屏幕右上方）。
        /// </summary>
        public static IndexedImage Render(string name, int facing)
        {
            var model = GetModel(name);
            if (model == null) return new IndexedImage { Error = "缺少体素 " + name };

            // 合并所有肢体：先求旋转后的整体包围盒，再按包围盒画布光栅
            var angle = (facing & 0xFF) * (2f * Mathf.PI / 256f);
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);

            // 水平：1 格 = 48 体素（MCV 模型 77×29 → 画布约 71×45，与原版单位精灵尺寸一致；
            //       24 体素会把 MCV 画成 136×91，明显过大）
            // 竖直：1 级高程 = 24 体素 = 15px（z 单独一档，否则车体会被压扁）
            const float CellVoxelsXY = 48f;
            const float CellVoxelsZ = 24f;
            const float StepX = 30f / CellVoxelsXY;
            const float StepY = 15f / CellVoxelsXY;
            const float StepZ = 15f / CellVoxelsZ;

            // 旋转后投影范围
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (var i = 0; i < model.Limbs.Length; i++)
            {
                var limb = model.Limbs[i];
                if (limb == null) continue;
                // 包围盒取 Bounds 原值（体素坐标，与 Size 网格同空间）；Scale 是渲染缩放因子，
                // 不能再乘到包围盒上 —— 乘过之后 MCV 被缩到 10×9，离线探针口径是 b0 + x。
                var bx0 = limb.Bounds[0]; var by0 = limb.Bounds[1]; var bz0 = limb.Bounds[2];
                var bx1 = limb.Bounds[3]; var by1 = limb.Bounds[4]; var bz1 = limb.Bounds[5];
                for (var c = 0; c < 8; c++)
                {
                    var vx = (c & 1) != 0 ? bx1 : bx0;
                    var vy = (c & 2) != 0 ? by1 : by0;
                    var vz = (c & 4) != 0 ? bz1 : bz0;
                    var rx = vx * cos - vy * sin;
                    var ry = vx * sin + vy * cos;
                    var px = (rx - ry) * StepX;
                    var py = (rx + ry) * StepY - vz * StepZ;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
            }
            if (minX > maxX) return new IndexedImage { Error = "体素包围盒为空 " + name };

            var width = Mathf.Max(1, Mathf.CeilToInt(maxX - minX)) + 4;
            var height = Mathf.Max(1, Mathf.CeilToInt(maxY - minY)) + 4;
            var pixels = new byte[width * height];
            var depth = new float[width * height];
            for (var i = 0; i < depth.Length; i++) depth[i] = float.MinValue;

            // 画家顺序：x+y+z 越大越靠前（等距视角的深度轴）
            for (var li = 0; li < model.Limbs.Length; li++)
            {
                var limb = model.Limbs[li];
                if (limb == null) continue;
                var sx = limb.Size[0];
                var sy = limb.Size[1];
                var sz = limb.Size[2];
                var vx0 = limb.Bounds[0];
                var vy0 = limb.Bounds[1];
                var pages = VoxelLighting.ComputePages(limb.Type, cos, sin);
                var vpl = GetVpl();

                for (var z = 0; z < sz; z++)
                {
                    for (var y = 0; y < sy; y++)
                    {
                        for (var x = 0; x < sx; x++)
                        {
                            var idx = z * sx * sy + y * sx + x;
                            var ci = limb.Color[idx];
                            if (ci == 0) continue;
                            var ni = limb.Normal[idx];
                            var shaded = vpl != null ? vpl.Lookup(pages[ni], ci) : ci;

                            var mx = vx0 + x;
                            var my = vy0 + y;
                            var mz = z;
                            var rx = mx * cos - my * sin;
                            var ry = mx * sin + my * cos;

                            var px = (rx - ry) * StepX - minX + 2f;
                            var py = (rx + ry) * StepY - mz * StepZ - minY + 2f;
                            var wDepth = rx + ry + mz;

                            // 2×2 覆盖：相邻体素间距不足 2px，1px 会穿孔
                            for (var oy = 0; oy < 2; oy++)
                            {
                                for (var ox = 0; ox < 2; ox++)
                                {
                                    var tx = Mathf.FloorToInt(px) + ox;
                                    var ty = Mathf.FloorToInt(py) + oy;
                                    if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;
                                    var di = ty * width + tx;
                                    if (wDepth < depth[di]) continue;
                                    depth[di] = wDepth;
                                    pixels[di] = shaded;
                                }
                            }
                        }
                    }
                }
            }

            var image = new IndexedImage
            {
                Width = width,
                Height = height,
                Pixels = pixels,
                IsSprite = true,
                Origin = "vxl:" + name + "#" + facing,
            };
            Ra2md.Core.BootLog.Log($"[体素] 光栅 {name} 朝向{facing} → {width}×{height} 源={model.Origin}");
            return image;
        }
    }
}
