// MainLoop.cs —— 原版 sub_55D360 (0x55D360, 488 行) 的一帧实现。
// 逐段对应（段号见 Docs/伪代码1比1复刻-启动链.md）：
//   入口    if (!a8e9a0) return 1;
//   等待    while (!a8ed80) { 状态 1..4 -> Sleep(10)+泵；状态 0/5 -> Sleep(500)+泵 }
//   帧号    if (a8e2e4 <= a8ed84) sub_69A300()
//   帧预算  fps -> a887350(=0x3c/fps) / aac(=1000/fps)
//   延迟    a8b550 > 0x1e 时按 1/4,1/2,3/4 三档各 +10ms
//   动作序  sub_4F4320 -> sub_55DEE0 -> sub_55F1E0 -> (帧号&7==7 && 状态4) sub_542520 -> sub_4D2370 -> sub_4F4480
//   同步    a8d5f8 bit1 发送 / bit2 接收（本实现只留钩子，序列化在 Network 层）
//   帧尾    elapsed=min(delta,1000) 累计; 帧号++; 74 sub_55E160; 75-80 收尾
using System;
using Ra2md.Gfx;

namespace Ra2md.Core
{
    public sealed class MainLoop
    {
        // ---- 每帧固定动作的挂点（对应原版 sub_xxx；未接实现时保持 null，不伪造行为）----
        public Action OnPumpMessages;      // sub_5D4D50
        public Action OnFrameCeiling;      // sub_69A300
        public Action OnInputSnapshot;     // sub_4F4320
        public Action OnInputApply;        // sub_55DEE0
        public Action OnPlaybackAdvance;   // sub_55F1E0（DAT_00a8b8b4 为真时）
        public Action OnEveryEighthFrame;  // sub_542520（仅状态 4）
        public Action OnSimulate;          // sub_4D2370
        public Action OnRender;            // sub_4F4480（GScreenClass::Render）
        public Action OnNetworkTick;       // a8d5f8 同步段
        public Action OnFrameTail;         // sub_55AFB0 / sub_637550 / sub_5D4430 / sub_647260 / sub_637270

        public bool PlaybackActive;        // DAT_00a8b8b4

        private int _frameBudgetMs = 66;
        private uint _frameStart;
        private uint _tickStart;

        public int FrameBudgetMs => _frameBudgetMs;
        public uint FrameStart => _frameStart;

        /// <summary>一帧。返回值语义与 sub_55D360 一致：true = 继续下一帧。</summary>
        public bool Tick()
        {
            if (!GameStatics.ExitRequested)
            {
                return true;
            }

            // ---- 入口：等焦点 ----
            WaitForFocus();

            // ---- 帧号上限 ----
            if (GameStatics.FrameCeiling <= GameStatics.CurrentFrame)
            {
                OnFrameCeiling?.Invoke();
            }

            _frameStart = GameTime.Now();
            _tickStart = _frameStart;

            // ---- 帧预算（55D360 的 518-248 段）----
            ComputeFrameBudget();

            // ---- 延迟补偿（a8b550 > 0x1e）----
            ApplyLagCompensation();

            // ---- 每帧动作序（顺序不可调，见 4.5 节）----
            GameStatics.Sync &= ~SyncFlags.None; // 占位：保持位表语义
            OnInputSnapshot?.Invoke();
            OnInputApply?.Invoke();

            if (PlaybackActive)
            {
                OnPlaybackAdvance?.Invoke();
            }

            // 每 8 帧，且仅战斗态：用帧号低 3 位，不是计数器
            if ((GameStatics.CurrentFrame & 7) == 7 && GameStatics.State == SessionState.Skirmish)
            {
                OnEveryEighthFrame?.Invoke();
            }

            OnSimulate?.Invoke();
            OnRender?.Invoke();

            // ---- 同步段 ----
            if ((GameStatics.Sync & (SyncFlags.SyncSend | SyncFlags.SyncReceive)) != 0)
            {
                OnNetworkTick?.Invoke();
            }

            // ---- 帧尾 ----
            OnFrameTail?.Invoke();

            // ---- 节流（55E160）----
            FrameThrottle.Run(_frameBudgetMs, _frameStart, OnSimulate, OnRender);

            // ---- 帧号推进与闹钟 ----
            if (!GameStatics.ExitConfirmed)
            {
                GameStatics.CurrentFrame++;
                GameStatics.Sync &= ~SyncFlags.SyncReceive;
            }

            return !GameStatics.ExitRequested;
        }

        private void WaitForFocus()
        {
            while (!GameStatics.IsFocused)
            {
                if (GameStatics.State != SessionState.Menu && GameStatics.State != SessionState.Exit)
                {
                    GameTime.Sleep(10);       // 窗口内：10ms
                    OnPumpMessages?.Invoke();
                    break;                    // 原版 pump 一次后即 break，靠外层再进
                }
                GameTime.Sleep(500);          // 菜单/退出：500ms
                OnPumpMessages?.Invoke();
            }
        }

        private void ComputeFrameBudget()
        {
            int fps = GameStatics.Fps;
            if (GameStatics.State == SessionState.Menu)
            {
                // 菜单态：a8eddc ? 2 : a8eb60（原版这里 fps 就是帧预算本身，单位 ms 的倒数语义见 4.3）
                fps = 2;
            }
            else
            {
                if ((GameStatics.Sync & SyncFlags.SyncReceive) != 0)
                {
                    fps = 0;
                }
            }

            GameStatics.TickPerSecond = fps > 0 ? 0x3c / fps : 0;
            _frameBudgetMs = fps > 0 ? 1000 / fps : 2;
            GameStatics.FrameBudgetMs = _frameBudgetMs;
        }

        /// <summary>a8b550 > 0x1e 时按三档各加 10ms（阈值是 fps/4、fps/2、3*fps/4）。</summary>
        private void ApplyLagCompensation()
        {
            if (GameStatics.LagMs <= 0x1e)
            {
                return;
            }

            int fps = GameStatics.Fps;
            if (GameStatics.LagMs >= fps >> 2) _frameBudgetMs += 10;
            if (GameStatics.LagMs >= fps >> 1) _frameBudgetMs += 10;
            if (GameStatics.LagMs >= (fps * 3) >> 2) _frameBudgetMs += 10;
            GameStatics.FrameBudgetMs = _frameBudgetMs;
        }
    }
}
