// MapPreview.cs —— 地图预览图解码 + 缩略图渲染（对应 gamemd 的 PreviewClass）。
//
// 依据（反编译 + 参考工程，不目测）：
//  · YRpp\PreviewClass.h：ReadPreviewPack(INIClass&) @0x641B00 / WritePreviewPack @0x6418B0 /
//    DrawStartPoints(HWND) @0x640710。
//  · sub_641B00：从 INI 段 "Preview" 读 "Size" 矩形 → 建 v4×v5 的表面，再通过
//    FUN_0055c720(1,0x2000) + FUN_006c9890 挂一个**解压管道**，之后每像素读 3 字节
//    （sub_55C7C0(straw,&v18,3)）写进表面。即像素数据是压缩流，不是裸 RGB。
//  · 压缩口径以 ConcordRTS 的同源实现为准（src/data/maps/MapDatabase.cpp: decodeMapPreviewImage /
//    summarizePackedSection / decodeLzo1xBlock）：
//      [PreviewPack] 各行 base64 拼接 → 二进制；二进制是 Format5 分块链
//      （u16 compressedBytes、u16 uncompressedBytes、随后 compressedBytes 字节的 LZO1X 流）；
//      所有块解压后拼成 w*h*3 的 RGB888，再转 RGBA。
//  · 所以硬校验是「解压后总字节 == w*h*3」；不满足就报错返回，不裁剪、不猜测。
using System;
using System.Text;
using UnityEngine;
using Ra2md.Core;

namespace Ra2md.World
{
    public sealed class MapPreviewData
    {
        public int Width;
        public int Height;
        public byte[] Rgba;      // 左上原点，RGBA32
        public bool Valid;
        public string Reason;
    }

    public static class MapPreview
    {
        /// <summary>从地图 INI 里取 [Preview] Size + [PreviewPack]，解 LZO1X 后转 RGBA 位图。</summary>
        public static MapPreviewData Decode(MapIniDocument ini)
        {
            var result = new MapPreviewData();
            if (ini == null) { result.Reason = "无 INI"; return result; }

            var previewSec = ini.GetSection("Preview");
            var packSec = ini.GetSection("PreviewPack");
            if (packSec == null) { result.Reason = "无 [PreviewPack]"; return result; }

            var w = 0;
            var h = 0;
            if (previewSec != null)
            {
                var size = MapIniDocument.SplitComma(previewSec.GetString("Size", string.Empty));
                if (size.Length >= 4)
                {
                    w = MapIniDocument.ParseInt(size[2]) - MapIniDocument.ParseInt(size[0]);
                    h = MapIniDocument.ParseInt(size[3]) - MapIniDocument.ParseInt(size[1]);
                }
            }
            if (w <= 0 || h <= 0) { result.Reason = "[Preview] Size 缺失"; return result; }

            var sb = new StringBuilder(packSec.Entries.Count * 70);
            foreach (var e in packSec.Entries) sb.Append(e.Value.Trim());
            byte[] packed;
            try { packed = Convert.FromBase64String(PadBase64(sb.ToString())); }
            catch (Exception ex) { result.Reason = "base64 解码失败: " + ex.Message; return result; }

            var expect = w * h * 3;
            var rgb = new byte[expect];
            int written;
            string error;
            if (!UnpackFormat5Lzo(packed, rgb, out written, out error))
            {
                result.Reason = error;
                BootLog.Log("[预览] " + result.Reason);
                return result;
            }
            if (written != expect)
            {
                result.Reason = $"解压长度不符：得到 {written} 字节，按 Size {w}×{h} 应为 {expect}";
                BootLog.Log("[预览] " + result.Reason);
                return result;
            }

            var rgba = new byte[w * h * 4];
            for (var i = 0; i < w * h; i++)
            {
                // [PreviewPack] 存的是 RGB888（R 在前），Unity 侧直接用 RGBA 即可，无需换道
                rgba[i * 4] = rgb[i * 3];
                rgba[i * 4 + 1] = rgb[i * 3 + 1];
                rgba[i * 4 + 2] = rgb[i * 3 + 2];
                rgba[i * 4 + 3] = 255;
            }

            result.Width = w;
            result.Height = h;
            result.Rgba = rgba;
            result.Valid = true;
            return result;
        }

