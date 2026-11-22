// MovieSubtitles.cs —— RA2/YR 影片字幕（subtitle.txt / subtitlemd.txt）。
//
// 文件就是原版那一份，语法照原样解析（不要求玩家改任何东西）：
//   BeginMovie A00_F00E
//   <Time=00:00:04:30, Duration=00:00:02:00, Position=14:Center, Color=255:255:0, Text="...">
//   EndMovie
// Time/Duration 用 HH:MM:SS:FF，末段两位是百分秒（这个文件里 00:00:02:30 = 2.3s）。
// Duration=00:00:00:00 表示「一直显示到下一句」，原版就是这么排连续对白的。
// Position 的第一段是字号（以 640×480 为基准），第二段是水平对齐 Center/Left/Right。
//
// 显示位置：
//   * 全屏播放（主菜单、片头、先睹为快）→ 画面底部居中，走 FullscreenVideoPlayer 的字幕层；
//   * 小窗口播放（游戏里雷达/地图区那种）→ 不进字幕层，转成消息列表条目，
//     走 MessageList.AppendMessage（对应 MessageListClass::Instance.AppendMessage）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Ra2md.Assets;
using Ra2md.Core;

namespace Ra2md.Menu
{
    public sealed class SubtitleCue
    {
        public float Start;          // 秒
        public float Duration;       // 秒（0 = 一直到下一句）
        public int FontSize;         // 文件里的字号（640×480 基准）
        public TextAnchor Align = TextAnchor.LowerCenter;
        public Color32 Color = new Color32(255, 255, 255, 255);
        public string Text;

        public float End(int nextIndex, float nextStart, float movieDuration)
        {
            if (Duration > 0.001f) return Start + Duration;
            if (nextStart > Start) return nextStart;
            return movieDuration > Start ? movieDuration : Start + 2f;
        }
    }

    public sealed class SubtitleTrack
    {
        public string Movie;
        public readonly List<SubtitleCue> Cues = new List<SubtitleCue>();

        /// <summary>返回当前时间该显示的那一条（没有则 null）。多句重叠时取最靠后开始的那条。</summary>
        public SubtitleCue CueAt(float time, float movieDuration)
        {
            SubtitleCue active = null;
            for (int i = 0; i < Cues.Count; i++)
            {
                var cue = Cues[i];
                if (cue.Start > time + 0.001f) break;

                float nextStart = i + 1 < Cues.Count ? Cues[i + 1].Start : float.MaxValue;
                float end = cue.End(i, nextStart, movieDuration);
                if (time >= cue.Start && time < end) active = cue;
            }
            return active;
        }
    }

    public static class MovieSubtitles
    {
        // 尤复字幕优先，简中/原版字幕兜底；表归口全局配置管理器。
        private static string[] FileNames => Ra2md.Managers.GlobalConfigManager.Resources.SubtitleNames;
        private static string[] KnownPaths => Ra2md.Managers.GlobalConfigManager.Resources.SubtitlePaths;

        private static bool _loaded;
        private static readonly Dictionary<string, SubtitleTrack> _tracks =
            new Dictionary<string, SubtitleTrack>(StringComparer.OrdinalIgnoreCase);

        public static int MovieCount { get { EnsureLoaded(); return _tracks.Count; } }

        public static SubtitleTrack Track(string movieName)
        {
            EnsureLoaded();
            string key = Normalize(movieName);
            if (key.Length == 0) return null;
            if (_tracks.TryGetValue(key, out var track)) return track;

            // 有的片名带 .bik 或路径，再退一步只留基名试一次
            int dot = key.LastIndexOf('.');
            if (dot > 0)
            {
                string bare = key.Substring(0, dot);
                if (_tracks.TryGetValue(bare, out track)) return track;
            }
            return null;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var vfs = MenuResourceLibrary.EnsureMounted();
            if (vfs == null) return;

            var texts = new List<string>();
            foreach (var fileName in FileNames)
            {
                byte[] bytes = null;
                if (vfs.TryResolve(fileName, out var raw, out _) && raw != null) bytes = raw;
                else
                {
                    foreach (var path in KnownPaths)
                    {
                        if (path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) &&
                            vfs.OpenFile(path, out var fromArchive) && fromArchive != null)
                        {
                            bytes = fromArchive;
                            break;
                        }
                    }
                }
                if (bytes != null) texts.Add(Decode(bytes));
            }

            if (texts.Count == 0)
            {
                Debug.LogWarning("[字幕] 找不到 subtitle.txt / subtitlemd.txt");
                return;
            }

            int movies = 0;
            foreach (var text in texts) movies += Parse(text);
            Debug.Log("[字幕] 已装载 " + _tracks.Count + " 部影片的字幕（共 " + movies + " 段）");
        }

        /// <summary>原版字幕文件是 UTF-16LE 带 BOM（FF FE），也见过 UTF-8 BOM / 无 BOM，逐个认。</summary>
        private static string Decode(byte[] bytes)
        {
            string text;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            else
            {
                text = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }

            int zero = text.IndexOf('\0');
            if (zero >= 0) text = text.Substring(0, zero);
            return text;
        }

