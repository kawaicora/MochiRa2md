// MixSource.cs —— MIX 的随机访问字节源。
// 设计目标（对齐 XCC Mixer 的用法）：只按需读一个条目，绝不把整档读进内存。
//   * FileMixSource   ：磁盘文件，内核只有一条 FileStream，按 offset seek+read
//   * MemoryMixSource ：显式给定的字节（编辑器/测试用）
//   * SliceMixSource  ：父源上的一个区间（嵌套 MIX 用它，不复制任何字节）
using System;
using System.IO;

namespace Ra2md.Assets
{
    /// <summary>带随机访问能力的只读字节源。</summary>
    public interface IMixByteSource
    {
        long Length { get; }
        string DisplayName { get; }
        /// <summary>从 offset 读 count 字节；返回实际读到的字节数（0 表示 EOF）。</summary>
        int Read(long offset, byte[] buffer, int bufferOffset, int count);
    }

    public sealed class FileMixSource : IMixByteSource, IDisposable
    {
        private readonly FileStream _stream;
        private readonly object _lock = new object();

        public string DisplayName { get; }
        public long Length { get; }

        public FileMixSource(string path)
        {
            DisplayName = Path.GetFileName(path);
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Length = _stream.Length;
        }

        public int Read(long offset, byte[] buffer, int bufferOffset, int count)
        {
            if (offset < 0 || count <= 0) return 0;
            lock (_lock)
            {
                if (offset >= Length) return 0;
                if (offset + count > Length) count = (int)(Length - offset);
                _stream.Seek(offset, SeekOrigin.Begin);
                int total = 0;
                while (total < count)
                {
                    int n = _stream.Read(buffer, bufferOffset + total, count - total);
                    if (n <= 0) break;
                    total += n;
                }
                return total;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _stream.Dispose();
            }
        }
    }

    public sealed class MemoryMixSource : IMixByteSource
    {
        private readonly byte[] _data;

        public string DisplayName { get; }
        public long Length { get { return _data.Length; } }

        public MemoryMixSource(byte[] data, string displayName)
        {
            _data = data ?? Array.Empty<byte>();
            DisplayName = displayName;
        }

        public int Read(long offset, byte[] buffer, int bufferOffset, int count)
        {
            if (offset < 0 || offset >= _data.Length || count <= 0) return 0;
            if (offset + count > _data.Length) count = (int)(_data.Length - offset);
            Array.Copy(_data, (int)offset, buffer, bufferOffset, count);
            return count;
        }
    }

    /// <summary>父源上的一个区间：嵌套 MIX 直接映射进父档，不复制。</summary>
    public sealed class SliceMixSource : IMixByteSource
    {
        private readonly IMixByteSource _parent;
        private readonly long _start;

        public string DisplayName { get; }
        public long Length { get; }

        public SliceMixSource(IMixByteSource parent, long start, long length, string displayName)
        {
            _parent = parent;
            _start = start;
            Length = Math.Max(0, Math.Min(length, parent.Length - start));
            DisplayName = displayName;
        }

        public int Read(long offset, byte[] buffer, int bufferOffset, int count)
        {
            if (offset < 0 || offset >= Length || count <= 0) return 0;
            if (offset + count > Length) count = (int)(Length - offset);
            return _parent.Read(_start + offset, buffer, bufferOffset, count);
        }
    }
}
