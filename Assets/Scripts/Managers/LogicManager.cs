// LogicManager.cs —— 逻辑总控：初始化 / 菜单阶段 / 对局阶段的唯一归属。
//
// 职责边界（对照用户口径）：
//   · 场景根对象只挂 GameManager；GameManager 启动后把生命周期交给本管理器；
//   · 阶段状态机：Boot → Menu → Loading → Session → Game → Exit；
//   · 菜单要画什么、画在哪，由本管理器决定，具体控件构造一律走 UIManager；
//   · 对局开始/结束由本管理器驱动 SessionManager，House 与模拟在世界层；
//   · 设置参数归 GlobalConfigManager，运行时数据归 GlobalDataManager，本类不越界存。
using System;
using Ra2md.Core;
using Ra2md.World;
using UnityEngine;

namespace Ra2md.Managers
{
    /// <summary>LogicManager 的阶段划分（与原版 SessionState 对应，但归口在逻辑层）。</summary>
    public enum LogicPhase
    {
        Boot = 0,
        Menu = 1,
        Loading = 2,
        Session = 3,
        Game = 4,
        Exit = 5,
    }

    public sealed class LogicManager : MonoBehaviour
    {
        private static LogicManager _instance;

        public static LogicManager Instance
        {
            get
            {
                if (_instance == null) Ensure();
                return _instance;
            }
        }

        public static LogicManager Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Ra2md.LogicManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LogicManager>();
            return _instance;
        }

        public LogicPhase Phase { get; private set; } = LogicPhase.Boot;

        public UIManager UI => UIManager.Instance;
        public SessionManager Session => SessionManager.Instance;

        public event Action<LogicPhase> PhaseChanged;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>启动初始化：保证各子管理器在场，随后进入菜单阶段。</summary>
        public void Boot()
        {
            Phase = LogicPhase.Boot;
            ManagerBootstrap.Ensure();
            UIManager.Ensure();
            BootLog.Log("[逻辑] 初始化完成，进入菜单阶段");
            EnterMenu();
        }

        /// <summary>进入主菜单/外壳阶段。</summary>
        public void EnterMenu()
        {
            Session.End();
            SetPhase(LogicPhase.Menu);
            // 菜单阶段必须有菜单控制器在场：场景里挂了就复用，没挂就由这里补挂。
            MainMenuController.Ensure();
        }

        /// <summary>加载对局（过场/读档阶段留位）。</summary>
        public void EnterLoading() => SetPhase(LogicPhase.Loading);

        /// <summary>对局开始：登记会话并进入对局阶段。</summary>
        public void BeginSession(SkirmishMatch match)
        {
            Session.Start(match);
            SetPhase(LogicPhase.Session);
        }

        /// <summary>对局进行中（模拟已接管），阶段标记留位。</summary>
        public void EnterGame() => SetPhase(LogicPhase.Game);

        /// <summary>结束对局：收回会话，回到菜单。</summary>
        public void EndSession()
        {
            Session.End();
            EnterMenu();
        }

        /// <summary>申请退出（由退出确认流程最终置 Exit）。</summary>
        public void RequestExit() => SetPhase(LogicPhase.Exit);

        private void SetPhase(LogicPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            BootLog.Log($"[逻辑] 阶段 → {phase}");
            PhaseChanged?.Invoke(phase);
        }

        private void Update()
        {
            // 子管理器自身的每帧驱动由其 MonoBehaviour Update 收口；
            // 世界模拟不在本类跑，归对局会话与世界层。
            if (!enabled) return;
        }
    }
}
