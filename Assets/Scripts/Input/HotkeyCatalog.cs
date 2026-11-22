// HotkeyCatalog.cs —— 默认绑定表与显示名（对应原版 KeyboardMD.ini 的 [Hotkey] 段）。
//
// 取值格式：高字节修饰键掩码（Shift=1 / Ctrl=2 / Alt=4），低字节 Win32 VK。原版文件里就是十进制整数。
// 读盘顺序：StreamingAssets 根目录 keyboardmd.ini → MIX 内同名文件 → 内置表（本文件这份，摘自原版文件）。
using System;
using System.Collections.Generic;
using System.Text;
using Ra2md.Menu;

namespace Ra2md.Input
{
    public static class HotkeyCatalog
    {
        /// <summary>一条默认绑定：段名 / CSF 键 / 兜底名 / 默认打包值。</summary>
        private struct Entry
        {
            public string Id;
            public string NameKey;
            public string Fallback;
            public int Raw;
        }

        /// <summary>原版 KeyboardMD.ini [Hotkey] 全量，顺序保持原文件顺序。</summary>
        private static readonly Entry[] BuiltIn =
        {
            New("CenterView", "TXT_CENTER_VIEW", "觀點置中", 12),
            New("Options", "TXT_OPTIONS", "選項", 27),
            New("CenterOnRadarEvent", "TXT_RADAR_EVENT", "雷達通報事件", 32),
            New("TeamSelect_10", "TXT_SELECT_TEAM", "選擇 10 隊", 48),
            New("TeamSelect_1", "TXT_SELECT_TEAM", "選擇 1 隊", 49),
            New("TeamSelect_2", "TXT_SELECT_TEAM", "選擇 2 隊", 50),
            New("TeamSelect_3", "TXT_SELECT_TEAM", "選擇 3 隊", 51),
            New("TeamSelect_4", "TXT_SELECT_TEAM", "選擇 4 隊", 52),
            New("TeamSelect_5", "TXT_SELECT_TEAM", "選擇 5 隊", 53),
            New("TeamSelect_6", "TXT_SELECT_TEAM", "選擇 6 隊", 54),
            New("TeamSelect_7", "TXT_SELECT_TEAM", "選擇 7 隊", 55),
            New("TeamSelect_8", "TXT_SELECT_TEAM", "選擇 8 隊", 56),
            New("TeamSelect_9", "TXT_SELECT_TEAM", "選擇 9 隊", 57),
            New("ToggleAlliance", null, "結盟", 65),
            New("PlaceBeacon", "Tip:Beacon", "信標", 66),
            New("AllToCheer", null, "全體歡呼", 67),
            New("DeployObject", "TXT_DEPLOY_OBJECT", "部署物件", 68),
            New("InfantryTab", "TXT_INFANTRY_TAB", "步兵頁面", 69),
            New("Follow", "TXT_FOLLOW", "跟隨", 70),
            New("GuardObject", "TXT_GUARD", "警戒", 71),
            New("CenterBase", "TXT_CENTER_BASE", "基地置中", 72),
            New("ToggleRepair", "TXT_REPAIR_MODE", "修護模式", 75),
            New("ToggleSell", "TXT_SELL_MODE", "變賣模式", 76),
            New("PreviousObject", "TXT_PREVIOUS_OBJECT", "上一個物件", 77),
            New("NextObject", "TXT_NEXT_OBJECT", "下一個物件", 78),
            New("CombatantSelect", "TXT_COMBATANT_SELECT", "選擇作戰單位", 80),
            New("StructureTab", "TXT_STRUCTURE_TAB", "建築物頁面", 81),
            New("UnitTab", "TXT_UNIT_TAB", "車輛頁面", 82),
            New("StopObject", "TXT_STOP_OBJECT", "物件停止", 83),
            New("TypeSelect", "TXT_TYPE_SELECT", "選擇同型", 84),
            New("PageUser", "TXT_PAGEUSER", "呼叫玩家", 85),
            New("DefenseTab", "TXT_DEFENSE_TAB", "防禦頁面", 87),
            New("ScatterObject", "TXT_SCATTER", "分散", 88),
            New("VeterancyNav", "TXT_VETERANCY", "老兵升級", 89),
            New("PlanningMode", "TXT_WAYPOINTMODE", "路徑點模式", 90),
            New("SidebarDown", "TXT_SIDEBAR_DOWN", "捲軸列向下", 98),
            New("SidebarUp", "TXT_SIDEBAR_UP", "捲軸列向上", 104),
            New("Delete", "TXT_DEL_WAYPOINT", "刪除路徑點", 110),
            New("View1", "TXT_VIEW_BOOKMARK1", "查看書籤 1", 112),
            New("GenUnit", null, "生成單位（除錯）", 113),
            New("ActiveAllSuperWeapon", null, "啟動所有超級武器", 114),
            New("Test", null, "測試（除錯）", 115),
            New("LaunchSuperWeaponA", null, "發射超級武器 A", 116),
            New("LaunchSuperWeaponB", null, "發射超級武器 B", 117),
            New("LaunchSuperWeaponC", null, "發射超級武器 C", 118),
            New("LaunchSuperWeaponD", null, "發射超級武器 D", 119),
            New("InstantConstruction", null, "瞬間建造（除錯）", 120),
            New("AutoChargePlayerAllSuperweapon", null, "超級武器全充能", 121),
            New("GiveMoney", null, "給錢（除錯）", 122),
            New("AIControl", null, "AI 控制（除錯）", 123),
            New("TeamAddSelect_10", "TXT_ADD_SELECT_TEAM", "新增選擇 10 隊", 304),
            New("TeamAddSelect_1", "TXT_ADD_SELECT_TEAM", "新增選擇 1 隊", 305),
            New("TeamAddSelect_2", "TXT_ADD_SELECT_TEAM", "新增選擇 2 隊", 306),
            New("TeamAddSelect_3", "TXT_ADD_SELECT_TEAM", "新增選擇 3 隊", 307),
            New("TeamAddSelect_4", "TXT_ADD_SELECT_TEAM", "新增選擇 4 隊", 308),
            New("TeamAddSelect_5", "TXT_ADD_SELECT_TEAM", "新增選擇 5 隊", 309),
            New("TeamAddSelect_6", "TXT_ADD_SELECT_TEAM", "新增選擇 6 隊", 310),
            New("TeamAddSelect_7", "TXT_ADD_SELECT_TEAM", "新增選擇 7 隊", 311),
            New("TeamAddSelect_8", "TXT_ADD_SELECT_TEAM", "新增選擇 8 隊", 312),
            New("TeamAddSelect_9", "TXT_ADD_SELECT_TEAM", "新增選擇 9 隊", 313),
            New("ScreenCapture", null, "螢幕擷取", 339),
            New("TeamCreate_10", "TXT_CREATE_TEAM", "創造 10 隊", 560),
            New("TeamCreate_1", "TXT_CREATE_TEAM", "創造 1 隊", 561),
            New("TeamCreate_2", "TXT_CREATE_TEAM", "創造 2 隊", 562),
            New("TeamCreate_3", "TXT_CREATE_TEAM", "創造 3 隊", 563),
            New("TeamCreate_4", "TXT_CREATE_TEAM", "創造 4 隊", 564),
            New("TeamCreate_5", "TXT_CREATE_TEAM", "創造 5 隊", 565),
            New("TeamCreate_6", "TXT_CREATE_TEAM", "創造 6 隊", 566),
            New("TeamCreate_7", "TXT_CREATE_TEAM", "創造 7 隊", 567),
            New("TeamCreate_8", "TXT_CREATE_TEAM", "創造 8 隊", 568),
            New("TeamCreate_9", "TXT_CREATE_TEAM", "創造 9 隊", 569),
            New("SetView1", "TXT_SET_VIEW_BOOKMARK1", "設定書籤 1", 624),
            New("SetView2", "TXT_SET_VIEW_BOOKMARK2", "設定書籤 2", 625),
            New("SetView3", "TXT_SET_VIEW_BOOKMARK3", "設定書籤 3", 626),
            New("SetView4", "TXT_SET_VIEW_BOOKMARK4", "設定書籤 4", 627),
            New("DisplayUnitInfo", null, "顯示單位資訊（除錯）", 880),
            New("DrawHouseInfo", null, "顯示陣營資訊（除錯）", 881),
            New("TeamCenter_10", "TXT_CENTER_TEAM", "將 10 隊置中", 1072),
            New("TeamCenter_1", "TXT_CENTER_TEAM", "將 1 隊置中", 1073),
            New("TeamCenter_2", "TXT_CENTER_TEAM", "將 2 隊置中", 1074),
            New("TeamCenter_3", "TXT_CENTER_TEAM", "將 3 隊置中", 1075),
            New("TeamCenter_4", "TXT_CENTER_TEAM", "將 4 隊置中", 1076),
            New("TeamCenter_5", "TXT_CENTER_TEAM", "將 5 隊置中", 1077),
            New("TeamCenter_6", "TXT_CENTER_TEAM", "將 6 隊置中", 1078),
            New("TeamCenter_7", "TXT_CENTER_TEAM", "將 7 隊置中", 1079),
            New("TeamCenter_8", "TXT_CENTER_TEAM", "將 8 隊置中", 1080),
            New("TeamCenter_9", "TXT_CENTER_TEAM", "將 9 隊置中", 1081),
        };

