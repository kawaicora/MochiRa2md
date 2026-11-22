// SessionManager.cs —— 会话管理器：一局对局的 Start/End 归口。
//
// 职责边界：
//   · 只记当前对局会话（是否在局、对局实例、开始帧），不跑模拟、不画 UI；
//   · 对局开始由 LogicManager 调 BeginSession → Session.Start；回主菜单由 EndSession → Session.End；
//   · 一个玩家对应一个 HouseClass，House 的创建/归属在世界层，这里只持有对局引用。
using System;
using Ra2md.Core;
using Ra2md.World;

namespace Ra2md.Managers
{
    public sealed class SessionManager
    {
        private static SessionManager _instance;

        public static SessionManager Instance
        {
            get
            {
                if (_instance == null) _instance = new SessionManager();
                return _instance;
            }
        }

        /// <summary>当前是否处于对局会话中。</summary>
        public bool Active { get; private set; }

        /// <summary>当前对局实例（外壳/主菜单阶段为 null）。</summary>
        public SkirmishMatch Match { get; private set; }

        public uint StartFrame { get; private set; }

        public event Action OnStarted;
        public event Action OnEnded;

        /// <summary>开局：登记会话并触发开始事件。</summary>
        public void Start(SkirmishMatch match)
        {
            Match = match;
            Active = true;
            StartFrame = GameStatics.CurrentFrame;
            BootLog.Log("[会话] 对局开始");
            OnStarted?.Invoke();
        }

        /// <summary>结束对局：清会话引用并触发结束事件。</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            Match = null;
            BootLog.Log("[会话] 对局结束");
            OnEnded?.Invoke();
        }
    }
}
