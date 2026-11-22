// RenderResolution.cs —— 分辨率策略：外壳（主菜单/设置/遭遇战大厅）固定 800×600，进局后跟随窗口（含全屏）。
//
// 原版口径：
//   * 外壳段（MainMenu / Options / Skirmish 对话框）用 800×600 的 DDRAW 面，对话框按 DLU 摆放，和窗口大小无关；
//   * 进局后（ScenarioClass 开始渲染战场）后备用面按**当前窗口客户区**重建，侧栏 168 宽从右侧贴边，
//     视口 = 客户区宽 - 168；全屏时客户区就是显示器分辨率；
//   * 窗口尺寸变化（WM_SIZE，AllowModeToggle 时也允许切换）就地重建后备用面，不等下一局。
//
// 本类只做三件事：决定当前该多大的面、重建 FramePresenter 的面、把矩形/尺寸写回 GameStatics + Surface16。
// 不做插值：面尺寸 = 客户区尺寸，RawImage 铺满，1:1 像素。
using System;
using UnityEngine;
using Ra2md.Core;
using Ra2md.Boot;

namespace Ra2md.Gfx
{
    public sealed class RenderResolution : MonoBehaviour
    {
        /// <summary>外壳固定尺寸（原版对话框基准，不随窗口变）。</summary>
        public const int ShellWidth = 800;
        public const int ShellHeight = 600;

        private static RenderResolution _instance;

        /// <summary>当前是否在局内（= 战场渲染态）。</summary>
        public static bool InGameplay { get; private set; }
        public static int ActiveWidth { get; private set; } = ShellWidth;
        public static int ActiveHeight { get; private set; } = ShellHeight;
        public static string Reason { get; private set; } = "shell";

        /// <summary>分辨率变化回调（HUD/侧栏/地形层订阅；参数 = 新宽、新高）。</summary>
        public static event Action<int, int> OnChanged;

        private int _lastScreenW = -1;
        private int _lastScreenH = -1;

        public static RenderResolution Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Ra2md.RenderResolution");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<RenderResolution>();
                }
                return _instance;
            }
        }

        /// <summary>进入局内：面尺寸 = 窗口客户区（Screen.width/height），随后每帧监测变化。</summary>
        public static void EnterGameplay(string reason)
        {
            Instance.Enter(reason, Screen.width, Screen.height);
        }

        /// <summary>回到外壳：面尺寸回 800×600。</summary>
        public static void EnterShell()
        {
            Instance.Enter("shell", ShellWidth, ShellHeight);
        }

        private void Enter(string reason, int width, int height)
        {
            InGameplay = reason != "shell" && reason != "menu";
            Reason = reason;
            Apply(width, height, true);
        }

        /// <summary>重建后备用面并同步矩形。force=false 时尺寸相同则不动。</summary>
        private void Apply(int width, int height, bool force)
        {
            if (width <= 0 || height <= 0) { width = ShellWidth; height = ShellHeight; }
            if (!force && width == ActiveWidth && height == ActiveHeight) return;

            ActiveWidth = width;
            ActiveHeight = height;
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;

            if (!FramePresenter.Instance.CreateSurface(width, height))
            {
                BootLog.Log($"[分辨率] 建面失败 {width}×{height}，保持原面");
                return;
            }

            GameStatics.ScreenWidth = width;
            GameStatics.ScreenHeight = height;
            DisplayMode.ApplyViewRects(width, height);

            BootLog.Log($"[分辨率] {Reason}: {width}×{height}（窗口 {Screen.width}×{Screen.height} 全屏={Screen.fullScreen}）");
            OnChanged?.Invoke(width, height);
        }

        private void Update()
        {
            // 只在局内跟随窗口；外壳固定 800×600（原版菜单不受窗口尺寸影响）。
            if (!InGameplay) return;
            if (Screen.width == _lastScreenW && Screen.height == _lastScreenH) return;
            Apply(Screen.width, Screen.height, true);
        }

        /// <summary>侧栏贴右边时的左上角（Surface16.SidebarBounds 目前是 (0,0,w,h)，这里给渲染层的换算）。</summary>
        public static Rect SidebarRect()
        {
            var w = GameStatics.SidebarWidth;
            return new Rect(ActiveWidth - w, 0, w, ActiveHeight);
        }

        /// <summary>视口矩形：客户区去掉右侧栏 168。</summary>
        public static Rect ViewRect()
        {
            return new Rect(0, 0, Mathf.Max(1, ActiveWidth - GameStatics.SidebarWidth), ActiveHeight);
        }
    }


}
