// CommandClass.cs —— 命令基类，对齐原版 YRPP 的 CommandClass 体系。
//
// 原版每个热键都是 GameCreate<XXXCommandClass>() 造出来的一具命令对象，
// 构造后自动注册进 InputManager，由键盘消息回调触发 Execute()。
// 这里保留同样的形状：GameCreate<T>() → new T() → InputManager 登记；
// 每个命令只负责「按下去干什么」，按键分配与冲突处理全在 InputManager。
using System;
using UnityEngine;
using Ra2md.Menu;

namespace Ra2md.Input
{
    [Flags]
    public enum HotkeyMods
    {
        None = 0,
        Shift = 1,
        Ctrl = 2,
        Alt = 4,
    }

    /// <summary>
    /// 一个绑定：高字节是修饰键掩码、低字节是 Win32 虚拟键码（KeyboardMD.ini 就是这个打包格式）。
    /// 例：561 = 0x0231 = Ctrl + '1'；1073 = 0x0431 = Alt + '1'；339 = 0x0153 = Shift + 'S'。
    /// </summary>
    [Serializable]
    public struct Hotkey : IEquatable<Hotkey>
    {
        public HotkeyMods Mods;
        public int Vk;

        public bool IsBound { get { return Vk != 0; } }

        /// <summary>打包成 KeyboardMD.ini 里的那个十进制整数。</summary>
        public int Raw { get { return (((int)Mods & 0xFF) << 8) | (Vk & 0xFF); } }

        public static Hotkey FromRaw(int raw)
        {
            return new Hotkey { Mods = (HotkeyMods)((raw >> 8) & 0xFF), Vk = raw & 0xFF };
        }

        public static readonly Hotkey None = new Hotkey();

        public string Label { get { return InputManager.LabelOf(this); } }

        public bool Equals(Hotkey other) { return Mods == other.Mods && Vk == other.Vk; }
        public override bool Equals(object obj) { return obj is Hotkey && Equals((Hotkey)obj); }
        public override int GetHashCode() { return Raw; }
        public override string ToString() { return Label; }
    }

    /// <summary>Execute 时能拿到的全部上下文（原版是全局单例 + 光标对象，这里显式传进去，便于以后接玩法）。</summary>
    public struct CommandContext
    {
        public Vector2 Cursor;       // 光标在战场平面上的世界坐标
        public object Target;        // 光标下的对象（还没玩法时恒为 null）
        public bool HasTarget;
        public object[] Selection;   // 当前选中集合
    }

    /// <summary>
    /// 命令基类。派生类只声明 Id / 名称 / 默认键，并实现 Execute。
    /// 注册走 CommandClass.GameCreate&lt;T&gt;()，与原版 GameCreate<AttackCommandClass>() 同一入口。
    /// </summary>
    public abstract class CommandClass
    {
        /// <summary>KeyboardMD.ini 的段名，同时也是存档里的键。</summary>
        public string Id { get; protected set; }

        /// <summary>CSF 里显示名的 key（取不到就退回 FallbackName）。</summary>
        public string NameKey { get; protected set; }

        public string FallbackName { get; protected set; }

        /// <summary>ini 缺省时用的默认绑定。</summary>
        public Hotkey Default { get; protected set; }

        /// <summary>当前绑定；Vk==0 表示未绑定。</summary>
        public Hotkey Bound { get; set; }

        /// <summary>界面上显示的名字（CSF 优先）。</summary>
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(NameKey))
                {
                    var s = MenuStrings.Get(NameKey, FallbackName);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                return FallbackName;
            }
        }

        /// <summary>当前环境能不能执行（暂停、非战场、正在重绑时都返回 false）。</summary>
        public virtual bool CanExecute(CommandContext ctx) { return true; }

        public abstract void Execute(CommandContext ctx);

        /// <summary>
        /// 原版入口：GameCreate&lt;AttackCommandClass&gt;();
        /// 造对象 + 立刻注册进 InputManager。
        /// </summary>
        public static T GameCreate<T>() where T : CommandClass, new()
        {
            return InputManager.GameCreate<T>();
        }
    }
}
