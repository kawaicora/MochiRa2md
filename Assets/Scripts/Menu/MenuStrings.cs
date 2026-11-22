// MenuStrings.cs —— 菜单文案出口：一切文字都走 CSF（原版语言文件），代码里只留 key 与英文兜底。
// 换语言 = 替换 StreamingAssets 里的本地化 ra2md.csf / ra2.csf，界面文字随之改变，无需改代码。
using System.Collections.Generic;
using UnityEngine;
using Ra2md.Assets;

namespace Ra2md.Menu
{
    public static class MenuStrings
    {
        private static readonly List<CsfFile> _tables = new List<CsfFile>();
        private static readonly List<string> _origins = new List<string>();
        private static bool _attempted;
        private static string _origin;

        /// <summary>主源 CSF（可能为 null：找不到或解析失败）；备用源只用于补主源缺失的条目。</summary>
        public static CsfFile Table { get { EnsureLoaded(); return _tables.Count > 0 ? _tables[0] : null; } }

        public static bool Loaded { get { EnsureLoaded(); return _tables.Count > 0; } }

        public static string Source { get { EnsureLoaded(); return _origin; } }

        /// <summary>按 key 取文案；CSF 缺失该条时用 fallback（与参考 csf.get(key, fallback) 同义）。</summary>
        public static string Get(string key, string fallback)
        {
            EnsureLoaded();
            if (_tables.Count == 0) return fallback;

            // 主源（根目录优先，由 VFS 决定）缺这条时才查备用源，逐档按优先级取第一个命中的。
            for (var i = 0; i < _tables.Count; i++)
            {
                if (_tables[i].TryGet(key, out var value) && !string.IsNullOrEmpty(value))
                {
                    if (i > 0 && !_missLogged.Contains(key))
                    {
                        _missLogged.Add(key);
                        Debug.Log("[菜单] " + key + " 主源缺条，取备用源 " + _origins[i]);
                    }
                    return value;
                }
            }
            return fallback;
        }

        private static readonly HashSet<string> _missLogged = new HashSet<string>();

        // 候选顺序：根目录的简体档优先，尤复繁体档只作补条兜底。
        // 反过来会把 langmd.mix 里那份繁体 ra2md.csf 顶到最前，整个菜单就变繁体。
        private static readonly string[] Candidates = { "ra2.csf", "ra2md.csf" };

        /// <summary>
        /// 每个候选名各解析一份 CSF（文件仍由 VFS 挑，解析层不合并、不改），按候选顺序排成查找链。
        /// 简化版 ra2.csf 没有尤复的战役影片名（Name:*md），主源缺条时才能回落到归档里的 ra2md.csf。
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_attempted) return;
            _attempted = true;

            for (var i = 0; i < Candidates.Length; i++)
            {
                var name = Candidates[i];
                if (!MenuResourceLibrary.TryLoadFirst(new[] { name }, out var bytes, out _origin) || bytes == null)
                    continue;

                if (!CsfFile.TryParse(bytes, out var csf, out var error))
                {
                    Debug.LogError("[菜单] CSF 解析失败（" + _origin + "）：" + error);
                    continue;
                }
                if (error != null)
                {
                    Debug.LogWarning("[菜单] CSF 部分解析（" + _origin + "）：" + error + "（已取 " + csf.Count + " 条）");
                }

                _tables.Add(csf);
                _origins.Add(_origin);
                Debug.Log("[菜单] CSF 装载 " + csf.Count + " 条（语言 " + csf.Language + "）来源 " + _origin
                    + (i == 0 ? " [主源]" : " [备用源]"));
            }

            if (_tables.Count == 0)
                Debug.LogWarning("[菜单] 未找到 CSF 语言文件，界面将只显示英文兜底文案");
        }
    }
}
