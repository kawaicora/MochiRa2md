// MenuResourceLibrary.cs —— 主菜单资源装载。
// 背景：expandmd01.mix 的 65AC42BD（632×568 静态背景），调色板 8412036B。
// 资源来源一律走 VirtualFileSystem：先按已知 ID 取，再按名字兜底，绝不硬编码文件路径。
using System;
using System.Collections.Generic;
using UnityEngine;
using Ra2md.Assets;

namespace Ra2md.Menu
{
    public sealed class MenuBackground
    {
        public ShpImage Image;
        public byte[] Palette;
        public string Origin;
        public Texture2D[] FrameTextures;

        public int FrameCount => Image == null ? 0 : Image.FrameCount;

        public Texture2D FirstFrame
        {
            get
            {
                if (FrameTextures == null || FrameTextures.Length == 0) return null;
                return FrameTextures[0];
            }
        }
    }

    public static class MenuResourceLibrary
    {
        // 原版主菜单背景（632×568，苏联战车 + 火海）；条目 ID 归口全局配置管理器。
        public const uint BackgroundShpId = Ra2md.Managers.GlobalConfigManager.Resources.BackgroundShpId;
        // 该背景配套调色板
        public const uint BackgroundPaletteId = Ra2md.Managers.GlobalConfigManager.Resources.BackgroundPaletteId;

        // 右列警告屏动画配套调色板（实测逐像素对原版截图最优；MIX 里无文件名，按别名登记）。
        public const uint WarningPaletteId = Ra2md.Managers.GlobalConfigManager.Resources.WarningPaletteId;

        private static readonly Dictionary<uint, byte[]> RawCache = new Dictionary<uint, byte[]>();
        private static bool _mounted;

        /// <summary>确保 StreamingAssets 下的全部 MIX 已挂进 VFS（只做一次）。</summary>
        public static VirtualFileSystem EnsureMounted()
        {
            var vfs = VirtualFileSystem.Instance;
            if (_mounted && vfs.Loaded) return vfs;

            string root = Application.streamingAssetsPath;
            if (!string.IsNullOrEmpty(root))
            {
                vfs.ScanDirectory(root);
                MixNameTable.RegisterDirectory(root, false);
                RegisterIniNames(vfs);
            }

            // 实测 ID 与原名 CRC 对不上的调色板，按逻辑名登记别名（与是否挂载无关，始终生效）。
            MixNameTable.RegisterAlias(
                Ra2md.Managers.GlobalConfigManager.Resources.WarningPaletteAlias, WarningPaletteId);

            _mounted = true;
            return vfs;
        }

        /// <summary>
        /// 用 art(md).ini 补名字表：MIX 里很多条目没有文件名，只有 CRC，
        /// 名字来自 INI 才能按 CRC 反查（先找 StreamingAssets 松散文件，再进 MIX）。
        /// </summary>
        private static void RegisterIniNames(VirtualFileSystem vfs)
        {
            int total = 0;
            foreach (var ini in IniCandidates())
            {
                if (!vfs.TryResolve(ini, out var bytes, out _) || bytes == null || bytes.Length == 0) continue;
                string text = System.Text.Encoding.ASCII.GetString(bytes);
                // INI 可能带 \0 填充
                int zero = text.IndexOf('\0');
                if (zero >= 0) text = text.Substring(0, zero);
                total += MixNameTable.RegisterIni(text);
            }
            if (total > 0) Debug.Log("[菜单] INI 名字登记 " + total + " 条，名字表共 " + MixNameTable.Count + " 条");
        }

        private static string[] IniCandidates()
        {
            var art = Ra2md.Managers.GlobalConfigManager.Resources.ArtIniNames;
            var rules = Ra2md.Managers.GlobalConfigManager.Resources.RulesIniNames;
            var result = new string[art.Length + rules.Length];
            Array.Copy(art, 0, result, 0, art.Length);
            Array.Copy(rules, 0, result, art.Length, rules.Length);
            return result;
        }

        /// <summary>按 ID 取原始字节（带一次性缓存）。</summary>
        public static byte[] LoadRaw(uint id, out string origin)
        {
            if (RawCache.TryGetValue(id, out var cached))
            {
                origin = "缓存";
                return cached;
            }

            var vfs = EnsureMounted();
            if (!vfs.TryResolveById(id, out var data, out origin)) return null;
            RawCache[id] = data;
            return data;
        }

        /// <summary>按名字取原始字节（松散文件优先）。</summary>
        public static byte[] LoadRawByName(string name, out string origin)
        {
            var vfs = EnsureMounted();
            if (!vfs.TryResolve(name, out var data, out origin)) return null;
            return data;
        }

        /// <summary>按一组候选名取第一个命中的（根目录优先由 VFS 判断）。</summary>
        public static bool TryLoadFirst(string[] candidateNames, out byte[] data, out string origin)
        {
            var vfs = EnsureMounted();
            return vfs.TryResolveFirst(candidateNames, out data, out origin);
        }

