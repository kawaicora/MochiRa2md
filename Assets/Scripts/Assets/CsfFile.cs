// CsfFile.cs —— 西木 CSF 语言文件解析。
//
// 文件结构（version 3 = 泰伯利亚之日/RA2/尤里的复仇）：
//   header : " FSC" | version | labelCount | stringCount | unused | language
//   label  : " LBL" | pairCount | nameLength | name(ASCII 字节) | pair × pairCount
//   pair   : " RTS" | length(字符数) | text(version>=3 为 UTF-16LE，否则单字节)
//            "WRTS"| length | extra(4B，忽略) | text
//
// 所有界面文字一律按 key 取：换语言 = 换一份本地化 CSF，代码不动。
using System;
using System.Collections.Generic;
using System.Text;
namespace Ra2md.Assets
{
    public sealed class CsfFile
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int Version { get; private set; }
        public int Language { get; private set; }
        public int LabelCount { get; private set; }
        public int StringCount { get; private set; }
        public int Count { get { return _values.Count; } }

        /// <summary>全部 key（诊断/回填用，只读视图）。</summary>
        public IEnumerable<string> Keys { get { return _values.Keys; } }

        public bool TryGet(string key, out string value)
        {
            if (string.IsNullOrEmpty(key)) { value = null; return false; }
            return _values.TryGetValue(key, out value);
        }

        /// <summary>按 key 取文本，缺失时返回 fallback（调用方给出的兜底串）。</summary>
        public string Get(string key, string fallback)
        {
            return TryGet(key, out var value) ? value : fallback;
        }

        public static bool TryParse(byte[] data, out CsfFile csf, out string error)
        {
            csf = null;
            error = null;

            if (data == null) { error = "数据为空"; return false; }
            if (data.Length < 24) { error = "长度不足 24 字节"; return false; }

            // 签名 " FSC"
            if (data[0] != (byte)' ' || data[1] != (byte)'F' || data[2] != (byte)'S' || data[3] != (byte)'C')
            {
                error = "签名不是 \" FSC\"";
                return false;
            }

            int version = BitConverter.ToInt32(data, 4);
            int labelCount = BitConverter.ToInt32(data, 8);
            int stringCount = BitConverter.ToInt32(data, 12);
            int language = BitConverter.ToInt32(data, 20);
            if (labelCount < 0 || stringCount < 0)
            {
                error = "头部计数非法";
                return false;
            }

            var result = new CsfFile
            {
                Version = version,
                Language = language,
                LabelCount = labelCount,
                StringCount = stringCount,
            };

            int charSize = version >= 3 ? 2 : 1;
            int pos = 24;
            var labelsRead = 0;

            while (labelsRead < labelCount && pos + 12 <= data.Length)
            {
                if (!TryReadTag(data, ref pos, out var tag))
                {
                    error = "标签头越界 @" + (pos - 4);
                    break;
                }
                if (tag != "LBL")
                {
                    error = "期望 LBL，实际 " + tag + " @" + (pos - 4);
                    break;
                }

                int pairs = ReadInt32(data, ref pos);
                if (pairs < 0 || pairs > 64) { error = "pairCount 非法 @" + pos; break; }

                int nameLength = ReadInt32(data, ref pos);
                if (nameLength < 0 || pos + nameLength > data.Length) { error = "nameLength 非法 @" + pos; break; }

                var name = Encoding.ASCII.GetString(data, pos, nameLength);
                pos += nameLength;

                for (var pair = 0; pair < pairs; pair++)
                {
                    if (!TryReadTag(data, ref pos, out var pairTag))
                    {
                        error = "字符串头越界 @" + (pos - 4);
                        break;
                    }

                    bool extended = pairTag == "WRTS";
                    if (!extended && pairTag != "RTS")
                    {
                        error = "期望 RTS/WRTS，实际 " + pairTag + " @" + (pos - 4);
                        break;
                    }

                    int length = ReadInt32(data, ref pos);
                    if (length < 0) { error = "字符串长度非法 @" + (pos - 4); break; }

                    int bytes = length * charSize;
                    if (pos + bytes > data.Length) { error = "字符串体越界 @" + pos; break; }

                    // 尤里的复仇（version 3）的 CSF 文本是逐字节取反存储的：
                    // 实测 主選單 期望 3B-4E 78-90 AE-55，文件里是 C4-B1 87-6F 51-AA（每字节 ^0xFF）。
                    var raw = new byte[bytes];
                    Array.Copy(data, pos, raw, 0, bytes);
                    if (version >= 3)
                    {
                        for (var b = 0; b < bytes; b++) raw[b] = (byte)(raw[b] ^ 0xFF);
                    }

                    var text = charSize == 2
                        ? Encoding.Unicode.GetString(raw)
                        : Encoding.UTF8.GetString(raw);
                    pos += bytes;

                    if (extended)
                    {
                        // WRTS 扩展串在文本之后还有一段 ASCII 附加字段（语音/文件名，实测长度 8），
                        // 布局为：extraLen(4) + ASCII(extraLen)。必须跳过，否则整条流错位。
                        int extraLength = ReadInt32(data, ref pos);
                        if (extraLength < 0 || pos + extraLength > data.Length)
                        {
                            error = "扩展字段越界 @" + pos;
                            break;
                        }
                        pos += extraLength;
                    }

                    if (pair == 0 && name.Length > 0) result._values[name] = text;
                }

                labelsRead++;
                if (error != null) break;
            }

            if (error != null)
            {
                // 允许半途失败：已解析出的条目仍可用，但把错误交回调用方记录。
                csf = result.Count > 0 ? result : null;
                return result.Count > 0;
            }

            csf = result;
            return true;
        }

        private static bool TryReadTag(byte[] data, ref int pos, out string tag)
        {
            tag = null;
            if (pos + 4 > data.Length) return false;

            var chars = new char[4];
            for (var i = 0; i < 4; i++) chars[i] = (char)data[pos + i];

            var text = new string(chars);
            // 标签统一带前导空格：" LBL" / " RTS"；扩展串是 "WRTS"（无前导空格）。
            tag = text[0] == ' ' ? text.Substring(1) : text;
            pos += 4;
            return true;
        }

        private static int ReadInt32(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) { pos = data.Length; return 0; }
            var value = BitConverter.ToInt32(data, pos);
            pos += 4;
            return value;
        }
    }
}
