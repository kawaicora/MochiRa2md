// ITriggerContext.cs —— 触发器动作执行时对接游戏世界的接缝。
//
// 对象/房子/音频/结算系统尚未全部落地，这里只暴露动作需要的最小能力；
// 未实现的对接点在具体系统接入后回填，不在触发器层猜测行为。
namespace Ra2md.World
{
    public interface ITriggerContext
    {
        /// <summary>玩家所属房子名。</summary>
        string PlayerHouse { get; }

        MapWaypoint FindWaypoint(int index);

        void ShowMessage(string text);
        void PlayAudio(string name);

        TeamRuntime CreateTeam(string teamTypeId, string house);
        TeamRuntime SpawnTeam(string teamTypeId, int waypointIndex, string house);

        /// <summary>结束本局：true=玩家胜利，false=失败。</summary>
        void EndScenario(bool playerWon);
    }
}
