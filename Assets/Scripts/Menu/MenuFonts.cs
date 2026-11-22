// MenuFonts.cs —— 主菜单统一取中文字体（缓存一次）。
using UnityEngine;

namespace Ra2md.Menu
{
    public static class MenuFonts
    {
        private static Font _cached;

        public static Font Chinese()
        {
            if (_cached != null) return _cached;

            var candidates = new[]
            {
                "Microsoft JhengHei", "Microsoft YaHei", "MingLiU", "PMingLiU", "SimHei", "Arial"
            };

            foreach (var name in candidates)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(name, 16);
                    if (font != null)
                    {
                        _cached = font;
                        return _cached;
                    }
                }
                catch
                {
                    // 字体缺失，继续试下一个
                }
            }

            _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _cached;
        }
    }
}
