// WinMainReplica.cs —— 原版 WinMain (0x6BB9A0, 1332 行) 的启动链复刻，按行号顺序执行。
// 每一步都对应原版行号；任何不实现的分支都写 [未实现] 而不是猜。
// 重要：原版 240-295 行的 56 次 Debug::Log 在 release 版是空实现，所以"闪退无提示"；
// 复刻侧把同样多的日志点写进 bootlog.txt（行为只有可观测性不同，控制流一致）。
using System;
using System.IO;
using Ra2md.Core;
using Ra2md.Gfx;
using Ra2md.World;

namespace Ra2md.Boot
{
    public static class WinMainReplica
    {
        public static GameSession Session { get; private set; }

        /// <summary>返回 true = 已进入会话循环（Bootstrap 接管逐帧）；false = 原版 return 0 / exit(1)。</summary>
        public static bool Run(string commandLine)
        {
            BootLog.Log("==== RA2MD boot (WinMain 0x6BB9A0 replica) ====");

            // 238-239
            GameStatics.MainThreadId = 0;  // 原版 GetCurrentThreadId；复刻侧只作记录
            GameStatics.CommandLine = commandLine ?? string.Empty;
            BootLog.Log("[boot] hInstance set, thread id recorded (238-239)");

            // 240-295：原版连续 56 次 Debug::Log（release 空实现）
            BootLog.Log("[boot] 240-295: 56 x Debug::Log (release build is a no-op;原版零输出的原因)");

            // 296-347：原版两道版权闸门 + 启动器排队互斥体。
            // Unity 版由编辑器/玩家直接启动，不存在外部启动器，这三段在本工程里**整体旁路**，不再调用：
            //   - 49F5C0 互斥体探测、302-318 单实例互斥体、320-347 启动器排队互斥体
            //   - 49F620 事件 + 0xBEEF 消息 + MapViewOfFile
            // 对应函数仍保留在 CopyProtection.cs 仅供对照原版，运行时不会触发退出。
            BootLog.Log("[boot] launcher mutex/event gates bypassed (Unity build, no external launcher)");

            // 349：_controlfp(0x300,0x300)
            GameStatics.ApplyFpuContract();

            // 350：CRT 初始化（无对应物）
            BootLog.Log("[boot] CRT init skipped (350)");

            // 351：GetModuleFileNameA(hInstance, buf, 0x84)
            GameStatics.ExeDirectory = ResolveExecutableDirectory();
            BootLog.Log("[boot] exe dir: " + GameStatics.ExeDirectory);

            // 355-366：命令行 token（上限 20）
            var args = CommandLine.Parse(GameStatics.CommandLine);
            BootLog.Log("[boot] cmdline tokens=%d accepted=%s", args.Tokens.Count, args.Accepted);

            // 368-370：_splitpath/_makepath/SetCurrentDirectoryA
            // 编辑器里绝不能真改 cwd：Unity 会把"工程目录被切走"判为致命并中止播放。
#if UNITY_EDITOR
            BootLog.Log("[boot] SetCurrentDirectory skipped in editor (would detach Unity's project cwd)");
#else
            Directory.SetCurrentDirectory(GameStatics.ExeDirectory);
#endif

            // 372：sub_52F620(argc, argv) —— 完整 token 表 [未确认]，当前按放行处理
            if (!args.Accepted)
            {
                BootLog.Log("[boot] sub_52F620 rejected args -> skip init");
                return false;
            }

            // 374-425：RA2MD.INI + [Video] + [Network]
            var ini = new CcIni(Path.Combine(GameStatics.ExeDirectory, "RA2MD.INI"));
            BootLog.Log("[boot] RA2MD.INI loaded: " + ini.Path);

            ApplyNetworkSettings(ini);

            // 441：OleInitialize(0)（无对应物）
            BootLog.Log("[boot] OleInitialize skipped (441)");

            // 478-950：约 40 次 CoRegisterClassObject
            BootLog.Log("[boot] CoRegisterClassObject x~40 skipped (478-950, 场景/工具类 CLSID)");

            // 1080-1178：显示模式 + 位深校验
            var mode = DisplayMode.Apply(ini, forceWindowed: false);
            if (!mode.Ok)
            {
                BootLog.Log("[boot] display mode failed, text id %d -> exit(1)", mode.TextId);
                return false;
            }

            // 1179-1186：失焦等待 + 第二道版权闸门。Unity 的焦点由引擎消息泵管理，
            // 0xBEEF 消息也不存在外部发起方，两段在本工程里旁路。
            GameStatics.IsFocused = true;
            BootLog.Log("[boot] focus wait + launcher handshake bypassed (Unity build)");

            // 1190-1192：byte_8A00A0=0；非全屏清屏
            var surface = FramePresenter.Instance.Surface;
            surface?.Clear(0);

            // 1193：sub_5BC180 —— 游戏初始化/规则装载 [未实现]
            BootLog.Log("[boot] sub_5BC180 (game init/rules) — not implemented yet");

            // 1205-1217：三矩形 + 侧栏 168
            DisplayMode.ApplyViewRects(GameStatics.ScreenWidth, GameStatics.ScreenHeight);

            // 1225：sub_4F4780(DSurface::Hidden, 640, ...) —— 640 宽的逻辑后备面 [未实现]
            BootLog.Log("[boot] hidden surface 640 (1225) — not implemented yet");

            // 1226-1242：Zbuffer 0x30 字节 + 第二缓冲
            BootLog.Log("[boot] zbuffer h=%d (1226-1242)", Surface16.ZBufferHeight);

            // 1259-1266：[Intro] Play & 1 -> bit2
            ApplyIntroFlags(ini);

            // 1286-1297：SEH 外壳（复刻为 try/catch + Unity 日志捕获）
            Session = new GameSession();

            // 场景运行时生命周期接缝：局结束关闭场景上下文；每帧驱动触发器/小队计时。
            // 场景 Open(地图名) 的调用点待战役/遭遇战地图选择接入后挂在 sub_5BC180 规则装载段。
            Session.OnFrameExtra += () => ScenarioContext.Current?.ProcessFrame();
            Session.OnSessionEnd += () =>
            {
                SkirmishMatch.CloseCurrent();     // 遭遇战装配层收尾（含 ScenarioContext）
                ScenarioContext.Current?.Close();
            };

            Session.Begin();
            BootLog.Log("[boot] entering session loop (48CCC0)");
            return true;
        }

