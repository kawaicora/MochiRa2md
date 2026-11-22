// TriggerManager.cs —— 触发器管理器：战役/遭遇战触发器运行时的统一归口。
//
// 职责边界：
//   · 触发器定义解析、条件判定、事件派发仍在 TriggerEngine / TriggerRuntime；
//   · 本类只持有当前局引擎实例，向世界层/UI 暴露 Find / 触发 / 逐帧推进的入口；
//   · 换局时由世界层 Attach 新引擎，旧引用随局释放。
using Ra2md.World;

namespace Ra2md.Managers
{
    public sealed class TriggerManager
    {
        private static TriggerManager _instance;

        public static TriggerManager Instance
        {
            get
            {
                if (_instance == null) _instance = new TriggerManager();
                return _instance;
            }
        }

        private TriggerEngine _engine;

        /// <summary>绑定本局触发器引擎（开局载入后调用）。</summary>
        public void Attach(TriggerEngine engine) => _engine = engine;

        public TriggerRuntime Find(string triggerId) => _engine != null ? _engine.Find(triggerId) : null;

        public void ForceTrigger(string triggerId)
        {
            if (_engine != null) _engine.ForceTrigger(triggerId);
        }

        /// <summary>每帧推进触发器（由 GameManager 帧循环调用）。</summary>
        public void ProcessFrame()
        {
            if (_engine != null) _engine.ProcessFrame();
        }
    }
}