        private static int Parse(string text)
        {
            int count = 0;
            SubtitleTrack current = null;

            // 统一换行（CRLF / LF / 纯 CR 都认）
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            foreach (var rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim().TrimEnd('\r').Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';') continue;

                if (line.StartsWith("BeginMovie", StringComparison.OrdinalIgnoreCase))
                {
                    string name = Normalize(line.Substring("BeginMovie".Length));
                    current = new SubtitleTrack { Movie = name };
                    _tracks[name] = current;         // 后读到的文件覆盖先读的
                    continue;
                }

                if (line.StartsWith("EndMovie", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

                if (current == null) continue;
                if (!line.StartsWith("<Time", StringComparison.OrdinalIgnoreCase)) continue;

                var cue = ParseCue(line);
                if (cue == null || string.IsNullOrEmpty(cue.Text)) continue;
                current.Cues.Add(cue);
                count++;
            }

            foreach (var track in _tracks.Values) track.Cues.Sort((a, b) => a.Start.CompareTo(b.Start));
            return count;
        }

        /// <summary>一行 &lt;Time=..., Text="..."&gt; → SubtitleCue。容忍缺逗号、大小写差、多余空格。</summary>
        private static SubtitleCue ParseCue(string line)
        {
            var cue = new SubtitleCue();

            float time;
            float duration = 0f;
            string token;

            if (!TryTakeValue(line, "Time", out token) || !TryParseTime(token, out time)) return null;
            cue.Start = time;

            if (TryTakeValue(line, "Duration", out token)) TryParseTime(token, out duration);
            cue.Duration = duration;

            if (TryTakeValue(line, "Position", out token))
            {
                var parts = token.Split(':');
                if (parts.Length > 0) cue.FontSize = (int)Math.Round(ParseFloat(parts[0], 14f));
                if (parts.Length > 1) cue.Align = AlignFrom(parts[1]);
            }

            if (TryTakeValue(line, "Color", out token))
            {
                var parts = token.Split(':');
                byte r = parts.Length > 0 ? (byte)Mathf.Clamp((int)ParseFloat(parts[0], 255f), 0, 255) : (byte)255;
                byte g = parts.Length > 1 ? (byte)Mathf.Clamp((int)ParseFloat(parts[1], 255f), 0, 255) : (byte)255;
                byte b = parts.Length > 2 ? (byte)Mathf.Clamp((int)ParseFloat(parts[2], 0f), 0, 255) : (byte)255;
                cue.Color = new Color32(r, g, b, 255);
            }

            if (TryTakeValue(line, "Text", out token)) cue.Text = token.Trim();
            return cue;
        }

        private static TextAnchor AlignFrom(string text)
        {
            if (text == null) return TextAnchor.LowerCenter;
            string t = text.Trim().ToLowerInvariant();
            if (t.StartsWith("left")) return TextAnchor.LowerLeft;
            if (t.StartsWith("right")) return TextAnchor.LowerRight;
            return TextAnchor.LowerCenter;
        }

        /// <summary>取 key=value（value 可能被引号包住，也可能被下一个 key 顶掉，缺逗号也认）。</summary>
        private static bool TryTakeValue(string line, string key, out string value)
        {
            value = null;
            string lower = line.ToLowerInvariant();
            int search = 0;
            while (true)
            {
                int at = lower.IndexOf(key.ToLowerInvariant() + "=", search, StringComparison.Ordinal);
                if (at < 0) return false;

                // 必须是键名开头（前面不是字母）
                if (at > 0 && char.IsLetter(lower[at - 1]))
                {
                    search = at + key.Length + 1;
                    continue;
                }

                int start = at + key.Length + 1;
                while (start < line.Length && (line[start] == ' ' || line[start] == ',')) start++;
                if (start >= line.Length) return false;

                if (line[start] == '"')
                {
                    int endQuote = line.IndexOf('"', start + 1);
                    if (endQuote < 0) endQuote = line.Length;
                    value = line.Substring(start + 1, endQuote - start - 1);
                    return true;
                }

                int stop = start;
                while (stop < line.Length && line[stop] != ',' && line[stop] != '>') stop++;
                value = line.Substring(start, stop - start).Trim();
                return true;
            }
        }

        /// <summary>HH:MM:SS:FF（FF 为百分秒）→ 秒。</summary>
        private static bool TryParseTime(string text, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrEmpty(text)) return false;

            var parts = text.Trim().Trim('[', ']', ' ').Split(':');
            if (parts.Length < 3) return false;

            float h = ParseFloat(parts[0], 0f);
            float m = ParseFloat(parts[1], 0f);
            float s = ParseFloat(parts[2], 0f);
            float frac = parts.Length > 3 ? ParseFloat(parts[3], 0f) / 100f : 0f;
            seconds = h * 3600f + m * 60f + s + frac;
            return true;
        }

        private static float ParseFloat(string text, float fallback)
        {
            if (text == null) return fallback;
            return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static string Normalize(string movieName)
        {
            if (string.IsNullOrEmpty(movieName)) return string.Empty;
            string s = movieName.Trim().Trim('"');
            int sep = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
            if (sep >= 0) s = s.Substring(sep + 1);
            return s.Trim();
        }
    }
}
