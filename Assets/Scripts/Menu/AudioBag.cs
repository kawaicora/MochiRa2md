// AudioBag.cs —— RA2/YR 音效库（audio.idx + audio.bag）读取器。
//
// 为什么要这个：语言档里的音效不是「一条 wav 一个条目」，而是
//   language.mix → audio.mix → audio.bag（全部样本拼成一个大 blob）+ audio.idx（名字 → 偏移/长度）
//   langmd.mix  → audiomd.mix → 同上（尤复数据）
// 所以按 "umenucl1.wav" 算 CRC 去 MIX 目录里找，永远是 0 命中 —— 名字根本不在 MIX 目录里，
// 名字只写在 audio.idx 的定长记录里。
//
// audio.idx 实测结构（audio 41520B / 1153 条，audiomd 82272B / 2285 条）：
//   0x00  'G','A','B','A'
//   0x04  uint32 版本（=2）
//   0x08  uint32 记录数
//   0x0C  记录[]，每条 36 字节：
//         +0x00 char[16] 采样名（不含扩展名，NUL 补齐）
//         +0x10 uint32 相对 audio.bag 起点的偏移
//         +0x14 uint32 字节长度
//         +0x18 uint32 采样率（实测全为 22050）
//         +0x1C uint32 编码：6/7=PCM16，12=IMA ADPCM 单声道(blockAlign 512)，13=IMA ADPCM 立体声(blockAlign 1024)
//         +0x20 uint32 块对齐（PCM 为 0）
//
// audio.bag 没有文件头，偏移 0 就是第一个样本；每条样本自身也没有头，直接是编码数据。
// 解码复用 WavClipLoader：这里拼一个最小 RIFF 头，再把样本字节喂进去。
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Ra2md.Assets;

namespace Ra2md.Menu
{
    public static class AudioBag
    {
        private static string IdxName => Ra2md.Managers.GlobalConfigManager.Resources.AudioBagIndexName;
        private static string BagName => Ra2md.Managers.GlobalConfigManager.Resources.AudioBagName;

        // 允许直接按路径打开（VFS 全库查不到 idx/bag 时的兜底）
        private static string[] IdxPaths => Ra2md.Managers.GlobalConfigManager.Resources.AudioBagIndexPaths;
        private static string[] BagPaths => Ra2md.Managers.GlobalConfigManager.Resources.AudioBagPaths;

        private sealed class Sample
        {
            public int Offset;
            public int Size;
            public int Rate;
            public uint Codec;
            public int BlockAlign;
        }

        private static bool _loadAttempted;
        private static bool _loaded;
        private static byte[] _bag;
        private static string _origin;
        private static readonly Dictionary<string, Sample> _samples = new Dictionary<string, Sample>();
        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private static readonly HashSet<string> _warned = new HashSet<string>();

        public static bool Loaded { get { EnsureLoaded(); return _loaded; } }
        public static int SampleCount { get { EnsureLoaded(); return _samples.Count; } }
        public static string Origin { get { EnsureLoaded(); return _origin; } }

        /// <summary>库里是否存在这个名字（不含扩展名）。</summary>
        public static bool Has(string name)
        {
            EnsureLoaded();
            return _samples.ContainsKey(Key(name));
        }

        /// <summary>取（并缓存）一个采样名对应的 AudioClip；找不到返回 null。</summary>
        public static AudioClip GetClip(string name)
        {
            EnsureLoaded();
            string key = Key(name);
            if (key.Length == 0) return null;

            if (_clips.TryGetValue(key, out var cached)) return cached;

            var sample = _samples.TryGetValue(key, out var found) ? found : null;
            if (sample == null)
            {
                if (_warned.Add(key))
                    Debug.LogWarning("[音效库] 没有这个采样：" + name + "（库内 " + _samples.Count + " 条）");
                return null;
            }

            var clip = Decode(sample, key);
            if (clip != null) _clips[key] = clip;
            return clip;
        }

        // ------------------------------------------------------------ 装载

        private static void EnsureLoaded()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            var vfs = MenuResourceLibrary.EnsureMounted();
            byte[] idx = null;
            string idxOrigin = null;

