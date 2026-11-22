// GlobalConfigManager.cs —— 全局配置管理器：所有设置参数的唯一归属。
//
// 职责边界：菜单（MenuManager / 各页面）只管交互与展示，不自己存分辨率、音量、难度这些参数；
// 读写一律走这里。后续接持久化（PlayerPrefs / ra2md.ini）也只改这一个类。
using System;
using UnityEngine;

namespace Ra2md.Managers
{
    public sealed class GlobalConfigManager
    {
        private static GlobalConfigManager _instance;

        public static GlobalConfigManager Instance
        {
            get
            {
                if (_instance == null) _instance = new GlobalConfigManager();
                return _instance;
            }
        }

        // ---- 显示 ----
        public Vector2Int Resolution = new Vector2Int(800, 600);
        public int TargetFps = 60;
        public int DetailLevel = 2;                 // 0 低 / 1 中 / 2 高
        public bool Tooltips = true;
        public bool TargetLines = true;
        public bool ShowHidden = true;

        // ---- 音频（0..5）----
        public int MusicVolume = 1;
        public int SoundVolume = 5;
        public int VoiceVolume = 3;

        // ---- 对局偏好 ----
        public int Difficulty = 2;                  // 0 简单 / 1 正常 / 2 困难
        public int ScrollRate = 6;

        // ---- 战役影片解锁进度（-1 = 未解锁；0 = 第一条已解锁）----
        public int SovietMovieProgress = -1;
        public int AlliedMovieProgress = -1;

        /// <summary>影片归档扩展名。</summary>
        public const string MovieExtension = ".bik";

        /// <summary>影片目录条目：Id 为 CSF Name 后缀，Stem 为 BIK 片名字段。</summary>
        public sealed class MovieCatalogEntry
        {
            public string Id;
            public string Stem;
            public MovieCatalogEntry(string id, string stem) { Id = id; Stem = stem; }
        }

        /// <summary>开场动画（常驻条目）。</summary>
        public static readonly MovieCatalogEntry IntroMovie =
            new MovieCatalogEntry("IntroMovie", "a00_f00e");

        /// <summary>苏军战役影片表（顺序即列表顺序）。</summary>
        public static readonly MovieCatalogEntry[] SovietMovies =
        {
            new MovieCatalogEntry("Sov01MD", "s01_f00e"),
            new MovieCatalogEntry("Sov02MD", "s02_f00e"),
            new MovieCatalogEntry("Sov03MD", "s03_f00e"),
            new MovieCatalogEntry("Sov04MD", "s04_f00e"),
            new MovieCatalogEntry("Sov05MD", "s05_f00e"),
            new MovieCatalogEntry("Sov06MD", "s06_f00e"),
            new MovieCatalogEntry("Sov07MD", "s07_f00e"),
            new MovieCatalogEntry("SovFinalMovie", "s08_f00e"),
        };

        /// <summary>盟军战役影片表（顺序即列表顺序）。</summary>
        public static readonly MovieCatalogEntry[] AlliedMovies =
        {
            new MovieCatalogEntry("All01MD", "a01_f00e"),
            new MovieCatalogEntry("All02MD", "a02_f00e"),
            new MovieCatalogEntry("All03MD", "a03_f00e"),
            new MovieCatalogEntry("All04MD", "a04_f00e"),
            new MovieCatalogEntry("All05MD", "a05_f00e"),
            new MovieCatalogEntry("All06MD", "a06_f00e"),
            new MovieCatalogEntry("All07MD", "a07_f00e"),
            new MovieCatalogEntry("AllFinalMovie", "a08_f00e"),
        };

        /// <summary>把已播放的战役影片记到对应进度上（进度只升不降）。</summary>
        public void NoteMoviePlayed(string stem)
        {
            SovietMovieProgress = RaiseProgress(SovietMovies, SovietMovieProgress, stem);
            AlliedMovieProgress = RaiseProgress(AlliedMovies, AlliedMovieProgress, stem);
        }

        private static int RaiseProgress(MovieCatalogEntry[] table, int progress, string stem)
        {
            for (var i = 0; i < table.Length; i++)
            {
                if (string.Equals(table[i].Stem, stem, StringComparison.OrdinalIgnoreCase))
                    return Math.Max(progress, i);
            }
            return progress;
        }

        /// <summary>把帧率设置落到 Unity（菜单/战场统一从这里生效）。</summary>
        public void ApplyTargetFps()
        {
            Application.targetFrameRate = TargetFps > 0 ? TargetFps : -1;
        }

        // 持久化留位：Load()/Save() 后续接 PlayerPrefs，不在菜单页里直接读 PlayerPrefs。
        public void Load() { }
        public void Save() { }

        /// <summary>
        /// 资源名 / 默认目录表的唯一归属。业务代码、注释以外的逻辑里不得再写死
        /// .shp/.pal/.bik/.wav/.mix/.ini/.csf/.fnt 等明文资源名或候选目录表。
        /// </summary>
        public static class Resources
        {
            // ---- 归档名表（已在本机真实归档上核对命中）----
            public static readonly string[] KnownArchiveNames =
            {
                "ra2.mix", "ra2md.mix", "language.mix", "langmd.mix", "multimd.mix", "thememd.mix",
                "movies01.mix", "movies02.mix", "movmd03.mix", "maps01.mix", "maps02.mix", "mapsmd03.mix",
                "wdt.mix", "multi.mix", "expandmd01.mix", "ares.mix", "theme.mix",
                "conquer.mix", "local.mix", "isosnow.mix", "isourb.mix", "isotem.mix",
                "snow.mix", "temperat.mix", "urban.mix", "sounds.mix", "sounds01.mix",
                "conqmd.mix", "genermd.mix", "localmd.mix", "moviesmd.mix", "soundsmd.mix",
            };

