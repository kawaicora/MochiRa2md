// MenuSoundDirector.cs —— 主菜单音效（点击 / 扫入 / 扫出），全部按 INI 配置取名字。
//
// 链路（与游戏一致，不写死任何音效名）：
//   rules(md).ini  GUIMainButtonSound=MenuClick
//                  GUIMoveInSound=MenuSlideIn
//                  GUIMoveOutSound=MenuSlideOut
//        ↓ 段名
//   sound(md).ini  [MenuClick] Sounds=umenucl1  Volume=60
//                 [MenuSlideIn] Sounds=uslide1  Volume=65
//                 [MenuSlideOut] Sounds=uslide2 Volume=60
//        ↓ 基名 + 扩展名
//   VFS 查找：先 StreamingAssets 松散文件，没有再进 MIX 按 CRC 查（audio.mix / audiomd.mix）
//        ↓ RIFF/WAVE 字节
//   WavClipLoader.Parse → AudioClip → AudioSource.PlayOneShot
//
// 本机数据现状（2026-11-20 实测）：audio.mix 459 条 WAV、audiomd.mix 738 条 WAV，
// 全都没有文件名；按 umenucl1/uslide1/uslide2/utab/umenscol × {.wav,.aud} 算 CRC
// （标准 Westwood、无末取反、种子 0、JAMCRC 四种变体）在 937 个条目 id 里 0 命中，
// 说明这套中文版数据里这三个菜单音效确实被拿掉了（或改名）。
// 因此这里额外支持两条显式通道，便于把音效补回来而不改代码：
//   1) 松散文件 Audio/menu_click.wav、Audio/menu_slide_in.wav、Audio/menu_slide_out.wav；
//   2) 松散文件 menu_sounds.ini：段名即逻辑名，键 File= 直接给文件名或 0xXXXXXXXX 形式的条目 ID。
// 都找不到就只打一次告警，不假装有声音。
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Ra2md.Assets;

namespace Ra2md.Menu
{
    public sealed class MenuSoundDirector : MonoBehaviour
    {
        /// <summary>逻辑音效名（由 rules(md).ini 的三个键给出，缺省就是你看到的这三个）。</summary>
        public const string ClickLogical = "MenuClick";
        public const string SlideInLogical = "MenuSlideIn";
        public const string SlideOutLogical = "MenuSlideOut";

        private static readonly Dictionary<string, string> DefaultSoundSections = new Dictionary<string, string>
        {
            { ClickLogical, "MenuClick" },
            { SlideInLogical, "MenuSlideIn" },
            { SlideOutLogical, "MenuSlideOut" },
        };

        // 采样名只来自 sound(md).ini 的 Sounds=/Sound=，绝不写死、绝不猜。

        private sealed class SoundEntry
        {
            public string Logical;
            public string SampleName;
            public string FileName;
            public float Volume;
            public AudioClip Clip;
            public bool Reported;
        }

        private readonly Dictionary<string, SoundEntry> _entries = new Dictionary<string, SoundEntry>();
        private bool _built;

        public static MenuSoundDirector Create(Transform parent)
        {
            var go = new GameObject("MenuSounds");
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<MenuSoundDirector>();
            director.Build();
            return director;
        }

        // ------------------------------------------------------------ 对外

        public void PlayClick() { Play(ClickLogical); }
        public void PlaySlideIn() { Play(SlideInLogical); }
        public void PlaySlideOut() { Play(SlideOutLogical); }

        public void Play(string logical)
        {
            if (!_built) Build();
            var entry = Entry(logical);
            if (entry == null || entry.Clip == null)
            {
                Report(entry);
                return;
            }

            float volume = Mathf.Clamp01(entry.Volume * MenuAudioSettings.Sound);
            // 统一交给 AudioManager 的界面/音效通道，避免每个模块各养一个 AudioSource。
            AudioManager.Instance.PlayUi(entry.Clip, volume);
        }

        // ------------------------------------------------------------ 装配