            if (vfs != null && vfs.TryResolve(IdxName, out var rawIdx, out var o1) && rawIdx != null)
            {
                idx = rawIdx;
                idxOrigin = o1;
            }
            else if (vfs != null)
            {
                foreach (var path in IdxPaths)
                {
                    if (vfs.OpenFile(path, out var bytes) && bytes != null)
                    {
                        idx = bytes;
                        idxOrigin = path;
                        break;
                    }
                }
            }

            if (vfs != null && vfs.TryResolve(BagName, out var rawBag, out var o2) && rawBag != null)
            {
                _bag = rawBag;
                _origin = o2;
            }
            else if (vfs != null)
            {
                foreach (var path in BagPaths)
                {
                    if (vfs.OpenFile(path, out var bytes) && bytes != null)
                    {
                        _bag = bytes;
                        _origin = path;
                        break;
                    }
                }
            }

            if (idx == null || _bag == null)
            {
                Debug.LogWarning("[音效库] 找不到 audio.idx / audio.bag（idx=" +
                                 (idx == null ? "无" : idx.Length + "B") + "，bag=" +
                                 (_bag == null ? "无" : _bag.Length + "B") + "）");
                return;
            }

            _origin = _origin ?? "audio.bag";
            if (!ParseIndex(idx)) return;

            _loaded = true;
            Debug.Log("[音效库] audio.idx 就绪：" + _samples.Count + " 条样本，bag " + _bag.Length +
                      "B（idx " + idxOrigin + "，bag " + _origin + "）");
        }

        private static bool ParseIndex(byte[] idx)
        {
            if (idx.Length < 0x0C || idx[0] != 'G' || idx[1] != 'A' || idx[2] != 'B' || idx[3] != 'A')
            {
                Debug.LogWarning("[音效库] audio.idx 魔数不对（应为 GABA）");
                return false;
            }

            int count = BitConverter.ToInt32(idx, 8);
            const int stride = 36;
            int limit = Math.Min(count, (idx.Length - 0x0C) / stride);
            if (limit <= 0)
            {
                Debug.LogWarning("[音效库] audio.idx 记录数为 0");
                return false;
            }

            for (int i = 0; i < limit; i++)
            {
                int p = 0x0C + i * stride;
                string name = ReadName(idx, p, 16);
                if (name.Length == 0) continue;

                int offset = BitConverter.ToInt32(idx, p + 16);
                int size = BitConverter.ToInt32(idx, p + 20);
                int rate = BitConverter.ToInt32(idx, p + 24);
                uint codec = BitConverter.ToUInt32(idx, p + 28);
                int blockAlign = BitConverter.ToInt32(idx, p + 32);

                if (size <= 0 || offset < 0 || offset + size > _bag.Length) continue;

                _samples[name] = new Sample
                {
                    Offset = offset, Size = size, Rate = rate, Codec = codec, BlockAlign = blockAlign,
                };
            }

            if (limit < count)
                Debug.LogWarning("[音效库] audio.idx 声明 " + count + " 条，但文件只够 " + limit + " 条");
            return _samples.Count > 0;
        }

