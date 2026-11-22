// AudioManager.cs —— 全局音频管理器：把声音分成音乐 / 环境 / 人声 / 音效 / 界面五条通道。
//
// 职责（原版行为）：
//   1) 播影片时自动把背景音乐淡出并停掉，影片结束再恢复（主菜单音乐、游戏内 BGM 都走这条）；
//   2) 游戏内暂停 / 播影片时只压音乐与环境音，人声与音效保留（用户明确要求）；
//   3) 音量统一读 MenuAudioSettings（Master/Sound/Music），不各写一套。
//
// 任何要播影片的地方（全屏影片、小地图视频）只要调用 BeginVideo()/EndVideo()，
// 不用自己管音乐；StopAll 只用于退出游戏这类硬切断。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Ra2md.Menu
{
    public sealed class AudioManager : MonoBehaviour
    {
        public enum Channel
        {
            Music,
            Ambient,
            Voice,
            Sfx,
            Ui,
            Video,
        }

        private static AudioManager _instance;

        public static AudioManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("AudioManager");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<AudioManager>();
                _instance.Build();
                return _instance;
            }
        }

        // ------------------------------------------------------------ 内部状态

        private AudioSource _music;
        private AudioSource _ambient;
        private AudioSource _voice;
        private AudioSource _ui;
        private readonly List<AudioSource> _sfxPool = new List<AudioSource>();
        private int _sfxCursor;

        // 调音台通道：音乐 / 环境 / 人声 / 音效 / 界面，各自可独立调音量、静音
        private readonly Dictionary<Channel, float> _channelVolume = new Dictionary<Channel, float>();
        private readonly Dictionary<Channel, bool> _channelMuted = new Dictionary<Channel, bool>();
        private AudioMixer _mixer;
        private AudioMixerGroup _musicGroup, _ambientGroup, _voiceGroup, _sfxGroup, _uiGroup;

        private const string PrefPrefix = "ra2md.audio.ch.";
        private const string PrefMaster = "ra2md.audio.ch.Master";

        private float _masterVolume = 1f;

        private AudioClip _musicClip;
        private bool _musicLoop = true;
        private float _musicPosition;
        private float _musicFade = 1f;

        private int _videoCount;          // 嵌套播放（全屏 + 小地图）用计数，谁最后结束谁恢复音乐
        private bool _gameplayPaused;
        private bool _suspendedForVideo;
        private Coroutine _musicFadeRoutine;

        public bool IsMusicPlaying { get { return _music != null && _music.isPlaying; } }
        public bool IsVideoActive { get { return _videoCount > 0; } }

        /// <summary>影片开始/结束时广播（Bink 音频在外部进程里，菜单里的背景影片要靠这个静音）。</summary>
        public event System.Action<bool> VideoStateChanged;

        /// <summary>影片通道的最终音量（0..100），= 主推子 × Video 通道推子，直接下发给 Bink 解码进程。</summary>
        public int VideoVolumePercent
        {
            get
            {
                var gain = ChannelGain(Channel.Video) * _masterVolume;
                return Mathf.Clamp(Mathf.RoundToInt(gain * 100f), 0, 100);
            }
        }

        // ------------------------------------------------------------ 装配

        private void Build()
        {
            LoadChannels();
            LoadMixer();
            ApplyMasterToMixer();
            ApplyAllChannelToMixer();

            _music = NewSource("Music", Channel.Music);
            _ambient = NewSource("Ambient", Channel.Ambient);
            _voice = NewSource("Voice", Channel.Voice);
            _ui = NewSource("Ui", Channel.Ui);

            for (var i = 0; i < 8; i++)
            {
                var src = NewSource("Sfx" + i, Channel.Sfx);
                src.loop = false;
                _sfxPool.Add(src);
            }

            SyncVolumes();
        }

        /// <summary>如果工程里有 Resources/RA2MDAudio.mixer（含 Music/Ambient/Voice/Sfx/Ui 五个 Group），
        /// 各通道的 AudioSource 就挂到对应 Group 上；没有就退回按通道音量直接算在 AudioSource 上。
        /// </summary>
        private void LoadMixer()
        {
            _mixer = Resources.Load<AudioMixer>("RA2MDAudio");
            if (_mixer == null) return;

            _musicGroup = FindGroup("Music");
            _ambientGroup = FindGroup("Ambient");
            _voiceGroup = FindGroup("Voice");
            _sfxGroup = FindGroup("Sfx");
            _uiGroup = FindGroup("Ui");
            Debug.Log("[音频] 已接入 AudioMixer RA2MDAudio");
        }

        private AudioMixerGroup FindGroup(string name)
        {
            if (_mixer == null) return null;
            var groups = _mixer.FindMatchingGroups(name);
            return groups != null && groups.Length > 0 ? groups[0] : null;
        }

        private AudioSource NewSource(string name, Channel channel)
        {
            var src = CreateSource(channel, name);
            src.loop = false;
            return src;
        }

        /// <summary>
        /// 建一个属于某条通道的 AudioSource（任何模块要出声都必须走这里，创建时就定好通道）。
        /// channel 决定它汇入哪条推子（Music/Ambient/Voice/Sfx/Ui）。
        /// </summary>
        public AudioSource CreateSource(Channel channel, string name, bool loop = false, bool spatial = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = spatial ? 1f : 0f;
            src.volume = 1f;

            var group = GroupFor(channel);
            if (group != null) src.outputAudioMixerGroup = group;

            if (!_channelSources.TryGetValue(channel, out var list))
            {
                list = new List<AudioSource>();
                _channelSources[channel] = list;
            }
            list.Add(src);
            ApplyChannelVolumeToSources(channel);
            return src;
        }

        private readonly Dictionary<Channel, List<AudioSource>> _channelSources =
            new Dictionary<Channel, List<AudioSource>>();

        /// <summary>该通道当前应该用的音量（已含影片淡出之外的所有因子）。</summary>
        private void ApplyChannelVolumeToSources(Channel channel)
        {
            if (!_channelSources.TryGetValue(channel, out var list)) return;
            var gain = ChannelGain(channel);
            if (channel == Channel.Music) gain *= _musicFade;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                list[i].volume = Mathf.Clamp01(gain);
            }
        }

        private AudioMixerGroup GroupFor(Channel channel)
        {
            switch (channel)
            {
                case Channel.Music: return _musicGroup;
                case Channel.Ambient: return _ambientGroup;
                case Channel.Voice: return _voiceGroup;
                case Channel.Ui: return _uiGroup ?? _sfxGroup;
                // 影片音频在 Bink 子进程里直接出声，Mixer 挂不上；Video 通道只负责算给它的百分比
                case Channel.Video: return _sfxGroup;
                default: return _sfxGroup;
            }
        }

        // ------------------------------------------------------------ 调音台通道

        /// <summary>主输出推子：所有通道都先汇到主输出，再乘这一个系数（0..1）。</summary>
        public float MasterVolume { get { return _masterVolume; } }

        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(PrefMaster, _masterVolume);
            PlayerPrefs.Save();
            MenuAudioSettings.SetMaster(_masterVolume);
            ApplyMasterToMixer();
            ApplyAllChannelToMixer();
            SyncVolumes();
        }

        /// <summary>通道音量（0..1，线性；写进 PlayerPrefs）。</summary>
        public float GetChannelVolume(Channel channel)
        {
            return _channelVolume.TryGetValue(channel, out var v) ? v : 1f;
        }

        public void SetChannelVolume(Channel channel, float volume)
        {
            volume = Mathf.Clamp01(volume);
            _channelVolume[channel] = volume;
            PlayerPrefs.SetFloat(PrefPrefix + channel, volume);
            PlayerPrefs.Save();
            // 音乐 / 音效两个推子同时作为旧的 MenuAudioSettings 值，避免两套音量相乘
            if (channel == Channel.Music) MenuAudioSettings.SetMusic(volume);
            else if (channel == Channel.Sfx || channel == Channel.Ui) MenuAudioSettings.SetSound(volume);
            ApplyChannelToMixer(channel, volume);
            SyncVolumes();
        }
        /// <summary>通道静音（不影响存下来的音量值）。</summary>
        public void SetChannelMuted(Channel channel, bool muted)
        {
            _channelMuted[channel] = muted;
            SyncVolumes();
        }

        public bool IsChannelMuted(Channel channel)
        {
            return _channelMuted.TryGetValue(channel, out var m) && m;
        }

        private void LoadChannels()
        {
            _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMaster, MenuAudioSettings.Master));
            foreach (Channel channel in System.Enum.GetValues(typeof(Channel)))
            {
                _channelVolume[channel] = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefPrefix + channel, 1f));
                _channelMuted[channel] = false;
            }
        }

        /// <summary>主输出参数写进 Mixer（MasterVolume，dB）；没有 Mixer 时用 AudioListener 当主推子。</summary>
        private void ApplyMasterToMixer()
        {
            var db = _masterVolume <= 0.0001f ? -80f : Mathf.Log10(_masterVolume) * 20f;
            if (_mixer != null) _mixer.SetFloat("MasterVolume", Mathf.Clamp(db, -80f, 10f));
            else AudioListener.volume = _masterVolume;
        }

        private void ApplyAllChannelToMixer()
        {
            if (_mixer == null) return;
            foreach (Channel channel in System.Enum.GetValues(typeof(Channel)))
                ApplyChannelToMixer(channel, GetChannelVolume(channel));
        }

        /// <summary>若接了 AudioMixer，就把通道音量写成对应的暴露参数（dB）。</summary>
        private void ApplyChannelToMixer(Channel channel, float volume)
        {
            if (_mixer == null) return;
            var param = MixerParam(channel);
            if (string.IsNullOrEmpty(param)) return;
            var db = volume <= 0.0001f ? -80f : Mathf.Log10(volume) * 20f;
            _mixer.SetFloat(param, Mathf.Clamp(db, -80f, 10f));
        }

        private static string MixerParam(Channel channel)
        {
            switch (channel)
            {
                case Channel.Music: return "MusicVolume";
                case Channel.Ambient: return "AmbientVolume";
                case Channel.Voice: return "VoiceVolume";
                case Channel.Sfx: return "SfxVolume";
                case Channel.Ui: return "UiVolume";
                case Channel.Video: return "VideoVolume";
                default: return null;
            }
        }

        private float ChannelGain(Channel channel)
        {
            if (IsChannelMuted(channel)) return 0f;
            // 影片推子永远自己算（Bink 子进程不进 Mixer），其它通道接了 Mixer 就交给 Mixer
            if (_mixer != null && channel != Channel.Video) return 1f;
            return GetChannelVolume(channel);
        }

        private void Update()
        {
            SyncVolumes();
        }

        /// <summary>每帧按通道推子套一遍所有已登记音源；主推子走 AudioListener（无 Mixer 时）。</summary>
        private void SyncVolumes()
        {
            foreach (KeyValuePair<Channel, List<AudioSource>> pair in _channelSources)
            {
                var channel = pair.Key;
                var gain = ChannelGain(channel);
                if (channel == Channel.Music) gain *= _musicFade;
                if (channel == Channel.Ambient && _gameplayPaused) gain = 0f;

                var list = pair.Value;
                for (var i = 0; i < list.Count; i++)
                {
                    var src = list[i];
                    if (src == null) continue;
                    src.volume = Mathf.Clamp01(gain);

                    // 暂停游戏：环境音与循环音效停住，人声和一次性音效不动
                    if (!src.loop) continue;
                    if (channel == Channel.Music || channel == Channel.Voice || channel == Channel.Video) continue;
                    if (_gameplayPaused && src.isPlaying) src.Pause();
                    else if (!_gameplayPaused && !src.isPlaying && src.clip != null) src.UnPause();
                }
            }

            // 没有 Mixer 时主推子挂在 AudioListener 上；接了 Mixer 就由 MasterVolume 参数负责
            AudioListener.volume = _mixer == null ? _masterVolume : 1f;
        }

        // ------------------------------------------------------------ 音乐

        /// <summary>播背景音乐（同一首正在放就不重来）。</summary>
        public void PlayMusic(AudioClip clip, bool loop = true, float fadeSeconds = 0.4f)
        {
            if (clip == null) return;
            if (_music.clip == clip && _music.isPlaying && !_suspendedForVideo) return;

            _musicClip = clip;
            _musicLoop = loop;
            _musicPosition = 0f;
            _suspendedForVideo = false;
            _music.clip = clip;
            _music.loop = loop;
            _music.time = 0f;
            _music.Play();
            FadeMusicTo(1f, fadeSeconds);
        }

        /// <summary>淡出并停掉背景音乐（影片接管声音）。</summary>
        public void StopMusic(float fadeSeconds = 0.3f)
        {
            if (_music == null) return;
            _musicClip = null;
            _musicPosition = 0f;
            FadeMusicTo(0f, fadeSeconds, StopMusicSource);
        }

        private void StopMusicSource()
        {
            if (_music == null) return;
            _music.Stop();
            _music.clip = null;
        }

        public void PauseMusic()
        {
            if (_music != null && _music.isPlaying) _music.Pause();
            _musicPosition = _music != null ? _music.time : 0f;
        }

        public void ResumeMusic()
        {
            if (_music == null || _music.clip == null) return;
            _music.time = Mathf.Clamp(_musicPosition, 0f, Mathf.Max(0f, _music.clip.length - 0.05f));
            _music.UnPause();
        }

        // ------------------------------------------------------------ 视频接管

        /// <summary>
        /// 开始播影片（全屏或小地图内嵌都算）：音乐淡出停掉、暂停游戏时环境音也停，人声与音效不动。
        /// 可嵌套调用，配对调用 EndVideo()。
        /// </summary>
        public void BeginVideo(bool pauseGameplayAudio = false)
        {
            _videoCount++;
            if (_videoCount == 1)
            {
                _suspendedForVideo = true;
                _musicPosition = _music != null ? _music.time : 0f;
                FadeMusicTo(0f, 0.25f, StopMusicSource);
                if (VideoStateChanged != null) VideoStateChanged(true);
            }
            if (pauseGameplayAudio) SetGameplayPaused(true);
        }

        /// <summary>影片播完：恢复音乐（只有最后一段结束时才恢复）。</summary>
        public void EndVideo()
        {
            if (_videoCount > 0) _videoCount--;
            if (_videoCount > 0) return;

            if (_musicClip != null)
            {
                _music.clip = _musicClip;
                _music.loop = _musicLoop;
                _music.time = Mathf.Clamp(_musicPosition, 0f, Mathf.Max(0f, _musicClip.length - 0.05f));
                _music.Play();
                FadeMusicTo(1f, 0.5f);
            }
            _suspendedForVideo = false;
            if (VideoStateChanged != null) VideoStateChanged(false);
        }

        // ------------------------------------------------------------ 游戏暂停

        /// <summary>游戏内暂停（或播剧情影片）：环境音与循环音效暂停，人声与一次性音效保留。</summary>
        public void SetGameplayPaused(bool paused)
        {
            _gameplayPaused = paused;
            SyncVolumes();
        }

        public bool GameplayPaused { get { return _gameplayPaused; } }

        // ------------------------------------------------------------ 环境 / 人声 / 音效 / 界面

        public void PlayAmbient(AudioClip clip, bool loop = true)
        {
            if (clip == null || _ambient == null) return;
            _ambient.clip = clip;
            _ambient.loop = loop;
            _ambient.Play();
        }

        public void StopAmbient()
        {
            if (_ambient == null) return;
            _ambient.Stop();
            _ambient.clip = null;
        }

        /// <summary>人声（EVA / 单位语音）：不受影片与暂停影响。</summary>
        public void PlayVoice(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _voice == null) return;
            _voice.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        // ------------------------------------------------------------ 通用创建 / 播放

        /// <summary>
        /// 通用播放入口：任何模块需要出声都调这里，创建时就绑到指定通道。
        /// loop=true 会建一个循环音源（适合环境音 / 持续音），否则用一次性音源池。
        /// </summary>
        public AudioSource PlayOn(Channel channel, AudioClip clip, float volumeScale = 1f,
            bool loop = false, string name = null)
        {
            if (clip == null) return null;

            if (channel == Channel.Music)
            {
                PlayMusic(clip, loop);
                return _music;
            }

            if (channel == Channel.Voice)
            {
                PlayVoice(clip, volumeScale);
                return _voice;
            }

            if (loop)
            {
                var looping = CreateSource(channel, name ?? (channel + "Loop"), true);
                looping.clip = clip;
                looping.volume = Mathf.Clamp01(ChannelGain(channel) * volumeScale);
                looping.Play();
                return looping;
            }

            if (channel == Channel.Ambient)
            {
                PlayAmbient(clip, false);
                return _ambient;
            }

            // 音效 / 界面走共享池，不每次新建对象
            if (channel == Channel.Sfx) PlaySfx(clip, volumeScale);
            else PlayUi(clip, volumeScale);
            return null;
        }

        /// <summary>一次性音效（爆炸、开火）。</summary>
        public void PlaySfx(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _sfxPool.Count == 0) return;
            var src = _sfxPool[_sfxCursor];
            _sfxCursor = (_sfxCursor + 1) % _sfxPool.Count;
            src.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        /// <summary>界面点击音（主菜单那套也走这里，受 Sound × Ui 通道音量控制）。</summary>
        public void PlayUi(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _ui == null) return;
            _ui.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        // ------------------------------------------------------------ 硬切断

        /// <summary>退出游戏 / 切场景时全部静音（不淡出）。</summary>
        public void StopAll()
        {
            if (_music != null) { _music.Stop(); _music.clip = null; }
            if (_ambient != null) { _ambient.Stop(); _ambient.clip = null; }
            if (_voice != null) _voice.Stop();
            for (var i = 0; i < _sfxPool.Count; i++)
            {
                if (_sfxPool[i] != null) _sfxPool[i].Stop();
            }
            foreach (var pair in _channelSources)
            {
                var list = pair.Value;
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] == null) continue;
                    list[i].Stop();
                    list[i].clip = null;
                }
            }
            _videoCount = 0;
            _suspendedForVideo = false;
            _gameplayPaused = false;
            _musicClip = null;
            _musicFade = 1f;
        }

        // ------------------------------------------------------------ 淡入淡出

        private void FadeMusicTo(float target, float seconds, System.Action onDone = null)
        {
            if (_musicFadeRoutine != null) StopCoroutine(_musicFadeRoutine);
            _musicFadeRoutine = StartCoroutine(FadeRoutine(target, seconds, onDone));
        }

        private IEnumerator FadeRoutine(float target, float seconds, System.Action onDone)
        {
            var from = _musicFade;
            if (seconds <= 0.01f)
            {
                _musicFade = target;
                SyncVolumes();
                if (onDone != null) onDone();
                _musicFadeRoutine = null;
                yield break;
            }

            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                _musicFade = Mathf.Lerp(from, target, Mathf.Clamp01(t / seconds));
                SyncVolumes();
                yield return null;
            }

            _musicFade = target;
            SyncVolumes();
            _musicFadeRoutine = null;
            if (onDone != null) onDone();
        }
    }
}
