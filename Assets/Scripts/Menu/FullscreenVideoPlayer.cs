// FullscreenVideoPlayer.cs —— 通用全屏 Bink 播放接口。
//
// 用途：片头、任务开场影片、先睹为快（LOGO + 正片）、制作人员名单，都走这一个入口。
// 特性：
//   * 队列播放：可以一次塞进多段影片，播完自动接下一段（先睹为快 = LOGO + 正片）。
//   * 时间查询：CurrentTime / Duration / FrameNumber / Fps 全部对外暴露，
//     并每帧回调 TimeChanged，字幕系统以后只要监听这个事件按秒对字幕轨即可。
//   * 影片来源：优先磁盘文件；给的是归档里的名字时，由 VFS 取出字节写到临时缓存再播。
//   * 播放期间独占：黑底铺满 800×600 平面，影片按比例居中，播完自动收起。
//
// 只做播放，不碰菜单状态；菜单/任务谁调用谁负责前后切换。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Ra2md.Assets;

namespace Ra2md.Menu
{
    /// <summary>队列里的一段影片。</summary>
    public struct BikClip
    {
        /// <summary>磁盘绝对路径或归档内文件名（如 "ra2intro.bik"）。</summary>
        public string Source;
        /// <summary>片名（给字幕/日志用，可为空）。</summary>
        public string Name;
        public bool Loop;

        public BikClip(string source, string name = null, bool loop = false)
        {
            Source = source;
            Name = name ?? Path.GetFileNameWithoutExtension(source);
            Loop = loop;
        }
    }

    public sealed class FullscreenVideoPlayer : MonoBehaviour
    {
        /// <summary>影片画面形态：全屏（字幕铺画面底部） / 小窗口（字幕进消息列表）。</summary>
        public enum VideoDisplayMode
        {
            Fullscreen,
            Minimap,
        }

        private const int BackdropOrder = 1900;
        private const int ScreenOrder = 1910;

        private static FullscreenVideoPlayer _instance;

        private Transform _plane;              // 800×600 平面中心（MenuWorld）
        private GameObject _backdrop;          // 黑底
        private GameObject _screen;            // 影片
        private readonly Queue<BikClip> _queue = new Queue<BikClip>();

        private BinkMovie _movie;
        private BikClip _current;
        private string _resolvedPath;         // 本段影片实际打开的文件（字幕按它取名）
        private bool _built;
        private bool _clipAnnounced;

        private string _exePath;
        private string _dllPath;

        // ---- 字幕 ----
        private SubtitleOverlay _subtitles;
        private SubtitleTrack _track;
        private int _lastMessageCueIndex = -1;

        /// <summary>全屏或小窗口。小窗口时不铺字幕，字幕转成消息列表条目。</summary>
        public VideoDisplayMode DisplayMode = VideoDisplayMode.Fullscreen;

        /// <summary>每帧回调当前播放时间（秒）。字幕系统监听这个。</summary>
        public event Action<float> TimeChanged;
        /// <summary>切到新片段（片名, 总时长秒）。字幕在这个事件里装载对应字幕轨。</summary>
        public event Action<string, float> ClipChanged;
        /// <summary>整个队列播完。</summary>
        public event Action Completed;

        public static FullscreenVideoPlayer GetOrCreate(Transform plane)
        {
            if (_instance != null) return _instance;

            var go = new GameObject("FullscreenVideo");
            go.transform.SetParent(plane, false);
            _instance = go.AddComponent<FullscreenVideoPlayer>();
            _instance._plane = go.transform;
            return _instance;
        }

        public static FullscreenVideoPlayer Instance { get { return _instance; } }

        public bool IsPlaying { get { return _movie != null || _queue.Count > 0; } }
        public string CurrentClipName { get { return _current.Name; } }
        public int FrameNumber { get { return _movie != null ? _movie.LastFrameNumber : 0; } }
        public int FrameCount { get { return _movie != null ? _movie.FrameCount : 0; } }
        public float Fps { get { return _movie != null ? _movie.Fps : 0f; } }

        /// <summary>当前片段总时长（秒）。</summary>
        public float Duration
        {
            get
            {
                if (_movie == null || _movie.Fps <= 0.001f) return 0f;
                return _movie.FrameCount / _movie.Fps;
            }
        }

        /// <summary>当前播放时间（秒），字幕按这个对齐。</summary>
        public float CurrentTime
        {
            get
            {
                if (_movie == null || _movie.Fps <= 0.001f) return 0f;
                var t = _movie.LastFrameNumber / _movie.Fps;
                return t < 0f ? 0f : t;
            }
        }

        public float Progress
        {
            get
            {
                var d = Duration;
                return d <= 0.001f ? 0f : Mathf.Clamp01(CurrentTime / d);
            }
        }