        private static string ReadName(byte[] data, int offset, int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------ 解码

        private static AudioClip Decode(Sample sample, string name)
        {
            int rate = sample.Rate >= 4000 && sample.Rate <= 192000 ? sample.Rate : 22050;

            bool adpcm;
            int blockAlign;
            int channels;

            // 实测布局（探针已验证：块边界 100% 合法、L/R 相关 0.92、按立体声解 rms 才回到正常量级）：
            //   12 → 单声道 IMA ADPCM，blockAlign 512（4 字节头 + 508 字节 nibble）
            //   13 → 立体声 IMA ADPCM，blockAlign 1024（8 字节头 + L/R 交替的 4 字节组）—— 只有 uslide1/uslide2
            //   6/7 → PCM16 单声道
            // 注意：codec 13 若按单声道来解，采样数会翻倍、时长变一半，听起来就是「快了一倍、音调变高」。
            switch (sample.Codec)
            {
                case 12: adpcm = true; blockAlign = 512; channels = 1; break;
                case 13: adpcm = true; blockAlign = 1024; channels = 2; break;
                case 6:
                case 7: adpcm = false; blockAlign = 0; channels = 1; break;
                default:
                    blockAlign = sample.BlockAlign > 4 ? sample.BlockAlign : 512;
                    channels = SniffChannels(_bag, sample.Offset, blockAlign);
                    adpcm = sample.Size >= blockAlign && _bag[sample.Offset + 2] <= 88;
                    break;
            }

            byte[] payload = new byte[sample.Size];
            Buffer.BlockCopy(_bag, sample.Offset, payload, 0, sample.Size);

            int formatTag = adpcm ? 0x11 : 0x01;
            int bits = adpcm ? 4 : 16;
            int frameBytes = adpcm ? blockAlign : 2 * channels;
            int byteRate;
            if (adpcm)
            {
                int framesPerBlock = (blockAlign - 4 * channels) * 2 / channels + 1;
                byteRate = rate * blockAlign / Math.Max(1, framesPerBlock);
            }
            else
            {
                byteRate = rate * 2 * channels;
            }

            var riff = BuildRiff(payload, rate, formatTag, bits, channels, frameBytes, byteRate);
            var clip = WavClipLoader.Parse(riff, name);
            if (clip != null)
            {
                Debug.Log("[音效库] " + name + "：" + (adpcm
                    ? "IMA-ADPCM×" + channels + "ch blockAlign=" + blockAlign
                    : "PCM16") + " / " + rate + "Hz / " + sample.Size + "B / " + clip.length.ToString("F2") + "s");
            }
            return clip;
        }

        /// <summary>未知编码时按块头分期决定声道数：8 字节头看两声道是否一致，4 字节头看单声道是否自洽。</summary>
        private static int SniffChannels(byte[] bag, int offset, int blockAlign)
        {
            if (offset + 8 >= bag.Length || blockAlign < 16) return 1;
            int l = bag[offset] | (bag[offset + 1] << 8);
            int r = bag[offset + 4] | (bag[offset + 5] << 8);
            int idxL = bag[offset + 2];
            int idxR = bag[offset + 6];
            if (idxL > 88 || idxR > 88) return 1;
            return Math.Abs(l - r) < 64 ? 2 : 1;
        }

        /// <summary>拼一个最小 RIFF/WAVE 头，把裸样本字节包成 WavClipLoader 能吃的结构。</summary>
        private static byte[] BuildRiff(byte[] payload, int sampleRate, int formatTag, int bits, int channels,
            int blockAlign, int avgBytesPerSec)
        {
            if (avgBytesPerSec <= 0) avgBytesPerSec = Math.Max(1, blockAlign);

            var data = new byte[44 + payload.Length];
            Tag(data, 0, "RIFF");
            Write32(data, 4, 36 + payload.Length);
            Tag(data, 8, "WAVE");
            Tag(data, 12, "fmt ");
            Write32(data, 16, 16);
            Write16(data, 20, (ushort)formatTag);
            Write16(data, 22, (ushort)channels);
            Write32(data, 24, sampleRate);
            Write32(data, 28, avgBytesPerSec);
            Write16(data, 32, (ushort)blockAlign);      // ADPCM 用块字节数，PCM 用 位深/8*声道
            Write16(data, 34, (ushort)bits);
            Tag(data, 36, "data");
            Write32(data, 40, payload.Length);
            Buffer.BlockCopy(payload, 0, data, 44, payload.Length);
            return data;
        }

        private static void Tag(byte[] buffer, int offset, string fourCc)
        {
            for (int i = 0; i < 4 && i < fourCc.Length; i++) buffer[offset + i] = (byte)fourCc[i];
        }

        private static void Write16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void Write32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        /// <summary>采样名规范化：去路径、去扩展名、小写（idx 里全是小写）。</summary>
        private static string Key(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var s = name.Trim().Trim('"');
            int sep = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
            if (sep >= 0) s = s.Substring(sep + 1);
            int dot = s.IndexOf('.');
            if (dot > 0) s = s.Substring(0, dot);
            return s.ToLowerInvariant();
        }
    }
}
