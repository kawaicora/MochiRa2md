// MenuManager.cs —— 菜单管理器：承载菜单阶段的生命周期与页面路由，底下挂 UIManager。
//
// 职责边界：
//   · 菜单只管"菜单怎么摆、点哪个页"；参数（分辨率/音量/难度…）一律读 GlobalConfigManager，
//     菜单类里不存设置、不直接读 PlayerPrefs；
//   · 控件构造走 UIManager；本类不自己 new Canvas / Text；
//   · 进局之后菜单卸载，战场 UI（GameHud）由世界层自己管，不归这里。
//
// 迁移口径：现有 MainMenuController 暂时继续驱动菜单；后续页面注册/切换逐步迁到这里，
// 迁一个页面收一个页面，不在一次改动里整体替换。
using UnityEngine;

namespace Ra2md.Managers
{
    public sealed class MenuManager : MonoBehaviour
    {
        private static MenuManager _instance;

        public static MenuManager Instance
        {
            get
            {
                if (_instance == null) Ensure();
                return _instance;
            }
        }

        public UIManager UI { get; private set; }

        public static MenuManager Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Ra2md.MenuManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MenuManager>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            UI = UIManager.Ensure();
        }

        // 页面路由留位：RegisterPage / ShowPage(pageId) 在旧控制器迁移时补。
    }
}
