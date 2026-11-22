// CommandClasses.cs —— 具体命令（对应 KeyboardMD.ini 每个段名一具 CommandClass）。
//
// 造法与原版一致：CommandClass.GameCreate<XXXCommandClass>()，
// 构造里声明 Id / CSF 名 / 默认键，Execute() 只写行为。
// 玩法层还没接进来，所以这里的 Execute 只做「记录 + 广播」，等模拟层起来再把函数体换掉即可。
using System;
using UnityEngine;
using Ra2md.Menu;

namespace Ra2md.Input
{
    /// <summary>有 Id 就有默认键：从 HotkeyCatalog 取名字与缺省绑定。</summary>
    public abstract class HotkeyCommandClass : CommandClass
    {
        protected HotkeyCommandClass(string id)
        {
            Id = id;
            HotkeyCatalog.Describe(id, out var nameKey, out var fallback);
            NameKey = nameKey;
            FallbackName = fallback;
            Default = Hotkey.FromRaw(HotkeyCatalog.DefaultRaw(id));
            Bound = Default;
        }

        protected void Notify(string detail = null)
        {
            Debug.Log("[命令] " + Id + (string.IsNullOrEmpty(detail) ? string.Empty : "：" + detail));
        }
    }

    /// <summary>攻击：把光标下的目标交给选中单位攻击。原版由 AttackCommandClass 处理，鼠标左键单击也会造一具。</summary>
    public sealed class AttackCommandClass : HotkeyCommandClass
    {
        public AttackCommandClass() : base("AttackCommand") { }

        public override bool CanExecute(CommandContext ctx) { return ctx.HasTarget; }

        public override void Execute(CommandContext ctx)
        {
            Notify(ctx.HasTarget ? "目标 " + ctx.Target : "无目标");
        }
    }

