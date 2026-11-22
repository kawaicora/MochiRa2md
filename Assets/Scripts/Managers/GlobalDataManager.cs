// GlobalDataManager.cs —— 全局数据管理器：对局运行时数据的统一访问门面。
//
// 职责边界：
//   · 这里只转发当前局的规则库 / 场景上下文 / 玩家阵营 / 对局实例；
//   · 不负责解析 INI（RulesDatabase 做）、不负责触发条件（TriggerEngine 做）、
//     不存用户设置（GlobalConfigManager 做）；
//   · 页面/世界层要取"当前是什么规则、谁是玩家"都从这里走，避免到处抓静态 Current。
using Ra2md.World;

namespace Ra2md.Managers
{
    public sealed class GlobalDataManager
    {
        private static GlobalDataManager _instance;

        public static GlobalDataManager Instance
        {
            get
            {
                if (_instance == null) _instance = new GlobalDataManager();
                return _instance;
            }
        }

        public RulesDatabase Rules => RulesDatabase.Current;
        public ArtDatabase Art => ArtDatabase.Current;
        public ScenarioContext Scenario => ScenarioContext.Current;
        public HouseClass PlayerHouse => HouseClass.CurrentPlayer;
        public SkirmishMatch Match => SkirmishMatch.Current;
    }
}
