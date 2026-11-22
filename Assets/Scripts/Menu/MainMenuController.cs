using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ra2md.Menu;

/// <summary>
/// 主菜单。启动相位 = 片头 Bink；进入菜单后由 MenuShell 承载内容区（循环视频）与右侧按钮列，
/// 与遭遇战等子菜单共用同一套壳体，见 MenuShell。
/// </summary>
public class MainMenuController : MonoBehaviour
{
    private Camera _camera;
    private Transform _world;
    private Canvas _labelCanvas;

    private GameObject _bootQuad;
    private BinkMovie _bootMovie;
    private MenuShell _shell;
    private MenuSoundDirector _sounds;


    private enum MenuPage { Home, Options, SinglePlayer, Movies, MovieList, Keyboard, Skirmish }
    private MenuPage _page = MenuPage.Home;
    private Transform _pageLabels;
    private Text _pageTitle;
    private int _resolutionIndex;
    private int _fpsIndex;
    private int _speedIndex;
    private Vector2Int _appliedResolution;
    private int _appliedFps = 60;
    private int _appliedSpeed = 3;

    private int _detailIndex = 2;      // 畫面精細度 0 低 / 1 中 / 2 高
    private int _difficultyIndex = 2;  // 困難度 0 簡單 / 1 正常 / 2 困難
    private int _scrollRateIndex = 6;  // 捲動速率 0 最慢 .. 6 最快
    private int _musicVolume = 1;
    private int _soundVolume = 5;
    private int _voiceVolume = 3;
    private int _appliedMusic = 1, _appliedSound = 5, _appliedVoice = 3;

    private GameObject _optionWidgetsRoot;
    private MovieListPanel _movieList;
    private Canvas _movieListCanvas;
    private Text _movieHint;
    private SliderWidget _detailSlider, _difficultySlider, _scrollRateSlider;
    private SpinnerWidget _musicSpinner, _soundSpinner, _voiceSpinner;
    private bool _tooltipsOn = true, _targetLinesOn = true, _showHiddenOn = true;
    private Text _detailValue;
    private Text _difficultyValue;
    private Text _scrollRateValue;
    private Text _resolutionValue;
    private Text _musicValue;
    private Text _soundValue;
    private Text _voiceValue;

    private volatile bool _vfsReady;
    private string _vfsError;
    private int _vfsArchiveCount;
    private int _vfsEntryCount;
    private bool _kickedOff;
    private bool _bootOk = true;
    private float _lastAspect = -1f;
    private MenuSplash _splash;

    // 按钮表对照参考 RuntimeMainMenuController.cpp:29-116：文案一律按 CSF key 取，英文兜底。
    private static readonly string[] HomeButtonKeys =
    {
        "GUI:SinglePlayer", "GUI:Internet", "GUI:Network", "GUI:MoviesAndCredits", "GUI:Options", "GUI:ExitGame",
    };

    private static readonly string[] HomeButtonFallbacks =
    {
        "Single Player", "Internet", "Network", "Movies & Credits", "Options", "Exit Game",
    };

    // 选项页右列按钮：鍵盤 / 網路 / 主選單（与原版选项页右列三键一致）。
    private static readonly string[] OptionButtonKeys =
    {
        "GUI:Keyboard", "GUI:Network",
    };

    private static readonly string[] OptionButtonFallbacks =
    {
        "Keyboard", "Network",
    };

    // 画面精细度 / 困难度可选值（CSF key）。
    private static readonly string[] DetailKeys = { "GUI:Low", "GUI:Medium", "GUI:High" };
    private static readonly string[] DetailFallbacks = { "Low", "Medium", "High" };
    private static readonly string[] DifficultyKeys = { "TXT_EASY", "TXT_NORMAL", "TXT_HARD" };
    private static readonly string[] DifficultyFallbacks = { "Easy", "Normal", "Hard" };

    // 单人游戏页：新戰役 / 載入儲存遊戲 / 遭遇戰 / 上一頁（全部文案从 CSF 取）。
    private static readonly string[] SinglePlayerButtonKeys =
    {
        "GUI:NewCampaign", "GUI:LoadSavedGame", "GUI:Skirmish",
    };

    private static readonly string[] SinglePlayerButtonFallbacks =
    {
        "New Campaign", "Load Saved Game", "Skirmish",
    };

    // 动画及制作人员页：先睹為快 / 播放動畫 / 檢視製作人員 / 主選單。
    private static readonly string[] MoviesButtonKeys =
    {
        "GUI:SneakPeeks", "GUI:PlayMovies", "GUI:ViewCredits",
    };

    private static readonly string[] MoviesButtonFallbacks =
    {
        "Sneak Peeks", "Play Movies", "View Credits",
    };

    private static readonly int[] FrameRateChoices = { 30, 60, 120, 144 };

    // 对照参考 gameSpeedLabel（RuntimeMainMenu.cpp:1205-1216）。
    private static readonly string[] GameSpeedKeys =
    {
        "TXT_SLOWEST", "TXT_SLOWER", "TXT_SLOW", "TXT_MEDIUM", "TXT_FAST", "TXT_FASTER", "TXT_FASTEST",
    };

    private static readonly string[] GameSpeedFallbacks =
    {
        "Slowest", "Slower", "Slow", "Medium", "Fast", "Faster", "Fastest",
    };

    /// <summary>候选分辨率，运行时构建（Screen.resolutions 不允许在静态初始化/构造函数里调用）。</summary>
    private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();

    /// <summary>场景里已有菜单控制器就取回；没有就由逻辑层在菜单阶段挂一个。</summary>
    public static MainMenuController Ensure()
    {
        var existing = FindObjectOfType<MainMenuController>();
        if (existing != null) return existing;
        var go = new GameObject("MainMenu");
        return go.AddComponent<MainMenuController>();
    }

    private void Awake()
    {
        Application.runInBackground = true;
        _bootOk = Ra2md.Managers.ManagerBootstrap.Ensure();
        MenuAudioSettings.Load();
        BuildResolutionChoices();
        SetupCamera();
        EnsureEventSystem();
        _world = new GameObject("MenuWorld").transform;
        _world.SetParent(transform, false);
        BuildLabelCanvas();
    }

    private void Start()
    {
        if (!_bootOk) return;

        // 命令对象全量装配（GameCreate<XXXCommandClass>()），进场就有绑定表；战场激活后由 GameplayActive 决定是否派发。
        Ra2md.Input.CommandRegistry.InstallAll();

        StartVfsScan();
        StartCoroutine(BootSequence());
    }