        private static string ResolveExecutableDirectory()
        {
            // 原版 GetModuleFileNameA + _splitpath + _makepath；复刻侧用 Unity 当前目录兜底。
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Directory.GetCurrentDirectory();
                }
                return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return Directory.GetCurrentDirectory();
            }
        }

        private static void ApplyNetworkSettings(CcIni ini)
        {
            // 389-395：[Network] Socket；port>0 且 port+0x4000 落在 [0x4000,0x8000) 才启用
            int port = ini.GetInt("Network", "Socket", 0);
            GameStatics.NetworkPort = port;
            if (port > 0)
            {
                int scaled = port + 0x4000;
                if (scaled >= 0x4000 && scaled < 0x8000)
                {
                    GameStatics.NetworkPortScaled = scaled;
                }
            }

            // 396-417：[Network] DestNet，逗号分隔 %x，≥4 个才置位
            var destNet = ini.GetString("Network", "DestNet", string.Empty);
            if (!string.IsNullOrEmpty(destNet))
            {
                var parts = destNet.Split(',');
                if (parts.Length >= 4)
                {
                    GameStatics.DestNetPresent = true;
                    for (int i = 0; i < 3 && i + 1 < parts.Length; i++)
                    {
                        if (int.TryParse(parts[i + 1].Trim(), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var value))
                        {
                            GameStatics.DestNetDwords[i] = value;
                        }
                    }
                }
            }
            BootLog.Log("[boot] network port=%d scaled=%d destnet=%s", GameStatics.NetworkPort,
                GameStatics.NetworkPortScaled, GameStatics.DestNetPresent);
        }

        private static void ApplyIntroFlags(CcIni ini)
        {
            bool playIntro = (ini.GetInt("Intro", "Play", 0) & 1) != 0;
            if (playIntro)
            {
                // 1276-1282：把 [Intro] Play 写回 0，然后播片 -> 所以第二次启动不会重复播
                ini.SetInt("Intro", "Play", 0);
                BootLog.Log("[boot] intro playback requested -> [Intro] Play=0 written back (1276-1282)");
            }
            else
            {
                BootLog.Log("[boot] intro skipped");
            }
        }
    }
}
