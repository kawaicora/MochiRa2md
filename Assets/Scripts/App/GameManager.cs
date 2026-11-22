// GameManager.cs —— 场景根对象（唯一需要挂到场景上的 MonoBehaviour）。
// 职责：持有会话循环、资源工厂、音频服务、网络会话；Update 驱动一帧。
// 挂法：场景里新建空物体命名 GameManager，挂本组件；或不带场景物体时由
// [RuntimeInitializeOnLoadMethod] 自动生成。
using Ra2md.Boot;
using Ra2md.Core;
using Ra2md.Gfx;
using Ra2md.Managers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ra2md.App
{
    public sealed class GameManager : MonoBehaviour
    {
        /// <summary>主菜单场景由 MainMenuController 独立驱动，不跑 WinMain 复刻。</summary>
        public const string MenuSceneName = "MainMenu";

        public static GameManager Instance { get; private set; }

        private GameSession _session;
        private bool _bootFailed;
        private bool _bootOk = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (scene != null && scene.name == MenuSceneName)
            {
                return;
            }

            var go = new GameObject(nameof(GameManager));
            go.AddComponent<GameManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (BootLog.Path == null)
            {
                BootLog.Open(Application.persistentDataPath);
            }

            // 管理器层统一启动（含 800×600 分辨率守卫；过小会挂黑屏提示并返回 false）。
            _bootOk = ManagerBootstrap.Ensure();

            Application.targetFrameRate = -1;   // 不限帧，节流由 FrameThrottle 负责
            QualitySettings.vSyncCount = 0;
        }

        private void Start()
        {
            if (!_bootOk)
            {
                BootLog.Log("[boot] 分辨率过小，会话未启动");
                return;
            }

            // 生命周期交给 LogicManager 阶段机：Boot → Menu（菜单控制器由阶段机补挂）→ Loading/Session/Game。
            // 原版 WinMain 复刻里的会话循环只在对局阶段驱动，菜单阶段不再空跑。
            LogicManager.Instance.Boot();
        }

        private void Update()
        {
            if (!_bootOk || _bootFailed) return;

            var phase = LogicManager.Instance.Phase;
            if (phase != LogicPhase.Session && phase != LogicPhase.Game)
            {
                return;
            }

            // 进入对局阶段才起原版启动链与会话循环；菜单/加载阶段不碰它。
            if (_session == null)
            {
                bool ok = WinMainReplica.Run(string.Empty);
                if (!ok)
                {
                    _bootFailed = true;
                    BootLog.Log("[boot] sequence failed, idle in scene");
                    return;
                }
                _session = WinMainReplica.Session;
            }

            if (_session == null || !_session.Active)
            {
                return;
            }

            // 一 Unity 帧 = 一次 sub_55D360
            _session.StepFrame();

            // 最终上屏（中间缓冲 → Texture2D/Mesh 由资源层负责）
            FramePresenter.Instance.Present();
            FrameThrottle.TickStatistics(GameStatics.FrameBudgetMs);
        }

        private void OnApplicationQuit()
        {
            BootLog.Log("[boot] quit (OnApplicationQuit)");
            BootLog.Close();
        }
    }
}
