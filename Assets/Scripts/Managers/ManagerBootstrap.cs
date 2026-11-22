// ManagerBootstrap.cs —— 管理器层的统一启动入口：GameManager 与菜单控制器都从这里接管。
//
// 架构口径（用户要求）：
//   · 场景里只挂 GameManager；GameManager 起 MenuManager，MenuManager 底下挂 UIManager；
//   · SHPManager / VXLManager / MapManager / TriggerManager / ModelManager /
//     GlobalConfigManager / GlobalDataManager 各管一摊，互不越界；
//   · 这里只负责"把单例建起来 + 分辨率门槛检查"，不替代各管理器自己的职责。
using UnityEngine;

namespace Ra2md.Managers
{
    public static class ManagerBootstrap
    {
        private static bool _booted;

        /// <summary>
        /// 确保管理器全部就位。返回 false = 分辨率小于 800×600，已挂黑屏提示，调用方应停住流程。
        /// </summary>
        public static bool Ensure()
        {
            if (_booted) return CommonManager.IsResolutionOk;
            _booted = true;

            CommonManager.Ensure();
            MenuManager.Ensure();
            UIManager.Ensure();
            ModelManager.Ensure();
            LuaManager.Ensure();
            LogicManager.Ensure();

            // 纯 C# 门面管理器：实例化一次即把默认配置/缓存口径挂上。
            _ = SHPManager.Instance;
            _ = VXLManager.Instance;
            _ = MapManager.Instance;
            _ = TriggerManager.Instance;
            _ = GlobalConfigManager.Instance;
            _ = GlobalDataManager.Instance;

            if (!CommonManager.IsResolutionOk)
            {
                UIManager.Instance.ShowResolutionTooSmall(Screen.width, Screen.height);
                return false;
            }
            return true;
        }
    }
}
