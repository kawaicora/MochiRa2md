// TeamRuntime.cs —— 运行中的小队实例，对照 YRpp TeamClass。
//
// TeamClass 字段映射：
//   Type / Owner / Target / SpawnCell / ClosestMember / QueuedFocus / Focus /
//   TotalObjects / TotalThreatValue / CreationFrame / FirstUnit /
//   IsTransient / NeedsReGrouping / IsFullStrength / IsUnderStrength / IsReforming /
//   CountObjects[6]。
// 成员实体（FootClass/TechnoClass）对象系统尚未落地，这里先用类型名占位登记，
// 实例化接缝在 TeamRegistry.SpawnTeam 里以 [未实现] 日志标出。
using System.Collections.Generic;

namespace Ra2md.World
{
    public sealed class TeamRuntime
    {
        public TeamTypeEntry Type;
        public string OwnerHouse;
        public string TargetHouse;
        public int SpawnWaypointIndex = -1;
        public int TotalObjects;
        public int TotalThreatValue;
        public uint CreationFrame;
        public bool IsTransient;
        public bool NeedsReGrouping;
        public bool IsFullStrength;
        public bool IsUnderStrength;
        public bool IsReforming;
        public bool IsLeavingMap;
        public bool IsSuspended;
        public bool StepCompleted;

        /// <summary>对照 CountObjects[6]：按类型槽位计数（槽位语义待对象类型系统落地后补齐）。</summary>
        public readonly int[] CountObjects = new int[6];

        /// <summary>成员类型名占位清单（TechnoClass 落地后替换为实体引用）。</summary>
        public readonly List<string> MemberTypes = new List<string>();

        public TeamRuntime(TeamTypeEntry type, string ownerHouse, uint creationFrame)
        {
            Type = type;
            OwnerHouse = ownerHouse;
            CreationFrame = creationFrame;
        }

        /// <summary>对照 TeamClass::AddMember：登记一名成员并刷新总人数 / 强弱状态。</summary>
        public void AddMember(string technoType)
        {
            MemberTypes.Add(technoType);
            TotalObjects = MemberTypes.Count;
            RefreshStrength();
        }

        /// <summary>对照 TeamClass::LiberateMember：移除一名成员。</summary>
        public void RemoveMember(string technoType)
        {
            MemberTypes.Remove(technoType);
            TotalObjects = MemberTypes.Count;
            RefreshStrength();
        }

        public void RefreshStrength()
        {
            // 满编判定对照 Type->Max / 任务力量需求；当前对象属性未落地，先按非空即满编。
            IsFullStrength = TotalObjects > 0;
            IsUnderStrength = TotalObjects == 0;
        }
    }
}