    private void Update()
    {
        UpdateCameraFraming();

        if (_bootMovie == null) return;

        if (_bootMovie.Failed)
        {
            Debug.LogError("[影片] 片头解码失败：" + _bootMovie.Error);
            FinishBootPhase();
            return;
        }

        if (_bootMovie.TryUpdate())
        {
            MenuSceneObjects.SetTexture(_bootQuad, _bootMovie.Texture);
            MenuSceneObjects.SetVisible(_bootQuad, true);
        }

        if (_bootMovie.Finished) FinishBootPhase();
    }

    private void OnDestroy()
    {
        if (_bootMovie != null) { _bootMovie.Dispose(); _bootMovie = null; }
        if (_splash != null) { _splash.Dispose(); _splash = null; }
    }

    // ---------------------------------------------------------------- 场景基础

    private void SetupCamera()
    {
        _camera = Camera.main;
        if (_camera == null)
        {
            var cameraGo = new GameObject("Main Camera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            _camera = cameraGo.GetComponent<Camera>();
        }

        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 100f;
        _camera.transform.rotation = Quaternion.identity;
        _camera.transform.position = new Vector3(
            MenuSceneObjects.PlaneWidth * 0.5f,
            MenuSceneObjects.PlaneHeight * 0.5f,
            -MenuSceneObjects.CameraDistance);
        _camera.orthographicSize = MenuSceneObjects.PlaneHeight * 0.5f;

        if (_camera.GetComponent<PhysicsRaycaster>() == null)
        {
            _camera.gameObject.AddComponent<PhysicsRaycaster>();
        }

        EnsureUniversalCameraData(_camera);
    }

    /// <summary>不直接引用 URP 程序集，只在管线存在时补相机数据组件。</summary>
    private static void EnsureUniversalCameraData(Camera camera)
    {
        var type = Type.GetType(
            "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        if (type == null) return;
        if (camera.GetComponent(type) == null) camera.gameObject.AddComponent(type);
    }

    /// <summary>800×600 完整可见（不足留黑边）；只在比例真的变化时改一次，避免每帧重设造成画面抖动。</summary>
    private void UpdateCameraFraming()
    {
        if (_camera == null) return;

        var aspect = _camera.aspect > 0.0001f ? _camera.aspect : 1f;
        if (Mathf.Abs(aspect - _lastAspect) < 0.0005f) return;

        _lastAspect = aspect;
        _camera.orthographicSize = Mathf.Max(MenuSceneObjects.PlaneHeight * 0.5f,
            MenuSceneObjects.PlaneWidth * 0.5f / aspect);
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(go);
    }

    private void BuildLabelCanvas()
    {
        var canvasGo = new GameObject("MenuLabels", typeof(RectTransform));
        canvasGo.transform.SetParent(_world, false);

        var rect = canvasGo.GetComponent<RectTransform>();
        rect.position = Vector3.zero;
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight);
        rect.localScale = Vector3.one;

        _labelCanvas = canvasGo.AddComponent<Canvas>();
        _labelCanvas.renderMode = RenderMode.WorldSpace;
        _labelCanvas.sortingOrder = 60;

        var raycaster = canvasGo.AddComponent<GraphicRaycaster>();
        raycaster.enabled = false;
    }

    /// <summary>另建一层文字画布（影片列表这类子页面单独一层，排序压在菜单文字之上）。</summary>
    private Canvas BuildLabelCanvas(string name)
    {
        var canvasGo = new GameObject(name, typeof(RectTransform));
        canvasGo.transform.SetParent(_world, false);

        var rect = canvasGo.GetComponent<RectTransform>();
        rect.position = Vector3.zero;
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight);
        rect.localScale = Vector3.one;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 80;

        var raycaster = canvasGo.AddComponent<GraphicRaycaster>();
        raycaster.enabled = false;
        return canvas;
    }

    // ---------------------------------------------------------------- 归档

