// LuaManager.cs —— xLua 运行时桥接单例：LuaEnv 的创建/逐帧 Tick/GC/释放统一归口。
//
// 职责边界：
//   · 这里只管理 Lua 虚拟机生命周期、外部脚本加载与最薄的生命周期回调；
//   · Lua 可直访 C# 脚本；事件由 Lua 侧自行注册，C# 只在固定相位回调同名全局函数；
//   · 业务逻辑（菜单/单位/触发器）仍在 C# 管理器层，Lua 只做可热更的表现与脚本层。
//
// 生命周期回调口径（由 C# 主循环在固定相位调用）：
//   · 游戏启动 OnGameStart / 游戏结束 OnGameEnd；
//   · 每场对局开始 OnMatchStart / 对局结束 OnMatchEnd；
//   · 每帧 OnUpdate(dt)。
// 钩子不存在时静默跳过；钩子抛错时打印异常与堆栈，不把异常抛回游戏循环。
using System;
using System.IO;
using System.Text;
using Ra2md.Core;
using UnityEngine;
using XLua;

namespace Ra2md.Managers
{
    public sealed class LuaManager : MonoBehaviour
    {
        private static LuaManager _instance;

        private LuaEnv _env;
        private bool _available;
        private bool _loaderAttached;
        private float _gcTimer;

        public static LuaManager Instance
        {
            get
            {
                if (_instance == null) Ensure();
                return _instance;
            }
        }

        public static LuaManager Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Ra2md.LuaManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LuaManager>();
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
            CreateEnv();
        }

        /// <summary>Lua 虚拟机是否可用（原生库加载成功且未释放）。</summary>
        public bool Available => _available && _env != null;

        private void CreateEnv()
        {
            try
            {
                _env = new LuaEnv();
                _available = true;
                AttachExternalLoader();
                BootLog.Log("[lua] LuaEnv 就绪");
            }
            catch (Exception e)
            {
                _env = null;
                _available = false;
                BootLog.Log("[lua] LuaEnv 初始化失败：" + e.Message);
            }
        }

        // ---------------------------------------------------------------- 外部脚本加载

        /// <summary>脚本目录（StreamingAssets 下，相对路径）。</summary>
        private static string ScriptDir => GlobalConfigManager.Resources.LuaScriptDir;

        private static string ResolveScriptPath(string name)
        {
            var fileName = name;
            if (!fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) fileName += ".lua";
            return Path.Combine(Application.streamingAssetsPath, ScriptDir, fileName);
        }

        /// <summary>挂自定义加载器：require / dofile 都从 StreamingAssets/Lua 取字节。</summary>
        private void AttachExternalLoader()
        {
            if (!Available || _loaderAttached) return;
            _env.AddLoader(ExternalLoader);
            _loaderAttached = true;
        }

        private byte[] ExternalLoader(ref string filepath)
        {
            try
            {
                var path = ResolveScriptPath(filepath);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e)
            {
                BootLog.Log("[lua] 加载器读取失败 " + filepath + "：" + e.Message);
                return null;
            }
        }

        /// <summary>外部加载并执行一个 Lua 脚本（相对脚本目录，可省略 .lua 后缀）。</summary>
        public void LoadExternalScript(string name)
        {
            if (!Available) return;
            AttachExternalLoader();
            var path = ResolveScriptPath(name);
            if (!File.Exists(path))
            {
                BootLog.Log("[lua] 脚本不存在：" + path);
                return;
            }
            try
            {
                var code = File.ReadAllText(path, Encoding.UTF8);
                _env.DoString(code, "@" + Path.GetFileName(path));
                BootLog.Log("[lua] 已加载外部脚本：" + Path.GetFileName(path));
            }
            catch (Exception e)
            {
                BootLog.Log("[lua] 脚本执行失败 " + name + "：" + e.Message);
            }
        }

        // ---------------------------------------------------------------- 生命周期回调

        /// <summary>游戏启动相位回调。</summary>
        public void NotifyGameStart() => CallHook("OnGameStart");

        /// <summary>游戏结束相位回调。</summary>
        public void NotifyGameEnd() => CallHook("OnGameEnd");

        /// <summary>每场对局开始回调。</summary>
        public void NotifyMatchStart() => CallHook("OnMatchStart");

        /// <summary>每场对局结束回调。</summary>
        public void NotifyMatchEnd() => CallHook("OnMatchEnd");

        /// <summary>每帧步进回调，dt 为帧间隔秒。</summary>
        public void NotifyUpdate(float dt) => CallHook("OnUpdate", dt);

        /// <summary>调用 Lua 侧全局钩子；不存在则跳过，异常带堆栈打印。</summary>
        private void CallHook(string functionName, params object[] args)
        {
            if (!Available) return;
            LuaFunction func = null;
            try
            {
                func = _env.Global.Get<LuaFunction>(functionName);
                if (func == null) return;
                func.Call(args);
            }
            catch (Exception e)
            {
                // xLua 抛出的异常消息已带 Lua 侧出错位置；这里补 C# 侧堆栈，便于定位。
                BootLog.Log("[lua] 钩子 " + functionName + " 异常：" + e.Message);
                BootLog.Log("[lua] " + e.StackTrace);
            }
            finally
            {
                func?.Dispose();
            }
        }

        /// <summary>执行一段 Lua 代码，返回 Lua 侧返回值；不可用时返回 null。</summary>
        public object[] DoString(string code, string chunkName = "=chunk")
        {
            if (!Available) return null;
            try { return _env.DoString(code, chunkName); }
            catch (Exception e)
            {
                BootLog.Log("[lua] DoString 异常：" + e.Message);
                return null;
            }
        }

        /// <summary>读全局值。</summary>
        public T GetGlobal<T>(string name)
        {
            if (!Available) return default;
            return _env.Global.Get<T>(name);
        }

        /// <summary>写全局值。</summary>
        public void SetGlobal(string name, object value)
        {
            if (!Available) return;
            _env.Global.Set(name, value);
        }

        /// <summary>挂自定义加载器（VFS/StreamingAssets/AB 加载后续接这里）。</summary>
        public void AddLoader(LuaEnv.CustomLoader loader)
        {
            if (Available && loader != null) _env.AddLoader(loader);
        }

        private void Update()
        {
            if (!Available) return;
            _env.Tick();

            _gcTimer += Time.deltaTime;
            if (_gcTimer >= 60f)
            {
                _gcTimer = 0f;
                _env.GC();
            }
        }

        /// <summary>释放虚拟机（换局/退出时调用；重复调用安全）。</summary>
        public void Dispose()
        {
            if (_env != null)
            {
                _env.Dispose();
                _env = null;
            }
            _available = false;
            _loaderAttached = false;
        }

        private void OnDestroy() => Dispose();
    }
}