        /// <summary>装载主菜单背景（含调色板与全部帧纹理）。</summary>
        public static MenuBackground LoadMainMenuBackground()
        {
            var result = new MenuBackground();

            var shpBytes = LoadRaw(BackgroundShpId, out string shpOrigin);
            if (shpBytes == null)
            {
                Debug.LogWarning("[菜单] 找不到主菜单背景 0x" + BackgroundShpId.ToString("X8"));
                return null;
            }

            if (!ShpReader.TryParse(shpBytes, out var image, out string error))
            {
                Debug.LogWarning("[菜单] 主菜单背景解析失败：" + error);
                return null;
            }

            var palBytes = LoadRaw(BackgroundPaletteId, out string palOrigin);
            if (palBytes == null || !PalReader.TryDecode(palBytes, out var palette, out _))
            {
                Debug.LogWarning("[菜单] 缺少主菜单调色板 0x" + BackgroundPaletteId.ToString("X8") + "，改用灰度兜底");
                palette = BuildGrayscalePalette();
                palOrigin = "灰度兜底";
            }

            result.Image = image;
            result.Palette = palette;
            result.Origin = shpOrigin + " / " + palOrigin;

            int frames = Math.Min(image.FrameCount, 64);   // 背景最多预解 64 帧
            var textures = new List<Texture2D>(frames);
            for (int i = 0; i < frames; i++)
            {
                var texture = ShpTextureFactory.FromFrame(image, i, palette, "主菜单背景#" + i);
                if (texture != null) textures.Add(texture);
            }
            result.FrameTextures = textures.ToArray();

            Debug.Log("[菜单] 背景装载完成 " + image.CanvasWidth + "×" + image.CanvasHeight
                      + "，帧数 " + image.FrameCount + "，来源 " + result.Origin);
            return result;
        }

        /// <summary>取一个带透明度的纯色 1×1 纹理，供 UI 面板/遮罩使用。</summary>
        public static Texture2D CreateSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Solid",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        public static byte[] BuildGrayscalePalette()
        {
            var palette = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                byte value = (byte)i;
                palette[i * 4] = value;
                palette[i * 4 + 1] = value;
                palette[i * 4 + 2] = value;
                palette[i * 4 + 3] = i == 0 ? (byte)0 : (byte)255;
            }
            return palette;
        }

        // ---- 主菜单工位资源（800×600 布局：中央 632×570 视频 + 底部 632×30 金属条 + 右侧 168 宽按钮列）----

        /// <summary>底部金属信息条 SHP（local.mix 632×32，10 帧）。</summary>
        public const uint InfoBarShpId = Ra2md.Managers.GlobalConfigManager.Resources.InfoBarShpId;
        /// <summary>信息条配套调色板。</summary>
        public const uint InfoBarPaletteId = Ra2md.Managers.GlobalConfigManager.Resources.InfoBarPaletteId;

        /// <summary>启动片头影片相对路径。</summary>
        public const string BootMoviePath = Ra2md.Managers.GlobalConfigManager.Resources.BootMoviePath;
        /// <summary>主菜单循环背景影片相对路径。</summary>
        public const string MenuMoviePath = Ra2md.Managers.GlobalConfigManager.Resources.MenuMoviePath;
        /// <summary>主菜单 BGM 相对路径。</summary>
        public const string MenuBgmPath = Ra2md.Managers.GlobalConfigManager.Resources.MenuBgmPath;

        /// <summary>片头影片的帧序列目录兜底。</summary>
        public const string BootMovieFramesDir = Ra2md.Managers.GlobalConfigManager.Resources.BootMovieFramesDir;
        /// <summary>主菜单背景影片的帧序列目录兜底。</summary>
        public const string MenuMovieFramesDir = Ra2md.Managers.GlobalConfigManager.Resources.MenuMovieFramesDir;
        /// <summary>两段影片的原始帧率。</summary>
        public const int MovieFps = Ra2md.Managers.GlobalConfigManager.Resources.MenuMovieFps;

        /// <summary>按给定 ID 的 SHP + 调色板渲染某一帧为点采样纹理。</summary>
        public static Texture2D LoadShpFrameTexture(uint shpId, uint paletteId, int frameIndex, string name,
            bool transparentZero = true)
        {
            var shpBytes = LoadRaw(shpId, out string shpOrigin);
            if (shpBytes == null)
            {
                Debug.LogWarning("[菜单] 找不到 SHP 0x" + shpId.ToString("X8"));
                return null;
            }

            if (!ShpReader.TryParse(shpBytes, out var image, out string parseError))
            {
                Debug.LogWarning("[菜单] SHP 0x" + shpId.ToString("X8") + " 解析失败：" + parseError);
                return null;
            }

            byte[] palette;
            var palBytes = LoadRaw(paletteId, out string palOrigin);
            if (palBytes == null || !PalReader.TryDecode(palBytes, out palette, out _))
            {
                palette = BuildGrayscalePalette();
                palOrigin = "灰度兜底";
            }

            frameIndex = Mathf.Clamp(frameIndex, 0, image.FrameCount - 1);
            var texture = ShpTextureFactory.FromFrame(image, frameIndex, palette, name, transparentZero);
            if (texture != null)
            {
                Debug.Log("[菜单] " + name + " = 0x" + shpId.ToString("X8")
                          + " " + image.CanvasWidth + "×" + image.CanvasHeight
                          + " 帧 " + frameIndex + "/" + image.FrameCount
                          + " 来源 " + shpOrigin + " / " + palOrigin);
            }
            return texture;
        }

        /// <summary>把原版资源文件名换算成 MIX 目录项 ID（西木 CRC32）。</summary>
        public static uint NameId(string fileName)
        {
            return MixArchive.FileIdFromName(fileName);
        }

        public static void ClearCache()
        {
            RawCache.Clear();
            _mounted = false;
        }
    }
}
