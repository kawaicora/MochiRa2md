// FrameThrottle.cs —— 原版 sub_55E160 (0x55E160, 130 行) 的复刻。
// 关键语义（不许改）：
//   1) 剩余预算 = 帧预算 - (timeGetTime() - 帧起点)；
//   2) 游戏/过场态（状态 != 0 且 != 5）在剩余时间里**反复** 推进逻辑；
//   3) 只有当"剩余 >= 11 ms"时才额外补一次渲染（原版 if (iVar1 < 0xb) goto Sleep）；
//      低于 11ms 只 Sleep(0)，不渲染 —— 这条是原版手感与 CPU 占用的分水岭；
//   4) 菜单/退出态只 Sleep 剩余时间，不做补帧；
//   5) 每秒统计窗口：abcd48 += abcd40; abcd4c++; abcd44 = abcd40; abcd40 = 0（溢出 0x7FFFFFFF 归零）。
// Unity 偏差：一次 Unity 帧只能 Present 一次，所以补帧的"渲染"只写后备面，
// 由主线程在 Update 末尾统一 Present；表面像素语义仍然一致。
using System;
using Ra2md.Core;

namespace Ra2md.Core
{
    public static class FrameThrottle
    {
        public const int RenderThresholdMs = 11;   // 原版 0xb

        private static uint _statWindowStart;
        private static bool _statWindowInited;
        private static int _frameCountInWindow;     // abcd4c
        private static int _accumulatedMs;           // abcd48
        private static int _lastFrameMs;            // abcd44

        public static int FramesInLastSecond => _lastFrameMs == 0 ? _frameCountInWindow : _lastFrameMs;
        public static int AccumulatedMs => _accumulatedMs;

        public static void Run(int budgetMs, uint frameStart, Action advanceLogic, Action render)
        {
            int remaining = budgetMs - GameTime.Since(frameStart);
            if (remaining < 0) remaining = 0;

            bool inGame = GameStatics.State != SessionState.Menu && GameStatics.State != SessionState.Exit;

            if (inGame)
            {
                int guard = 0;
                while (remaining > 0 && guard++ < 64)
                {
                    int left = budgetMs - GameTime.Since(frameStart);
                    if (left <= 0)
                    {
                        break;
                    }

                    advanceLogic?.Invoke();

                    if (left >= RenderThresholdMs)
                    {
                        render?.Invoke();
                    }

                    GameTime.Yield();                       // Sleep(0)
                    remaining = budgetMs - GameTime.Since(frameStart);
                }
            }
            else if (remaining > 0)
            {
                GameTime.Sleep(remaining);                  // 菜单：整段睡掉
            }
        }

        /// <summary>每秒统计（对应 abcd48/abcd4c/abcd40/abcd44/abcd90=0x3c 那段）。</summary>
        public static void TickStatistics(int frameBudgetMs)
        {
            uint now = GameTime.Now();
            if (!_statWindowInited)
            {
                _statWindowInited = true;
                _statWindowStart = now;
                return;
            }

            if (GameTime.Since(_statWindowStart) < 1000)
            {
                return;
            }

            _accumulatedMs += frameBudgetMs;                // abcd48 += abcd40
            _frameCountInWindow++;                          // abcd4c++
            _lastFrameMs = frameBudgetMs;                   // abcd44 = abcd40
            if (_accumulatedMs > 0x7FFFFFFF)
            {
                _accumulatedMs = 0;
                _frameCountInWindow = 0;
            }

            _statWindowStart = now;                          // abcd90 = 0x3c
        }
    }
}
