// GameTime.cs —— 替代原版 timeGetTime()（毫秒，32 位回绕语义）
// 原版计时点：55D360 帧起点 _DAT_00a8b55c、55E160 的 DVar3 - DAT_00887328、
// a8b560/a8b564 的每秒累计（elapsed 上限 1000ms）。
using System.Diagnostics;

namespace Ra2md.Core
{
    public static class GameTime
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        /// <summary>等价 timeGetTime()。返回 uint（自动 32 位回绕，和 DWORD 减法一致）。</summary>
        public static uint Now() => unchecked((uint)Clock.ElapsedMilliseconds);

        /// <summary>等价 (int)(timeGetTime() - start)：有符号差值，原版用来和帧预算比较。</summary>
        public static int Since(uint start) => unchecked((int)(Now() - start));

        /// <summary>等价 Sleep(0)：让出时间片。</summary>
        public static void Yield() => System.Threading.Thread.Sleep(0);

        /// <summary>等价 Sleep(ms)。</summary>
        public static void Sleep(int ms)
        {
            if (ms > 0) System.Threading.Thread.Sleep(ms);
            else System.Threading.Thread.Sleep(0);
        }
    }
}
