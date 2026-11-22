// PlayLimits.cs —— 对局容量上限（跨管理器共用，不在各代码里写死数字）。
//
// 口径：一个玩家 = 一个 HouseClass；席位上限按目标规模取 120。
// 原版位域/统计数组原本按 32/20 席收窄，这里统一放宽到 MaxHouses。
namespace Ra2md.Core
{
    public static class PlayLimits
    {
        /// <summary>最大 House（玩家/阵营/中立）席位数。</summary>
        public const int MaxHouses = 120;

        /// <summary>可由人类控制的玩家席位数（遭遇战可选槽位）。</summary>
        public const int MaxPlayers = 120;
    }
}
