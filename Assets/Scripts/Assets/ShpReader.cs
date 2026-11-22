// ShpReader.cs —— Westwood SHP(TS) 索引图像解码。
// 文件头 8 字节：uint16 zero、uint16 canvasWidth、uint16 canvasHeight、uint16 frameCount
// 帧记录 24 字节：int16 x、int16 y、int16 width、int16 height、byte format、byte 保留、10 字节保留、uint32 dataOffset
// format 0/1：width*height 原始索引像素
// format 2：逐行 uint16 行长（含长度字）+ 逐行字面像素
// format 3：逐行 uint16 行长 + 透明游程（0x00 → 跳过下一字节个像素）
using System;
using System.Collections.Generic;

namespace Ra2md.Assets
{
    public sealed class ShpFrameInfo
    {
        public int Index;
        public int OffsetX;
        public int OffsetY;
        public int Width;
        public int Height;
        public int Format;
        public uint DataOffset;
        public bool Empty;   // 纯占位帧（宽高为 0）
    }

    public sealed class ShpImage
    {
        public int CanvasWidth;
        public int CanvasHeight;
        public int FrameCount;
        public readonly List<ShpFrameInfo> Frames = new List<ShpFrameInfo>();

        public bool TryDecodeFrame(int index, out byte[] pixels, out int width, out int height, out string error)
        {
            pixels = null;
            width = 0;
            height = 0;
            error = null;

            if (index < 0 || index >= Frames.Count)
            {
                error = "帧索引越界";
                return false;
            }

            var frame = Frames[index];
            if (frame.Empty)
            {
                width = 0;
                height = 0;
                pixels = Array.Empty<byte>();
                return true;
            }

            width = frame.Width;
            height = frame.Height;

            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            {
                error = "帧尺寸非法";
                return false;
            }

            if (frame.DataOffset >= _source.Length)
            {
                error = "帧数据偏移越界";
                return false;
            }

            var payload = new ReadOnlySpan<byte>(_source, (int)frame.DataOffset,
                _source.Length - (int)frame.DataOffset);

            switch (frame.Format)
            {
                case 0:
                case 1:
                {
                    int count = width * height;
                    if (count > payload.Length)
                    {
                        error = "原始帧数据不足";
                        return false;
                    }
                    pixels = payload.Slice(0, count).ToArray();
                    return true;
                }
                case 2:
                case 3:
                    return DecodeRows(payload, frame.Format, width, height, out pixels, out error);
                default:
                    error = "不支持的 SHP 格式 " + frame.Format;
                    return false;
            }
        }

        private byte[] _source;

        internal void AttachSource(byte[] source) => _source = source;

        private static bool TakeRow(ref ReadOnlySpan<byte> remaining, out ReadOnlySpan<byte> row, out string error)
        {
            row = default;
            error = null;
            if (remaining.Length < 2)
            {
                error = "行长度字被截断";
                return false;
            }

            int stored = remaining[0] | (remaining[1] << 8);
            if (stored < 2)
            {
                error = "行长度非法";
                return false;
            }
            if (stored > remaining.Length)
            {
                error = "行数据超出文件尾";
                return false;
            }

            row = remaining.Slice(2, stored - 2);
            remaining = remaining.Slice(stored);
            return true;
        }