        private void Build()
        {
            _built = true;

            var vfs = MenuResourceLibrary.EnsureMounted();

            // 1 逻辑名 → sound ini 段名（rules(md).ini 的 GUI*Sound 键）
            var sectionFor = new Dictionary<string, string>();
            foreach (var pair in DefaultSoundSections) sectionFor[pair.Key] = pair.Value;
            ReadRulesSounds(vfs, sectionFor);

            // 2 段名 → 采样基名 + 音量（sound(md).ini）
            var sampleFor = new Dictionary<string, string>();
            var volumeFor = new Dictionary<string, float>();
            ReadSoundIni(vfs, sectionFor, sampleFor, volumeFor);

            // 3 menu_sounds.ini 手工覆盖（可选）
            var overrides = ReadOverrides(vfs);

            foreach (var logical in new[] { ClickLogical, SlideInLogical, SlideOutLogical })
            {
                var entry = new SoundEntry
                {
                    Logical = logical,
                    SampleName = sampleFor.ContainsKey(logical) ? sampleFor[logical] : null,
                    Volume = volumeFor.ContainsKey(logical) ? volumeFor[logical] : 1f,
                };

                string overrideName = overrides.ContainsKey(logical) ? overrides[logical] : null;
                entry.Clip = LoadClip(vfs, entry, overrideName);
                _entries[logical] = entry;
            }
        }

        private static void ReadRulesSounds(VirtualFileSystem vfs, Dictionary<string, string> into)
        {
            foreach (var ini in Ra2md.Managers.GlobalConfigManager.Resources.RulesIniNames)
            {
                if (!vfs.TryResolve(ini, out var bytes, out _) || bytes == null || bytes.Length == 0) continue;
                string text = DecodeIni(bytes);
                if (TryGetValue(text, "GUIMainButtonSound", out var click) && click.Length > 0) into[ClickLogical] = click;
                if (TryGetValue(text, "GUIMoveInSound", out var inName) && inName.Length > 0) into[SlideInLogical] = inName;
                if (TryGetValue(text, "GUIMoveOutSound", out var outName) && outName.Length > 0) into[SlideOutLogical] = outName;
                return;
            }
        }

        private static void ReadSoundIni(VirtualFileSystem vfs, Dictionary<string, string> sectionFor,
            Dictionary<string, string> sampleFor, Dictionary<string, float> volumeFor)
        {
            foreach (var ini in Ra2md.Managers.GlobalConfigManager.Resources.SoundIniNames)
            {
                if (!vfs.TryResolve(ini, out var bytes, out _) || bytes == null || bytes.Length == 0) continue;
                string text = DecodeIni(bytes);

                foreach (var logical in sectionFor.Keys)
                {
                    string section = sectionFor[logical];
                    if (!TryGetSection(text, section, out var body)) continue;

                    if (TryGetValueInBody(body, "Sounds", out var sounds) && sounds.Length > 0)
                        sampleFor[logical] = FirstToken(sounds);
                    else if (TryGetValueInBody(body, "Sound", out var sound) && sound.Length > 0)
                        sampleFor[logical] = FirstToken(sound);

                    if (TryGetValueInBody(body, "Volume", out var vol) && float.TryParse(vol,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v))
                        volumeFor[logical] = Mathf.Clamp01(v / 100f);
                }
                return;
            }
        }

        private static Dictionary<string, string> ReadOverrides(VirtualFileSystem vfs)
        {
            var result = new Dictionary<string, string>();
            if (!vfs.TryResolve(Ra2md.Managers.GlobalConfigManager.Resources.SoundOverrideIni, out var bytes, out _) || bytes == null) return result;
            string text = DecodeIni(bytes);
            string[] logics = { ClickLogical, SlideInLogical, SlideOutLogical };
            foreach (var logical in logics)
            {
                if (!TryGetSection(text, logical, out var body)) continue;
                if (TryGetValueInBody(body, "File", out var file) && file.Length > 0)
                    result[logical] = file;
            }
            return result;
        }