        private static Entry New(string id, string nameKey, string fallback, int raw)
        {
            return new Entry { Id = id, NameKey = nameKey, Fallback = fallback, Raw = raw };
        }

        public static string Source { get; private set; } = "内置表";

        /// <summary>默认绑定（ini 优先，取不到就内置表）。</summary>
        public static Dictionary<string, int> ReadDefaults()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var vfs = MenuResourceLibrary.EnsureMounted();
            if (vfs != null)
            {
                foreach (var candidate in Ra2md.Managers.GlobalConfigManager.Resources.KeyboardIniCandidates)
                {
                    if (!vfs.TryResolve(candidate, out var bytes, out var origin) || bytes == null || bytes.Length == 0)
                        continue;

                    ParseInto(map, bytes);
                    if (map.Count > 0)
                    {
                        Source = candidate + "（" + origin + "）";
                        return map;
                    }
                }
            }

            foreach (var entry in BuiltIn) map[entry.Id] = entry.Raw;
            Source = "内置表";
            return map;
        }

        private static void ParseInto(Dictionary<string, int> map, byte[] bytes)
        {
            string text = Decode(bytes);
            bool inSection = false;

            foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';') continue;

                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inSection = line.TrimEnd(']').TrimStart('[').Trim()
                        .Equals("Hotkey", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string name = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1);
                int comment = value.IndexOf(';');
                if (comment >= 0) value = value.Substring(0, comment);
                value = value.Trim();

                if (int.TryParse(value, out var raw)) map[name] = raw;
            }
        }

        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>某个段名对应的 CSF 键与兜底名（没登记的返回 null / 段名本身）。</summary>
        public static void Describe(string id, out string nameKey, out string fallback)
        {
            foreach (var entry in BuiltIn)
            {
                if (!entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                nameKey = entry.NameKey;
                fallback = entry.Fallback;
                return;
            }
            nameKey = null;
            fallback = id;
        }

        public static int DefaultRaw(string id)
        {
            foreach (var entry in BuiltIn)
                if (entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return entry.Raw;
            return 0;
        }
    }
}
