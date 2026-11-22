// GameSession.cs —— 原版 sub_48CCC0 (0x48CCC0, 95 行)：会话循环（"外层每局、内层每帧"）。
// 结构：
//   sub_7CA650()                     子系统初始化
//   sub_52BA60() != 0  -> MessageBoxIndirectA（原版弹错误框；复刻侧只写日志，避免和 Unity 弹窗打架）
//   while (sub_52D9A0() != 0) {       每轮 = 一局
//       局初始化 -> do { c = sub_55D360(); if (c && sub_55CFD0()) break; sub_48C8B0(); } while (!sub_55CFD0());
//       局收尾
//   }
//   渲染器 vtable+0x14 关闭
// Unity 侧：Unity 的 Update 驱动 StepFrame()，一次 Update = 一次 sub_55D360；会话边界照原版处理。
using System;
using Ra2md.Core;

namespace Ra2md.Core
{
    public sealed class GameSession
    {
        public Action OnSessionBegin;      // 局初始化（sub_4A3C30/sub_54F720 等）
        public Action OnSessionEnd;        // 局收尾
        public Action OnFrameExtra;        // sub_48C8B0（帧间杂项）
        public Action OnRendererShutdown;  // vtable+0x14

        public readonly MainLoop Loop = new MainLoop();

        private bool _active;
        private bool _sessionStarted;

        public bool Active => _active;

        public void Begin()
        {
            BootLog.Log("[session] init (sub_7CA650)");
            _active = true;
            _sessionStarted = false;
            GameStatics.ExitConfirmed = false;
        }

        /// <summary>一次 Unity 帧 = 一次 sub_55D360。返回 true 表示整个会话循环已结束。</summary>
        public bool StepFrame()
        {
            if (!_active)
            {
                return true;
            }

            if (!_sessionStarted)
            {
                _sessionStarted = true;
                StartupSession();
            }

            bool continueFrame = Loop.Tick();

            // 原版：if (c && sub_55CFD0()) break;   —— 退出确认后结束本局
            if (!continueFrame || GameStatics.ExitConfirmed)
            {
                EndSession();
                if (GameStatics.NextSession == 0)
                {
                    _active = false;
                    OnRendererShutdown?.Invoke();
                    BootLog.Log("[session] loop ended (sub_52D9A0()==0)");
                    return true;
                }

                // 下一局：重置会话内状态，回主菜单
                GameStatics.State = SessionState.Menu;
                GameStatics.CurrentFrame = 0;
                GameStatics.Sync = SyncFlags.None;
                GameStatics.ExitRequested = false;
                GameStatics.ExitConfirmed = false;
                _sessionStarted = false;
                return false;
            }

            OnFrameExtra?.Invoke();
            return false;
        }

        private void StartupSession()
        {
            // 原版每局开头：
            //   a8e7ac = 0; 81d4e0 = 1; sub_4A3C30(); sub_54F720();
            //   if (a8d5f8 & 2) { renderer+0xc(); abcidfc = abce00 = 0; }
            //   if (a8b238 == 4) { sub_6C87F0(); a8f900=0; b0bdb8=0; b0bd78=0; }
            //   if (887368) sub_7241A0();
            //   a8ed9c = *(BYTE*)(a8b230 + 0x35a2);
            //   a8eda0 = 0; 822cfc = 1; sub_69BAB0();
            GameStatics.ExitRequested = true;      // DAT_00a8e9a0 = 1（主循环入口判据）
            GameStatics.IsActive = true;           // 822cfc / bIsActive
            GameStatics.CurrentFrame = 0;
            OnSessionBegin?.Invoke();
            BootLog.Log("[session] begin, state=%s", GameStatics.State);
        }

        private void EndSession()
        {
            // 原版每局收尾：sub_72DFB0(); sub_69BB40(); a8ed5c=0; a8e378=0; sub_48E0B0(); 主线程状态复位
            BootLog.Log("[session] end, state=%s", GameStatics.State);
            OnSessionEnd?.Invoke();
            GameStatics.IsActive = false;
        }

        /// <summary>原版局内状态切换（对应 switch (a8b238) 的四个分支，钩子留空表示未接实现）。</summary>
        public Action OnStateDispatch;   // case 1,2: sub_5F0070; case 3: sub_5DA720; case 4: sub_699450

        public void DispatchState()
        {
            switch (GameStatics.State)
            {
                case SessionState.Loading:
                case SessionState.ScoreScreen:
                case SessionState.Campaign:
                case SessionState.Skirmish:
                    OnStateDispatch?.Invoke();
                    break;
            }
        }
    }
}