        // ------------------------------------------------------------ 对外接口

        /// <summary>整队列播放（先清空旧队列）。影片期间音乐由 AudioManager 接管停掉。</summary>
        public void PlayQueue(IEnumerable<BikClip> clips)
        {
            Stop();
            if (clips == null) return;
            if (!_audioHeld)
            {
                AudioManager.Instance.BeginVideo();
                _audioHeld = true;
            }
            foreach (var clip in clips) _queue.Enqueue(clip);
            Advance();
        }

        /// <summary>在队尾追加一段。</summary>
        public void Enqueue(BikClip clip)
        {
            _queue.Enqueue(clip);
            if (_movie == null) Advance();
        }

        /// <summary>先播 LOGO 再播正片（先睹为快就是这条路）。</summary>
        public void PlayLogoThen(string movieSource, string movieName = null)
        {
            var clips = new List<BikClip>();
            var logo = LogoPath();
            if (logo != null) clips.Add(new BikClip(logo, "boot_logo"));
            clips.Add(new BikClip(movieSource, movieName));
            PlayQueue(clips);
        }

        /// <summary>跳过当前片段，接着播队列里的下一段。</summary>
        public void SkipCurrent()
        {
            DisposeMovie();
            Advance();
        }

        /// <summary>停掉播放并清空队列，收起画面。</summary>
        public void Stop()
        {
            _queue.Clear();
            DisposeMovie();
            Hide();
            _track = null;

            if (_subtitles != null) _subtitles.Hide();
            if (_audioHeld)
            {
                _audioHeld = false;
                AudioManager.Instance.EndVideo();
            }
        }

        private bool _audioHeld;

        // ------------------------------------------------------------ 播放推进

        private void Advance()
        {
            DisposeMovie();

            if (_queue.Count == 0)
            {
                Hide();
                if (_audioHeld)
                {
                    _audioHeld = false;
                    AudioManager.Instance.EndVideo();
                }
                if (Completed != null) Completed();
                return;
            }

            _current = _queue.Dequeue();
            _clipAnnounced = false;
            var path = ResolveSource(_current.Source);
            if (path == null)
            {
                Debug.LogWarning("[影片] 找不到片段：" + _current.Source);
                Advance();
                return;
            }
            _resolvedPath = path;

            EnsurePaths();
            _movie = BinkMovie.Start(_exePath, _dllPath, path, _current.Loop,
                MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight);
            if (_movie.Failed)
            {
                Debug.LogError("[影片] 打开失败 " + _current.Name + "：" + _movie.Error);
                DisposeMovie();
                Advance();
                return;
            }
            _movie.SetVolume(AudioManager.Instance.VideoVolumePercent);
            Debug.Log("[影片] 开始播放 " + _current.Name + " ← " + path);
        }

        private void Update()
        {
            if (_movie == null) return;

            // 全屏影片按 Esc 直接退出播放（原版也是这个行为）：清队列、收画面、还音频，
            // 然后通知一次 Completed，让调用页把按钮和背景恢复。
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log("[影片] Esc 退出播放：" + _current.Name);
                Stop();
                if (Completed != null) Completed();
                return;
            }

            if (_movie.Failed)
            {
                Debug.LogError("[影片] 解码失败 " + _current.Name + "：" + _movie.Error);
                SkipCurrent();
                return;
            }

            if (_movie.TryUpdate())
            {
                SyncScreenSize();
                MenuSceneObjects.SetTexture(_screen, _movie.Texture);
                MenuSceneObjects.SetVisible(_screen, true);
            }

            if (TimeChanged != null) TimeChanged(CurrentTime);
            UpdateSubtitles();

            // 影片推子（主输出 × Video 通道）每帧下发，设置页调主音量能立刻作用到影片
            _movie.SetVolume(AudioManager.Instance.VideoVolumePercent);

            // 片头信息要等解码进程读出流头（帧数/帧率）才知道，拿到就通知一次，字幕按片名装轨。
            if (!_clipAnnounced && _movie.FrameCount > 0)
            {
                _clipAnnounced = true;
                if (ClipChanged != null) ClipChanged(_current.Name, Duration);
                LoadSubtitlesFor(_current, _resolvedPath);
                Debug.Log($"[影片] {_current.Name} 时长 {Duration:F2}s，{_movie.FrameCount} 帧 @ {_movie.Fps:F2}fps");
            }

            if (_movie.Finished) SkipCurrent();
        }

        private void DisposeMovie()
        {
            if (_movie == null) return;
            _movie.Dispose();
            _movie = null;
        }

        // ------------------------------------------------------------ 字幕

