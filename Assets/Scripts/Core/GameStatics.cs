// GameStatics.cs —— 原版全局的等价物（名字后面注释就是原版符号，方便逐条核对）
using System;

namespace Ra2md.Core
{
    /// <summary>DAT_00a8b238：会话状态机。整个会话循环 48CCC0 的调度键。</summary>
    public enum SessionState
    {
        Menu = 0,          // 0 主菜单/外壳（帧节流 500ms 轮询，帧预算 2）
        Loading = 1,       // 1 加载/过场
        ScoreScreen = 2,   // 2 结算/简报
        Campaign = 3,      // 3 剧情/战役
        Skirmish = 4,      // 4 战斗（每 8 帧跑一次 sub_542520）
        Exit = 5,          // 5 退出（帧节流 500ms 轮询）
    }

    /// <summary>SessionClass::GameMode（GeneralDefinitions.h 872-878，数值照抄）：
    /// 区分战役 / 局域网 / 互联网 / 遭遇战。与帧调度的 SessionState 是两个独立字段。</summary>
    public enum GameModeKind : uint
    {
        Campaign = 0x0,
        LAN = 0x3,
        Internet = 0x4,
        Skirmish = 0x5,
    }

    /// <summary>DAT_00a8d5f8：同步/渲染位表。</summary>
    [Flags]
    public enum SyncFlags
    {
        None = 0,
        SyncSend = 1 << 1,      // bit1 发送帧状态（55D360 的 sub_6D6170/sub_473AE0 段）
        SyncReceive = 1 << 2,   // bit2 接收并对校验（sub_473B10/sub_55F690 段）
        SyncPending = 1 << 0,   // bit0 待同步（48CCC0 里 a8d5f8 & 3 触发生成 sub_473CE0）
        RenderNull = 1 << 3,    // bit3 空渲染（原版用于跳过部分 UI 绘制）[未确认]
    }

    public static class GameStatics
    {
        // ---- 进程/线程 ----
        public static IntPtr HInstance;                 // Game::hInstance
        public static uint MainThreadId;                // dword_B730E8
        public static string ExeDirectory = "";         // (WinMain 368-370 SetCurrentDirectoryA 的目标)
        public static string CommandLine = "";          // lpCmdLine

        // ---- 会话/帧 ----
        public static SessionState State = SessionState.Menu;   // DAT_00a8b238
        /// <summary>SessionClass::Instance.GameMode @0xA8B238：战役/局域网/互联网/遭遇战。</summary>
        public static GameModeKind GameMode = GameModeKind.Campaign;
        public static SyncFlags Sync = SyncFlags.None;          // DAT_00a8d5f8
        public static uint CurrentFrame;                        // DAT_00a8ed84
        public static uint FrameCeiling = 0x7FFFFFFF;           // DAT_00a8e2e4
        public static bool ExitRequested;                       // DAT_00a8e9a0
        public static bool ExitConfirmed;                       // sub_55CFD0 的"退出已确认"
        public static uint NextSession = 1;                      // sub_52D9A0 的返回值来源（1=还有下一局）

        // ---- 焦点/激活 ----
        public static bool IsFocused = true;                    // Game::bIsFocused（DAT_00a8ed80）
        public static bool IsActive;                            // Game::bIsActive
        public static bool InMenuShellLoaded;                   // DAT_00abce08（首帧外壳件已加载）

        // ---- 视频 ----
        public static bool VideoBackBuffer;                     // Game::bVideoBackBuffer
        public static bool AllowModeToggle;                     // byte_A8ED63
        public static int ScreenWidth = 800;                    // qword_A8EB84 低 dword
        public static int ScreenHeight = 600;                   // qword_A8EB84 高 dword
        public static int BitDepth = 16;                        // byte_8175B0 ? 16 : 8；原版要求 2(=16bpp)
        public static bool IsFullScreen;                        // byte_89F978
        public static int SidebarWidth = 168;                   // WinMain 1207 行 v221[2]
        public static int ViewReservedTop;                      // dword_886FA4（Z 面顶部预留）
        public static int LogicalViewHeight = 480;              // WinMain 1226 行固定 480

        // ---- 声音/网络 ----
        public static int NetworkPort;                          // [Network] Socket
        public static int NetworkPortScaled;                    // port + 0x4000（仅当落在 [0x4000,0x8000)）
        public static readonly int[] DestNetDwords = new int[3]; // dword_A8D600/604/608
        public static bool DestNetPresent;                      // dword_A8D5FC

        // ---- 逻辑节奏 ----
        public static int Fps = 15;                             // a8b558 / a8eb60（菜单默认 2 仅用于占位）
        public static int TickPerSecond = 0x3c;                 // a887350 = 0x3c / fps
        public static int FrameBudgetMs = 66;                   // a887330 = 1000 / fps
        public static int LagMs;                                // DAT_00a8b550（>0x1e 才做延迟补偿）

        /// <summary>WinMain 349 行 _controlfp(0x300,0x300) 的等价说明：Unity 无法改 FPU 控制字，
        /// 复刻侧的确定性靠"全部仿真使用整数/定点"保证，浮点只用于渲染插值。</summary>
        public static void ApplyFpuContract()
        {
            BootLog.Log("[boot] FPU contract: integer/fixed-point simulation only (原版 _controlfp(0x300,0x300))");
        }

        // ---- GameMode 判定（对照 SessionClass::IsCampaign / IsSkirmish / IsSingleplayer / IsMultiplayer）----

        public static bool IsCampaign { get { return GameMode == GameModeKind.Campaign; } }
        public static bool IsSkirmish { get { return GameMode == GameModeKind.Skirmish; } }

        public static bool IsSingleplayer
        {
            get { return GameMode == GameModeKind.Campaign || GameMode == GameModeKind.Skirmish; }
        }

        public static bool IsMultiplayer
        {
            get { return GameMode == GameModeKind.LAN || GameMode == GameModeKind.Internet; }
        }
    }
}