        private static string PadBase64(string value)
        {
            var mod = value.Length % 4;
            return mod == 0 ? value : value + new string('=', 4 - mod);
        }

        /// <summary>
        /// Format5 分块链（见 ConcordRTS summarizePackedSection）：
        /// 循环读 u16 comp / u16 uncomp，再按 comp 长度做 LZO1X 解压，累加到 outBuf。
        /// </summary>
        private static bool UnpackFormat5Lzo(byte[] packed, byte[] outBuf, out int written, out string error)
        {
            written = 0;
            error = null;
            var offset = 0;
            while (offset < packed.Length)
            {
                if (offset + 4 > packed.Length)
                {
                    error = "Format5 块头被截断 @" + offset;
                    return false;
                }
                var comp = packed[offset] | (packed[offset + 1] << 8);
                var uncomp = packed[offset + 2] | (packed[offset + 3] << 8);
                if (comp == 0)
                {
                    error = "Format5 零长度块 @" + offset;
                    return false;
                }
                if (offset + 4 + comp > packed.Length)
                {
                    error = "Format5 块越界 @" + offset;
                    return false;
                }
                if (written + uncomp > outBuf.Length)
                {
                    error = $"Format5 解压超出目标 {written}+{uncomp} > {outBuf.Length}";
                    return false;
                }
                string e;
                if (!Lzo1x.Decompress(packed, offset + 4, comp, uncomp, outBuf, written, out e))
                {
                    error = "LZO1X 解压失败 @" + offset + "：" + e;
                    return false;
                }
                written += uncomp;
                offset += 4 + comp;
            }
            return true;
        }

        /// <summary>
        /// 通用 Format5 段解码：把 [Section] 的所有键值拼成 base64 → LZO 分块链 → outBuf。
        /// 预览（[PreviewPack]）与地形（[IsoMapPack5]）用的是同一套容器，只是记录结构不同。
        /// </summary>
        public static bool UnpackSection(MapIniDocument ini, string sectionName, int maxBytes,
            out byte[] data, out int written, out string error)
        {
            data = null;
            written = 0;
            error = null;
            var sec = ini != null ? ini.GetSection(sectionName) : null;
            if (sec == null) { error = "无 [" + sectionName + "] 段"; return false; }

            var sb = new StringBuilder(sec.Entries.Count * 70);
            foreach (var e in sec.Entries) sb.Append((e.Value ?? string.Empty).Trim());
            byte[] packed;
            try { packed = Convert.FromBase64String(PadBase64(sb.ToString())); }
            catch (Exception ex) { error = "base64 解码失败: " + ex.Message; return false; }

            // 容量口径：段头里写了每块的 uncompressed 长度，先扫链求和，再按精确总长分配。
            // （不能用 packed*N 兜底：[IsoMapPack5] 的重复地形压缩比远超 16×，会假报“超出目标”。）
            var capacity = maxBytes;
            if (capacity <= 0)
            {
                int total;
                string scanError;
                if (!MeasureUncompressed(packed, out total, out scanError)) { error = scanError; return false; }
                capacity = total;
            }
            if (capacity <= 0) { error = "Format5 解压总长为 0"; return false; }

            var buf = new byte[capacity];
            if (!UnpackFormat5Lzo(packed, buf, out written, out error)) return false;
            data = buf;
            return true;
        }

