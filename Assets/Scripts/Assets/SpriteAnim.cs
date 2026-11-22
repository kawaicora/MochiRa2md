// SpriteAnim.cs —— 通用精灵动画状态机（建筑部件 / 单位 / 飞机 / 覆盖物共用一套）。
//
// 口径对照参考美术段：
//   · Start= 起始帧；LoopStart= 循环回到的帧；LoopEnd= 结束帧；
//   · End= 是序列帧数（不是结束帧号），结束帧 = End-1；
//   · LoopCount=-1 无限循环；>=0 播完指定次数后停在末帧；
//   · Rate= 每帧毫秒。
// 判定为静态的条件（任一成立就不进动画，避免只有一两帧的部件来回闪）：
//   速率 <=0；结束帧 <= 循环起始帧；可用帧数不足 2。
using Ra2md.World;

namespace Ra2md.Assets
{
    /// <summary>一个对象视图上的通用动画状态；不绑定具体对象类型。</summary>
    public sealed class SpriteAnim
    {
        public int Start;
        public int LoopStart;
        public int LoopEnd;
        public int LoopCount = -1;
        public int RateMs;
        public bool PingPong;
        public bool Active;

        private int _frame;
        private float _timer;
        private int _doneLoops;
        private bool _finished;
        private int _dir = 1;

        public int CurrentFrame => _frame;
        public bool Finished => _finished;

        /// <summary>从美术记录装配；参数缺失或范围不足时返回静态动画（Active=false）。</summary>
        public static SpriteAnim FromArt(ArtRecord art)
        {
            var anim = new SpriteAnim();
            if (art == null) return anim;

            anim.Start = art.AnimStart;
            anim.LoopStart = art.AnimLoopStart;
            anim.LoopEnd = art.AnimLoopEnd;
            anim.LoopCount = art.AnimLoopCount;
            anim.RateMs = art.AnimRateMs;
            anim.PingPong = art.AnimPingPong;

            // End= 是帧数：结束帧 = End-1（参考 artmd 注释口径）。
            if (anim.LoopEnd <= anim.LoopStart && art.AnimEndCount > 0)
                anim.LoopEnd = art.AnimEndCount - 1;

            anim.Active = anim.RateMs > 0 && anim.LoopEnd > anim.LoopStart;
            anim._frame = anim.Start;
            return anim;
        }

        /// <summary>
        /// 推进一帧。返回当前应显示的帧号；静态或已播完返回 -1（调用方保持当前帧）。
        /// </summary>
        public int Step(float dt)
        {
            if (!Active) return -1;
            if (_finished) return _frame;
            if (RateMs <= 0) { Active = false; return -1; }

            _timer += dt * 1000f;
            while (_timer >= RateMs)
            {
                _timer -= RateMs;
                _frame += _dir;
                if (_frame > LoopEnd)
                {
                    if (PingPong)
                    {
                        _frame = LoopEnd - 1;
                        _dir = -1;
                    }
                    else if (LoopCount >= 0)
                    {
                        _doneLoops++;
                        if (_doneLoops >= LoopCount)
                        {
                            _finished = true;
                            _frame = LoopEnd;
                            return _frame;
                        }
                        _frame = LoopStart;
                    }
                    else
                    {
                        _frame = LoopStart;
                    }
                }
                else if (_frame < LoopStart)
                {
                    if (PingPong)
                    {
                        _frame = LoopStart + 1;
                        _dir = 1;
                    }
                    else
                    {
                        _frame = LoopEnd;
                    }
                }
            }
            return _frame;
        }
    }
}