    public sealed class GuardCommandClass : HotkeyCommandClass
    {
        public GuardCommandClass() : base("GuardObject") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class DeployCommandClass : HotkeyCommandClass
    {
        public DeployCommandClass() : base("DeployObject") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class ScatterCommandClass : HotkeyCommandClass
    {
        public ScatterCommandClass() : base("ScatterObject") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class StopCommandClass : HotkeyCommandClass
    {
        public StopCommandClass() : base("StopObject") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class RepairModeCommandClass : HotkeyCommandClass
    {
        public RepairModeCommandClass() : base("ToggleRepair") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class SellModeCommandClass : HotkeyCommandClass
    {
        public SellModeCommandClass() : base("ToggleSell") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class BeaconCommandClass : HotkeyCommandClass
    {
        public BeaconCommandClass() : base("PlaceBeacon") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class FollowCommandClass : HotkeyCommandClass
    {
        public FollowCommandClass() : base("Follow") { }
        public override bool CanExecute(CommandContext ctx) { return ctx.HasTarget; }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class PlanningModeCommandClass : HotkeyCommandClass
    {
        public PlanningModeCommandClass() : base("PlanningMode") { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    /// <summary>中心视图 / 基地中心 / 雷达事件：三个都是「摄像机跳转」，先共用同一具。</summary>
    public sealed class CameraCommandClass : HotkeyCommandClass
    {
        public CameraCommandClass(string id) : base(id) { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class SidebarScrollCommandClass : HotkeyCommandClass
    {
        public SidebarScrollCommandClass(string id) : base(id) { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class SidebarTabCommandClass : HotkeyCommandClass
    {
        public SidebarTabCommandClass(string id) : base(id) { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    public sealed class ObjectCycleCommandClass : HotkeyCommandClass
    {
        public ObjectCycleCommandClass(string id) : base(id) { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    /// <summary>书签：View1-4 = 跳过去，SetView1-4 = 存当前位置。</summary>
    public sealed class BookmarkCommandClass : HotkeyCommandClass
    {
        public int Slot { get; private set; }
        public bool Store { get; private set; }

        public BookmarkCommandClass(string id, int slot, bool store) : base(id)
        {
            Slot = slot;
            Store = store;
        }

        public override void Execute(CommandContext ctx) { Notify((Store ? "存" : "跳") + "书签 " + Slot); }
    }

    /// <summary>队伍命令：选择 / 加入选择 / 创造 / 置中，编号 1-10。</summary>
    public sealed class TeamCommandClass : HotkeyCommandClass
    {
        public enum TeamMode { Select, AddSelect, Create, Center }

        public TeamMode Mode { get; private set; }
        public int Number { get; private set; }

        public TeamCommandClass(TeamMode mode, int number) : base(IdFor(mode, number))
        {
            Mode = mode;
            Number = number;
        }

        public static string IdFor(TeamMode mode, int number)
        {
            switch (mode)
            {
                case TeamMode.AddSelect: return "TeamAddSelect_" + number;
                case TeamMode.Create: return "TeamCreate_" + number;
                case TeamMode.Center: return "TeamCenter_" + number;
                default: return "TeamSelect_" + number;
            }
        }

        public override void Execute(CommandContext ctx) { Notify("队伍 " + Number); }
    }

    /// <summary>未单独实现的段名统一挂在这上面（作弊/除错类），保证键盘页里每条都能改键。</summary>
    public sealed class GenericHotkeyCommandClass : HotkeyCommandClass
    {
        public GenericHotkeyCommandClass(string id) : base(id) { }
        public override void Execute(CommandContext ctx) { Notify(); }
    }

    /// <summary>把 ini 里全部热键都造出来；已单独实现的用专用类，剩下的走 Generic。</summary>
    public static class CommandRegistry
    {
        private static bool _installed;

        public static void InstallAll()
        {
            if (_installed) return;
            _installed = true;

            var manager = InputManager.Instance;

            CommandClass.GameCreate<AttackCommandClass>();
            CommandClass.GameCreate<GuardCommandClass>();
            CommandClass.GameCreate<DeployCommandClass>();
            CommandClass.GameCreate<ScatterCommandClass>();
            CommandClass.GameCreate<StopCommandClass>();
            CommandClass.GameCreate<RepairModeCommandClass>();
            CommandClass.GameCreate<SellModeCommandClass>();
            CommandClass.GameCreate<BeaconCommandClass>();
            CommandClass.GameCreate<FollowCommandClass>();
            CommandClass.GameCreate<PlanningModeCommandClass>();

            foreach (var id in new[] { "CenterView", "CenterBase", "CenterOnRadarEvent" })
                manager.Register(new CameraCommandClass(id));
            foreach (var id in new[] { "SidebarUp", "SidebarDown" })
                manager.Register(new SidebarScrollCommandClass(id));
            foreach (var id in new[] { "InfantryTab", "UnitTab", "StructureTab", "DefenseTab" })
                manager.Register(new SidebarTabCommandClass(id));
            foreach (var id in new[] { "PreviousObject", "NextObject", "TypeSelect", "CombatantSelect" })
                manager.Register(new ObjectCycleCommandClass(id));

            for (int i = 1; i <= 4; i++)
            {
                manager.Register(new BookmarkCommandClass("View" + i, i, false));
                manager.Register(new BookmarkCommandClass("SetView" + i, i, true));
            }

            foreach (var mode in new[]
                     {
                         TeamCommandClass.TeamMode.Select, TeamCommandClass.TeamMode.AddSelect,
                         TeamCommandClass.TeamMode.Create, TeamCommandClass.TeamMode.Center,
                     })
            {
                for (int i = 1; i <= 10; i++)
                    manager.Register(new TeamCommandClass(mode, i));
            }

            // 剩下的段名（作弊键、除错键等）一律生成 Generic，键盘页里不出现空洞
            var defaults = HotkeyCatalog.ReadDefaults();
            foreach (var id in defaults.Keys)
            {
                if (manager.Find(id) != null) continue;
                manager.Register(new GenericHotkeyCommandClass(id));
            }

            Debug.Log("[输入] 命令装配完成：" + manager.Commands.Count + " 条");
        }
    }
}
