// ScenarioContext.cs —— 一局场景的运行时聚合：文档 + 小队注册表 + 触发器引擎 + 上下文接缝。
//
// 对照原版 sub_5BC180（游戏初始化/规则装载）与会话开场流程：
//   装载场景 INI → 建 TeamType 实例表 → 建 Trigger 实例表 → 挂接世界事件。
// 对象实例化/地形渲染/寻路尚未落地，本类只持有数据与运行时骨架，每帧 ProcessFrame 驱动引擎。
using Ra2md.Core;

namespace Ra2md.World
{
    public sealed class ScenarioContext
    {
        public ScenarioDocument Document { get; private set; }
        public TeamRegistry Teams { get; private set; }
        public TriggerEngine Triggers { get; private set; }
        public ITriggerContext WorldContext { get; private set; }
        public string MapName { get; private set; }

        private bool _open;

        /// <summary>当前会话场景（未装载为 null）。</summary>
        public static ScenarioContext Current { get; private set; }

        /// <summary>装载场景并构建运行时；重复调用先关闭旧场景。</summary>
        public static ScenarioContext Open(string mapName, ITriggerContext worldContext = null)
        {
            var doc = ScenarioLoader.LoadByName(mapName);
            if (doc == null)
            {
                BootLog.Log("[map] 场景装载失败: " + mapName);
                return null;
            }

            return OpenDocument(doc, mapName, worldContext);
        }

        /// <summary>
        /// 用已装载好的文档开场景（遭遇战归档图走 id:XXXXXXXX@归档 键，文档由 ScenarioLoader 先取出）。
        /// </summary>
        public static ScenarioContext OpenDocument(ScenarioDocument doc, string mapName,
            ITriggerContext worldContext = null)
        {
            if (doc == null) return null;

            Current?.Close();

            var ctx = worldContext ?? new DefaultTriggerContext(doc);
            var context = new ScenarioContext
            {
                Document = doc,
                MapName = mapName,
                WorldContext = ctx,
            };
            context.Teams = new TeamRegistry(doc);
            context.Triggers = new TriggerEngine(doc, ctx);
            context._open = true;

            Current = context;
            BootLog.Log($"[map] 场景上下文就绪: {mapName}");
            return context;
        }

        public void Close()
        {
            if (!_open) return;
            BootLog.Log($"[map] 关闭场景: {MapName}");
            _open = false;
            Document = null;
            Teams = null;
            Triggers = null;
            WorldContext = null;
            MapName = null;
            if (Current == this) Current = null;
        }

        /// <summary>每帧推进（在 GameSession.StepFrame 战斗态里调用）。</summary>
        public void ProcessFrame()
        {
            if (!_open) return;
            Triggers.ProcessFrame();
        }

        /// <summary>默认上下文：所有世界对接点先落日志，待对应系统落地后回填。</summary>
        private sealed class DefaultTriggerContext : ITriggerContext
        {
            private readonly ScenarioDocument _doc;

            public DefaultTriggerContext(ScenarioDocument doc) => _doc = doc;

            public string PlayerHouse => _doc.Houses.Count > 0 ? _doc.Houses[0] : null;

            public MapWaypoint FindWaypoint(int index)
            {
                if (index < 0 || index >= _doc.Waypoints.Count) return null;
                return _doc.Waypoints[index];
            }

            public void ShowMessage(string text) => BootLog.Log("[trigger] 文本提示: " + text);

            public void PlayAudio(string name) => BootLog.Log("[trigger] 播放音频: " + name + " [未实现音频对接]");

            public TeamRuntime CreateTeam(string teamTypeId, string house)
                => Current?.Teams?.CreateTeam(teamTypeId, house);

            public TeamRuntime SpawnTeam(string teamTypeId, int waypointIndex, string house)
                => Current?.Teams?.SpawnTeam(teamTypeId, waypointIndex, house);

            public void EndScenario(bool playerWon)
                => BootLog.Log($"[trigger] 结束对局 胜利={playerWon} [未实现结算流程]");
        }
    }
}
