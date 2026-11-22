// WavClipLoader.cs —— 运行时 WAV 解析成 AudioClip。
// 支持 RIFF/WAVE：PCM(1) / IEEE float(3) / MS-IMA ADPCM 4bit(0x11)。
// language.mix / langmd.mix 里的音效/语音是 22050Hz 单声道 4bit IMA-ADPCM。
using System;
using System.IO;
using UnityEngine;

namespace Ra2md.Menu
{
    public static class WavClipLoader
    {
        public static AudioClip LoadFile(string path, string clipName = null)
        {
            string name = string.IsNullOrEmpty(clipName) ? Path.GetFileName(path) : clipName;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                // 硬规矩：磁盘上没有就叫 audio.bag 兜一次 —— 原版大量音效只存在于 audio.idx/bag，
                // 不是以 wav 文件形式散在 MIX 目录里，任何按名字找音频的地方都必须走这条。
                var bagClip = AudioBag.GetClip(name ?? path);
                if (bagClip != null)
                {
                    Debug.Log("[音频] " + name + " ← audio.idx（" + AudioBag.Origin + "）");
                    return bagClip;
                }
                Debug.LogWarning("[音频] 找不到 WAV：" + path);
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            return Parse(bytes, string.IsNullOrEmpty(name) ? Path.GetFileName(path) : name);
        }

