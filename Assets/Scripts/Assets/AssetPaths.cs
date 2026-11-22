// AssetPaths.cs —— 资源路径解析：运行时优先 Assets/StreamingAssets，开发期回退到本地原始资源目录。
using System.IO;
using UnityEngine;

namespace Ra2md.Assets
{
    public static class AssetPaths
    {
        // 开发期回退目录（大体积 MIX 暂不复制进工程）；打包后此目录不存在，自动跳过。
        private const string DevFallbackDir = @"D:\__PGAME\content\RA2MD_LITE";

        /// <summary>按相对资源名解析绝对路径；优先 StreamingAssets，其次开发回退目录。</summary>
        public static string Resolve(string relativeName)
        {
            var streaming = Path.Combine(Application.streamingAssetsPath, relativeName);
            if (File.Exists(streaming)) return streaming;

            if (Directory.Exists(DevFallbackDir))
            {
                var dev = Path.Combine(DevFallbackDir, relativeName);
                if (File.Exists(dev)) return dev;
            }

            return streaming;
        }

        /// <summary>开发回退目录是否存在（仅编辑器/开发期用于补全未打包资源）。</summary>
        public static bool HasDevFallback => Directory.Exists(DevFallbackDir);

        /// <summary>列出开发回退目录下指定扩展名的松散资源名（相对名）。</summary>
        public static string[] EnumerateLoose(string extension)
        {
            if (!Directory.Exists(DevFallbackDir)) return System.Array.Empty<string>();
            return Directory.GetFiles(DevFallbackDir, "*" + extension);
        }
    }
}
