// CommandLine.cs —— WinMain 355-366 行的 strtok(lpCmdLine, " ") + 上限 20
// 原版：每个 token 走 sub_727CF0(token) 转成 ID，存进 argv 表（v7 从 1 起，<20 截止）；
// 之后 WinMain 372 行 sub_52F620(argc, argv) 决定是否继续初始化。
// sub_52F620 的完整 token 表仍 [未确认]，这里只保留结构，未知 token 原样记录，不做语义猜测。
using System;
using System.Collections.Generic;

namespace Ra2md.Boot
{
    public static class CommandLine
    {
        public const int MaxTokens = 20;   // 原版 v7 < 20

        public sealed class ParsedArgs
        {
            public readonly List<string> Tokens = new List<string>(MaxTokens);
            public readonly List<int> Ids = new List<int>(MaxTokens);   // sub_727CF0 的结果
            public string Mod;        // -MOD=xxx 形式（原版由 sub_52F620 消费）[未确认具体键名]
            public string SpawnPath;  // 录像/回放路径 [未确认]
            public bool Accepted = true; // sub_52F620 返回值（false -> WinMain 跳过初始化）
        }

        public static ParsedArgs Parse(string commandLine)
        {
            var result = new ParsedArgs();
            if (commandLine == null)
            {
                return result;
            }

            foreach (var token in Tokenize(commandLine))
            {
                if (result.Tokens.Count >= MaxTokens)
                {
                    break;
                }
                result.Tokens.Add(token);
                result.Ids.Add(TokenToId(token));
            }

            // 已知形态只按"原版代码里出现过"的写法解析，其余交给后续 sub_52F620 复刻。
            foreach (var token in result.Tokens)
            {
                if (token.StartsWith("-MOD=", StringComparison.OrdinalIgnoreCase))
                {
                    result.Mod = token.Substring(5);
                }
                else if (token.StartsWith("-SPAWN", StringComparison.OrdinalIgnoreCase) && token.Length > 7)
                {
                    result.SpawnPath = token.Substring(7).TrimStart('=');
                }
            }

            return result;
        }

        /// <summary>strtok(..., " ") 的等价：空格为唯一分隔符，连续空格折叠。</summary>
        private static IEnumerable<string> Tokenize(string line)
        {
            int index = 0;
            while (index < line.Length)
            {
                while (index < line.Length && line[index] == ' ') index++;
                if (index >= line.Length) break;
                int start = index;
                while (index < line.Length && line[index] != ' ') index++;
                yield return line.Substring(start, index - start);
            }
        }

        /// <summary>sub_727CF0：token -> ID。原版映射表未导出，这里用稳定的 FNV-1a（只做内部身份，
        /// 不参与任何与原版数值的比较）。[未确认原版算法]</summary>
        public static int TokenToId(string token)
        {
            uint hash = 2166136261u;
            foreach (char c in token.ToLowerInvariant())
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            return unchecked((int)hash);
        }
    }
}