        public static AudioClip Parse(byte[] data, string clipName)
        {
            if (data == null || data.Length < 44)
            {
                Debug.LogWarning("[音频] WAV 数据过短");
                return null;
            }

            if (!(data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'))
            {
                Debug.LogWarning("[音频] 不是 RIFF 文件");
                return null;
            }

            int fmtOffset = -1;
            int dataOffset = -1;
            int dataLength = 0;

            int cursor = 12;
            while (cursor + 8 <= data.Length)
            {
                string chunkId = new string(new[]
                {
                    (char)data[cursor], (char)data[cursor + 1],
                    (char)data[cursor + 2], (char)data[cursor + 3],
                });
                int chunkSize = BitConverter.ToInt32(data, cursor + 4);
                if (chunkSize < 0) break;

                if (chunkId == "fmt ") fmtOffset = cursor + 8;
                else if (chunkId == "data")
                {
                    dataOffset = cursor + 8;
                    dataLength = Math.Min(chunkSize, data.Length - dataOffset);
                }

                cursor += 8 + chunkSize + (chunkSize & 1);   // 块按偶数字节对齐
            }

            if (fmtOffset < 0 || dataOffset < 0 || dataLength <= 0)
            {
                Debug.LogWarning("[音频] WAV 缺少 fmt 或 data 块");
                return null;
            }

            int format = BitConverter.ToUInt16(data, fmtOffset);
            int channels = BitConverter.ToUInt16(data, fmtOffset + 2);
            int sampleRate = BitConverter.ToInt32(data, fmtOffset + 4);
            int blockAlign = BitConverter.ToUInt16(data, fmtOffset + 12);
            int bits = BitConverter.ToUInt16(data, fmtOffset + 14);

            if (channels < 1 || channels > 2 || sampleRate < 4000 || sampleRate > 192000)
            {
                Debug.LogWarning("[音频] 不支持的声道/采样率：" + channels + "ch " + sampleRate + "Hz");
                return null;
            }

            // RA2/YR 的音效库（language.mix / langmd.mix 里的 461/738 条）是
            // MS-IMA ADPCM 4bit，format tag = 0x11；不解它等于把音效全砍掉。
            if (format == 0x11 && bits == 4)
            {
                return DecodeImaAdpcm(data, dataOffset, dataLength, channels, sampleRate, blockAlign, clipName);
            }

            int bytesPerSample = Math.Max(1, bits / 8);
            int frameCount = dataLength / (bytesPerSample * channels);
            if (frameCount <= 0)
            {
                Debug.LogWarning("[音频] 采样帧为 0");
                return null;
            }

            float[] samples = new float[frameCount * channels];

            if (format == 1)
            {
                switch (bits)
                {
                    case 8:
                        for (int i = 0; i < samples.Length; i++)
                            samples[i] = (data[dataOffset + i] - 128) / 128f;
                        break;
                    case 16:
                        for (int i = 0; i < samples.Length; i++)
                            samples[i] = BitConverter.ToInt16(data, dataOffset + i * 2) / 32768f;
                        break;
                    case 24:
                        for (int i = 0; i < samples.Length; i++)
                        {
                            int off = dataOffset + i * 3;
                            int v = data[off] | (data[off + 1] << 8) | (data[off + 2] << 16);
                            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                            samples[i] = v / 8388608f;
                        }
                        break;
                    case 32:
                        for (int i = 0; i < samples.Length; i++)
                            samples[i] = BitConverter.ToInt32(data, dataOffset + i * 4) / 2147483648f;
                        break;
                    default:
                        Debug.LogWarning("[音频] 不支持的位深：" + bits);
                        return null;
                }
            }
            else if (format == 3 && bits == 32)
            {
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToSingle(data, dataOffset + i * 4);
            }
            else
            {
                Debug.LogWarning("[音频] 未支持的 WAV 编码 format=" + format + " bits=" + bits);
                return null;
            }

            var clip = AudioClip.Create(clipName, frameCount, channels, sampleRate, false);
            clip.SetData(samples, 0);
            Debug.Log("[音频] WAV 装载 " + clipName + "：" + frameCount + " 帧 / " + channels + "ch / "
                      + sampleRate + "Hz / " + bits + "bit，时长 " + clip.length.ToString("F1") + "s");
            return clip;
        }

        // ---- MS-IMA ADPCM（format tag 0x11）----

        private static readonly int[] ImaIndexTable =
        {
            -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8,
        };

        private static readonly int[] ImaStepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
            34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
            724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
            3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };

        /// <summary>
        /// 每 block = 4 字节/声道 的初始状态（int16 prevsample + uint8 index + uint8 保留），
        /// 之后是交错 4bit nibble。blockAlign 由 fmt 给出；缺省时按 36*ch 兜底。
        /// </summary>
        private static AudioClip DecodeImaAdpcm(byte[] data, int dataOffset, int dataLength,
            int channels, int sampleRate, int blockAlign, string clipName)
        {
            if (blockAlign <= 0) blockAlign = 36 * channels;
            if (blockAlign <= 4 * channels) blockAlign = 36 * channels;

            // 末块可能不满一个 blockAlign（audio.bag 里的采样就常这样），按 ceil 算块数，末块按实际长度解。
            int blockCount = (dataLength + blockAlign - 1) / blockAlign;
            if (blockCount <= 0)
            {
                Debug.LogWarning("[音频] IMA ADPCM blockAlign 非法：" + blockAlign);
                return null;
            }

            int samplesPerBlock = (blockAlign - 4 * channels) * 2 / channels + 1;
            int totalFrames = blockCount * samplesPerBlock;

            float[] samples = new float[totalFrames * channels];

            int[] predictor = new int[channels];
            int[] stepIndex = new int[channels];
            int outFrame = 0;

            for (int block = 0; block < blockCount; block++)
            {
                int blockStart = dataOffset + block * blockAlign;
                int blockBytes = Math.Min(blockAlign, dataOffset + dataLength - blockStart);
                if (blockBytes <= 4 * channels) break;
                int pos = blockStart;

                for (int ch = 0; ch < channels; ch++)
                {
                    predictor[ch] = BitConverter.ToInt16(data, pos);
                    stepIndex[ch] = data[pos + 2];
                    if (stepIndex[ch] < 0) stepIndex[ch] = 0;
                    if (stepIndex[ch] > 88) stepIndex[ch] = 88;
                    pos += 4;
                    samples[(outFrame + 0) * channels + ch] = predictor[ch] / 32768f;
                }

                int dataBytes = blockBytes - 4 * channels;
                int groups = dataBytes / (4 * channels);   // 每声道每组的 4 字节块数

                for (int g = 0; g < groups; g++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int basePos = blockStart + 4 * channels + g * (4 * channels) + ch * 4;
                        for (int k = 0; k < 4; k++)
                        {
                            int packed = data[basePos + k];
                            for (int half = 0; half < 2; half++)
                            {
                                int nibble = half == 0 ? (packed & 0x0F) : ((packed >> 4) & 0x0F);

                                int step = ImaStepTable[stepIndex[ch]];
                                int diff = step >> 3;
                                if ((nibble & 1) != 0) diff += step >> 2;
                                if ((nibble & 2) != 0) diff += step >> 1;
                                if ((nibble & 4) != 0) diff += step;

                                if ((nibble & 8) != 0) predictor[ch] -= diff;
                                else predictor[ch] += diff;
                                if (predictor[ch] > 32767) predictor[ch] = 32767;
                                if (predictor[ch] < -32768) predictor[ch] = -32768;

                                stepIndex[ch] += ImaIndexTable[nibble];
                                if (stepIndex[ch] < 0) stepIndex[ch] = 0;
                                if (stepIndex[ch] > 88) stepIndex[ch] = 88;

                                // 帧号必须按「组内第 n 个样本」算，不能跨声道累加，
                                // 否则立体声第二条声道整体错位。
                                int frame = 1 + g * 8 + k * 2 + half;
                                if (frame < samplesPerBlock && outFrame + frame < totalFrames)
                                {
                                    samples[(outFrame + frame) * channels + ch] = predictor[ch] / 32768f;
                                }
                            }
                        }
                    }
                }

                outFrame += samplesPerBlock;
                if (outFrame >= totalFrames) break;
            }

            var clip = AudioClip.Create(clipName, totalFrames, channels, sampleRate, false);
            clip.SetData(samples, 0);
            Debug.Log("[音频] IMA-ADPCM 装载 " + clipName + "：" + totalFrames + " 帧 / " + channels + "ch / "
                      + sampleRate + "Hz / blocks=" + blockCount + "，时长 " + clip.length.ToString("F1") + "s");
            return clip;
        }
    }
}
