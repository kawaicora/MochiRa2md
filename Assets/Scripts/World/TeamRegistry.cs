// TeamRegistry.cs —— 小队类型库 + 运行中小队实例表，对照 YRpp TeamTypeClass 数组与 TeamClass 数组。
//
//   TeamTypeClass::CreateTeam(house)  → 本类 CreateTeam
//   TeamTypeClass::DestroyAllInstances → 本类 DestroyAllInstances
//   Action CreateTeam(4) / Reinforcement(7) / ReinforcementAt(0x50) → 这里建队 / 增援。
using System.Collections.Generic;
using Ra2md.Core;

namespace Ra2md.World
{
    public sealed class TeamRegistry
    {
        private readonly ScenarioDocument _doc;
        private readonly Dictionary<string, TeamTypeEntry> _types;
        private readonly List<TeamRuntime> _instances = new List<TeamRuntime>();

        public IReadOnlyList<TeamRuntime> Instances => _instances;

        public TeamRegistry(ScenarioDocument doc)
        {
            _doc = doc;
            _types = doc != null ? doc.TeamTypes : new Dictionary<string, TeamTypeEntry>();
        }

        public TeamTypeEntry FindType(string id)
        {
            return _types.TryGetValue(id, out var t) ? t : null;
        }

        /// <summary>对照 TeamTypeClass::CreateTeam：按任务力量编成建一支空队，成员由后续 AddMember / 生成流程补齐。</summary>
        public TeamRuntime CreateTeam(string teamTypeId, string house)
        {
            var type = FindType(teamTypeId);
            if (type == null)
            {
                BootLog.Log($"[team] CreateTeam: 队伍类型 {teamTypeId} 不存在");
                return null;
            }

            var team = new TeamRuntime(type, house ?? type.House, GameStatics.CurrentFrame);
            ApplyTaskForceComposition(team);
            _instances.Add(team);
            BootLog.Log($"[team] 建队 {type.Name} ({teamTypeId}) house={team.OwnerHouse} 需求人数={team.TotalObjects}");
            return team;
        }

        /// <summary>对照 Action Reinforcement / SpawnTeamAtWP：在指定航路点建队并标记生成位置。</summary>
        public TeamRuntime SpawnTeam(string teamTypeId, int waypointIndex, string house)
        {
            var team = CreateTeam(teamTypeId, house);
            if (team == null) return null;
            team.SpawnWaypointIndex = waypointIndex;

            // [未实现] 单位/步兵实体实例化：对象类型系统（UnitClass/InfantryClass）落地后，
            // 在这里按 TaskForce 清单在 SpawnCell 上生成成员并 AddMember。
            BootLog.Log($"[team] 增援队伍 {teamTypeId} 航路点={waypointIndex} 实体生成待对象系统接入");
            return team;
        }

        /// <summary>对照 TeamTypeClass::DestroyAllInstances：销毁该类型的全部运行实例。</summary>
        public void DestroyAllInstances(string teamTypeId)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_instances[i].Type?.Id, teamTypeId, System.StringComparison.OrdinalIgnoreCase))
                {
                    _instances.RemoveAt(i);
                }
            }
        }

        /// <summary>对照 TeamTypeClass::ProcessTaskForce：按任务力量清单把需求人数填进 CountObjects。</summary>
        private void ApplyTaskForceComposition(TeamRuntime team)
        {
            string tfBase = ScenarioParser.StripRefSuffix(team.Type.TaskForceId);
            var tf = _doc.FindTaskForce(tfBase);
            if (tf == null) return;

            int total = 0;
            for (int i = 0; i < tf.Items.Count && i < team.CountObjects.Length; i++)
            {
                team.CountObjects[i] = tf.Items[i].Count;
                total += tf.Items[i].Count;
                team.MemberTypes.Add(tf.Items[i].Type);
            }
            team.TotalObjects = total;
            team.RefreshStrength();
        }
    }
}