        /// <summary>
        /// 同一套 Format5 容器，但块内数据是 **LCW(Format80)**：[OverlayPack] / [OverlayDataPack] 必须走这里
        /// （OpenRA ImportGen2MapCommand::UnpackLCW 用 LCWCompression 而不是 LZO1X）。
        /// </summary>
        public static bool UnpackSectionLcw(MapIniDocument ini, string sectionName, int maxBytes,
            out byte[] data, out int written, out string error)
        {
            data = null;
            written = 0;
            error = null;
            var sec = ini != null ? ini.GetSection(sectionName) : null;
            if (sec == null) { error = "无 [" + sectionName + "] 段"; return false; }

            var sb = new StringBuilder(sec.Entries.Count * 70);
            foreach (var e in sec.Entries) sb.Append((e.Value ?? string.Empty).Trim());
            byte[] packed;
            try { packed = Convert.FromBase64String(PadBase64(sb.ToString())); }
            catch (Exception ex) { error = "base64 解码失败: " + ex.Message; return false; }
            if (packed.Length == 0) { error = "[" + sectionName + "] 空"; return false; }

            int total;
            string scanError;
            if (!MeasureUncompressed(packed, out total, out scanError)) { error = scanError; return false; }
            var capacity = maxBytes > 0 && maxBytes < total ? maxBytes : total;
            if (capacity <= 0) { error = "解压总长为 0"; return false; }

            var buf = new byte[capacity];
            var offset = 0;
            var dst = 0;
            while (offset < packed.Length && dst < capacity)
            {
                if (offset + 4 > packed.Length) { error = "LCW 块头被截断 @" + offset; return false; }
                var comp = packed[offset] | (packed[offset + 1] << 8);
                var uncomp = packed[offset + 2] | (packed[offset + 3] << 8);
                if (comp == 0 || offset + 4 + comp > packed.Length) { error = "LCW 块非法 @" + offset; return false; }

                var block = new byte[uncomp];
                var got = LcwCompression.DecodeInto(packed, block, offset + 4, out var lcwError);
                if (got < 0) { error = "LCW 解压失败 @" + offset + "：" + lcwError; return false; }
                Array.Copy(block, 0, buf, dst, uncomp);
                dst += uncomp;
                offset += 4 + comp;
            }
            data = buf;
            written = dst;
            return true;
        }

        /// <summary>扫 Format5 块链，累加每块声明的解压长度（不做解压）。</summary>
        private static bool MeasureUncompressed(byte[] packed, out int total, out string error)
        {
            total = 0;
            error = null;
            var offset = 0;
            while (offset < packed.Length)
            {
                if (offset + 4 > packed.Length) { error = "Format5 块头被截断 @" + offset; return false; }
                var comp = packed[offset] | (packed[offset + 1] << 8);
                var uncomp = packed[offset + 2] | (packed[offset + 3] << 8);
                if (comp == 0) { error = "Format5 零长度块 @" + offset; return false; }
                if (offset + 4 + comp > packed.Length) { error = "Format5 块越界 @" + offset; return false; }
                total += uncomp;
                offset += 4 + comp;
            }
            return true;
        }

