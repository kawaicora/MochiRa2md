// InputManager.cs —— 输入总管（对齐原版 InputManager::Instance）。
//
// 职责：
//   1) 持有全部 CommandClass（GameCreate<T>() 造出来就登记到这里）
//   2) 绑定表：KeyboardMD.ini 的 [Hotkey] 段 → 每个命令的默认键；
//      persistentDataPath/KeyboardMD.ini 里的同段 → 玩家改过的键（优先级更高）
//   3) 每帧把按下的键翻译成命令并派发 Execute()
//   4) 重绑模式：BeginRebind() 后吃掉下一次按键，Esc 取消
//
// VFS 顺序不变：先在 StreamingAssets 根目录找 keyboardmd.ini，找不到再去 MIX 里找；
// 玩家改的键不回写 StreamingAssets，另存到 persistentDataPath/KeyboardMD.ini（原版也是写在自己目录下的同名文件）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Ra2md.Assets;

namespace Ra2md.Input
{
    public sealed class InputManager : MonoBehaviour
    {
        private static InputManager _instance;

        /// <summary>单例（第一次访问时自建 GameObject，跨场景保留）。</summary>
        public static InputManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("InputManager");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<InputManager>();
                return _instance;
            }
        }

        public const string SaveFileName = Ra2md.Managers.GlobalConfigManager.Resources.KeyboardIniName;
        private const string Section = "Hotkey";

        private readonly List<CommandClass> _commands = new List<CommandClass>();
        private readonly Dictionary<string, CommandClass> _byId = new Dictionary<string, CommandClass>();
        private readonly Dictionary<int, CommandClass> _byRaw = new Dictionary<int, CommandClass>();
        private bool _loaded;

        /// <summary>战场里为 true；主菜单里为 false，避免菜单按键误触发游戏命令。</summary>
        public bool GameplayActive { get; set; }

        /// <summary>命令被触发（以后玩法层订阅这个，而不是各自再读键盘）。</summary>
        public event Action<CommandClass, CommandContext> CommandInvoked;

        /// <summary>绑定发生变化（键盘选项页据此刷新显示）。</summary>
        public event Action<CommandClass> BindingChanged;

        public IList<CommandClass> Commands { get { EnsureLoaded(); return _commands; } }

        // ------------------------------------------------------------ 注册

        /// <summary>原版 GameCreate&lt;T&gt;()：造命令并登记。</summary>
        public static T GameCreate<T>() where T : CommandClass, new()
        {
            var command = new T();
            Instance.Register(command);
            return command;
        }

        public void Register(CommandClass command)
        {
            if (command == null || string.IsNullOrEmpty(command.Id)) return;
            EnsureLoaded();

            if (_byId.ContainsKey(command.Id))
            {
                Debug.LogWarning("[输入] 命令重复注册：" + command.Id);
                return;
            }

            _commands.Add(command);
            _byId[command.Id] = command;

            var saved = LookupSaved(command.Id, out var found);
            command.Bound = found ? saved : command.Default;
            Index(command);
        }

        private static Hotkey LookupSaved(string id, out bool found)
        {
            found = false;
            var map = _saved;
            if (map != null && map.TryGetValue(id, out var raw))
            {
                found = true;
                return Hotkey.FromRaw(raw);
            }
            return Hotkey.None;
        }

        private void Index(CommandClass command)
        {
            // 同一绑定只保留一个命令（重绑时旧命令会被清空，这里再兜一层）
            foreach (var kv in _byRaw)
            {
                if (kv.Value == command)
                {
                    _byRaw.Remove(kv.Key);
                    break;
                }
            }
            if (command.Bound.IsBound) _byRaw[command.Bound.Raw] = command;
        }

        // ------------------------------------------------------------ 查询 / 绑定

        public CommandClass Find(string id)
        {
            EnsureLoaded();
            return _byId.TryGetValue(id, out var c) ? c : null;
        }

        public CommandClass FindByBinding(Hotkey key)
        {
            EnsureLoaded();
            if (!key.IsBound) return null;
            return _byRaw.TryGetValue(key.Raw, out var c) ? c : null;
        }

        /// <summary>把命令绑到某个键；该键原来若属于别的命令，那个命令会被清空（原版行为）。</summary>
        public void Bind(CommandClass command, Hotkey key)
        {
            if (command == null) return;
            EnsureLoaded();

            if (key.IsBound)
            {
                var owner = FindByBinding(key);
                if (owner != null && owner != command)
                {
                    owner.Bound = Hotkey.None;
                    Index(owner);
                    Debug.Log("[输入] " + owner.Id + " 的键被 " + command.Id + " 占用，已清空");
                    if (BindingChanged != null) BindingChanged(owner);
                }
            }

            command.Bound = key;
            Index(command);
            if (BindingChanged != null) BindingChanged(command);
        }

        /// <summary>全部恢复成 KeyboardMD.ini（或内置缺省表）的默认键。</summary>
        public void ResetToDefaults()
        {
            EnsureLoaded();
            foreach (var command in _commands)
            {
                command.Bound = command.Default;
                if (BindingChanged != null) BindingChanged(command);
            }
            _byRaw.Clear();
            foreach (var command in _commands)
                if (command.Bound.IsBound) _byRaw[command.Bound.Raw] = command;
            SaveBindings();
            Debug.Log("[输入] 快捷键已恢复默认");
        }

        // ------------------------------------------------------------ 重绑

        public CommandClass Rebinding { get; private set; }

        public void BeginRebind(CommandClass command)
        {
            Rebinding = command;
            if (command != null) Debug.Log("[输入] 等待为 " + command.Id + " 按新键（Esc 取消）");
        }

        public void CancelRebind()
        {
            Rebinding = null;
        }

        // ------------------------------------------------------------ 取键（只捕获、不立刻绑定）

        private Action<Hotkey> _captureCallback;

        /// <summary>正在等一个键（键盘选项页「輸入新的快捷鍵」用）。</summary>
        public bool Capturing { get { return _captureCallback != null; } }

        /// <summary>最近一次捕获到的键（按 指定 才真正写进绑定）。</summary>
        public Hotkey PendingKey { get; private set; }

        public void BeginCapture(Action<Hotkey> onCaptured)
        {
            _captureCallback = onCaptured;
            PendingKey = Hotkey.None;
        }

        public void CancelCapture()
        {
            _captureCallback = null;
        }

        /// <summary>等键期间每帧轮询：Esc 取消，任意可映射键落进 PendingKey 后交回回调（只捕获不绑定）。</summary>
        private void PollCapture()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                _captureCallback = null;
                PendingKey = Hotkey.None;
                Debug.Log("[输入] 取键取消");
                return;
            }

            var mods = CurrentMods();
            foreach (var code in CapturableKeys)
            {
                if (!UnityEngine.Input.GetKeyDown(code)) continue;

                var key = new Hotkey { Mods = mods, Vk = KeyCodeToVk(code) };
                if (key.Vk == 0) continue;

                PendingKey = key;
                var callback = _captureCallback;
                _captureCallback = null;
                Debug.Log("[输入] 取到键 " + key.Label);
                if (callback != null) callback(key);
                return;
            }
        }

        // ------------------------------------------------------------ 装载 / 存档

        private static Dictionary<string, int> _saved;

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _saved = ReadSavedBindings();

            // 给出「ini 里有什么」的可见记录，方便和玩家手里的原版文件对照
            var defaults = HotkeyCatalog.ReadDefaults();
            Debug.Log("[输入] 快捷键默认表：" + defaults.Count + " 条（" + HotkeyCatalog.Source + "），" +
                      "玩家改键覆盖 " + (_saved.Count) + " 条");
        }

        private static Dictionary<string, int> ReadSavedBindings()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(Application.persistentDataPath, SaveFileName);
            if (!File.Exists(path)) return map;

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '[') continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                string name = trimmed.Substring(0, eq).Trim();
                string value = trimmed.Substring(eq + 1).Trim();
                if (int.TryParse(value, out var raw)) map[name] = raw;
            }
            return map;
        }

        /// <summary>把当前绑定写回 persistentDataPath/KeyboardMD.ini（原版同名文件，可直接拷贝回游戏目录）。</summary>
        public void SaveBindings()
        {
            EnsureLoaded();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("; Ra2md 快捷键（与 KeyboardMD.ini 同格式，数值 = 修饰键<<8 | Win32 VK）");
                sb.AppendLine("[" + Section + "]");
                foreach (var command in _commands)
                {
                    if (!command.Bound.IsBound) continue;
                    sb.AppendLine(command.Id + "=" + command.Bound.Raw);
                }

                var path = Path.Combine(Application.persistentDataPath, SaveFileName);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Debug.Log("[输入] 快捷键已写入 " + path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[输入] 写快捷键失败：" + exception.Message);
            }
        }

        // ------------------------------------------------------------ 每帧派发

        private void Update()
        {
            EnsureLoaded();

            if (_captureCallback != null)
            {
                PollCapture();
                return;
            }

            if (Rebinding != null)
            {
                if (TryCaptureRebind()) return;
            }

            if (!GameplayActive) return;

            foreach (var command in _commands)
            {
                if (!command.Bound.IsBound) continue;
                if (!Down(command.Bound)) continue;

                var ctx = BuildContext();
                if (!command.CanExecute(ctx)) continue;

                if (CommandInvoked != null) CommandInvoked(command, ctx);
                try
                {
                    command.Execute(ctx);
                }
                catch (Exception exception)
                {
                    Debug.LogError("[输入] " + command.Id + " 执行异常：" + exception);
                }
            }
        }

        private static bool TryCaptureRebind()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Instance.CancelRebind();
                return true;
            }

            var mods = CurrentMods();
            foreach (var code in CapturableKeys)
            {
                if (!UnityEngine.Input.GetKeyDown(code)) continue;

                var key = new Hotkey { Mods = mods, Vk = KeyCodeToVk(code) };
                if (key.Vk == 0) continue;

                var command = Instance.Rebinding;
                Instance.Bind(command, key);
                Instance.SaveBindings();
                Debug.Log("[输入] " + command.Id + " ← " + key.Label);
                Instance.CancelRebind();
                return true;
            }
            return false;
        }

        private static CommandContext BuildContext()
        {
            var camera = Camera.main;
            var ctx = new CommandContext();
            if (camera == null) return ctx;

            var mouse = UnityEngine.Input.mousePosition;
            var ray = camera.ScreenPointToRay(mouse);
            if (Mathf.Abs(ray.direction.z) > 1e-5f)
            {
                float t = -ray.origin.z / ray.direction.z;
                if (t > 0f) ctx.Cursor = ray.origin + ray.direction * t;
            }
            ctx.HasTarget = false;
            ctx.Selection = null;
            return ctx;
        }

        private static HotkeyMods CurrentMods()
        {
            var mods = HotkeyMods.None;
            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift)) mods |= HotkeyMods.Shift;
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)) mods |= HotkeyMods.Ctrl;
            if (UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt)) mods |= HotkeyMods.Alt;
            return mods;
        }

        private static bool Down(Hotkey key)
        {
            if (!key.IsBound) return false;
            var code = VkToKeyCode(key.Vk);
            if (code == KeyCode.None) return false;
            if (!UnityEngine.Input.GetKeyDown(code)) return false;

            var mods = CurrentMods();
            return (mods & key.Mods) == key.Mods;
        }

        // ------------------------------------------------------------ VK ↔ 名称

        // 静态字段按声明顺序初始化，这里必须让两张映射表先构造，可捕获键表再懒加载，
        // 否则 BuildCapturableKeys 会在 VkToCode 还是 null 时枚举它（CS0168 级的低级坑）。
        private static readonly Dictionary<int, KeyCode> VkToCode = BuildVkMap();
        private static readonly Dictionary<KeyCode, int> CodeToVk = BuildCodeMap();

        private static KeyCode[] _capturableKeys;

        private static KeyCode[] CapturableKeys
        {
            get
            {
                if (_capturableKeys != null) return _capturableKeys;

                var list = new List<KeyCode>();
                foreach (var pair in VkToCode)
                    if (!list.Contains(pair.Value)) list.Add(pair.Value);
                _capturableKeys = list.ToArray();
                return _capturableKeys;
            }
        }

        private static Dictionary<int, KeyCode> BuildVkMap()
        {
            var map = new Dictionary<int, KeyCode>
            {
                { 0x08, KeyCode.Backspace }, { 0x09, KeyCode.Tab }, { 0x0C, KeyCode.Clear },
                { 0x0D, KeyCode.Return }, { 0x13, KeyCode.Pause }, { 0x14, KeyCode.CapsLock },
                { 0x1B, KeyCode.Escape }, { 0x20, KeyCode.Space },
                { 0x21, KeyCode.PageUp }, { 0x22, KeyCode.PageDown }, { 0x23, KeyCode.End },
                { 0x24, KeyCode.Home }, { 0x25, KeyCode.LeftArrow }, { 0x26, KeyCode.UpArrow },
                { 0x27, KeyCode.RightArrow }, { 0x28, KeyCode.DownArrow },
                { 0x2D, KeyCode.Insert }, { 0x2E, KeyCode.Delete },
                { 0x6A, KeyCode.KeypadMultiply }, { 0x6B, KeyCode.KeypadPlus },
                { 0x6D, KeyCode.KeypadMinus }, { 0x6E, KeyCode.KeypadPeriod }, { 0x6F, KeyCode.KeypadDivide },
                { 0xBA, KeyCode.Semicolon }, { 0xBB, KeyCode.Equals }, { 0xBC, KeyCode.Comma },
                { 0xBD, KeyCode.Minus }, { 0xBE, KeyCode.Period }, { 0xBF, KeyCode.Slash },
                { 0xC0, KeyCode.BackQuote }, { 0xDB, KeyCode.LeftBracket }, { 0xDC, KeyCode.Backslash },
                { 0xDD, KeyCode.RightBracket },
            };

            for (int i = 0; i < 10; i++) map[0x30 + i] = (KeyCode)((int)KeyCode.Alpha0 + i);
            for (int i = 0; i < 26; i++) map[0x41 + i] = (KeyCode)((int)KeyCode.A + i);
            for (int i = 0; i < 10; i++) map[0x60 + i] = (KeyCode)((int)KeyCode.Keypad0 + i);
            for (int i = 0; i < 15; i++) map[0x70 + i] = (KeyCode)((int)KeyCode.F1 + i);
            return map;
        }

        private static Dictionary<KeyCode, int> BuildCodeMap()
        {
            var map = new Dictionary<KeyCode, int>();
            foreach (var pair in VkToCode) map[pair.Value] = pair.Key;
            return map;
        }

        public static KeyCode VkToKeyCode(int vk)
        {
            return VkToCode.TryGetValue(vk, out var code) ? code : KeyCode.None;
        }

        public static int KeyCodeToVk(KeyCode code)
        {
            return CodeToVk.TryGetValue(code, out var vk) ? vk : 0;
        }

        private static readonly Dictionary<int, string> VkLabels = BuildLabels();

        private static Dictionary<int, string> BuildLabels()
        {
            var map = new Dictionary<int, string>
            {
                { 0x08, "BKSP" }, { 0x09, "TAB" }, { 0x0C, "CLEAR" }, { 0x0D, "ENTER" },
                { 0x13, "PAUSE" }, { 0x14, "CAPS" }, { 0x1B, "ESC" }, { 0x20, "SPACE" },
                { 0x21, "PGUP" }, { 0x22, "PGDN" }, { 0x23, "END" }, { 0x24, "HOME" },
                { 0x25, "←" }, { 0x26, "↑" }, { 0x27, "→" }, { 0x28, "↓" },
                { 0x2D, "INS" }, { 0x2E, "DEL" },
                { 0x6A, "NUM*" }, { 0x6B, "NUM+" }, { 0x6D, "NUM-" }, { 0x6E, "NUM." }, { 0x6F, "NUM/" },
                { 0xBA, ";" }, { 0xBB, "=" }, { 0xBC, "," }, { 0xBD, "-" }, { 0xBE, "." },
                { 0xBF, "/" }, { 0xC0, "`" }, { 0xDB, "[" }, { 0xDC, "\\" }, { 0xDD, "]" },
            };
            for (int i = 0; i < 10; i++) map[0x30 + i] = i.ToString();
            for (int i = 0; i < 26; i++) map[0x41 + i] = ((char)('A' + i)).ToString();
            for (int i = 0; i < 10; i++) map[0x60 + i] = "NUM" + i;
            for (int i = 0; i < 15; i++) map[0x70 + i] = "F" + (i + 1);
            return map;
        }

        /// <summary>显示用文本：修饰键 + 键名，未绑定给「—」。</summary>
        public static string LabelOf(Hotkey key)
        {
            if (!key.IsBound) return "—";

            var sb = new StringBuilder();
            if ((key.Mods & HotkeyMods.Ctrl) != 0) sb.Append("Ctrl+");
            if ((key.Mods & HotkeyMods.Shift) != 0) sb.Append("Shift+");
            if ((key.Mods & HotkeyMods.Alt) != 0) sb.Append("Alt+");

            sb.Append(VkLabels.TryGetValue(key.Vk, out var label) ? label : "VK" + key.Vk);
            return sb.ToString();
        }
    }
}
