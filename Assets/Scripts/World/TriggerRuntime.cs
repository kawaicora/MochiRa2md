// TriggerRuntime.cs —— 触发器实例，对照 YRpp TriggerClass。
//
// 字段映射：Type(MapTrigger) / House / Destroyed / Timer / OccuredEvents(32 位事件位表) / Enabled。
// 判定流程对照 TriggerClass::RegisterEvent：事件冒泡上来 → 标记对应事件位 →
// 全部事件满足后 FireActions；持久事件记住已发生状态。
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    public sealed class TriggerRuntime
    {
        public MapTrigger Type;
        public readonly List<MapEvent> Events = new List<MapEvent>();
        public readonly List<MapAction> Actions = new List<MapAction>();

        public bool Destroyed;
        public bool Enabled;
        /// <summary>32 位事件发生位表（OccuredEvents）。</summary>
        public uint OccuredEvents;
        /// <summary>计时器帧戳（ElapsedTime / RandomDelay 事件用，帧计时落地后驱动）。</summary>
        public uint ActivationFrame;

        public TriggerRuntime(MapTrigger trigger)
        {
            Type = trigger;
            Enabled = trigger != null && trigger.Enabled;
        }

        public void Enable()
        {
            Enabled = true;
            ResetTimers();
        }

        public void Disable() => Enabled = false;

        /// <summary>对照 TriggerClass::ResetTimers：重置 ElapsedTime / RandomDelay 事件计时。</summary>
        public void ResetTimers()
        {
            ActivationFrame = GameStatics.CurrentFrame;
        }

        public void MarkEventAsOccured(int idx)
        {
            if (idx >= 0 && idx < 32) OccuredEvents |= 1u << idx;
        }

        public void MarkEventAsNotOccured(int idx)
        {
            if (idx >= 0 && idx < 32) OccuredEvents &= ~(1u << idx);
        }

        public bool HasEventOccured(int idx)
        {
            return idx >= 0 && idx < 32 && (OccuredEvents & (1u << idx)) != 0u;
        }

        /// <summary>
        /// 对照 TriggerClass::RegisterEvent：记录命中的事件，返回是否该触发器全部事件已满足。
        /// 事件按 Kind 匹配；同一触发器内同 Kind 多个事件逐个标记。
        /// </summary>
        public bool RegisterEvent(TriggerEventKind kind, object source, bool forceFire, bool persistent, string sourceHouse)
        {
            if (!Enabled || Destroyed) return false;

            bool anyMatched = false;
            for (int i = 0; i < Events.Count; i++)
            {
                if (Events[i].Kind != kind) continue;
                anyMatched = true;
                MarkEventAsOccured(i);
            }

            if (!anyMatched && !forceFire) return false;
            return AllEventsOccured();
        }

        /// <summary>对照 TriggerClass::HaveEventsOccured：32 个事件位全部置位即满足。</summary>
        public bool AllEventsOccured()
        {
            if (Events.Count == 0) return false;
            for (int i = 0; i < Events.Count; i++)
            {
                if (!HasEventOccured(i)) return false;
            }
            return true;
        }
    }
}
