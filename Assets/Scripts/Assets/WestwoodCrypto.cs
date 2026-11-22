// WestwoodCrypto.cs —— MIX 目录加密解包：RSA-65537 还原 Blowfish 密钥 + Blowfish ECB 解密。
// 流程：80 字节密钥块拆两个 40 字节密文块 → 模公共模数 n → 取前 56 字节作 Blowfish 密钥 → 8 字节块解密目录。
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ra2md.Assets
{
    internal static class WestwoodCrypto
    {
        private static readonly byte[] PublicModulusBe =
        {
            0x51, 0xbc, 0xda, 0x08, 0x6d, 0x39, 0xfc, 0xe4,
            0x56, 0x51, 0x60, 0xd6, 0x51, 0x71, 0x3f, 0xa2,
            0xe8, 0xaa, 0x54, 0xfa, 0x66, 0x82, 0xb0, 0x4a,
            0xab, 0xdd, 0x0e, 0x6a, 0xf8, 0xb0, 0xc1, 0xe6,
            0xd1, 0xfb, 0x4f, 0x3d, 0xaa, 0x43, 0x7f, 0x15,
        };

        private static BigInteger BigFromBe(byte[] bytes)
        {
            var padded = new byte[bytes.Length + 1];
            Array.Copy(bytes, 0, padded, 1, bytes.Length);
            Array.Reverse(padded);
            return new BigInteger(padded);
        }

        private static BigInteger BigFromLe(byte[] le, int count)
        {
            var be = new byte[count];
            for (int i = 0; i < count; i++) be[i] = le[count - 1 - i];
            return BigFromBe(be);
        }

        /// <summary>解包出 56 字节 Blowfish 密钥。</summary>
        private static bool DecryptMixBlowfishKey(byte[] keyBlock, out byte[] key, out string error)
        {
            key = null;
            error = null;
            if (keyBlock.Length != 80)
            {
                error = "MIX encrypted key block must be exactly 80 bytes.";
                return false;
            }

            var modulus = BigFromBe(PublicModulusBe);
            var decrypted = new List<byte>(78);

            for (int chunk = 0; chunk < 2; chunk++)
            {
                var chunkBytes = new byte[40];
                Array.Copy(keyBlock, chunk * 40, chunkBytes, 0, 40);
                var encrypted = BigFromLe(chunkBytes, 40);
                var plain = BigInteger.ModPow(encrypted, 65537, modulus);

                byte[] plainLe = plain.ToByteArray(true);
                // 39 字节小端；不足补 0。
                for (int i = 0; i < 39; i++)
                {
                    decrypted.Add(i < plainLe.Length ? plainLe[i] : (byte)0);
                }
            }

            key = decrypted.GetRange(0, 56).ToArray();
            return true;
        }

        private static uint ReadLe32(byte[] bytes, int offset)
        {
            return (uint)bytes[offset]
                   | ((uint)bytes[offset + 1] << 8)
                   | ((uint)bytes[offset + 2] << 16)
                   | ((uint)bytes[offset + 3] << 24);
        }

        private static void WriteLe32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static uint ByteSwap(uint value)
        {
            value = (value << 16) | (value >> 16);
            return ((value << 8) & 0xFF00FF00u) | ((value >> 8) & 0x00FF00FFu);
        }

        private sealed class BlowfishCipher
        {
            private readonly uint[] _p = new uint[18];
            private readonly uint[] _s = new uint[1024];

            public BlowfishCipher(byte[] key)
            {
                Array.Copy(WestwoodBlowfishConstants.PInitial, _p, 18);
                Array.Copy(WestwoodBlowfishConstants.SInitial, _s, 1024);

                int j = 0;
                for (int i = 0; i < _p.Length; i++)
                {
                    uint word = 0;
                    for (int b = 0; b < 4; b++)
                    {
                        word = (word << 8) | key[j % key.Length];
                        j++;
                    }
                    _p[i] ^= word;
                }

                uint left = 0, right = 0;
                for (int i = 0; i < _p.Length; i += 2)
                {
                    EncryptPair(ref left, ref right);
                    _p[i] = left;
                    _p[i + 1] = right;
                }
                for (int i = 0; i < _s.Length; i += 2)
                {
                    EncryptPair(ref left, ref right);
                    _s[i] = left;
                    _s[i + 1] = right;
                }
            }

            public byte[] DecryptBlocks(byte[] encrypted)
            {
                var decrypted = new byte[encrypted.Length];
                for (int offset = 0; offset < encrypted.Length; offset += 8)
                {
                    uint left = ByteSwap(ReadLe32(encrypted, offset));
                    uint right = ByteSwap(ReadLe32(encrypted, offset + 4));
                    DecryptPair(ref left, ref right);
                    WriteLe32(decrypted, offset, ByteSwap(left));
                    WriteLe32(decrypted, offset + 4, ByteSwap(right));
                }
                return decrypted;
            }

            private uint SBox(uint value, int index)
            {
                int b = (int)((value >> ((3 - index) * 8)) & 0xFF);
                return _s[index * 256 + b];
            }

            private uint Function(uint value)
            {
                return ((SBox(value, 0) + SBox(value, 1)) ^ SBox(value, 2)) + SBox(value, 3);
            }

            private void Round(ref uint left, uint right, int index)
            {
                left ^= Function(right) ^ _p[index];
            }

            private void EncryptPair(ref uint workLeft, ref uint workRight)
            {
                uint l = workLeft, r = workRight;
                l ^= _p[0];
                bool alternate = false;
                for (int i = 1; i <= 16; i++, alternate = !alternate)
                {
                    if (alternate) Round(ref l, r, i);
                    else Round(ref r, l, i);
                }
                r ^= _p[17];
                workLeft = r;
                workRight = l;
            }

            private void DecryptPair(ref uint workLeft, ref uint workRight)
            {
                uint l = workLeft, r = workRight;
                l ^= _p[17];
                bool alternate = false;
                for (int i = 16; i >= 1; i--, alternate = !alternate)
                {
                    if (alternate) Round(ref l, r, i);
                    else Round(ref r, l, i);
                    if (i == 1) break;
                }
                r ^= _p[0];
                workLeft = r;
                workRight = l;
            }
        }

        /// <summary>解密 MIX 目录的 Blowfish 8 字节块。</summary>
        public static bool DecryptMixIndexBlocks(byte[] keyBlock, byte[] encrypted, out byte[] decrypted, out string error)
        {
            decrypted = null;
            error = null;
            if (encrypted.Length % 8 != 0)
            {
                error = "MIX encrypted index length is not a multiple of 8 bytes.";
                return false;
            }

            if (!DecryptMixBlowfishKey(keyBlock, out byte[] blowfishKey, out error))
            {
                return false;
            }

            var cipher = new BlowfishCipher(blowfishKey);
            decrypted = cipher.DecryptBlocks(encrypted);
            return true;
        }
    }
}
