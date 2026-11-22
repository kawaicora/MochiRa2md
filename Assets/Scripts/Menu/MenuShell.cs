// MenuShell.cs — 所有非游戏内菜单共用外壳：左侧内容区(632x570) + 右侧按钮列。
//
//   MenuShell
//   ├─ Content      632x570   主菜单=Bink 视频；子菜单换 Texture / 换 Quad
//   ├─ Bar          632x32    lwscrnl
//   ├─ ColumnTop    168x199   sdtp (帧0)
//   ├─ Warning      92x53     sdwrnanm 91 帧 75ms/帧 @(670,48)
//   ├─ ColumnBottom 168x65    sdbtm
//   └─ ButtonPanel  (MenuButtonPanel)
//
// 换菜单 = SetContent* + SetButtons，其它不动。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ra2md.Menu
{
    public class MenuShell : MonoBehaviour
    {
        public const int ContentWidth = 632;
        public const int ContentHeight = 570;
        public const int ColumnLeft = 632;
        public const int ColumnWidth = 168;
        public const int BarTop = 568;
        public const int BarHeight = 32;

        // 对照参考 RuntimeMainMenuController.h:27,30 与 .cpp:459-467
        public const int WarningLeft = 670;
        public const int WarningTop = 48;
        public const int WarningWidth = 92;
        public const int WarningHeight = 53;
        // 帧间隔 = SHP 帧数（91 帧 → 91ms/帧）；滑入期间保持帧 0，滑入结束后才逐帧推进。
        public const int WarningFrameMs = 91;

        private GameObject _contentQuad;
        private BinkMovie _contentMovie;
        private MenuButtonPanel _panel;

        private GameObject _warningQuad;
        private bool _warningVisible = true;
        private readonly List<Texture2D> _warningFrames = new List<Texture2D>();
        private int _warningIndex;

        private float _contentShade = 1f;

        public MenuButtonPanel Panel { get { return _panel; } }
        public BinkMovie ContentMovie { get { return _contentMovie; } }
        public GameObject Content { get { return _contentQuad; } }

        /// <summary>建壳：底条、右列顶/底、警告动画、按钮容器、内容槽，全部按 RA2 资源名加载。</summary>
        public static MenuShell Create(Transform parent)
        {
            var go = new GameObject("MenuShell");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var shell = go.AddComponent<MenuShell>();

            // 内容区底图 mnscrnl（632×568 雷达地图）：视频在时被影片盖住，视频移除/子页面时露出来，
            // 这就是原版「影片下面那张像地图的图」。排序必须低于 Content 影片。
            var contentBase = MenuResourceLibrary.LoadShpFrameTexture(
                MenuResourceLibrary.NameId("mnscrnl.shp"), MenuResourceLibrary.NameId("shell.pal"), 0, "内容区底图", false);
            if (contentBase != null)
            {
                MenuSceneObjects.Quad(go.transform, "ContentBase", contentBase, 0, 0, ContentWidth, ContentHeight, -1, false);
            }

            var bar = MenuResourceLibrary.LoadShpFrameTexture(
                MenuResourceLibrary.NameId("lwscrnl.shp"), MenuResourceLibrary.NameId("shell.pal"), 0, "底条", false);
            if (bar != null)
            {
                MenuSceneObjects.Quad(go.transform, "Bar", bar, 0, BarTop, ContentWidth, BarHeight, 10, false);
            }

            var columnTop = MenuResourceLibrary.LoadShpFrameTexture(
                MenuResourceLibrary.NameId("sdtp.shp"), MenuResourceLibrary.NameId("shell.pal"), 0, "右列顶", false);
            if (columnTop != null)
            {
                MenuSceneObjects.Quad(go.transform, "ColumnTop", columnTop, ColumnLeft, 0, ColumnWidth, 199, 12, false);
            }

            // 参考装配表：sdwrnanm 91 帧、透明，叠在 sdtp 的 WARNING 屏上。
            // 调色板不是 shell.pal：这条动画有自己的一套 0x31301AE2（实测逐像素对得上原版，
            // 用 shell.pal 会把扫描线画成红蓝噪点）。
            shell._warningFrames.Clear();
            for (var f = 0; f < 91; f++)
            {
                var frame = MenuResourceLibrary.LoadShpFrameTexture(
                    MenuResourceLibrary.NameId("sdwrnanm.shp"), MenuResourceLibrary.WarningPaletteId, f, "警告动画", true);
                if (frame != null) shell._warningFrames.Add(frame);
            }
            if (shell._warningFrames.Count > 0)
            {
                shell._warningQuad = MenuSceneObjects.Quad(go.transform, "Warning", shell._warningFrames[0],
                    WarningLeft, WarningTop, WarningWidth, WarningHeight, 14, false);
            }

            var columnBottom = MenuResourceLibrary.LoadShpFrameTexture(
                MenuResourceLibrary.NameId("sdbtm.shp"), MenuResourceLibrary.NameId("shell.pal"), 0, "右列底", false);
            if (columnBottom != null)
            {
                MenuSceneObjects.Quad(go.transform, "ColumnBottom", columnBottom, ColumnLeft, 535, ColumnWidth, 65, 12, false);
            }

            var panelGo = new GameObject("ButtonPanel");
            panelGo.transform.SetParent(go.transform, false);
            var panel = panelGo.AddComponent<MenuButtonPanel>();
            // 托架配 shell2.pal（0x31301AE2）：配 shell.pal 会整片红噪点。
            panel.SlotBackground = MenuResourceLibrary.LoadShpFrameTexture(
                MenuResourceLibrary.NameId("sdbtnbkgd.shp"), MenuResourceLibrary.NameId("shell2.pal"), 0, "按钮槽托架", false);

            panel.Frames.Clear();
            for (var f = 0; f < 17; f++)
            {
                var frame = MenuResourceLibrary.LoadShpFrameTexture(
                    MenuResourceLibrary.NameId("sdbtnanm.shp"), MenuResourceLibrary.NameId("sdbtnanm.pal"), f, "槽位底板", true);
                if (frame != null) panel.Frames.Add(frame);
            }

            shell._panel = panel;

            shell._contentQuad = MenuSceneObjects.Quad(go.transform, "Content", null,
                0, 0, ContentWidth, ContentHeight, 0, false);
            shell._contentQuad.SetActive(false);

            // 壳体任一层缺图都会让整块界面“消失”，这里必须显式报错，不允许静默。
            ReportMissing(bar, "lwscrnl.shp 底部信息条", 0x2BDA2707u);
            ReportMissing(columnTop, "sdtp.shp 右列顶", 0xDC1F2311u);
            ReportMissing(columnBottom, "sdbtm.shp 右列底", 0x37CC7600u);
            ReportMissing(shell._warningFrames.Count > 0 ? shell._warningFrames[0] : null, "sdwrnanm.shp 警告动画", 0x674B55D4u);
            ReportMissing(panel.SlotBackground, "sdbtnbkgd.shp 槽位垫层", 0x64459F0Du);
            ReportMissing(panel.Frames.Count > 0 ? panel.Frames[MenuButtonPanel.StateNormal] : null, "sdbtnanm.shp 槽位底板", 0xD5D666F2u);
            ReportMissing(contentBase, "mnscrnl.shp 内容区底图", 0x65C49126u);

            return shell;
        }

        private static void ReportMissing(Texture texture, string what, uint id)
        {
            if (texture != null) return;
            Debug.LogError("[菜单] 壳体资源缺失：" + what + " (0x" + id.ToString("X8") + ")，该层不会绘制");
        }

        // ---------------------------------------------------------------- 内容区

        /// <summary>内容区换成一张静态图（子菜单用）。</summary>
        public void SetContentTexture(Texture texture)
        {
            StopContentMovie();
            if (_contentQuad == null) return;
            MenuSceneObjects.SetTexture(_contentQuad, texture);
            MenuSceneObjects.SetVisible(_contentQuad, texture != null);
        }

        /// <summary>内容区换成 Bink 视频（主菜单用）。Shell 每帧把解码帧刷到 Content Quad。</summary>
        public void SetContentMovie(BinkMovie movie)
        {
            StopContentMovie();
            _contentMovie = movie;
            if (_contentQuad != null && movie != null)
            {
                MenuSceneObjects.SetVisible(_contentQuad, false);
            }
        }

        public void StopContentMovie()
        {
            if (_contentMovie != null)
            {
                _contentMovie.Dispose();
                _contentMovie = null;
            }
        }

        /// <summary>内容区压暗倍率。对照 shadeContentPanel：子页面（选项等）乘 2/5。</summary>
        public void SetContentShade(float shade)
        {
            _contentShade = Mathf.Clamp(shade, 0f, 1f);
            ApplyContentShade();
        }

        private void ApplyContentShade()
        {
            if (_contentQuad == null) return;
            MenuSceneObjects.SetAlpha(_contentQuad, _contentShade);
        }

        // ---------------------------------------------------------------- 按钮区

        /// <summary>按钮定义：Slot 为普通槽位 0..7，-1 为 SDBTM 底部按钮；Key 为 CSF key（文案来源）。</summary>
        public struct ButtonSpec
        {
            public int Slot;
            public string Key;
            public string Label;
            /// <summary>文字色；null 用原版黄。</summary>
            public Color32? TextColor;
            /// <summary>false = 灰/红字不可点（原版无存档的「載入儲存遊戲」）。</summary>
            public bool Enabled;

            public ButtonSpec(int slot, string key, string label)
            {
                Slot = slot;
                Key = key;
                Label = label;
                TextColor = null;
                Enabled = true;
            }

            public ButtonSpec(int slot, string key, string label, Color32? textColor, bool enabled)
            {
                Slot = slot;
                Key = key;
                Label = label;
                TextColor = textColor;
                Enabled = enabled;
            }
        }

        /// <summary>整体换一组按钮（数量、槽位、文案随意）。</summary>
        public void SetButtons(IList<ButtonSpec> specs, Action<int, string, string> onClick)
        {
            if (_panel == null) return;
            _panel.Clicked = onClick;
            _panel.Clear();
            if (specs == null) return;
            foreach (var spec in specs)
            {
                var color = spec.TextColor.HasValue ? spec.TextColor.Value : MenuButtonPanel.DefaultTextColor;
                var slot = _panel.Add(spec.Label, spec.Slot, spec.Key, color);
                // 不可用按钮：红字且不接受点击（帧动画观感保持）。
                if (slot != null) _panel.SetSlotEnabled(slot.Index, spec.Enabled);
            }
        }

        public void RevealButtons()
        {
            if (_panel != null) _panel.RevealAll();
        }

        /// <summary>换页：清空旧按钮后执行 onDone（里面换页并 RevealButtons 落状态帧），不播动画。</summary>
        public void SlideOutThen(Action onDone)
        {
            if (_panel != null)
            {
                _panel.BeginSlideOut(onDone);
                return;
            }
            if (onDone != null) onDone();
        }

        public bool ButtonsIdle { get { return _panel == null || _panel.IsIdle; } }

        public void ShowButtonsIdle()
        {
            if (_panel != null) _panel.ShowIdleImmediately();
        }

        private void Update()
        {
            UpdateWarning();
            UpdateContentMovie();
            UpdateContentMovieVolume();
        }

        /// <summary>
        /// 影片通道推子随主输出变化：内容区背景影片也是 Bink 子进程出声，音量得一直跟着 AudioManager。
        /// 全屏正片在播时（IsVideoActive）直接静音，避免和正片两条声音一起响。
        /// </summary>
        private void UpdateContentMovieVolume()
        {
            if (_contentMovie == null) return;
            var audio = AudioManager.Instance;
            var percent = audio.IsVideoActive ? 0 : audio.VideoVolumePercent;
            _contentMovie.SetVolume(percent);
        }

        /// <summary>
        /// 右列顶区显隐。对照原版：主菜单这里是 sdwrnanm 的 WARNING 动画屏，
        /// 遭遇战大厅 / 联机大厅这里换成雷达（小地图）框 + 地图信息框，动画必须停掉。
        /// </summary>
        public void SetWarningVisible(bool visible)
        {
            _warningVisible = visible;
            if (_warningQuad != null) MenuSceneObjects.SetVisible(_warningQuad, visible);
        }

        private void UpdateWarning()
        {
            if (!_warningVisible) return;
            if (_warningQuad == null || _warningFrames.Count == 0) return;

            int frame = (int)(Time.unscaledTime * 1000f / WarningFrameMs) % _warningFrames.Count;
            if (frame == _warningIndex) return;
            _warningIndex = frame;
            MenuSceneObjects.SetTexture(_warningQuad, _warningFrames[frame]);
        }

        private void UpdateContentMovie()
        {
            if (_contentMovie == null) return;

            if (_contentMovie.Failed)
            {
                Debug.LogError("[影片] 内容区解码失败：" + _contentMovie.Error);
                StopContentMovie();
                return;
            }

            if (_contentMovie.TryUpdate())
            {
                MenuSceneObjects.SetTexture(_contentQuad, _contentMovie.Texture);
                MenuSceneObjects.SetVisible(_contentQuad, true);
                ApplyContentShade();
            }
        }

        private void OnDestroy()
        {
            StopContentMovie();
        }
    }
}
