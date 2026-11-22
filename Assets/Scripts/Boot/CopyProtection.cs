// CopyProtection.cs —— 原版两道版权闸门
//   FUN_0049f5c0 (0x49F5C0) : "COPYPROTECTION - Checking if launcher is running"
//   FUN_0049f620 (0x49F620) : "COPYPROTECTION - Notify launcher"
// 伪代码源：ra2de_aaa\code_g\sub_49F5C0.c / sub_49F620.c（各 15 / 45 行，已全文核对）
// 这两条是"无窗口、无提示、直接闪退"的唯一机制解释：失败时 WinMain 296 行 return 0 / 1182 行 exit(1).
using System;
using System.Runtime.InteropServices;
using Ra2md.Core;

namespace Ra2md.Boot
{
    public static class CopyProtection
    {
        // ---- 原版字面量（不要改，改了就和真实启动器对不上）----
        private const string LauncherMutex = "48BC11BD-C4D7-466b-8A31-C6ABBAD47B3E"; // 0x81F3E0
        private const string LauncherEvent = "D6E7FC97-64F9-4d28-B52C-754EDF721C6F"; // 0x81F498
        private const int    LauncherMessageId = 0xBEEF;                              // cmp dword, 0xBEEF
        private const int    FileMapAccess = 0xF001F;                                // MapViewOfFileEx 的 4 个 prot 位
        private const uint   ErrorAlreadyExists = 183;                               // 0xB7

        private const uint EVENT_MODIFY_STATE = 0x0002;
        private const uint SYNCHRONIZE = 0x00100000;
        private const uint MUTEX_ALL_ACCESS = 0x1F0001;

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr CreateMutexA(IntPtr security, bool initialOwner, string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr OpenMutexA(uint access, bool inherit, string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr OpenEventA(uint access, bool inherit, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetEvent(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFileEx(IntPtr fileMapping, uint access, uint offsetHigh,
            uint offsetLow, IntPtr bytesToMap, IntPtr baseAddress);

        /// <summary>FUN_0049f5c0：CreateMutex(0,0,名字) 后看 GetLastError()==183。</summary>
        /// <returns>true = 启动器在运行（放行）；false = 原版会 return 0 秒退。</summary>
        public static bool LauncherRunning()
        {
            BootLog.Log("COPYPROTECTION - Checking if launcher is running");
            IntPtr handle = CreateMutexA(IntPtr.Zero, false, LauncherMutex);
            uint lastError = unchecked((uint)Marshal.GetLastWin32Error());
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
            bool result = lastError == ErrorAlreadyExists;
            BootLog.Log("result was %d", result ? 1 : 0);
            return result;
        }

        /// <summary>
        /// FUN_0049f620：开事件 -> SetEvent -> 等 0xBEEF 消息 -> MapViewOfFileEx(lParam)。
        /// 偏差说明（必须写清）：原版用 PeekMessageA(&msg, 0, 0xbeef, 0xbeef, 1) 从**自己的窗口消息队列**
        /// 取 0xBEEF；Unity 的消息泵由引擎独占，C# 侧 Cannot 安全地去 Peek 同一线程队列（会吞掉 Unity 的
        /// WM_* 消息）。因此这里保留 SetEvent/映射两段，消息段在 enableMessageWait=false 时跳过。
        /// 若要与真实启动器联调，把 enableMessageWait 打开，并且只在独立 Win32 宿主进程里跑。
        /// </summary>
        public static bool NotifyLauncher(bool enableMessageWait = false)
        {
            BootLog.Log("COPYPROTECTION - Notify launcher");

            uint outerStart = GameTime.Now();
            IntPtr shared = IntPtr.Zero;

            while (GameTime.Since(outerStart) < 60000)
            {
                IntPtr evt = OpenEventA(EVENT_MODIFY_STATE, false, LauncherEvent);
                if (evt != IntPtr.Zero)
                {
                    SetEvent(evt);
                    CloseHandle(evt);
                    BootLog.Log("Launcher notified.");
                    BootLog.Log("Waiting for message from launcher.");

                    if (!enableMessageWait)
                    {
                        BootLog.Log("COPYPROTECTION skipped: Unity owns the message queue (deviation, see header)");
                        return true;
                    }

                    // 真实联调路径：由外部宿主进程提供 0xBEEF 消息与文件映射句柄。
                    uint innerStart = GameTime.Now();
                    while (GameTime.Since(innerStart) < 10000)
                    {
                        GameTime.Yield();
                        IntPtr mapping = CopyProtection.TryGetLauncherMapping();
                        if (mapping != IntPtr.Zero)
                        {
                            shared = MapViewOfFileEx(mapping, FileMapAccess, 0, 0, IntPtr.Zero, IntPtr.Zero);
                            break;
                        }
                    }

                    if (shared != IntPtr.Zero)
                    {
                        return true;
                    }
                    break;
                }

                GameTime.Yield();
            }

            BootLog.Log("***** Failed to notify launcher!");
            return false;
        }

        /// <summary>占位：真实实现由外部启动器桥接进程通过共享内存/管道提供映射句柄。</summary>
        private static IntPtr TryGetLauncherMapping() => IntPtr.Zero;

        /// <summary>原版单实例锁：CreateMutex(0,0,Name)，183 表示已有实例。</summary>
        public static bool IsAnotherInstanceRunning()
        {
            IntPtr handle = CreateMutexA(IntPtr.Zero, false, "RA2MD_SINGLETON");
            uint lastError = unchecked((uint)Marshal.GetLastWin32Error());
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
            return lastError == ErrorAlreadyExists;
        }

        /// <summary>原版第二具名互斥体（启动器排队：OpenMutex 0x1F0001 + WaitForSingleObject 30s，
        /// 失败则 Create 并 Sleep(2500) 重试）。复刻侧只做非阻塞探测，避免启动卡 30 秒。</summary>
        public static bool TryAcquireLauncherQueue()
        {
            IntPtr handle = OpenMutexA(MUTEX_ALL_ACCESS, false, "A01AF999-3349-2111-1111-111111111111");
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
                return true;
            }
            return false;
        }
    }
}

