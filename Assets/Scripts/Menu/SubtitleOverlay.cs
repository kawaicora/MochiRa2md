// SubtitleOverlay.cs —— 影片字幕层（全屏播放时画面底部居中）。
//
// 只做显示：谁给 cue 谁负责时间轴（时间轴在 MovieSubtitles / FullscreenVideoPlayer）。
// 用 Screen Space - Overlay 画布：不参与 3D 深度排序，永远不会被影片平面挡在背后。
// 画布按 800×600 等比缩放，字幕条锚在底部居中。
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Menu
{
    public sealed class SubtitleOverlay : MonoBehaviour
    {
        private const int Order = 32000;
        private const int BottomInset = 22;   // 距画面底边
        private const int SideInset = 40;     // 左右留白
        private const int Height = 64;
        private const int BaseWidth = 640;    // 原版字号以 640×480 为基准

        private Canvas _canvas;
        private Text _label;
        private SubtitleCue _current;

        public static SubtitleOverlay Create(Transform plane)
        {
            var go = new GameObject("SubtitleLayer");
            go.transform.SetParent(plane, false);
            var overlay = go.AddComponent<SubtitleOverlay>();
            overlay.EnsureBuilt();
            return overlay;
        }

        private void EnsureBuilt()
        {
            if (_label != null) return;

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = Order;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var labelGo = new GameObject("SubtitleText", typeof(RectTransform));
            labelGo.transform.SetParent(transform, false);

            var rect = (RectTransform)labelGo.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(SideInset, BottomInset);
            rect.offsetMax = new Vector2(-SideInset, BottomInset + Height);

            _label = labelGo.AddComponent<Text>();
            _label.font = MenuFonts.Chinese();
            _label.fontSize = 22;
            _label.color = Color.white;
            _label.alignment = TextAnchor.LowerCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.supportRichText = false;
            _label.raycastTarget = false;

            var outline = labelGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            _label.gameObject.SetActive(false);
        }

        /// <summary>显示一条字幕；cue 为 null 时隐藏。</summary>
        public void Show(SubtitleCue cue, float movieDuration)
        {
            EnsureBuilt();
            if (cue == null)
            {
                Hide();
                return;
            }

            if (_current == cue && _label.gameObject.activeSelf) return;
            _current = cue;

            _label.text = cue.Text;
            _label.color = cue.Color;
            _label.alignment = cue.Align;

            // Position 的字号以 640×480 为基准，按本工程的 800 宽等比放大
            float scale = MenuSceneObjects.PlaneWidth / (float)BaseWidth;
            int size = Mathf.Max(14, Mathf.RoundToInt(cue.FontSize * scale));
            if (_label.fontSize != size) _label.fontSize = size;

            if (!_label.gameObject.activeSelf) _label.gameObject.SetActive(true);
        }

        public void Hide()
        {
            _current = null;
            if (_label != null && _label.gameObject.activeSelf) _label.gameObject.SetActive(false);
        }
    }
}
