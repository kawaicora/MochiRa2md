// LcwCompression.cs —— Westwood LCW / Format80 解压器。
//
// 依据：OpenRA.Mods.Cnc\FileFormats\LCWCompression.cs（DecodeInto，逐行照抄控制流），
// 用于 [OverlayPack] / [OverlayDataPack]：它们的容器和 [IsoMapPack5]/[PreviewPack] 一样是
// “u16 comp / u16 uncomp / comp 字节数据”的分块链，但块内数据是 **LCW** 而不是 LZO1X
// （OpenRA ImportGen2MapCommand::UnpackLCW 用 LCWCompression.DecodeInto，不是 LZO）。
// 原来把 OverlayPack 当 LZO 解会直接在 @0 报“字面量段越界”，地图里就一个 overlay 都没有。
using System;

namespace Ra2md.World
{
    public static class LcwCompression
    {
        /// <summary>把 src[srcOffset..] 的 LCW 流解到 dest（从 0 开始），返回写入字节数；失败返回 -1。</summary>
        public static int DecodeInto(byte[] src, byte[] dest, int srcOffset, out string error)
        {
            error = null;
            if (src == null || dest == null) { error = "空缓冲"; return -1; }

            var srcIdx = srcOffset;
            var destIdx = 0;
            while (true)
            {
                if (srcIdx >= src.Length) { error = "LCW 流提前结束 @" + srcIdx; return -1; }
                var i = src[srcIdx++];

                if ((i & 0x80) == 0)
                {
                    // case 2：0 0ccc dddd + 低 8 位 → 从 destIdx-rpos 复制 count 字节
                    if (srcIdx >= src.Length) { error = "LCW case2 缺低位 @" + srcIdx; return -1; }
                    var secondByte = src[srcIdx++];
                    var count = ((i & 0x70) >> 4) + 3;
                    var rpos = ((i & 0x0F) << 8) + secondByte;
                    if (destIdx + count > dest.Length) return destIdx;
                    var from = destIdx - rpos;
                    if (from < 0) { error = "LCW case2 回指越界"; return -1; }
                    for (var k = 0; k < count; k++)
                    {
                        // 距离 1 时逐字节自复制（OpenRA ReplicatePrevious 的 destIndex-srcIndex==1 分支）
                        dest[destIdx + k] = rpos == 1 ? dest[destIdx + k - 1] : dest[from + k];
                    }
                    destIdx += count;
                }
                else if ((i & 0x40) == 0)
                {
                    // case 1：10cccccc + 原样 count 字节
                    var count = i & 0x3F;
                    if (count == 0) return destIdx;                 // 终止符
                    if (srcIdx + count > src.Length) { error = "LCW case1 越界"; return -1; }
                    if (destIdx + count > dest.Length) return destIdx;
                    Array.Copy(src, srcIdx, dest, destIdx, count);
                    srcIdx += count;
                    destIdx += count;
                }
                else
                {
                    var count3 = i & 0x3F;
                    if (count3 == 0x3E)
                    {
                        // case 4：0xFE + u16 count + 1 字节 color → RLE
                        if (srcIdx + 2 >= src.Length) { error = "LCW case4 越界"; return -1; }
                        var count = src[srcIdx] | (src[srcIdx + 1] << 8);
                        var color = src[srcIdx + 2];
                        srcIdx += 3;
                        if (destIdx + count > dest.Length) return destIdx;
                        for (var end = destIdx + count; destIdx < end; destIdx++) dest[destIdx] = color;
                    }
                    else
                    {
                        // case 3（count3+3）/ case 5（0xFF + u16 count）：0xFF/… + u16 srcIndex 复制
                        int count;
                        if (count3 == 0x3F)
                        {
                            if (srcIdx + 1 >= src.Length) { error = "LCW case5 越界"; return -1; }
                            count = src[srcIdx] | (src[srcIdx + 1] << 8);
                            srcIdx += 2;
                        }
                        else count = count3 + 3;

                        if (srcIdx + 1 >= src.Length) { error = "LCW 回指偏移越界"; return -1; }
                        var srcIndex = src[srcIdx] | (src[srcIdx + 1] << 8);
                        srcIdx += 2;
                        if (srcIndex >= destIdx) { error = $"LCW 回指 {srcIndex} >= 写指针 {destIdx}"; return -1; }
                        if (destIdx + count > dest.Length) return destIdx;

                        for (var end = destIdx + count; destIdx < end; destIdx++)
                            dest[destIdx] = dest[srcIndex++];
                    }
                }
            }
        }
    }
}
