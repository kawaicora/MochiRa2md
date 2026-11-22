// ActionExecutor.cs —— 触发器动作分派，对照 TActionClass::Execute 与各 ACTION_FUNC。
//
// 已接通（走 ITriggerContext 接缝）：
//   CreateTeam(4) / Reinforcement(7) / ReinforcementAt(0x50) /
//   Win(1) / Lose(2) / AnnounceWin(0x43) / AnnounceLose(0x44) /
//   TextTrigger(0xB) / 声音语音音乐类(0x13/0x14/0x15/0x62/0x63)。
// 其余动作在对象/武器/视野/超武系统落地前，按 [未实现] 记录，不猜行为。
using Ra2md.Core;

namespace Ra2md.World
{
    public static class ActionExecutor
    {
        public static bool Execute(MapAction action, TriggerRuntime trigger, ITriggerContext ctx)
        {
            if (action == null || !action.Enabled) return false;
            if (ctx == null)
            {
                BootLog.Log("[trigger] 动作执行缺少上下文，跳过 " + action.Kind);
                return false;
            }

            string house = trigger != null ? trigger.Type.House : null;

            switch (action.Kind)
            {
                case TriggerActionKind.CreateTeam:
                    ctx.CreateTeam(action.RefId, house);
                    return true;

                case TriggerActionKind.Reinforcement:
                case TriggerActionKind.ReinforcementAt:
                    ctx.SpawnTeam(action.RefId, action.Waypoint, house);
                    return true;

                case TriggerActionKind.Win:
                case TriggerActionKind.AnnounceWin:
                    ctx.EndScenario(true);
                    return true;

                case TriggerActionKind.Lose:
                case TriggerActionKind.AnnounceLose:
                    ctx.EndScenario(false);
                    return true;

                case TriggerActionKind.TextTrigger:
                    ctx.ShowMessage(!string.IsNullOrEmpty(action.Text) ? action.Text : action.RefId);
                    return true;

                case TriggerActionKind.PlaySoundEffect:
                case TriggerActionKind.PlaySoundEffectAtWaypoint:
                case TriggerActionKind.PlaySoundEffectRandom:
                case TriggerActionKind.PlaySpeech:
                case TriggerActionKind.PlayMusicTheme:
                    ctx.PlayAudio(!string.IsNullOrEmpty(action.RefId) ? action.RefId : action.Text);
                    return true;

                default:
                    BootLog.Log($"[trigger] 动作未实现: {action.Kind} ({(int)action.Kind}) ref={action.RefId}");
                    return false;
            }
        }
    }
}
