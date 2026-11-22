// VplFile.cs —— Bink/VXL 体素光照查找表（.vpl）解析。
//
// 口径对照参考项目 vpl_file.rs：
//   · 16 字节头：firstRemap u32、lastRemap u32、numSections u32、unknown u32；
//   · 768 字节内部调色板（256 RGB，通常不用）；
//   · numSections × 256 字节查找页：pages[page][颜色索引] → 光照后调色板索引。
// 页面号由法线·光源漫反射计算得到；缺文件时调用方回落原始颜色索引。
using System;

namespace Ra2md.Assets
{
    public sealed class VplFile
    {
        public int SectionCount { get; private set; }
        private byte[][] _pages;

        public static VplFile Parse(byte[] data)
        {
            if (data == null || data.Length < 16 + 768) return null;

            int firstRemap = BitConverter.ToInt32(data, 0);
            int lastRemap = BitConverter.ToInt32(data, 4);
            int numSections = BitConverter.ToInt32(data, 8);
            if (numSections <= 0 || numSections > 64) return null;

            const int headerEnd = 16 + 768;
            if (data.Length < headerEnd + numSections * 256) return null;

            var vpl = new VplFile { SectionCount = numSections, _pages = new byte[numSections][] };
            for (var s = 0; s < numSections; s++)
            {
                var page = new byte[256];
                Array.Copy(data, headerEnd + s * 256, page, 0, 256);
                vpl._pages[s] = page;
            }
            return vpl;
        }

        /// <summary>按亮度页与原色索引查光照后调色板索引；页号越界钳到末页。</summary>
        public byte Lookup(int page, byte color)
        {
            if (_pages == null || _pages.Length == 0) return color;
            var pageIndex = page < 0 ? 0 : (page >= _pages.Length ? _pages.Length - 1 : page);
            return _pages[pageIndex][color];
        }
    }
}
