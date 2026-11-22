// LuaManager.cs —— xLua 运行时桥接单例：LuaEnv 的创建/逐帧 Tick/GC/释放统一归口。
//
// 职责边界：
//   · 这里只管理 Lua 虚拟机生命周期与最薄的调用入口；
//   · Lua 脚本放哪里、怎么加载（VFS/StreamingAssets/AB）由后续加载器决定，
//     需要时通过 AddLoader 挂进来，本类不认识 MIX/VFS；
//   · 业务逻辑（菜单/单位/触发器）仍在 C# 管理器层，Lua 只做可热更的表现与脚本层。
//
// 失败口径：原生库缺失或构造抛错时，标记不可用，后续调用全部空转，不把异常抛到游戏循环里。
using System;
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
                BootLog.Log("[lua] LuaEnv 就绪");
            }
            catch (Exception e)
            {
                _env = null;
                _available = false;
                BootLog.Log("[lua] LuaEnv 初始化失败：" + e.Message);
            }
        }

        /// <summary>执行一段 Lua 代码，返回 Lua 侧返回值；不可用时返回 null。</summary>
        public object[] DoString(string code, string chunkName = "=chunk")
        {
            if (!Available) return null;
            return _env.DoString(code, chunkName);
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
        }

        private void OnDestroy() => Dispose();
    }
}