        /// <summary>
        /// 装字幕轨。字幕文件里的 BeginMovie 用的是**归档文件名**（S02_F00E），
        /// 而界面上显示的是 CSF 里的任务名（軍事行動：似曾相識），两者完全不同，
        /// 所以按「源名 → 实际打开的文件 → 显示名」的顺序试。
        /// </summary>
        private void LoadSubtitlesFor(BikClip clip, string resolvedPath)
        {
            _track = MovieSubtitles.Track(clip.Source)
                     ?? MovieSubtitles.Track(resolvedPath)
                     ?? MovieSubtitles.Track(clip.Name);

            _lastMessageCueIndex = -1;
            if (_subtitles != null) _subtitles.Hide();

            string key = clip.Source ?? resolvedPath ?? clip.Name;
            Debug.Log("[字幕] " + clip.Name + "（" + key + "） → " +
                      (_track == null ? "无字幕轨" : _track.Cues.Count + " 段"));
        }

        private void UpdateSubtitles()
        {
            if (_track == null || _track.Cues.Count == 0) return;

            float time = CurrentTime;
            var cue = _track.CueAt(time, Duration);

            if (DisplayMode == VideoDisplayMode.Minimap)
            {
                // 小窗口：字幕不进画面，按原版转成消息列表
                if (cue != null && _track.Cues.IndexOf(cue) != _lastMessageCueIndex)
                {
                    _lastMessageCueIndex = _track.Cues.IndexOf(cue);
                    float life = cue.Duration > 0.001f ? cue.Duration : 4f;
                    MessageList.AppendMessage(cue.Text, cue.Color, Mathf.Max(2f, life + 0.6f));
                }
                return;
            }

            if (_subtitles == null) _subtitles = SubtitleOverlay.Create(_plane);
            _subtitles.Show(cue, Duration);
        }

        // ------------------------------------------------------------ 画面

        private void EnsurePaths()
        {
            if (_exePath != null) return;
            var dir = Path.Combine(Application.streamingAssetsPath, "Bink");
            _exePath = Path.Combine(dir, "BinkBridge32.exe");
            _dllPath = Path.Combine(dir, "BINKW32.DLL");
        }

