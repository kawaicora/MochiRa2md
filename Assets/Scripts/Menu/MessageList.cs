// MessageList.cs —— 原版 MessageListClass::Instance.AppendMessage 的对应实现。
//
// 用途：小窗口（雷达/地图区）里播影片时，字幕不铺画面，而是按原版规则变成消息条目，
// 一条一条进消息列表，几秒后自己淡出。
//   MessageList.Instance.AppendMessage("...")          文字，白字，默认 5 秒
//   MessageList.Instance.AppendMessage("...", color, 秒数)
// 消息列表自身只保存数据（纯逻辑，不依赖场景），画面由 MessageListOverlay 负责：
// 屏幕左下角起、往上堆叠、最多 6 行，每行按剩余时间淡出。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ra2md.Core;

namespace Ra2md.Menu
{
    public sealed class MessageEntry
    {
        public string Text;
        public Color32 Color = new Color32(255, 255, 255, 255);
        public uint StartMs;
        public float LifeSeconds = 5f;
        public bool Fading;

        public float AgeSeconds { get { return GameTime.Since(StartMs) / 1000f; } }
        public float Remaining { get { return LifeSeconds - AgeSeconds; } }
    }

    public static class MessageList
    {
        public sealed class Impl
        {
            public readonly List<MessageEntry> Entries = new List<MessageEntry>();
            public int MaxEntries = 24;
        }

        private static Impl _instance;

        public static Impl Instance
        {
            get
            {
                if (_instance == null) _instance = new Impl();
                return _instance;
            }
        }

        /// <summary>追加一条消息（默认白字 5 秒）。</summary>
        public static void AppendMessage(string text)
        {
            AppendMessage(text, new Color32(255, 255, 255, 255), 5f);
        }

        public static void AppendMessage(string text, Color32 color, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;

            var list = Instance.Entries;
            list.Add(new MessageEntry
            {
                Text = text,
                Color = color,
                StartMs = GameTime.Now(),
                LifeSeconds = Mathf.Max(0.5f, seconds),
            });

            // 原版也是有上限的：堆太多就把最老的挤掉
            while (list.Count > Instance.MaxEntries) list.RemoveAt(0);

            Debug.Log("[消息] " + text);
        }

        /// <summary>清空（换界面/回主菜单时用）。</summary>
        public static void Clear()
        {
            Instance.Entries.Clear();
        }

        /// <summary>剔除过期条目，返回当前仍要显示的那些（新的在前）。</summary>
        public static List<MessageEntry> Collect()
        {
            var list = Instance.Entries;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Remaining <= 0f) list.RemoveAt(i);
            }
            return list;
        }
    }

    /// <summary>消息列表画面：左下角往上堆叠。</summary>
    public sealed class MessageListOverlay : MonoBehaviour
    {
        private const int MaxLines = 6;
        private const int LineHeight = 18;
        private const int FontSize = 14;
        private const int Left = 24;
        private const int Bottom = 96;

        private RectTransform _canvas;
        private readonly List<Text> _lines = new List<Text>();

        public static MessageListOverlay Create(Transform plane)
        {
            var go = new GameObject("MessageList");
            go.transform.SetParent(plane, false);
            var overlay = go.AddComponent<MessageListOverlay>();
            overlay.EnsureBuilt();
            return overlay;
        }

        private void EnsureBuilt()
        {
            if (_canvas != null) return;
            _canvas = MenuSceneObjects.AttachLabelCanvas(gameObject,
                MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight, 1960);

            for (int i = 0; i < MaxLines; i++)
            {
                var label = MenuSceneObjects.AddCenteredTextAt(_canvas, string.Empty,
                    Left, MenuSceneObjects.PlaneHeight - Bottom - (i + 1) * LineHeight,
                    MenuSceneObjects.PlaneWidth - Left * 2, LineHeight, FontSize,
                    new Color(1f, 1f, 1f, 1f));
                label.alignment = TextAnchor.LowerLeft;
                label.gameObject.SetActive(false);
                _lines.Add(label);
            }
        }

        private void Update()
        {
            EnsureBuilt();
            var entries = MessageList.Collect();

            for (int i = 0; i < MaxLines; i++)
            {
                var label = _lines[i];
                int index = entries.Count - 1 - i;      // 最新的排最下面，往上是旧的
                if (index < 0)
                {
                    if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
                    continue;
                }

                var entry = entries[index];
                label.text = entry.Text;
                var color = entry.Color;
                color.a = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * Mathf.Clamp01(entry.Remaining / 1.2f)), 0, 255);
                label.color = color;
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            }
        }
    }
}