    private void StartVfsScan()
    {
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var vfs = MenuResourceLibrary.EnsureMounted();
                _vfsArchiveCount = vfs.ArchiveCount;
                _vfsEntryCount = vfs.EntryCount;
            }
            catch (Exception e)
            {
                _vfsError = e.Message;
            }
            _vfsReady = true;
        });
        thread.IsBackground = true;
        thread.Name = "VfsScan";
        thread.Start();
    }

    private void ReportVfs()
    {
        if (!string.IsNullOrEmpty(_vfsError))
        {
            Debug.LogError("[菜单] 归档扫描失败：" + _vfsError);
            return;
        }
        Debug.Log($"[菜单] 归档 {_vfsArchiveCount} 个，条目 {_vfsEntryCount} 条");
    }

    // ---------------------------------------------------------------- 片头

    private IEnumerator BootSequence()
    {
        _bootQuad = MenuSceneObjects.Quad(_world, "BootMovie", null, 0, 0, 800, 600, 0, false);
        _bootQuad.SetActive(false);

        var exe = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.BinkBridgeExe);
        var dll = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.BinkDll);
        var boot = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.BootMoviePath);

        Debug.Log("[影片] 片头 Bink 解码 " + boot);
        _bootMovie = BinkMovie.Start(exe, dll, boot, false, 800, 600);
        _bootMovie.SetVolume(Mathf.Clamp(Mathf.RoundToInt(MenuAudioSettings.MasterPercent), 0, 100));

        if (_bootMovie.Failed)
        {
            Debug.LogError("[影片] 无法启动片头解码：" + _bootMovie.Error);
            FinishBootPhase();
            yield break;
        }

        var deadline = Time.unscaledTime + 60f;
        // Update() 在解码失败时会 Dispose 并置空 _bootMovie，这里必须每帧重新检查引用，
        // 否则会命中 NullReferenceException（旧写法直接解引用 _bootMovie）。
        while (Time.unscaledTime < deadline)
        {
            var movie = _bootMovie;
            if (movie == null || movie.Finished || movie.Failed) break;
            yield return null;
        }

        FinishBootPhase();
    }

    private void FinishBootPhase()
    {
        if (_bootMovie != null)
        {
            _bootMovie.Dispose();
            _bootMovie = null;
        }

        if (_bootQuad != null)
        {
            Destroy(_bootQuad);
            _bootQuad = null;
        }

        if (_kickedOff) return;
        _kickedOff = true;
        StartCoroutine(ShowSplashThenMenu());
    }

    /// <summary>logo 影片结束后上启动信息画面，停留到最短时长结束，再进主菜单外壳。</summary>
    private IEnumerator ShowSplashThenMenu()
    {
        _splash = MenuSplash.Show(_world);
        while (_splash != null && !_splash.Done) yield return null;
        if (_splash != null)
        {
            _splash.Dispose();
            _splash = null;
        }
        StartCoroutine(EnterMenuPhase());
    }

    // ---------------------------------------------------------------- 主菜单

    private IEnumerator EnterMenuPhase()
    {
        while (!_vfsReady) yield return null;
        ReportVfs();

        MenuSceneObjects.Quad(_world, "Backdrop", BuildBackdropTexture(),
            0, 0, MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight, -10, false);

        _shell = MenuShell.Create(_world);
        _sounds = MenuSoundDirector.Create(_world);

        var exe = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.BinkBridgeExe);
        var dll = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.BinkDll);
        var bg = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.MenuMoviePath);

        Debug.Log("[影片] 内容区 Bink 解码（循环）" + bg);
        var movie = BinkMovie.Start(exe, dll, bg, true,
            MenuShell.ContentWidth, MenuShell.ContentHeight);
        if (movie.Failed)
        {
            Debug.LogError("[影片] 主菜单背景影片打开失败：" + movie.Error);
            _shell.SetContentMovie(null);
        }
        else
        {
            movie.SetVolume(AudioManager.Instance.VideoVolumePercent);
            _shell.SetContentMovie(movie);
        }

        _shell.SetContentShade(1f);
        BuildPageLabels();
        ShowHome(true);
        if (_sounds != null) _sounds.PlaySlideIn();
        StartCoroutine(PlayMenuMusic());

        yield return new WaitForSecondsRealtime(0.1f);
        Debug.Log("[菜单] 主菜单就绪（MenuShell 内容区 + 按钮列）");
    }

    // ---------------------------------------------------------------- 页

    private void BuildResolutionChoices()
    {
        _resolutions.Clear();
        foreach (var mode in Screen.resolutions)
        {
            if (mode.width < MenuShell.ContentWidth || mode.height < MenuSceneObjects.PlaneHeight) continue;
            var size = new Vector2Int(mode.width, mode.height);
            if (!_resolutions.Contains(size)) _resolutions.Add(size);
        }

        if (_resolutions.Count == 0)
        {
            // 无显示模式列表（无头/编辑器首帧）时退回 4:3 三档。
            _resolutions.Add(new Vector2Int(800, 600));
            _resolutions.Add(new Vector2Int(1024, 768));
            _resolutions.Add(new Vector2Int(1280, 960));
        }

        _appliedResolution = new Vector2Int(Screen.width, Screen.height);
        if (!_resolutions.Contains(_appliedResolution))
        {
            _resolutionIndex = 0;
        }
        else
        {
            _resolutionIndex = _resolutions.IndexOf(_appliedResolution);
        }
        _appliedFps = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
        _fpsIndex = Mathf.Max(0, System.Array.IndexOf(FrameRateChoices, _appliedFps));
        _speedIndex = _appliedSpeed;
    }

    /// <summary>主菜单按钮：前五个进 0..4，最后一个是 8（SDBTM 内的「退出遊戲」底板）。</summary>
    private static MenuShell.ButtonSpec[] BuildHomeButtons()
    {
        var specs = new List<MenuShell.ButtonSpec>(HomeButtonKeys.Length);
        for (var i = 0; i < HomeButtonKeys.Length; i++)
        {
            int slot = i == HomeButtonKeys.Length - 1 ? MenuButtonPanel.BottomSlot : i;
            specs.Add(new MenuShell.ButtonSpec(slot, HomeButtonKeys[i],
                MenuStrings.Get(HomeButtonKeys[i], HomeButtonFallbacks[i])));
        }
        return specs.ToArray();
    }

    private static MenuShell.ButtonSpec[] BuildOptionButtons()
    {
        var specs = new List<MenuShell.ButtonSpec>(OptionButtonKeys.Length + 1);
        for (var i = 0; i < OptionButtonKeys.Length; i++)
        {
            specs.Add(new MenuShell.ButtonSpec(i, OptionButtonKeys[i],
                MenuStrings.Get(OptionButtonKeys[i], OptionButtonFallbacks[i])));
        }
        // 底部底板放 GUI:MainMenu（选项页的返回）。
        specs.Add(new MenuShell.ButtonSpec(MenuButtonPanel.BottomSlot, "GUI:MainMenu",
            MenuStrings.Get("GUI:MainMenu", "Main Menu")));
        return specs.ToArray();
    }

    /// <summary>单人游戏页按钮：新戰役 / 載入儲存遊戲（无存档则红字不可点）/ 遭遇戰 + 上一頁。</summary>
    private static MenuShell.ButtonSpec[] BuildSinglePlayerButtons()
    {
        bool hasSave = HasSavedGame();
        var specs = new List<MenuShell.ButtonSpec>(SinglePlayerButtonKeys.Length + 1);
        for (var i = 0; i < SinglePlayerButtonKeys.Length; i++)
        {
            var key = SinglePlayerButtonKeys[i];
            var text = MenuStrings.Get(key, SinglePlayerButtonFallbacks[i]);
            var enabled = key != "GUI:LoadSavedGame" || hasSave;
            var color = enabled ? (Color32?)null : MenuButtonPanel.DisabledTextColor;
            specs.Add(new MenuShell.ButtonSpec(i, key, text, color, enabled));
        }
        specs.Add(new MenuShell.ButtonSpec(MenuButtonPanel.BottomSlot, "GUI:Back",
            MenuStrings.Get("GUI:Back", "Back")));
        return specs.ToArray();
    }

    /// <summary>是否存在存档：扫存档目录里的 *.sav / *.SAV（没有就是没有，不猜）。</summary>
    private static bool HasSavedGame()
    {
        try
        {
            var root = Application.streamingAssetsPath;
            if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f);
                    if (string.Equals(ext, ".sav", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[菜单] 存档检测失败：" + e.Message);
        }
        return false;
    }

    /// <summary>主菜单的内容区循环影片；子页面把它停掉后，回主菜单用这里重新装回来。</summary>
    private void EnsureContentMovie()
    {
        if (_shell == null || _shell.ContentMovie != null) return;

        var exe = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.BinkBridgeExe);
        var dll = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.BinkDll);
        var bg = Path.Combine(Application.streamingAssetsPath, Ra2md.Managers.GlobalConfigManager.Resources.MenuMoviePath);
        if (!File.Exists(exe) || !File.Exists(bg)) return;

        var movie = BinkMovie.Start(exe, dll, bg, true,
            MenuShell.ContentWidth, MenuShell.ContentHeight);
        if (movie.Failed)
        {
            Debug.LogError("[影片] 主菜单背景影片打开失败：" + movie.Error);
            _shell.SetContentMovie(null);
            return;
        }
        movie.SetVolume(AudioManager.Instance.VideoVolumePercent);
        _shell.SetContentMovie(movie);
        Debug.Log("[影片] 回主菜单，内容区影片重新装载");
    }

    private void ShowHome(bool animate)
    {
        _page = MenuPage.Home;
        if (_shell == null) return;

        ClearMovieList();
        // 回主菜单要把影片装回来（底图 mnscrnl 一直在，影片只是盖在上面的一层）。
        EnsureContentMovie();
        _shell.SetContentShade(1f);
        ClearPageLabels();
        ClearOptionWidgets();
        SetPageTitle("GUI:MainMenu", "Main Menu");
        _shell.SetButtons(BuildHomeButtons(), OnMenuButtonClicked);
        if (animate) _shell.RevealButtons();
        else _shell.ShowButtonsIdle();
    }

    /// <summary>选项页：同一壳体，按钮换成解析度/畫面速率/遊戲速度/套用/取消。</summary>
    private void ShowOptions()
    {
        if (_page == MenuPage.Options) return;
        _page = MenuPage.Options;
        if (_shell == null) return;

        ClearMovieList();

        // 子页面第一件事：停掉并移除内容区影片，否则内容区被视频占着，选项控件根本没地方画。
        _shell.StopContentMovie();
        _shell.SetContentTexture(null);
        _shell.SetContentShade(1f);
        ClearPageLabels();
        ClearOptionWidgets();
        SetPageTitle("GUI:Options", "Options");
        BuildOptionsPage();
        _shell.SetButtons(BuildOptionButtons(), OnMenuButtonClicked);
        _shell.RevealButtons();
    }

    /// <summary>
    /// 选项页四个分区（顯示選項 / 遊戲選項 / 介面選項 / 音效選項）＋ 控件，
    /// 坐标按 802×632 原版截图逐项量出：滑条 177×20、下拉 180×30、复选 14×14、数字框 122×46。
    /// </summary>
    private void BuildOptionsPage()
    {
        if (_shell == null) return;

        var root = new GameObject("OptionsPage");
        root.transform.SetParent(_shell.transform, false);
        root.transform.localPosition = Vector3.zero;
        _optionWidgetsRoot = root;

        // 音频三个框的初值直接来自调音台通道推子（不是写死的 1/5/3）
        var audio = AudioManager.Instance;
        _musicVolume = Mathf.RoundToInt(audio.GetChannelVolume(AudioManager.Channel.Music) * 10f);
        _soundVolume = Mathf.RoundToInt(audio.GetChannelVolume(AudioManager.Channel.Sfx) * 10f);
        _voiceVolume = Mathf.RoundToInt(audio.GetChannelVolume(AudioManager.Channel.Voice) * 10f);

        const int order = 60;
        var t = root.transform;

        // ---------------- 顯示選項 ----------------
        AddPageLabel("GrpDisplay", MenuStrings.Get("GUI:DisplayOptions", "Display Options"),
            24, 46, 220, 28, TextAnchor.MiddleLeft, 20, TitleColor);
        AddPageLabel("LblVisualDetails", MenuStrings.Get("GUI:VisualDetails", "Visual Details"),
            135, 84, 110, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        _detailValue = AddPageLabel("ValVisualDetails", DetailLabel(), 250, 84, 60, 22,
            TextAnchor.MiddleRight, 14, ValueColor);
        _detailSlider = MenuWidgets.SliderLive(t, "VisualSlider", 135, 112, 177, 20,
            _detailIndex / 2f, order, v =>
            {
                _detailIndex = Mathf.RoundToInt(v * 2f);
                _detailSlider.SetValue(_detailIndex / 2f);
                if (_detailValue != null) _detailValue.text = DetailLabel();
            });

        AddPageLabel("LblResolution", MenuStrings.Get("GUI:SetResolution", "Set Resolution"),
            355, 84, 180, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        MenuWidgets.Combo(t, "ResolutionCombo", 353, 106, 180, 30, order);
        _resolutionValue = AddPageLabel("ValResolution", CurrentResolutionLabel() + " x 16",
            366, 110, 146, 22, TextAnchor.MiddleLeft, 13, new Color32(236, 236, 236, 255));

        // ---------------- 遊戲選項 ----------------
        AddPageLabel("GrpGame", MenuStrings.Get("GUI:GameOptions", "Game Options"),
            24, 176, 220, 28, TextAnchor.MiddleLeft, 20, TitleColor);
        AddPageLabel("LblDifficulty", MenuStrings.Get("GUI:Difficulty", "Difficulty"),
            135, 208, 110, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        _difficultyValue = AddPageLabel("ValDifficulty", DifficultyLabel(), 250, 208, 60, 22,
            TextAnchor.MiddleRight, 14, ValueColor);
        _difficultySlider = MenuWidgets.SliderLive(t, "DifficultySlider", 135, 236, 177, 20,
            _difficultyIndex / 2f, order, v =>
            {
                _difficultyIndex = Mathf.RoundToInt(v * 2f);
                _difficultySlider.SetValue(_difficultyIndex / 2f);
                if (_difficultyValue != null) _difficultyValue.text = DifficultyLabel();
            });

        // ---------------- 介面選項 ----------------
        AddPageLabel("GrpUI", MenuStrings.Get("GUI:UIOptions", "Interface Options"),
            24, 300, 220, 28, TextAnchor.MiddleLeft, 20, TitleColor);
        MenuWidgets.CheckBoxLive(t, "CbTooltips", 143, 338, _tooltipsOn, order, v => _tooltipsOn = v);
        AddPageLabel("LblTooltips", MenuStrings.Get("GUI:Tooltips", "Tooltips"),
            163, 334, 130, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        MenuWidgets.CheckBoxLive(t, "CbTargetLines", 143, 364, _targetLinesOn, order, v => _targetLinesOn = v);
        AddPageLabel("LblTargetLines", MenuStrings.Get("GUI:TargetLines", "Target Lines"),
            163, 360, 130, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        MenuWidgets.CheckBoxLive(t, "CbShowHidden", 143, 388, _showHiddenOn, order, v => _showHiddenOn = v);
        AddPageLabel("LblShowHidden", MenuStrings.Get("GUI:ShowHidden", "Show Hidden Objects"),
            163, 384, 150, 22, TextAnchor.MiddleLeft, 14, LabelColor);

        AddPageLabel("LblScrollRate", MenuStrings.Get("GUI:ScrollRate", "Scroll Rate"),
            355, 336, 110, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        _scrollRateValue = AddPageLabel("ValScrollRate", ScrollRateLabel(), 470, 336, 64, 22,
            TextAnchor.MiddleRight, 14, ValueColor);
        _scrollRateSlider = MenuWidgets.SliderLive(t, "ScrollRateSlider", 353, 362, 180, 20,
            _scrollRateIndex / 6f, order, v =>
            {
                _scrollRateIndex = Mathf.RoundToInt(v * 6f);
                _scrollRateSlider.SetValue(_scrollRateIndex / 6f);
                if (_scrollRateValue != null) _scrollRateValue.text = ScrollRateLabel();
            });

        // ---------------- 音效選項 ----------------
        AddPageLabel("GrpSound", MenuStrings.Get("GUI:SoundOptions", "Sound Options"),
            24, 436, 220, 28, TextAnchor.MiddleLeft, 20, TitleColor);
        AddPageLabel("LblMusic", MenuStrings.Get("GUI:MusicVolume", "Music Volume"),
            135, 462, 110, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        _musicSpinner = MenuWidgets.SpinnerLive(t, "MusicSpinner", 123, 486, 122, 46, _musicVolume / 10f, order, v =>
        {
            _musicVolume = Mathf.RoundToInt(v * 10f);
            _musicSpinner.SetValue(_musicVolume / 10f);
            AudioManager.Instance.SetChannelVolume(AudioManager.Channel.Music, _musicVolume / 10f);
            if (_musicValue != null) _musicValue.text = _musicVolume.ToString();
        });
        _musicValue = AddPageLabel("ValMusic", _musicVolume.ToString(), 219, 490, 24, 20,
            TextAnchor.MiddleCenter, 14, ValueColor);

        AddPageLabel("LblSound", MenuStrings.Get("GUI:SoundVolume", "Sound Volume"),
            265, 462, 110, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        _soundSpinner = MenuWidgets.SpinnerLive(t, "SoundSpinner", 250, 486, 122, 46, _soundVolume / 10f, order, v =>
        {
            _soundVolume = Mathf.RoundToInt(v * 10f);
            _soundSpinner.SetValue(_soundVolume / 10f);
            AudioManager.Instance.SetChannelVolume(AudioManager.Channel.Sfx, _soundVolume / 10f);
            AudioManager.Instance.SetChannelVolume(AudioManager.Channel.Ui, _soundVolume / 10f);
            if (_soundValue != null) _soundValue.text = _soundVolume.ToString();
        });
        _soundValue = AddPageLabel("ValSound", _soundVolume.ToString(), 346, 490, 24, 20,
            TextAnchor.MiddleCenter, 14, ValueColor);

        AddPageLabel("LblVoice", MenuStrings.Get("GUI:VoiceVolume", "Voice Volume"),
            396, 462, 110, 22, TextAnchor.MiddleLeft, 14, LabelColor);
        _voiceSpinner = MenuWidgets.SpinnerLive(t, "VoiceSpinner", 392, 486, 122, 46, _voiceVolume / 10f, order, v =>
        {
            _voiceVolume = Mathf.RoundToInt(v * 10f);
            _voiceSpinner.SetValue(_voiceVolume / 10f);
            AudioManager.Instance.SetChannelVolume(AudioManager.Channel.Voice, _voiceVolume / 10f);
            if (_voiceValue != null) _voiceValue.text = _voiceVolume.ToString();
        });
        _voiceValue = AddPageLabel("ValVoice", _voiceVolume.ToString(), 488, 490, 24, 20,
            TextAnchor.MiddleCenter, 14, ValueColor);
    }

    private static readonly Color32 TitleColor = new Color32(255, 222, 25, 255);
    private static readonly Color32 LabelColor = new Color32(255, 222, 25, 255);
    private static readonly Color32 ValueColor = new Color32(255, 222, 25, 255);

    private string DetailLabel()
    {
        var i = Mathf.Clamp(_detailIndex, 0, DetailKeys.Length - 1);
        return MenuStrings.Get(DetailKeys[i], DetailFallbacks[i]);
    }

    private string DifficultyLabel()
    {
        var i = Mathf.Clamp(_difficultyIndex, 0, DifficultyKeys.Length - 1);
        return MenuStrings.Get(DifficultyKeys[i], DifficultyFallbacks[i]);
    }

    private string ScrollRateLabel()
    {
        var i = Mathf.Clamp(_scrollRateIndex, 0, GameSpeedKeys.Length - 1);
        return MenuStrings.Get(GameSpeedKeys[i], GameSpeedFallbacks[i]);
    }

    /// <summary>键盘选项页：命令 ↔ 按键，点行即可改键。</summary>
    private void ShowKeyboardOptions()
    {
        if (_page == MenuPage.Keyboard) return;
        _page = MenuPage.Keyboard;
        if (_shell == null) return;

        ClearMovieList();
        _shell.StopContentMovie();
        _shell.SetContentTexture(null);
        _shell.SetContentShade(1f);
        ClearPageLabels();
        ClearOptionWidgets();
        SetPageTitle("GUI:KeyboardOptions", "Keyboard Options");

        _keyboardPage = KeyboardOptionsPage.Create(_shell.transform);

        _shell.SetButtons(new[]
        {
            new MenuShell.ButtonSpec(MenuButtonPanel.BottomSlot, "GUI:Back",
                MenuStrings.Get("GUI:Back", "Back")),
        }, OnMenuButtonClicked);
        _shell.RevealButtons();
    }

    private KeyboardOptionsPage _keyboardPage;

    private SkirmishPage _skirmishPage;

    /// <summary>遭遇战大厅：入口由 GameStatics.GameMode=Skirmish 决定（对照 SessionClass::GameMode）。</summary>
    private void ShowSkirmish()
    {
        if (_shell == null) return;
        _page = MenuPage.Skirmish;

        ClearMovieList();
        _shell.StopContentMovie();
        _shell.SetContentTexture(null);
        _shell.SetContentShade(1f);
        ClearPageLabels();
        ClearOptionWidgets();
        SetPageTitle("GUI:SkirmishGame", "Skirmish Game");

        Ra2md.Core.GameStatics.GameMode = Ra2md.Core.GameModeKind.Skirmish;
        // 原版大厅右列顶区不是 WARNING 屏，换成雷达框 + 地图信息框
        _shell.SetWarningVisible(false);
        _skirmishPage = SkirmishPage.Create(_shell.transform, RequestSkirmishButtons);
        SetSkirmishButtons("lobby");
        _shell.RevealButtons();
    }

    private void RequestSkirmishButtons(string view)
    {
        SetSkirmishButtons(view);
    }

    private void SetSkirmishButtons(string view)
    {
        if (_shell == null) return;

        IList<MenuShell.ButtonSpec> specs;
        if (view == "map")
        {
            specs = new List<MenuShell.ButtonSpec>(2)
            {
                new MenuShell.ButtonSpec(1, "GUI:UseMap", MenuStrings.Get("GUI:UseMap", "Use Map")),
                new MenuShell.ButtonSpec(MenuButtonPanel.BottomSlot, "GUI:Cancel",
                    MenuStrings.Get("GUI:Cancel", "Cancel")),
            };
        }
        else
        {
            specs = new List<MenuShell.ButtonSpec>(3)
            {
                new MenuShell.ButtonSpec(1, "GUI:StartGame", MenuStrings.Get("GUI:StartGame", "Start Game")),
                new MenuShell.ButtonSpec(2, "GUI:ChooseMap", MenuStrings.Get("GUI:ChooseMap", "Choose Map")),
                new MenuShell.ButtonSpec(MenuButtonPanel.BottomSlot, "GUI:Back",
                    MenuStrings.Get("GUI:Back", "Back")),
            };
        }
        _shell.SetButtons(specs, OnMenuButtonClicked);
        _shell.RevealButtons();
    }

    private void ClearSkirmishPage()
    {
        if (_skirmishPage != null)
        {
            Destroy(_skirmishPage.gameObject);
            _skirmishPage = null;
        }
    }

    private void ClearKeyboardPage()
    {
        if (_keyboardPage != null)
        {
            Destroy(_keyboardPage.gameObject);
            _keyboardPage = null;
        }
    }

    private void ClearOptionWidgets()
    {
        if (_optionWidgetsRoot != null)
        {
            Destroy(_optionWidgetsRoot);
            _optionWidgetsRoot = null;
        }
        _detailValue = _difficultyValue = _scrollRateValue = null;
        _resolutionValue = _musicValue = _soundValue = _voiceValue = null;
        _detailSlider = _difficultySlider = _scrollRateSlider = null;
        _musicSpinner = _soundSpinner = _voiceSpinner = null;
        ClearKeyboardPage();
        ClearSkirmishPage();
    }

    /// <summary>单人游戏页：新戰役 / 載入儲存遊戲 / 遭遇戰 / 上一頁；内容区先停影片，底图保留。</summary>
    private void ShowSinglePlayer()
    {
        if (_shell == null) return;
        _page = MenuPage.SinglePlayer;

        ClearMovieList();
        _shell.StopContentMovie();
        _shell.SetContentTexture(null);
        _shell.SetContentShade(1f);
        ClearPageLabels();
        ClearOptionWidgets();
        SetPageTitle("GUI:SinglePlayer", "Single Player");
        _shell.SetButtons(BuildSinglePlayerButtons(), OnMenuButtonClicked);
        _shell.RevealButtons();
    }

    /// <summary>动画及制作人员页：同一壳体，按钮换成先睹為快/播放動畫/檢視製作人員 + 主選單。</summary>
    private void ShowMovies()
    {
        if (_shell == null) return;
        _page = MenuPage.Movies;

        ClearMovieList();
        // 子页面：停影片、露底图，内容区留给后续影片列表绘制。
        _shell.StopContentMovie();
        _shell.SetContentTexture(null);
        _shell.SetContentShade(1f);
        ClearPageLabels();
        ClearOptionWidgets();
        SetPageTitle("GUI:MoviesAndCredits", "Movies & Credits");
        _shell.SetButtons(BuildMoviesButtons(), OnMenuButtonClicked);
        _shell.RevealButtons();
    }

    private static MenuShell.ButtonSpec[] BuildMoviesButtons()
    {
        var specs = new List<MenuShell.ButtonSpec>(MoviesButtonKeys.Length + 1);
        for (var i = 0; i < MoviesButtonKeys.Length; i++)
        {
            specs.Add(new MenuShell.ButtonSpec(i, MoviesButtonKeys[i],
                MenuStrings.Get(MoviesButtonKeys[i], MoviesButtonFallbacks[i])));
        }
        specs.Add(new MenuShell.ButtonSpec(MenuButtonPanel.BottomSlot, "GUI:MainMenu",
            MenuStrings.Get("GUI:MainMenu", "Main Menu")));
        return specs.ToArray();
    }

    /// <summary>先睹为快：LOGO + 正片，交给通用全屏播放器按队列播。</summary>
    private void PlaySneakPeek()
    {
        if (_shell == null) return;
        var player = FullscreenVideoPlayer.GetOrCreate(_world);
        player.Completed -= OnFullscreenMovieCompleted;
        player.Completed += OnFullscreenMovieCompleted;

        // 先睹为快 = LOGO + 正片，正片就是开场动画（a00_f00e.bik），不需要任何配置。
        var clip = MovieListPanel.AutoResolveClip("IntroMovie", 0);
        if (string.IsNullOrEmpty(clip))
        {
            Debug.LogWarning("[影片] 先睹为快没有可用正片，先只播 LOGO；下面是可选条目：");
            BikCatalog.LogCatalog(BikCatalog.Scan());
        }

        _shell.StopContentMovie();
        _shell.SetButtons(new List<MenuShell.ButtonSpec>().ToArray(), OnMenuButtonClicked);
        if (clip != null) player.PlayLogoThen(clip, "先睹为快");
        else
        {
            var logo = Path.Combine(Application.streamingAssetsPath,
                Ra2md.Managers.GlobalConfigManager.Resources.BootMoviePath);
            player.PlayQueue(new[] { new BikClip(logo, "boot_logo") });
        }
    }

    private void OnFullscreenMovieCompleted()
    {
        // 从哪一页发起的播放，就回哪一页（先睹為快 → 動畫页；播放影片 → 選擇影片页）。
        if (_page == MenuPage.Movies) ShowMovies();
        else if (_page == MenuPage.MovieList) ShowMovieList();
    }

    /// <summary>「選擇影片」页：左侧影片清单，右侧只剩「播放影片」+ 上一頁。</summary>
    private void ShowMovieList()
    {
        if (_shell == null) return;
        _page = MenuPage.MovieList;

        _shell.StopContentMovie();
        _shell.SetContentTexture(null);
        _shell.SetContentShade(1f);
        ClearPageLabels();
        ClearOptionWidgets();
        ClearMovieList();

        SetPageTitle("GUI:SelectMovie", "Select Movie");
        if (_movieListCanvas == null) _movieListCanvas = BuildLabelCanvas("MovieListCanvas");
        _movieList = MovieListPanel.Create(_world, _movieListCanvas);

        var specs = new List<MenuShell.ButtonSpec>(2)
        {
            new MenuShell.ButtonSpec(0, "GUI:PlayMovie", MenuStrings.Get("GUI:PlayMovie", "Play Movie")),
            new MenuShell.ButtonSpec(MenuButtonPanel.BottomSlot, "GUI:MainMenu",
                MenuStrings.Get("GUI:MainMenu", "Main Menu")),
        };
        _shell.SetButtons(specs, OnMenuButtonClicked);
        _shell.RevealButtons();
    }

    private void ClearMovieList()
    {
        if (_movieList != null)
        {
            Destroy(_movieList.gameObject);
            _movieList = null;
        }
        if (_movieListCanvas != null)
        {
            Destroy(_movieListCanvas.gameObject);
            _movieListCanvas = null;
        }
        _movieHint = null;
    }

    /// <summary>「播放影片」：播当前选中的那条；没配影片源就只打日志，不瞎猜。</summary>
    private void PlaySelectedMovie()
    {
        if (_movieList == null) return;
        var item = _movieList.SelectedItem;
        if (item == null)
        {
            Debug.Log("[影片] 还没有选中列表里的影片");
            return;
        }

        var movie = (MovieItem)item;
        // 命名约定应该总能命中；真失配就报出来，不让界面无声无息。
        if (string.IsNullOrEmpty(movie.Clip))
        {
            Debug.LogWarning("[影片] " + movie.Id + " 没有解析到影片文件；下面是可选条目：");
            BikCatalog.LogCatalog(BikCatalog.Scan());
            ShowMovieHint(movie.Id + " 找不到对应影片文件");
            return;
        }

        var player = FullscreenVideoPlayer.GetOrCreate(_world);
        player.Completed -= OnFullscreenMovieCompleted;
        player.Completed += OnFullscreenMovieCompleted;
        // 先停掉内容区背景影片：它的音轨在 Bink 子进程里直接出声，不停就会和正片一起响
        _shell.StopContentMovie();
        _shell.SetButtons(new List<MenuShell.ButtonSpec>().ToArray(), OnMenuButtonClicked);
        player.PlayQueue(new[] { new BikClip(movie.Clip, movie.Title) });
    }

    /// <summary>列表页底部提示（红字），不弹窗、不打断。</summary>
    private void ShowMovieHint(string text)
    {
        if (_movieListCanvas == null) return;
        if (_movieHint != null) Destroy(_movieHint.gameObject);
        _movieHint = MenuSceneObjects.AddCenteredTextAt(_movieListCanvas.transform, text,
            118, 470, 397, 22, 13, new Color(226, 40, 32));
    }

    private void ApplyOptions()
    {
        if (_resolutions.Count > 0)
        {
            var size = _resolutions[Mathf.Clamp(_resolutionIndex, 0, _resolutions.Count - 1)];
            Screen.SetResolution(size.x, size.y, false);
            _appliedResolution = size;
            _appliedFps = FrameRateChoices[Mathf.Clamp(_fpsIndex, 0, FrameRateChoices.Length - 1)];
            _appliedSpeed = Mathf.Clamp(_speedIndex, 0, GameSpeedKeys.Length - 1);
        }

        if (FrameRateChoices.Length > 0)
        {
            Application.targetFrameRate = FrameRateChoices[Mathf.Clamp(_fpsIndex, 0, FrameRateChoices.Length - 1)];
        }

        var audio = AudioManager.Instance;
        audio.SetChannelVolume(AudioManager.Channel.Music, _musicVolume / 10f);
        audio.SetChannelVolume(AudioManager.Channel.Sfx, _soundVolume / 10f);
        audio.SetChannelVolume(AudioManager.Channel.Ui, _soundVolume / 10f);
        audio.SetChannelVolume(AudioManager.Channel.Voice, _voiceVolume / 10f);
        _appliedMusic = _musicVolume;
        _appliedSound = _soundVolume;
        _appliedVoice = _voiceVolume;

        Debug.Log($"[菜单] 套用选项：{CurrentResolutionLabel()} / {CurrentFrameRateLabel()} / {CurrentSpeedLabel()}");
    }

    private void OnMenuButtonClicked(int index, string key, string label)
    {
        // 点击即有音效（rules(md).ini: GUIMainButtonSound → sound(md).ini [MenuClick]）。
        if (_sounds != null) _sounds.PlayClick();

        switch (key)
        {
            case "GUI:SinglePlayer":
                Navigate(ShowSinglePlayer);
                break;
            case "GUI:NewCampaign":
                Debug.Log("[菜单] 新戰役：战役页待拼（按钮已接）");
                break;
            case "GUI:Skirmish":
                Navigate(ShowSkirmish);
                break;
            case "GUI:StartGame":
            case "GUI:ChooseMap":
            case "GUI:UseMap":
            case "GUI:Cancel":
                if (_page == MenuPage.Skirmish && _skirmishPage != null)
                {
                    _skirmishPage.HandleButton(key);
                }
                break;
            case "GUI:LoadSavedGame":
                Debug.Log("[菜单] 載入儲存遊戲：存档列表待接");
                break;
            case "GUI:MoviesAndCredits":
                Navigate(ShowMovies);
                break;
            case "GUI:SneakPeeks":
                // 先睹为快 = 先播 LOGO，再播正片；两段走同一个全屏队列播放入口。
                PlaySneakPeek();
                break;
            case "GUI:PlayMovie":
                PlaySelectedMovie();
                break;
            case "GUI:PlayMovies":
                ShowMovieList();
                break;
            case "GUI:ViewCredits":
                Debug.Log("[菜单] 檢視製作人員：制作人员页待接");
                break;
            case "GUI:Options":
                Navigate(ShowOptions);
                break;
            case "GUI:ExitGame":
                QuitGame();
                break;
            case "GUI:Keyboard":
                Navigate(ShowKeyboardOptions);
                break;
            case "GUI:Network":
                Debug.Log("[菜单] 網路页待拼（按钮已接）");
                break;
            case "GUI:GameSpeed":
                _speedIndex = (_speedIndex + 1) % GameSpeedKeys.Length;
                break;
            case "GUI:MainMenu":
                // 原版选项页是即时生效的：拖动推子当场就改音量并写盘，离开页面不回滚。
                SaveOptions();
                Navigate(() => ShowHome(false));
                break;
            case "GUI:Back":
                // 键盘页从选项页进来；遭遇战大厅从单人游戏页进来；其余页回主菜单。
                if (_page == MenuPage.Keyboard) Navigate(ShowOptions);
                else if (_page == MenuPage.Skirmish)
                {
                    // 离开遭遇战页面：GameMode 回到 Campaign（对照 SessionClass::GameMode）
                    Ra2md.Core.GameStatics.GameMode = Ra2md.Core.GameModeKind.Campaign;
                    _shell.SetWarningVisible(true);
                    ClearSkirmishPage();
                    Navigate(ShowSinglePlayer);
                }
                else
                {
                    SaveOptions();
                    Navigate(() => ShowHome(false));
                }
                break;
            default:
                Debug.Log($"[菜单] 未处理按钮 {index} key={key} label={label}");
                break;
        }
    }

    /// <summary>
    /// 换页的标准动作：旧按钮逐槽扫出 → 换页/换按钮 → 扫入。
    /// 扫出期间面板不接受点击，避免连点造成两次换页叠在一起。
    /// </summary>
    private void Navigate(Action apply)
    {
        if (_shell == null)
        {
            if (apply != null) apply();
            return;
        }

        if (!_shell.ButtonsIdle) return;

        if (_sounds != null) _sounds.PlaySlideOut();
        _shell.SlideOutThen(() =>
        {
            if (apply != null) apply();
            _shell.RevealButtons();
            if (_sounds != null) _sounds.PlaySlideIn();
        });
    }

    /// <summary>离开选项页时把当前值写盘（原版就是这么即时生效的，没有「取消」语义）。</summary>
    private void SaveOptions()
    {
        PlayerPrefs.SetInt("ra2md.opt.width", _resolutions.Count > 0 ? _resolutions[Mathf.Clamp(_resolutionIndex, 0, _resolutions.Count - 1)].x : Screen.width);
        PlayerPrefs.SetInt("ra2md.opt.height", _resolutions.Count > 0 ? _resolutions[Mathf.Clamp(_resolutionIndex, 0, _resolutions.Count - 1)].y : Screen.height);
        PlayerPrefs.SetInt("ra2md.opt.fps", FrameRateChoices[Mathf.Clamp(_fpsIndex, 0, FrameRateChoices.Length - 1)]);
        PlayerPrefs.SetInt("ra2md.opt.speed", Mathf.Clamp(_speedIndex, 0, GameSpeedKeys.Length - 1));
        PlayerPrefs.Save();

        // 音频三个推子在拖动时已经写进 AudioManager（各自落 PlayerPrefs），这里只补一次对齐。
        var audio = AudioManager.Instance;
        audio.SetChannelVolume(AudioManager.Channel.Music, _musicVolume / 10f);
        audio.SetChannelVolume(AudioManager.Channel.Sfx, _soundVolume / 10f);
        audio.SetChannelVolume(AudioManager.Channel.Ui, _soundVolume / 10f);
        audio.SetChannelVolume(AudioManager.Channel.Voice, _voiceVolume / 10f);
        _appliedMusic = _musicVolume;
        _appliedSound = _soundVolume;
        _appliedVoice = _voiceVolume;
    }

    // ---------------------------------------------------------------- 页文本

    private void BuildPageLabels()
    {
        var go = new GameObject("PageLabels", typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(_labelCanvas.transform, false);
        rect.pivot = new Vector2(0f, 0f);
        rect.localPosition = Vector3.zero;
        rect.sizeDelta = new Vector2(MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight);
        _pageLabels = rect;

        // 页标题：侧栏顶 657..775、y 3..35（对照 RuntimeMainMenu.cpp:1445-1449）。
        _pageTitle = AddPageLabel("Title", string.Empty, 657, 3, 118, 32,
            TextAnchor.MiddleCenter, 14, new Color32(255, 222, 25, 255));
    }

    private void ClearPageLabels()
    {
        // 标题常驻，其余行销毁。
        if (_pageLabels == null) return;
        for (var i = _pageLabels.childCount - 1; i >= 0; i--)
        {
            var child = _pageLabels.GetChild(i);
            if (child == _pageTitle.transform) continue;
            Destroy(child.gameObject);
        }
        SetPageTitle(null, null);
    }

    /// <summary>页标题：侧栏顶 657..775、y 3..35（对照 RuntimeMainMenu.cpp:1445-1449）。</summary>
    private void SetPageTitle(string key, string fallback)
    {
        if (_pageTitle == null) return;
        _pageTitle.text = string.IsNullOrEmpty(key) ? string.Empty : MenuStrings.Get(key, fallback);
    }

    private string CurrentResolutionLabel()
    {
        if (_resolutions.Count == 0) return "-";
        var size = _resolutions[Mathf.Clamp(_resolutionIndex, 0, _resolutions.Count - 1)];
        return size.x + " x " + size.y;
    }

    private string CurrentFrameRateLabel()
    {
        if (FrameRateChoices.Length == 0) return "-";
        return FrameRateChoices[Mathf.Clamp(_fpsIndex, 0, FrameRateChoices.Length - 1)] + " FPS";
    }

    private string CurrentSpeedLabel()
    {
        var index = Mathf.Clamp(_speedIndex, 0, GameSpeedKeys.Length - 1);
        return MenuStrings.Get(GameSpeedKeys[index], GameSpeedFallbacks[index]);
    }

    /// <summary>在 800×600 像素矩形（左上原点）里放一段文字。</summary>
    private Text AddPageLabel(string name, string text, int x, int yTop, int width, int height,
        TextAnchor anchor, int fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(_pageLabels, false);
        // 画布原点在 800×600 平面的左下角；页内全是「左上原点、y 向下」的像素坐标，
        // 所以锚点取左下、pivot 取左上，再按 (x, 600 - yTop) 摆放，不能拿平面绝对坐标
        // 去填 anchoredPosition（那会以画布中心为基准，把「主選單」甩到界面外面）。
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, MenuSceneObjects.PlaneHeight - yTop);

        var label = go.AddComponent<Text>();
        label.text = text;
        label.font = MenuFonts.Chinese();
        label.fontSize = fontSize;
        label.alignment = anchor;
        label.color = color;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    private static Texture2D BuildBackdropTexture()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var pixels = new Color32[4];
        for (var i = 0; i < 4; i++) pixels[i] = new Color32(10, 10, 12, 255);
        texture.SetPixels32(pixels);
        texture.Apply(false);
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private IEnumerator PlayMenuMusic()
    {
        var movie = _shell != null ? _shell.ContentMovie : null;

        var waited = 0f;
        while (movie != null && !movie.HasAudio && movie.DecodedFrames == 0 && !movie.Failed && waited < 2f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (movie.HasAudio)
        {
            Debug.Log($"[音频] 背景音乐走 bik 内嵌音轨（{movie.AudioTracks} 轨），不再加载 THEME.INI");
            yield break;
        }

        var path = Path.Combine(Application.streamingAssetsPath, MenuResourceLibrary.MenuBgmPath);
        var clip = WavClipLoader.LoadFile(path, "菜单主题曲");
        if (clip == null)
        {
            Debug.LogWarning("[音频] 读不到背景音乐 " + path);
            yield break;
        }

        AudioManager.Instance.PlayMusic(clip, true);
        Debug.Log($"[音频] 背景音乐交给 AudioManager（{clip.length:F1}s，bik 无音轨，回退 THEME.INI）");
    }

    // ---------------------------------------------------------------- 交互

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

}