        private static bool DecodeRows(ReadOnlySpan<byte> payload, int format, int width, int height,
            out byte[] pixels, out string error)
        {
            pixels = new byte[width * height];
            error = null;

            for (int rowIndex = 0; rowIndex < height; rowIndex++)
            {
                if (!TakeRow(ref payload, out var encoded, out error)) return false;

                var row = new Span<byte>(pixels, rowIndex * width, width);

                if (format == 2)
                {
                    int copy = Math.Min(encoded.Length, row.Length);
                    encoded.Slice(0, copy).CopyTo(row);
                    continue;
                }

                // format 3：透明游程
                int column = 0;
                while (encoded.Length > 0)
                {
                    byte token = encoded[0];
                    encoded = encoded.Slice(1);
                    if (token != 0)
                    {
                        if (column < row.Length) row[column] = token;
                        column++;
                    }
                    else
                    {
                        if (encoded.Length == 0)
                        {
                            error = "透明游程被截断";
                            return false;
                        }
                        column += encoded[0];
                        encoded = encoded.Slice(1);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 把帧按帧头 OffsetX/OffsetY 合进 CanvasWidth×CanvasHeight 的整张画布（索引色，0 透明）。
        /// RA2 的树/灯（TREE15.sno：canvas=107×107、帧=50×54@31,0）只有按画布尺寸落位才对得上格子；
        /// 直接用帧尺寸会把树缩到一格大小。
        /// </summary>
        public bool TryDecodeCanvas(int index, out byte[] pixels, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            pixels = null;
            error = null;

            if (index < 0 || index >= Frames.Count) { error = "帧索引越界"; return false; }
            if (CanvasWidth <= 0 || CanvasHeight <= 0 || CanvasWidth > 2048 || CanvasHeight > 2048)
            {
                error = "SHP 画布尺寸非法";
                return false;
            }

            var frame = Frames[index];
            if (frame.Empty || frame.Width <= 0 || frame.Height <= 0)
            {
                width = CanvasWidth;
                height = CanvasHeight;
                pixels = new byte[width * height];
                return true;
            }

            if (!TryDecodeFrame(index, out var framePixels, out var fw, out var fh, out error)) return false;

            width = CanvasWidth;
            height = CanvasHeight;
            pixels = new byte[width * height];

            var ox = frame.OffsetX;
            var oy = frame.OffsetY;

            // 越界的帧（negative offset / 超出画布）先裁到画布内，不改变画布尺寸
            for (var y = 0; y < fh; y++)
            {
                var dy = oy + y;
                if (dy < 0 || dy >= height) continue;
                var srcRow = y * fw;
                var dstRow = dy * width;
                for (var x = 0; x < fw; x++)
                {
                    var dx = ox + x;
                    if (dx < 0 || dx >= width) continue;
                    var v = framePixels[srcRow + x];
                    if (v != 0) pixels[dstRow + dx] = v;
                }
            }

            return true;
        }
    }

    public static class ShpReader
    {
        public static bool TryParse(byte[] data, out ShpImage image, out string error)
        {
            image = null;
            error = null;

            if (data == null || data.Length < 8)
            {
                error = "SHP 文件太小";
                return false;
            }

            int frameCount = data[6] | (data[7] << 8);
            if (frameCount <= 0)
            {
                error = "SHP 没有帧";
                return false;
            }

            int tableBytes = 8 + frameCount * 24;
            if (data.Length < tableBytes)
            {
                error = "SHP 帧表被截断（需要 " + tableBytes + " 字节，实际 " + data.Length + "）";
                return false;
            }

            image = new ShpImage
            {
                CanvasWidth = data[2] | (data[3] << 8),
                CanvasHeight = data[4] | (data[5] << 8),
                FrameCount = frameCount,
            };
            image.AttachSource(data);

            for (int i = 0; i < frameCount; i++)
            {
                int record = 8 + i * 24;
                int x = (short)(data[record] | (data[record + 1] << 8));
                int y = (short)(data[record + 2] | (data[record + 3] << 8));
                int w = (short)(data[record + 4] | (data[record + 5] << 8));
                int h = (short)(data[record + 6] | (data[record + 7] << 8));
                int format = data[record + 8];
                uint offset = (uint)(data[record + 20] | (data[record + 21] << 8)
                              | (data[record + 22] << 16) | (data[record + 23] << 24));

                image.Frames.Add(new ShpFrameInfo
                {
                    Index = i,
                    OffsetX = x,
                    OffsetY = y,
                    Width = w,
                    Height = h,
                    Format = format,
                    DataOffset = offset,
                    Empty = w == 0 && h == 0 && offset == 0,
                });
            }

            return true;
        }
    }
}
