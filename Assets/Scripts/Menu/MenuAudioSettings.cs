// MenuAudioSettings.cs —— 主菜单音量设置（原版 [Audio] 三通道的最小可运行实现）。
// 主音量直接作用到 AudioListener.volume；音效/音乐分别存储，供后续混音分组读取。
using UnityEngine;

namespace Ra2md.Menu
{
    public static class MenuAudioSettings
    {
        private const string KeyMaster = "ra2md.audio.master";
        private const string KeySound = "ra2md.audio.sound";
        private const string KeyMusic = "ra2md.audio.music";

        public static float Master { get; private set; } = 1f;
        public static float Sound { get; private set; } = 1f;
        public static float Music { get; private set; } = 1f;

        public static void Load()
        {
            Master = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMaster, 1f));
            Sound = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySound, 1f));
            Music = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMusic, 1f));
            ApplyMaster();
        }

        public static void SetMaster(float value)
        {
            Master = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMaster, Master);
            PlayerPrefs.Save();
            ApplyMaster();
        }

        public static void SetSound(float value)
        {
            Sound = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeySound, Sound);
            PlayerPrefs.Save();
        }

        public static void SetMusic(float value)
        {
            Music = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMusic, Music);
            PlayerPrefs.Save();
        }

        public static void ApplyMaster()
        {
            AudioListener.volume = Master;
        }

        public static int MasterPercent => Mathf.RoundToInt(Master * 100f);
        public static int SoundPercent => Mathf.RoundToInt(Sound * 100f);
        public static int MusicPercent => Mathf.RoundToInt(Music * 100f);
    }
}
