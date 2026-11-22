// MixArchive.cs —— Westwood MIX 归档读取（RA2 / 尤里的复仇）。
// 支持：
//   * 未加密目录（普通 MIX + 旧版无标志头）
//   * 目录加密（flags 0x00020000）：RSA-65537 解包 Blowfish 密钥 → Blowfish ECB 解目录
//   * 任意随机访问源（磁盘文件 / 内存 / 父档切片）
//
// 重要：装载只读「头部 + 目录」，条目字节按需 seek 读取，绝不整档读入内存。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ra2md.Assets
{
    public sealed class MixEntry
    {
        public uint Id;              // CRC32(文件名)，目录项 0..3
        public uint Offset;          // 相对数据区偏移，4..7
        public uint Size;            // 8..11
        public long AbsoluteOffset;  // 数据区起点 + Offset
        public string Name;          // 名字（来自词典回填，可为空）
    }

    public sealed class MixArchive
    {
        private const uint FlagChecksum = 0x00010000;
        private const uint FlagEncryptedIndex = 0x00020000;
        private const uint KnownFlags = FlagChecksum | FlagEncryptedIndex;
        private const int EntrySize = 12;
        private const int KeyBlockBytes = 80;
        private const int BlockBytes = 8;

        private readonly List<MixEntry> _entries = new List<MixEntry>();
        private readonly Dictionary<uint, int> _byId = new Dictionary<uint, int>();

        private IMixByteSource _source;
        private long _length;

        public string Path { get; private set; }
        public string DisplayName { get; private set; }
        public uint Flags { get; private set; }
        public ushort FileCount { get; private set; }
        public uint DataSize { get; private set; }
        public long DataStartOffset { get; private set; }
        public bool HasChecksum { get; private set; }
        public bool HasEncryptedIndex { get; private set; }
        public bool IndexReadable { get; private set; }
        public string Error { get; private set; }
        public IReadOnlyList<MixEntry> Entries => _entries;

        /// <summary>本档的字节源（嵌套档时是父档上的切片）。</summary>
        public IMixByteSource Source => _source;

        public long Length => _length;

        // ---- 装载入口 ----

        /// <summary>打开磁盘上的 MIX：只读头部与目录，文件句柄保留供按需读取。</summary>
        public bool Load(string path)
        {
            if (!File.Exists(path))
            {
                Reset();
                Path = path;
                DisplayName = System.IO.Path.GetFileName(path);
                Error = "MIX not found: " + path;
                return false;
            }

            Path = path;
            return Open(new FileMixSource(path), System.IO.Path.GetFileName(path));
        }

        /// <summary>从显式字节打开（编辑器/测试）。</summary>
        public bool LoadFromBytes(byte[] data, string displayName)
        {
            Path = null;
            return Open(new MemoryMixSource(data, displayName), displayName);
        }

        /// <summary>
        /// 打开父档里的嵌套 MIX：源是父档上的切片，不复制字节，
        /// 因此 374MB 的 audio.mix 也只占一条 FileStream 加一份目录。
        /// </summary>
        public bool OpenNested(IMixByteSource parent, long offset, long size, string displayName)
        {
            Path = null;
            return Open(new SliceMixSource(parent, offset, size, displayName), displayName);
        }

        /// <summary>从任意随机访问源打开，仅解析头部与目录。</summary>
        public bool Open(IMixByteSource source, string displayName)
        {
            Reset();
            _source = source;
            _length = source != null ? source.Length : 0;
            DisplayName = displayName;

            if (_source == null || _length < 6)
            {
                Error = "MIX 源为空或过短";
                return false;
            }

            return ParseIndex();
        }

        private void Reset()
        {
            _entries.Clear();
            _byId.Clear();
            _source = null;
            _length = 0;
            Error = null;
            IndexReadable = false;
            HasChecksum = false;
            HasEncryptedIndex = false;
            Flags = 0;
            FileCount = 0;
            DataSize = 0;
            DataStartOffset = 0;
        }

        // ---- 目录解析（只读头部 + 目录） ----

        private bool ParseIndex()
        {
            var head = ReadAt(0, 6);
            if (head == null) { Error = "读不到 MIX 头部"; return false; }

            Flags = BitConverter.ToUInt32(head, 0);
            bool legacyUnflagged = (Flags & ~KnownFlags) != 0;

            if (legacyUnflagged)
            {
                FileCount = (ushort)(Flags & 0xFFFF);
                uint dataLow = (Flags >> 16) & 0xFFFF;
                ushort dataHigh = BitConverter.ToUInt16(head, 4);
                DataSize = dataLow | ((uint)dataHigh << 16);
                Flags = 0;
                DataStartOffset = 6 + (long)FileCount * EntrySize;
                return ReadPlainDirectory(6);
            }

            var head10 = ReadAt(0, 10);
            if (head10 == null) { Error = "读不到 MIX 头部"; return false; }

            HasChecksum = (Flags & FlagChecksum) != 0;
            HasEncryptedIndex = (Flags & FlagEncryptedIndex) != 0;
            FileCount = BitConverter.ToUInt16(head10, 4);
            DataSize = BitConverter.ToUInt32(head10, 6);

            if (HasEncryptedIndex) return ReadEncryptedDirectory();

            DataStartOffset = 10 + (long)FileCount * EntrySize;
            return ReadPlainDirectory(10);
        }

        private bool ReadPlainDirectory(long start)
        {
            long bytes = (long)FileCount * EntrySize;
            if (bytes <= 0) { IndexReadable = true; return true; }

            if (start + bytes > _length)
            {
                Error = "目录越界（声明 " + FileCount + " 条，超出档案长度）";
                return false;
            }

            var dir = ReadAt(start, (int)bytes);
            if (dir == null) { Error = "读不到 MIX 目录"; return false; }

            for (int i = 0; i < FileCount; i++)
            {
                int o = i * EntrySize;
                AddEntry(BitConverter.ToUInt32(dir, o),
                    BitConverter.ToUInt32(dir, o + 4),
                    BitConverter.ToUInt32(dir, o + 8));
            }

            IndexReadable = true;
            return true;
        }

        private bool ReadEncryptedDirectory()
        {
            // 4 字节 flags 之后紧跟 80 字节密钥块
            var keyBlock = ReadAt(4, KeyBlockBytes);
            if (keyBlock == null) { Error = "读不到 MIX 加密目录密钥块"; return false; }

            long encryptedIndexOffset = 4 + KeyBlockBytes;

            var firstBlock = ReadAt(encryptedIndexOffset, BlockBytes);
            if (firstBlock == null) { Error = "读不到 MIX 加密目录首块"; return false; }

            if (!WestwoodCrypto.DecryptMixIndexBlocks(keyBlock, firstBlock,
                    out var firstDecrypted, out string cryptoError))
            {
                Error = cryptoError;
                return false;
            }

            if (firstDecrypted.Length < 6)
            {
                Error = "MIX 加密目录首块解密后过短";
                return false;
            }

            FileCount = BitConverter.ToUInt16(firstDecrypted, 0);

            long indexPayloadBytes = (long)FileCount * EntrySize;
            long plaintextHeaderBytes = 6 + indexPayloadBytes;
            int encryptedIndexBytes = (int)((plaintextHeaderBytes + BlockBytes - 1) / BlockBytes) * BlockBytes;

            long encryptedIndexEnd = encryptedIndexOffset + encryptedIndexBytes;
            if (encryptedIndexEnd > _length)
            {
                Error = "MIX 加密目录越界";
                return false;
            }

            var encryptedIndex = ReadAt(encryptedIndexOffset, encryptedIndexBytes);
            if (encryptedIndex == null) { Error = "读不到完整 MIX 加密目录"; return false; }

            if (!WestwoodCrypto.DecryptMixIndexBlocks(keyBlock, encryptedIndex,
                    out var decryptedIndex, out cryptoError))
            {
                Error = cryptoError;
                return false;
            }

            if (decryptedIndex.Length < plaintextHeaderBytes)
            {
                Error = "MIX 加密目录解密后小于头长度";
                return false;
            }

            DataSize = BitConverter.ToUInt32(decryptedIndex, 2);
            DataStartOffset = encryptedIndexEnd;

            int entryOffset = 6;
            for (int i = 0; i < FileCount; i++)
            {
                if (entryOffset + EntrySize > decryptedIndex.Length)
                {
                    Error = "MIX 加密目录条目数不足";
                    return false;
                }

                uint id = BitConverter.ToUInt32(decryptedIndex, entryOffset);
                uint offset = BitConverter.ToUInt32(decryptedIndex, entryOffset + 4);
                uint size = BitConverter.ToUInt32(decryptedIndex, entryOffset + 8);
                entryOffset += EntrySize;
                AddEntry(id, offset, size);
            }

            IndexReadable = true;
            return true;
        }

        private void AddEntry(uint id, uint offset, uint size)
        {
            _entries.Add(new MixEntry
            {
                Id = id,
                Offset = offset,
                Size = size,
                AbsoluteOffset = DataStartOffset + offset,
            });
            _byId[id] = _entries.Count - 1;
        }

        /// <summary>从源里精确读取一段；越界或读不全返回 null。</summary>
        private byte[] ReadAt(long offset, int count)
        {
            if (offset < 0 || count <= 0 || offset + count > _length) return null;
            var buffer = new byte[count];
            int read = _source.Read(offset, buffer, 0, count);
            return read == count ? buffer : null;
        }

        // ---- 查询 ----

        public MixEntry FindById(uint id)
        {
            return _byId.TryGetValue(id, out int index) ? _entries[index] : null;
        }

        public MixEntry FindByName(string fileName) => FindById(FileIdFromName(fileName));

        public bool TryReadEntry(string fileName, out byte[] data) => TryReadEntry(FindByName(fileName), out data);

        /// <summary>只读这一个条目的字节；不触碰档案其它区域。</summary>
        /// <summary>只读条目头部若干字节（判断文件类型 / 读影片头用，不把整条读进内存）。</summary>
        public byte[] ReadEntryRange(MixEntry entry, int count)
        {
            if (entry == null || !IndexReadable || _source == null) return null;
            if (entry.AbsoluteOffset < 0 || entry.Size == 0) return null;
            if (count <= 0 || count > entry.Size) return null;
            if (entry.AbsoluteOffset + entry.Size > _length) return null;
            return ReadChildAt(entry.AbsoluteOffset, count);
        }

        public bool TryReadEntry(MixEntry entry, out byte[] data)
        {
            data = null;
            if (entry == null || !IndexReadable || _source == null) return false;
            if (entry.AbsoluteOffset < 0 || entry.Size == 0) return false;
            if (entry.AbsoluteOffset + entry.Size > _length) return false;

            var buffer = new byte[entry.Size];
            int read = _source.Read(entry.AbsoluteOffset, buffer, 0, (int)entry.Size);
            if (read != buffer.Length) return false;
            data = buffer;
            return true;
        }

        /// <summary>
        /// 廉价嗅探：只读该条目的 10 字节头，判断它是否像一个能解析的 MIX 目录。
        /// 代价 = 一次 10 字节 read，不碰条目正文，也不建目录，用于在几十上百个候选里筛出真正的嵌套档。
        /// </summary>
        public bool LooksLikeChildArchive(MixEntry entry)
        {
            if (entry == null || _source == null) return false;
            if (entry.Size < 4096) return false;
            if (entry.AbsoluteOffset + entry.Size > _length) return false;

            long size = entry.Size;
            var head = ReadChildAt(entry.AbsoluteOffset, 10);
            if (head == null) return false;

            uint flags = BitConverter.ToUInt32(head, 0);

            // 旧版无标志头：低 16 位是文件数，需要 6 + 12N 装得下。
            if ((flags & ~(FlagChecksum | FlagEncryptedIndex)) != 0)
            {
                ushort legacyCount = (ushort)(flags & 0xFFFF);
                if (legacyCount == 0 || legacyCount > 4000) return false;
                return 6 + (long)legacyCount * EntrySize <= size;
            }

            // 普通/加密头：文件数在 offset 4。
            ushort count = BitConverter.ToUInt16(head, 4);
            if ((flags & FlagEncryptedIndex) != 0)
            {
                // 加密档的明文头里文件数不可信（真值在解密后的首块里），只做体积下限预筛，
                // 真正的合法性由调用方 OpenChild + 目录项越界校验裁决。
                return size >= 4 + KeyBlockBytes + BlockBytes;
            }

            if (count == 0 || count > 4000) return false;

            long directoryBytes = 10 + (long)count * EntrySize;
            return directoryBytes <= size;
        }

        /// <summary>从本档源里读取子条目区间的字节（不经过档案长度上限校验）。</summary>
        private byte[] ReadChildAt(long absoluteOffset, int count)
        {
            if (absoluteOffset < 0 || count <= 0) return null;
            var buffer = new byte[count];
            int read = _source.Read(absoluteOffset, buffer, 0, count);
            return read == count ? buffer : null;
        }

        /// <summary>把一个条目当作嵌套 MIX 打开（切片源，不复制字节）。</summary>
        public MixArchive OpenChild(MixEntry entry, string displayName)
        {
            if (entry == null || entry.Size <= 0 || _source == null) return null;
            var child = new MixArchive();
            if (!child.OpenNested(_source, entry.AbsoluteOffset, entry.Size,
                    displayName ?? ("0x" + entry.Id.ToString("X8"))) || !child.IndexReadable)
            {
                return null;
            }
            return child;
        }

        // ---- 文件名 → MIX ID（Westwood CRC32）----

        public static uint FileIdFromName(string fileName)
        {
            string normalized = Normalize(fileName);
            var upper = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                upper.Append(c >= 'a' && c <= 'z' ? (char)(c - 'a' + 'A') : c);
            }

            // RA2/YR 的 MIX id 走 Westwood 口径：名字**补齐到 4 的倍数**后再做反射 CRC32。
            // 实测（all_ids 命中）：ovrpsb01.tem → 58A73068、water06.sno → 5EECF573、missionsmd.pkt → 480B82E4。
            var padded = upper.ToString();
            int wholeGroups = padded.Length >> 2;
            int remainder = padded.Length & 3;
            if (remainder != 0)
            {
                padded += (char)remainder;
                int padCount = 3 - ((padded.Length - 1) & 3);
                char repeat = padded[wholeGroups << 2];
                while (padCount-- > 0)
                {
                    padded += repeat;
                }
            }

            uint crc = 0xFFFFFFFFu;
            foreach (char c in padded)
            {
                crc = UpdateCrc32(crc, (byte)c);
            }
            return ~crc;
        }

        private static uint UpdateCrc32(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 1u) != 0) crc = (crc >> 1) ^ 0xEDB88320u;
                else crc >>= 1;
            }
            return crc;
        }

        private static string Normalize(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            var s = fileName.Trim();

            if (s.Length >= 2 &&
                ((s[0] == '"' && s[s.Length - 1] == '"') ||
                 (s[0] == '\'' && s[s.Length - 1] == '\'')))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }

            while (s.Length > 0 && (s[s.Length - 1] == '/' || s[s.Length - 1] == '\\'))
            {
                s = s.Substring(0, s.Length - 1);
            }

            int sep = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
            if (sep >= 0) s = s.Substring(sep + 1);
            return s;
        }
    }
}