        private string LogoPath()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Video", "boot_logo.bik");
            return File.Exists(path) ? path : null;
        }

        /// <summary>文件路径直接用；归档里的名字由 VFS 取字节写到临时缓存再返回缓存路径。</summary>
        public static string ResolveSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (File.Exists(source)) return source;

            var vfs = VirtualFileSystem.Instance;

            // 支持 "0x1234ABCD" 形式的归档条目 ID（影片名往往拿不到名字，只能按 ID 引用）。
            if (TryParseId(source, out var id))
            {
                if (!vfs.TryResolveById(id, out var idBytes, out _) || idBytes == null) return null;
                return CacheToDisk("bik_0x" + id.ToString("X8") + ".bik", idBytes);
            }

            if (!vfs.TryResolve(source, out var bytes, out _) || bytes == null || bytes.Length == 0) return null;
            return CacheToDisk(Path.GetFileName(source), bytes);
        }

        private static bool TryParseId(string text, out uint id)
        {
            id = 0;
            if (text == null) return false;
            var t = text.Trim();
            if (!t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                id = Convert.ToUInt32(t.Substring(2), 16);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string CacheToDisk(string fileName, byte[] bytes)
        {
            var cacheDir = Path.Combine(Application.persistentDataPath, "bikcache");
            try
            {
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                var cachePath = Path.Combine(cacheDir, fileName);
                // 归档内容不可变：大小一致就复用，避免每次播放重写几百 MB。
                if (!File.Exists(cachePath) || new FileInfo(cachePath).Length != bytes.Length)
                {
                    File.WriteAllBytes(cachePath, bytes);
                }
                return cachePath;
            }
            catch (Exception e)
            {
                Debug.LogError("[影片] 写缓存失败 " + fileName + " : " + e.Message);
                return null;
            }
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _backdrop = MenuSceneObjects.Quad(_plane, "VideoBackdrop",
                MenuWidgets.Solid(new Color32(0, 0, 0, 255)),
                0, 0, MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight, BackdropOrder, false);
            MenuSceneObjects.SetVisible(_backdrop, false);
        }

        /// <summary>影片数据到了以后按真实分辨率重建居中画面（原版影片就是 800×600，正好铺满）。</summary>
        private void SyncScreenSize()
        {
            EnsureBuilt();

            int w = Mathf.Max(1, _movie.Width);
            int h = Mathf.Max(1, _movie.Height);
            if (_screen != null)
            {
                var marker = _screen.GetComponent<ScreenSizeMarker>();
                if (marker != null && marker.Width == w && marker.Height == h) return;
                Destroy(_screen);
            }

            int pw = MenuSceneObjects.PlaneWidth;
            int ph = MenuSceneObjects.PlaneHeight;
            var scale = Mathf.Min(pw / (float)w, ph / (float)h);
            int dw = Mathf.RoundToInt(w * scale);
            int dh = Mathf.RoundToInt(h * scale);

            _screen = MenuSceneObjects.Quad(_plane, "VideoScreen", null,
                (pw - dw) / 2, (ph - dh) / 2, dw, dh, ScreenOrder, false);
            _screen.AddComponent<ScreenSizeMarker>().Set(w, h);
            MenuSceneObjects.SetTexture(_screen, MenuSceneObjects.TransparentTexture);
            MenuSceneObjects.SetVisible(_screen, false);

            MenuSceneObjects.SetVisible(_backdrop, true);
        }

        private void Hide()
        {
            EnsureBuilt();
            MenuSceneObjects.SetVisible(_screen, false);
            MenuSceneObjects.SetVisible(_backdrop, false);
        }

        private void OnDestroy()
        {
            DisposeMovie();
            if (_instance == this) _instance = null;
        }

        private sealed class ScreenSizeMarker : MonoBehaviour
        {
            public int Width;
            public int Height;
            public void Set(int w, int h) { Width = w; Height = h; }
        }
    }

    /// <summary>归档里的一条 Bink 影片（影片名大多没有名字表，只能按 ID 引用）。</summary>
    public struct BikEntry
    {
        public uint Id;
        public long Size;
        public string Archive;
        public int Width;
        public int Height;
        public int Frames;
        public float Fps;

        public float Seconds { get { return Fps > 0.001f ? Frames / Fps : 0f; } }
        public string Address { get { return "0x" + Id.ToString("X8"); } }
    }

    /// <summary>
    /// 影片目录：把 movies*.mix / mov*.mix 里的 Bink 条目列出来（只读每条 32 字节头，不把整个影片读进内存）。
    /// 结果会打进日志，供 menu_movies.ini 引用；也供影片列表页做数据源。
    /// </summary>
    public static class BikCatalog
    {
        private const int HeadBytes = 32;

        public static List<BikEntry> Scan()
        {
            var list = new List<BikEntry>();
            var vfs = VirtualFileSystem.Instance;

            foreach (var mounted in vfs.Archives)
            {
                var name = mounted.Name == null ? string.Empty : mounted.Name.ToLowerInvariant();
                if (!name.Contains("mov")) continue;

                foreach (var entry in mounted.Archive.Entries)
                {
                    if (entry.Size < 200_000) continue;
                    var head = mounted.Archive.ReadEntryRange(entry, HeadBytes);
                    if (head == null || head.Length < 24) continue;
                    if (head[0] != (byte)'B' || head[1] != (byte)'I' || head[2] != (byte)'K') continue;

                    int w = (int)BitConverter.ToUInt32(head, 4);
                    int h = (int)BitConverter.ToUInt32(head, 8);
                    int frames = (int)BitConverter.ToUInt32(head, 12);
                    int rate = (int)BitConverter.ToUInt32(head, 16);
                    int div = head.Length >= 24 ? (int)BitConverter.ToUInt32(head, 20) : 0;
                    if (rate <= 0) continue;

                    list.Add(new BikEntry
                    {
                        Id = entry.Id,
                        Size = entry.Size,
                        Archive = mounted.Name,
                        Width = w,
                        Height = h,
                        Frames = frames,
                        Fps = div > 0 ? rate / (float)div : rate,
                    });
                }
            }

            list.Sort((a, b) => b.Size.CompareTo(a.Size));
            return list;
        }

        /// <summary>把目录打进日志，方便照着填 menu_movies.ini。</summary>
        public static void LogCatalog(List<BikEntry> entries)
        {
            if (entries == null) return;
            Debug.Log("[影片] 归档影片目录共 " + entries.Count + " 条（格式：ID 尺寸 帧数 时长）");
            foreach (var e in entries)
            {
                Debug.Log(string.Format("[影片] {0} {1}x{2} {3}帧 {4:F1}s {5}",
                    e.Address, e.Width, e.Height, e.Frames, e.Seconds, e.Archive));
            }
        }
    }

    /// <summary>
    /// 影片配置：根目录/归档里的 menu_movies.ini 指定各用途播哪段片（可写成 0xID 或文件名）。
    /// 没配就不播正片，只打目录日志——不猜。
    /// </summary>
    public static class BikPlaylist
    {
        private const string IniName = "menu_movies.ini";

        public static string Lookup(string section, string key)
        {
            var vfs = VirtualFileSystem.Instance;
            if (!vfs.TryResolve(IniName, out var bytes, out _) || bytes == null) return null;

            string text;
            try { text = System.Text.Encoding.UTF8.GetString(bytes); }
            catch { return null; }

            string current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var k = line.Substring(0, eq).Trim();
                if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
                return line.Substring(eq + 1).Trim();
            }
            return null;
        }
    }
}