            // ---- 固定结构的内层归档名 ----
            public static readonly string[] KnownNestedArchiveNames =
            {
                "local.mix", "localmd.mix", "conquer.mix", "conqmd.mix",
                "audio.mix", "audiomd.mix", "expandmd01.mix", "expandmd02.mix",
                "thememd.mix", "theme.mix", "multi.mix", "multimd.mix",
                "movies01.mix", "movmd03.mix", "wdt.mix",
            };

            // ---- 归档外的松散资源名（CSF / 字体 / 影片 / 密钥 / 库 / 调色板）----
            public static readonly string[] LooseResourceNames =
            {
                "ra2.csf", "ra2md.csf", "game.fnt", "rules.ini", "rulesmd.ini", "art.ini", "artmd.ini",
                "ea_wwlogo.bik", "pre.bik", "ra2ts_l.bik", "v_001.bik",
                "woldata.key", "binkw32.dll",
                "unittem.pal", "unitsno.pal", "uniturb.pal", "temperat.pal", "snow.pal", "urban.pal",
                "isopalg.pal", "anim.pal", "mouse.pal", "campaign.pal",
            };

            // ---- 裸文件名在归档内的候选扩展名 ----
            public static readonly string[] ExtensionCandidates =
            {
                ".shp", ".vxl", ".hva", ".tmp", ".pal", ".ini", ".map", ".mpr", ".mmx",
                ".yrm", ".yro", ".yrx", ".tem", ".sno", ".urb", ".ubn", ".des", ".lun",
                ".pcx", ".fnt", ".csf", ".wav", ".aud",
            };

            // ---- 规则 / 文本表候选名（按优先顺序）----
            public static readonly string[] RulesIniNames = { "rulesmd.ini", "rules.ini" };
            public static readonly string[] ArtIniNames = { "artmd.ini", "art.ini" };
            public static readonly string[] SoundIniNames = { "soundmd.ini", "sound.ini" };
            public static readonly string[] CsfNames = { "ra2md.csf", "ra2.csf" };
            public const string KeyboardIniName = "KeyboardMD.ini";
            public static readonly string[] KeyboardIniCandidates = { "keyboardmd.ini", "keyboard.ini" };
            public const string SoundOverrideIni = "menu_sounds.ini";
            public const string WavExtension = ".wav";

            // ---- 菜单外壳 SHP / 调色板逻辑名 ----
            public const string ShellPalette = "shell.pal";
            public const string Shell2Palette = "shell2.pal";
            public const string ContentBaseShp = "mnscrnl.shp";
            public const string BottomStripShp = "lwscrnl.shp";
            public const string ColumnTopShp = "sdtp.shp";
            public const string ColumnBottomShp = "sdbtm.shp";
            public const string WarningAnimShp = "sdwrnanm.shp";
            public const string SlotBackingShp = "sdbtnbkgd.shp";
            public const string SlotTileShp = "sdbtnanm.shp";
            public const string SlotTilePalette = "sdbtnanm.pal";
            public const string ButtonShp = "mnbttn.shp";
            public const string WarningPaletteAlias = "sdwrnanm.pal";

            // ---- 菜单外壳实测条目 CRC（名字表未回填时按 ID 直取）----
            public const uint BackgroundShpId = 0x65AC42BDu;
            public const uint BackgroundPaletteId = 0x8412036Bu;
            public const uint WarningPaletteId = 0x31301AE2u;
            public const uint InfoBarShpId = 0x25E640F4u;
            public const uint InfoBarPaletteId = 0x8412036Bu;
            public const uint ButtonPaletteId = 0xC087631Eu;
            public const uint ButtonShpId = 0x1BB65278u;

            // ---- 菜单媒体相对路径（StreamingAssets 下）----
            public const string BootMoviePath = "Video/boot_logo.bik";
            public const string MenuMoviePath = "Video/menu_bg.bik";
            public const string MenuBgmPath = "Audio/menu_bgm_pcm.wav";
            public const string BootMovieFramesDir = "Video/boot_logo_frames";
            public const string MenuMovieFramesDir = "Video/menu_bg_frames";
            public const string BinkBridgeExe = "Bink/BinkBridge32.exe";
            public const string BinkDll = "Bink/BINKW32.DLL";
            public const int MenuMovieFps = 15;

            // ---- 音频采样库 / 字幕表 ----
            public const string AudioBagIndexName = "audio.idx";
            public const string AudioBagName = "audio.bag";
            public static readonly string[] AudioBagIndexPaths =
            {
                "audio.idx",
                "langmd.mix/audiomd.mix/audio.idx",
                "language.mix/audio.mix/audio.idx",
            };
            public static readonly string[] AudioBagPaths =
            {
                "audio.bag",
                "langmd.mix/audiomd.mix/audio.bag",
                "language.mix/audio.mix/audio.bag",
            };
            public static readonly string[] SubtitleNames = { "subtitlemd.txt", "subtitle.txt" };
            public static readonly string[] SubtitlePaths =
            {
                "langmd.mix/subtitlemd.txt",
                "language.mix/subtitle.txt",
            };

            // ---- 地图包归档与包扩展名 ----
            public static readonly string[] MapArchiveNames =
            {
                "multi.mix", "multimd.mix", "maps01.mix", "maps02.mix", "mapsmd03.mix", "expandmd01.mix",
            };
            public static readonly string[] MapPackageExtensions = { ".mmx", ".mix" };

            // ---- 地形剧院 ----
            public const string DefaultTheater = "TEMPERATE";
            public const string TheaterIniSuffix = ".ini";
            public const string TheaterMdIniSuffix = "md.ini";
            public const string TheaterFallbackIni = "temperat.ini";
        }
    }
}
