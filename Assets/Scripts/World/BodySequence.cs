// BodySequence.cs —— SHP 身体帧布局（待机 / 行走 / 开火条带 × 朝向槽位）。
//
// 口径对照参考 shp_vehicle_sequence / animation_sequence：
//   · 条带顺序 Walk → Stand → Attack，每条带按 Facings 个朝向块连续排布；
//   · 帧号 = 条带起始帧 + 朝向槽位 × 条带帧数 + 条带内循环帧；
//   · 车辆朝向槽位：方向八等分后 +1（帧块 0 = NW，不是 N）；
//   · 无开火且无炮塔时 Facings 回落为 1（不分朝向）；
//   · 待机条带为 0 时回落到行走条带的首帧。
using Ra2md.World;

namespace Ra2md.World
{
    /// <summary>身体当前动作（只取 SHP 身体条带；开火结束回待机）。</summary>
    public enum BodyAction
    {
        Stand = 0,
        Walk = 1,
        Attack = 2,
    }

    /// <summary>一个类型的 SHP 身体帧布局，不可变；每实例只存当前动作 / 循环帧 / 朝向。</summary>
    public sealed class BodySequence
    {
        public int Facings;
        public int WalkFrames;
        public int FireFrames;
        public int StandFrames;
        public int StartWalk;
        public int StartStand;
        public int StartFire;

        public bool IsDirectional => Facings > 1;

        public static BodySequence FromArt(ArtRecord art, bool hasTurret)
        {
            var seq = new BodySequence();
            if (art == null) return seq;

            seq.WalkFrames = art.WalkFrames > 0 ? art.WalkFrames : 12;
            seq.FireFrames = art.FiringFrames;
            seq.StandFrames = art.StandingFrames;
            seq.Facings = art.Facings;
            // 参考构造口径：不能开火且没有炮塔的身体只有一个朝向块。
            if (seq.FireFrames == 0 && !hasTurret) seq.Facings = 1;
            if (seq.Facings < 1) seq.Facings = 1;

            seq.StartWalk = art.StartWalkFrame >= 0 ? art.StartWalkFrame : 0;
            seq.StartStand = art.StartStandFrame >= 0
                ? art.StartStandFrame
                : (seq.StandFrames > 0 ? seq.WalkFrames * seq.Facings : seq.StartWalk);
            seq.StartFire = art.StartFiringFrame >= 0
                ? art.StartFiringFrame
                : (seq.FireFrames > 0 ? (seq.WalkFrames + seq.StandFrames) * seq.Facings : seq.StartStand);
            return seq;
        }

        /// <summary>车辆朝向槽位：方向八等分后 +1，帧块 0 = NW。</summary>
        public int FacingSlot(int facingByte)
        {
            if (!IsDirectional) return 0;
            var dir = ((facingByte + 16) / 32) & 7;
            return (dir + 1) & 7;
        }

        /// <summary>解析当前动作 / 朝向下应显示的 SHP 帧号。</summary>
        public int ResolveFrame(BodyAction action, int facingByte, int cycle)
        {
            var slot = FacingSlot(facingByte);
            if (action == BodyAction.Walk)
                return StartWalk + slot * WalkFrames + (WalkFrames <= 0 ? 0 : cycle % WalkFrames);

            if (action == BodyAction.Attack && FireFrames > 0)
                return StartFire + slot * FireFrames + (FireFrames <= 0 ? 0 : cycle % FireFrames);

            // 待机（或无开火条带时回落到待机）：待机条带为 0 时取行走条带首帧。
            if (StandFrames > 0)
                return StartStand + slot * StandFrames + (StandFrames <= 0 ? 0 : cycle % StandFrames);
            return StartWalk + slot * WalkFrames;
        }
    }
}
