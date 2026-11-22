// TriggerEngine.cs —— 触发器实例表 + 事件冒泡 + 动作执行，对照 YRpp TriggerClass::Array。
//
// 装载：按 [Triggers] 建实例，[Events]/[Actions] 按触发器 ID 挂到对应实例。
// 流程：RegisterEvent 冒泡 → 实例判定全部事件满足 → FireActions；
// 非重复触发器动作执行完即销毁（对照 MustTransfer / 一次性触发器语义）。
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    public sealed class TriggerEngine
    {
        private readonly ScenarioDocument _doc;
        private readonly ITriggerContext _ctx;
        private readonly Dictionary<string, TriggerRuntime> _triggers =
            new Dictionary<string, TriggerRuntime>(System.StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, TriggerRuntime> Triggers => _triggers;

        public TriggerEngine(ScenarioDocument doc, ITriggerContext ctx)
        {
            _doc = doc;
            _ctx = ctx;
            Build();
        }

        private void Build()
        {
            if (_doc == null) return;

            foreach (var pair in _doc.Triggers)
            {
                _triggers[pair.Key] = new TriggerRuntime(pair.Value);
            }

            foreach (var evt in _doc.Events)
            {
                if (_triggers.TryGetValue(evt.TriggerId, out var trigger))
                {
                    trigger.Events.Add(evt);
                }
            }

            foreach (var action in _doc.Actions)
            {
                if (_triggers.TryGetValue(action.TriggerId, out var trigger))
                {
                    trigger.Actions.Add(action);
                }
            }

            BootLog.Log($"[trigger] 装载触发器 {_triggers.Count} 个，事件 {_doc.Events.Count} 条，动作 {_doc.Actions.Count} 条");
        }

        public TriggerRuntime Find(string triggerId)
        {
            return _triggers.TryGetValue(triggerId, out var t) ? t : null;
        }

        /// <summary>事件冒泡：对照 TriggerClass::RegisterEvent，推给全部相关触发器。</summary>
        public void RegisterEvent(TriggerEventKind kind, object source, string sourceHouse,
            bool forceFire = false, bool persistent = false)
        {
            foreach (var trigger in _triggers.Values)
            {
                if (trigger.Destroyed) continue;
                if (trigger.RegisterEvent(kind, source, forceFire, persistent, sourceHouse))
                {
                    FireActions(trigger, source);
                }
            }
        }

        /// <summary>对照 ActionClass::ForceTrigger：忽略事件条件，直接执行动作。</summary>
        public void ForceTrigger(string triggerId)
        {
            var trigger = Find(triggerId);
            if (trigger == null)
            {
                BootLog.Log($"[trigger] ForceTrigger: {triggerId} 不存在");
                return;
            }
            FireActions(trigger, null);
        }

        private void FireActions(TriggerRuntime trigger, object source)
        {
            if (trigger.Destroyed) return;

            bool any = false;
            foreach (var action in trigger.Actions)
            {
                if (ActionExecutor.Execute(action, trigger, _ctx)) any = true;
            }

            // 非重复触发器：动作执行完销毁（对照一次性触发语义）；重复触发器保持启用并清事件位。
            if (trigger.Type.Repeating)
            {
                trigger.OccuredEvents = 0;
                trigger.ResetTimers();
            }
            else
            {
                trigger.Destroyed = true;
            }

            if (any)
            {
                BootLog.Log($"[trigger] 触发 {trigger.Type.Name} ({trigger.Type.Id}) 动作 {trigger.Actions.Count} 组");
            }
        }

        /// <summary>每帧推进：计时器类事件（ElapsedTime/RandomDelay）在这里评估，帧计时系统接入后补全。</summary>
        public void ProcessFrame()
        {
            // [未实现] 计时事件轮询：需要 CDTimerClass 帧计数与事件 Value(帧/秒) 比对。
        }
    }
}
