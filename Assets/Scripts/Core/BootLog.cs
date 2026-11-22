// BootLog.cs —— 原版 Debug::Log 的等价物。
// 关键事实：release 版 gamemd 的 Debug::Log（0x4068E0）是空实现（WinMain 里连写 56 次都无输出，
// "Launcher not running....Bail!"(0x840F3C)、"COPYPROTECTION - ..."(0x81F408) 这些字符串
// 永远不会进任何日志）。所以原版"秒退无任何提示"不是 bug 而是这个空实现造成的可观测性缺失。
// 复刻时反过来：我们保留同名日志点，把输出落到 bootlog.txt（行缓冲），并保留"不输出任何 UI 提示"的行为。
using System;
using System.IO;
using System.Text;

namespace Ra2md.Core
{
    public static class BootLog
    {
        private static StreamWriter _writer;
        private static readonly object Sync = new object();
        private static string _path;

        public static void Open(string dir)
        {
            lock (Sync)
            {
                if (_writer != null) return;
                try
                {
                    _path = System.IO.Path.Combine(dir, "bootlog.txt");
                    _writer = new StreamWriter(_path, false, Encoding.UTF8) { AutoFlush = true };
                }
                catch (Exception)
                {
                    _writer = null;
                }
            }
        }

        public static string Path => _path;

        public static void Log(string message)
        {
            lock (Sync)
            {
                _writer?.WriteLine(message);
            }
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.Log(message);
#endif
        }

        /// <summary>对应原版 FUN_004068e0(格式串[, 参数]) 的调用点。原版是 printf 风格（%d/%s/%x），
        /// 所以这里把 printf 占位符按出现顺序换成 {0}/{1}…，保持日志文本和原版一致。</summary>
        public static void Log(string format, params object[] args)
        {
            if (args == null || args.Length == 0 || format == null)
            {
                Log(format ?? string.Empty);
                return;
            }

            var sb = new StringBuilder(format.Length + 16);
            int argIndex = 0;
            for (int i = 0; i < format.Length; i++)
            {
                if (format[i] == '%' && i + 1 < format.Length)
                {
                    char spec = format[i + 1];
                    if (spec == '%')
                    {
                        sb.Append('%');
                        i++;
                        continue;
                    }
                    if ("dsuxXf".IndexOf(spec) >= 0 && argIndex < args.Length)
                    {
                        sb.Append('{').Append(argIndex++).Append('}');
                        i++;
                        continue;
                    }
                }
                sb.Append(format[i]);
            }

            Log(string.Format(sb.ToString(), args));
        }

        public static void Close()
        {
            lock (Sync)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