        private AudioClip LoadClip(VirtualFileSystem vfs, SoundEntry entry, string overrideName)
        {
            // a) menu_sounds.ini 指定：文件名或 0xXXXXXXXX
            if (!string.IsNullOrEmpty(overrideName))
            {
                if (overrideName.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    uint.TryParse(overrideName.Substring(2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out var id))
                {
                    if (vfs.TryResolveById(id, out var idBytes, out var idOrigin) && idBytes != null)
                    {
                        Debug.Log($"[菜单] 音效 {entry.Logical} ← 0x{id:X8}（{idBytes.Length}B, {idOrigin}）");
                        return WavClipLoader.Parse(idBytes, entry.Logical);
                    }
                }
                else if (vfs.TryResolve(overrideName, out var ovBytes, out var ovOrigin) && ovBytes != null)
                {
                    Debug.Log($"[菜单] 音效 {entry.Logical} ← {overrideName}（{ovBytes.Length}B, {ovOrigin}）");
                    return WavClipLoader.Parse(ovBytes, entry.Logical);
                }
            }

            // b) sound(md).ini 里写的采样名：先查 audio.idx 音效库（这才是原版存放处）
            if (string.IsNullOrEmpty(entry.SampleName)) return null;
            entry.FileName = entry.SampleName;

            var bagClip = AudioBag.GetClip(entry.SampleName);
            if (bagClip != null)
            {
                Debug.Log("[菜单] 音效 " + entry.Logical + " ← audio.idx:" + entry.SampleName +
                          "（" + AudioBag.Origin + "）");
                return bagClip;
            }

            // c) 兜底：当真当成文件名去 VFS 找（MOD 覆盖或松散 wav）
            entry.FileName = entry.SampleName.IndexOf('.') >= 0
                ? entry.SampleName
                : entry.SampleName + Ra2md.Managers.GlobalConfigManager.Resources.WavExtension;
            if (vfs.TryResolve(entry.FileName, out var bytes, out var origin) && bytes != null)
            {
                Debug.Log($"[菜单] 音效 {entry.Logical} ← {entry.FileName}（{bytes.Length}B, {origin}）");
                return WavClipLoader.Parse(bytes, entry.Logical);
            }
            return null;
        }

        private void Report(SoundEntry entry)
        {
            if (entry == null || entry.Reported) return;
            entry.Reported = true;
            if (string.IsNullOrEmpty(entry.SampleName))
            {
                Debug.LogWarning("[菜单] 音效缺失：" + entry.Logical + " 在 sound(md).ini 对应段里没有 Sounds=，" +
                                 "无法知道该播哪个文件；用 menu_sounds.ini 的 File= 指定条目 ID 或文件名即可。");
                return;
            }
            entry.FileName = entry.SampleName;
            Debug.LogWarning("[菜单] 音效缺失：" + entry.Logical + " 需要采样 " + entry.SampleName +
                             "，audio.idx 里没有（" + AudioBag.Origin + "）；" +
                             "把同名松散 wav 放进 StreamingAssets/ 或在 menu_sounds.ini 里用 File= 指定。");
        }

        private SoundEntry Entry(string logical)
        {
            return _entries.TryGetValue(logical, out var entry) ? entry : _entries.TryGetValue(ClickLogical, out entry) ? entry : null;
        }

        // ------------------------------------------------------------ INI 小工具

        private static string DecodeIni(byte[] bytes)
        {
            int start = 0;
            if (bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3;
            string text = Encoding.ASCII.GetString(bytes, start, bytes.Length - start);
            int zero = text.IndexOf('\0');
            if (zero >= 0) text = text.Substring(0, zero);
            return text;
        }

        private static string FirstToken(string value)
        {
            var parts = value.Trim().Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : string.Empty;
        }

        private static bool TryGetSection(string text, string section, out string body)
        {
            body = null;
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            bool inSection = false;
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';') continue;

                if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                {
                    string name = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    if (inSection) break;
                    if (name.Equals(section, StringComparison.OrdinalIgnoreCase)) inSection = true;
                    continue;
                }
                if (inSection) sb.Append(line).Append('\n');
            }
            if (!inSection) return false;
            body = sb.ToString();
            return true;
        }

        private static bool TryGetValueInBody(string body, string key, out string value)
        {
            value = null;
            var lines = body.Split('\n');
            foreach (var line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                string raw = line.Substring(eq + 1);
                int semi = raw.IndexOf(';');
                if (semi >= 0) raw = raw.Substring(0, semi);
                value = raw.Trim();
                return true;
            }
            return false;
        }

        private static bool TryGetValue(string text, string key, out string value)
        {
            value = null;
            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '[') continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                if (!trimmed.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                string raw = trimmed.Substring(eq + 1);
                int semi = raw.IndexOf(';');
                if (semi >= 0) raw = raw.Substring(0, semi);
                value = raw.Trim();
                return true;
            }
            return false;
        }
    }
}