        /// <summary>RGBA 缓冲 → Texture2D（Y 翻转，Point 采样，和 SHP/PCX 一致）。</summary>
        public static Texture2D ToTexture(MapPreviewData data)
        {
            if (data == null || !data.Valid) return null;
            var w = data.Width;
            var h = data.Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                name = "MapPreview",
            };
            var colors = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                var srcRow = (h - 1 - y) * w;
                for (var x = 0; x < w; x++)
                {
                    var s = (srcRow + x) * 4;
                    colors[y * w + x] = new Color32(data.Rgba[s], data.Rgba[s + 1], data.Rgba[s + 2],
                        data.Rgba[s + 3]);
                }
            }
            tex.SetPixels32(colors);
            tex.Apply(false, false);
            return tex;
        }
    }

    /// <summary>
    /// LZO1X 解压（Westwood/RA2 用的就是标准 lzo1x 流），逐行对照
    /// ConcordRTS MapDatabase.cpp 的 decodeLzo1xBlock 移植，语义完全一致。
    /// </summary>
    internal static class Lzo1x
    {
        public static bool Decompress(byte[] data, int start, int compressed, int expected,
            byte[] dst, int dstStart, out string error)
        {
            error = null;
            var end = start + compressed;
            var offset = start;
            var written = 0;                       // 本块已写入 dst 的字节数
            uint token = 0;
            var firstBlock = true;
            var step = Step.Top;

            if (offset >= end) { error = "空 LZO 块"; return false; }

            while (true)
            {
                switch (step)
                {
                    case Step.Top:
                        if (firstBlock)
                        {
                            firstBlock = false;
                            if (data[offset] > 17)
                            {
                                token = (uint)(data[offset] - 17);
                                offset++;
                                if (token < 4)
                                {
                                    if (!CopyLiterals(data, ref offset, end, token, dst, dstStart, ref written, expected, out error)) return false;
                                    if (offset >= end) { error = "初段字面量后流结束"; return false; }
                                    token = data[offset];
                                    offset++;
                                    step = Step.Match;
                                    break;
                                }
                                if (!CopyLiterals(data, ref offset, end, token, dst, dstStart, ref written, expected, out error)) return false;
                                step = Step.FirstLiteralRun;
                                break;
                            }
                        }

                        if (offset >= end) { error = "命令前流结束"; return false; }
                        token = data[offset];
                        offset++;
                        if (token >= 16) { step = Step.Match; break; }
                        if (token == 0)
                        {
                            if (!ReadLenExtension(data, ref offset, end, 15, out token, out error)) return false;
                        }
                        if (!CopyLiterals(data, ref offset, end, token + 3, dst, dstStart, ref written, expected, out error)) return false;
                        step = Step.FirstLiteralRun;
                        break;

                    case Step.FirstLiteralRun:
                        if (offset >= end) { error = "匹配命令前流结束"; return false; }
                        token = data[offset];
                        offset++;
                        if (token >= 16) { step = Step.Match; break; }

                        if (offset >= end) { error = "短匹配缺偏移字节"; return false; }
                        {
                            var distance = 1 + 0x0800 + (token >> 2) + ((uint)data[offset] << 2);
                            offset++;
                            int src;
                            if (!Backref(written, distance, out src, out error)) return false;
                            if (!CopyRange(dst, dstStart, src, 3, ref written, expected, out error)) return false;
                        }
                        // 短匹配后直接进入匹配收尾（对应 C++ 的 matchDone 跳转）
                        if (offset < 2) { error = "匹配状态非法"; return false; }
                        token = (uint)(data[offset - 2] & 3);
                        if (token == 0) { step = Step.Top; break; }
                        if (!CopyLiterals(data, ref offset, end, token, dst, dstStart, ref written, expected, out error)) return false;
                        if (offset >= end) { error = "匹配后字面量处流结束"; return false; }
                        token = data[offset];
                        offset++;
                        step = Step.Match;
                        break;

                    case Step.Match:
                        if (token >= 64)
                        {
                            if (offset >= end) { error = "M2 匹配缺偏移字节"; return false; }
                            var distance = 1 + ((token >> 2) & 7) + ((uint)data[offset] << 3);
                            offset++;
                            int src;
                            if (!Backref(written, distance, out src, out error)) return false;
                            var count = (int)(token >> 5) + 1;
                            if (!CopyRange(dst, dstStart, src, count, ref written, expected, out error)) return false;
                        }
                        else if (token >= 32)
                        {
                            uint countToken = token & 31u;
                            if (countToken == 0)
                            {
                                if (!ReadLenExtension(data, ref offset, end, 31, out countToken, out error)) return false;
                            }
                            if (offset + 2 > end) { error = "M3 匹配缺偏移"; return false; }
                            var raw = (uint)(data[offset] | (data[offset + 1] << 8));
                            offset += 2;
                            var distance = 1 + (raw >> 2);
                            int src;
                            if (!Backref(written, distance, out src, out error)) return false;
                            if (!CopyRange(dst, dstStart, src, (int)countToken + 2, ref written, expected, out error)) return false;
                        }
                        else if (token >= 16)
                        {
                            var highDistance = (token & 8u) << 11;
                            uint countToken = token & 7u;
                            if (countToken == 0)
                            {
                                if (!ReadLenExtension(data, ref offset, end, 7, out countToken, out error)) return false;
                            }
                            if (offset + 2 > end) { error = "M4 匹配缺偏移"; return false; }
                            var lowDistance = (uint)(data[offset] | (data[offset + 1] << 8)) >> 2;
                            offset += 2;

                            if (highDistance == 0 && lowDistance == 0)
                            {
                                if (written != expected) { error = "LZO 结束标记处字节数不符"; return false; }
                                if (offset != end) { error = "结束标记后仍有尾随字节"; return false; }
                                return true;
                            }

                            var distance = highDistance + lowDistance + 0x4000;
                            int src;
                            if (!Backref(written, distance, out src, out error)) return false;
                            if (!CopyRange(dst, dstStart, src, (int)countToken + 2, ref written, expected, out error)) return false;
                        }
                        else
                        {
                            if (offset >= end) { error = "M1 匹配缺偏移字节"; return false; }
                            var distance = 1 + (token >> 2) + ((uint)data[offset] << 2);
                            offset++;
                            int src;
                            if (!Backref(written, distance, out src, out error)) return false;
                            if (!CopyRange(dst, dstStart, src, 2, ref written, expected, out error)) return false;
                        }

                        if (offset < 2) { error = "匹配状态非法"; return false; }
                        token = (uint)(data[offset - 2] & 3);
                        if (token == 0) { step = Step.Top; break; }
                        if (!CopyLiterals(data, ref offset, end, token, dst, dstStart, ref written, expected, out error)) return false;
                        if (offset >= end) { error = "匹配后字面量处流结束"; return false; }
                        token = data[offset];
                        offset++;
                        break;
                }
            }
        }

        private enum Step { Top, FirstLiteralRun, Match }
        private static bool CopyLiterals(byte[] data, ref int offset, int end, uint count,
            byte[] dst, int dstStart, ref int written, int expected, out string error)
        {
            error = null;
            if (offset + (int)count > end) { error = "字面量段越界"; return false; }
            for (var i = 0; i < (int)count; i++)
            {
                if (written >= expected) { error = "输出超出预期字节数"; return false; }
                dst[dstStart + written] = data[offset + i];
                written++;
            }
            offset += (int)count;
            return true;
        }

        private static bool CopyRange(byte[] dst, int dstStart, int srcIndex, int count,
            ref int written, int expected, out string error)
        {
            error = null;
            if (srcIndex >= written) { error = "匹配源在已输出区之外"; return false; }
            for (var i = 0; i < count; i++)
            {
                var read = srcIndex + i;
                if (read >= written) { error = "匹配源越过已输出区"; return false; }
                if (written >= expected) { error = "输出超出预期字节数"; return false; }
                dst[dstStart + written] = dst[dstStart + read];
                written++;
            }
            return true;
        }

        private static bool Backref(int written, uint distance, out int srcIndex, out string error)
        {
            error = null;
            srcIndex = 0;
            if (distance > (uint)written) { error = "匹配指向已输出区之前"; return false; }
            srcIndex = written - (int)distance;
            return true;
        }

        private static bool ReadLenExtension(byte[] data, ref int offset, int end, uint baseLen,
            out uint length, out string error)
        {
            error = null;
            length = 0;
            while (offset < end && data[offset] == 0) { length += 255; offset++; }
            if (offset >= end) { error = "扩展长度被截断"; return false; }
            length += baseLen + data[offset];
            offset++;
            return true;
        }
    }
}



