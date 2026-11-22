// GameHud.cs —— 游戏内覆盖 UI + 小地图。
//
// 覆盖层口径：独立 ScreenSpaceOverlay Canvas（sortingOrder=20），压在地图画布之上：
//   · 右侧面板：资金（HouseClass.Balance，原版单位是「元」，非 ×100）+ 选中单位信息 + 小地图；
//   · 左下提示条：操作键位（原版在游戏里是 tooltip，这里先给常驻提示）；
//   · 小地图：把整图画布按比例缩成一张小图（底图 1/24 采样），每 0.25s 在上面点单位/建筑；
//     点击小地图 = 视口对准那一格（只改 MapCanvasView 的 pan，不重绘）。
// 所有位置/尺寸都按屏幕比例锚定，窗口缩放不会错位。
using System;
using System.Collections.Generic;
using Ra2md.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.World
{
    public sealed class GameHud : MonoBehaviour
    {
        private static GameHud _instance;
        private static RectTransform _rootRect;

        public static GameHud Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Ra2md.GameHud");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<GameHud>();
                }
                return _instance;
            }
        }

        private Canvas _canvas;
        private Text _credits;
        private Text _selectedText;
        private Text _hint;
        private RawImage _minimap;
        private Texture2D _minimapTexture;
        private Color32[] _minimapBase;
        private Color32[] _minimapPixels;
        private int _minimapW, _minimapH;
        private float _refreshTimer;
        private RectTransform _minimapRect;
        private RectTransform _panelRect;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            Build();
        }

        private Font UiFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private void Build()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;
            canvasGo.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("Root");
            rootGo.transform.SetParent(canvasGo.transform, false);
            _rootRect = rootGo.AddComponent<RectTransform>();
            _rootRect.anchorMin = Vector2.zero;
            _rootRect.anchorMax = Vector2.one;
            _rootRect.offsetMin = Vector2.zero;
            _rootRect.offsetMax = Vector2.zero;

            BuildSidePanel();
            BuildHint();
        }

        /// <summary>右上角：资金条 + 选中信息 + 小地图（原版侧栏是竖条，这里按同位置落主信息）。</summary>
        private void BuildSidePanel()
        {
            var panelGo = new GameObject("SidePanel");
            panelGo.transform.SetParent(_rootRect, false);
            _panelRect = panelGo.AddComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(1f, 1f);
            _panelRect.anchorMax = new Vector2(1f, 1f);
            _panelRect.pivot = new Vector2(1f, 1f);
            _panelRect.anchoredPosition = new Vector2(-8f, -8f);
            _panelRect.sizeDelta = new Vector2(252f, 240f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.08f, 0.82f);
            bg.raycastTarget = true;
            _panelRect.gameObject.AddComponent<RectMask2D>();

            _credits = CreateText(_panelRect, "Credits", new Vector2(10f, -8f), new Vector2(232f, 26f),
                TextAnchor.MiddleLeft, 18, new Color(1f, 0.86f, 0.35f, 1f));
            _selectedText = CreateText(_panelRect, "Selected", new Vector2(10f, -36f), new Vector2(232f, 24f),
                TextAnchor.MiddleLeft, 14, new Color(0.86f, 0.9f, 0.95f, 1f));

            var minimapGo = new GameObject("Minimap");
            minimapGo.transform.SetParent(_panelRect, false);
            _minimapRect = minimapGo.AddComponent<RectTransform>();
            _minimapRect.anchorMin = new Vector2(0f, 1f);
            _minimapRect.anchorMax = new Vector2(0f, 1f);
            _minimapRect.pivot = new Vector2(0f, 1f);
            _minimapRect.anchoredPosition = new Vector2(10f, -66f);
            _minimapRect.sizeDelta = new Vector2(232f, 160f);

            var frame = minimapGo.AddComponent<Image>();
            frame.color = new Color(0.02f, 0.02f, 0.02f, 0.9f);
            frame.raycastTarget = true;

            var imgGo = new GameObject("MinimapTexture");
            imgGo.transform.SetParent(_minimapRect, false);
            _minimap = imgGo.AddComponent<RawImage>();
            var irt = _minimap.rectTransform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(2f, 2f);
            irt.offsetMax = new Vector2(-2f, -2f);
            _minimap.raycastTarget = false;
        }

        private void BuildHint()
        {
            var go = new GameObject("Hint");
            go.transform.SetParent(_rootRect, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(10f, 10f);
            rt.sizeDelta = new Vector2(560f, 24f);
            _hint = go.AddComponent<Text>();
            _hint.font = UiFont();
            _hint.fontSize = 14;
            _hint.alignment = TextAnchor.MiddleLeft;
            _hint.color = new Color(0.85f, 0.92f, 1f, 0.95f);
            _hint.raycastTarget = false;
            _hint.text = "左键选择单位 · 右键移动 · 按住左键拖动平移视口 · D 展开基地车 · 点击小地图跳转";
        }

        private static Text CreateText(RectTransform parent, string name, Vector2 pos, Vector2 size,
            TextAnchor anchor, int fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>点击是否落在 UI 上（HUD 面板 / 小地图），世界拾取先问这里。</summary>
        public static bool IsPointerOverUI(Vector2 screen)
        {
            var self = _instance;
            if (self == null) return false;
            var p = new Vector2(screen.x / Screen.width, screen.y / Screen.height);
            if (self._panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(self._panelRect, screen))
                return true;
            return self._hint != null && RectTransformUtility.RectangleContainsScreenPoint(
                self._hint.rectTransform, screen);
        }

        private void Update()
        {
            if (SkirmishMatch.Current == null) return;
            UpdateTexts();
            UpdateMinimap();
            HandleMinimapClick();
        }

        private void UpdateTexts()
        {
            var house = SkirmishMatch.Current.PlayerHouseClass;
            if (_credits != null)
                _credits.text = house != null ? "资金 $" + house.Balance : "资金 $—";

            if (_selectedText != null)
            {
                var primary = PlayerController.Instance.Primary;
                if (primary == null) _selectedText.text = PlayerController.Instance.LastOrder;
                else
                {
                    var u = primary.BoundUnit;
                    if (u != null && u.Type != null)
                        _selectedText.text = u.Type.UIName + "  HP " + u.Health + "/" + u.MaxHealth
                            + "  (" + u.CellX + "," + u.CellY + ")";
                    else
                        _selectedText.text = primary.Kind + ":" + primary.TypeName
                            + "  (" + primary.CellX + "," + primary.CellY + ")";
                }
            }
        }

        private void HandleMinimapClick()
        {
            if (!UnityEngine.Input.GetMouseButtonDown(0) || _minimapRect == null) return;
            var screen = (Vector2)UnityEngine.Input.mousePosition;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_minimapRect, screen)) return;

            var local = screen - (Vector2)_minimapRect.position;
            var w = _minimapRect.rect.width;
            var h = _minimapRect.rect.height;
            if (w <= 0f || h <= 0f) return;
            var nx = Mathf.Clamp01(local.x / w);
            var ny = Mathf.Clamp01(local.y / h);      // UGUI 下 y 向上
            // 小地图按整图比例内嵌（左右留边），换算回画布像素
            var mapW = (float)MapCanvas.Width;
            var mapH = (float)MapCanvas.Height;
            var innerW = w;
            var innerH = innerW * mapH / Mathf.Max(1f, mapW);
            var offsetY = (h - innerH) * 0.5f;
            var px = nx * mapW;
            var py = Mathf.Clamp((ny * h - offsetY) / Mathf.Max(1f, innerH), 0f, 1f) * mapH;
            MapCanvasView.Instance.CenterOn(new Vector2(px, py));
        }

        // ---- 小地图 ----

        private void UpdateMinimap()
        {
            if (_minimap == null) return;
            _refreshTimer -= Time.deltaTime;
            if (_minimapTexture == null)
            {
                BuildMinimapBase();
                _refreshTimer = 0f;
            }
            if (_refreshTimer > 0f) return;
            _refreshTimer = 0.25f;
            StampUnits();
        }

        private void BuildMinimapBase()
        {
            var source = MapCanvas.Texture;
            if (source == null) return;

            _minimapW = 232;
            _minimapH = Mathf.Max(1, Mathf.RoundToInt(_minimapW * MapCanvas.Height / (float)Mathf.Max(1, MapCanvas.Width)));
            _minimapTexture = new Texture2D(_minimapW, _minimapH, TextureFormat.RGBA32, false, false);
            _minimapTexture.filterMode = FilterMode.Point;
            _minimapTexture.wrapMode = TextureWrapMode.Clamp;

            _minimapBase = new Color32[_minimapW * _minimapH];
            _minimapPixels = new Color32[_minimapW * _minimapH];

            var sw = source.width;
            var sh = source.height;
            // 整图动辄 1 万×6 千像素（GetPixels32 = 数百 MB），只按小地图尺寸逐点采样。
            try
            {
                for (var y = 0; y < _minimapH; y++)
                {
                    var sy = Mathf.Clamp(Mathf.RoundToInt(y / (float)_minimapH * sh), 0, sh - 1);
                    var row = y * _minimapW;
                    for (var x = 0; x < _minimapW; x++)
                    {
                        var sx = Mathf.Clamp(Mathf.RoundToInt(x / (float)_minimapW * sw), 0, sw - 1);
                        _minimapBase[row + x] = source.GetPixel(sx, sy);
                    }
                }
            }
            catch (Exception)
            {
                // 纹理不可读时小地图只有单位点，不抛到上层
            }

            _minimap.texture = _minimapTexture;
            _minimap.rectTransform.sizeDelta = new Vector2(0f, 0f);
        }

        private void StampUnits()
        {
            if (_minimapTexture == null) return;
            Array.Copy(_minimapBase, _minimapPixels, _minimapPixels.Length);

            var views = MapObjectLayer.Instance.Views;
            for (var i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v == null) continue;
                Color32 color;
                if (v.Kind == "unit")
                {
                    if (v.BoundUnit == null || !v.BoundUnit.IsAlive) continue;
                    var house = v.BoundUnit.Owner;
                    var self = house == SkirmishMatch.Current.PlayerHouseClass;
                    color = self ? new Color32(80, 255, 120, 255) : new Color32(255, 80, 80, 255);
                }
                else if (v.Kind == "building")
                {
                    if (v.BoundBuilding == null || !v.BoundBuilding.IsAlive) continue;
                    var house = v.BoundBuilding.Owner;
                    var self = house == SkirmishMatch.Current.PlayerHouseClass;
                    color = self ? new Color32(255, 220, 90, 255) : new Color32(255, 140, 60, 255);
                }
                else continue;

                var mx = Mathf.RoundToInt((v.CellX - MapCanvas.MinCellX) / (float)Mathf.Max(1, MapCanvas.MaxCellX - MapCanvas.MinCellX) * (_minimapW - 1));
                var my = Mathf.RoundToInt((v.CellY - MapCanvas.MinCellY) / (float)Mathf.Max(1, MapCanvas.MaxCellY - MapCanvas.MinCellY) * (_minimapH - 1));
                if (mx < 0 || my < 0 || mx >= _minimapW || my >= _minimapH) continue;
                _minimapPixels[my * _minimapW + mx] = color;
                // 2×2 点，小地图上看得清
                if (mx + 1 < _minimapW) _minimapPixels[my * _minimapW + mx + 1] = color;
                if (my + 1 < _minimapH) _minimapPixels[(my + 1) * _minimapW + mx] = color;
            }

            _minimapTexture.SetPixels32(_minimapPixels);
            _minimapTexture.Apply(false, false);
        }

        public void Close()
        {
            if (_minimapTexture != null) Destroy(_minimapTexture);
            _minimapTexture = null;
            for (var i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}
