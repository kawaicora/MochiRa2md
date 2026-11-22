// MenuButtonPanel.cs —— 右侧按钮槽（9 块底板 + 按钮本体 + 文字）。
//
// 两条独立的东西，别混：
//   1) 状态帧：sdbtnanm 的 f2 常态 / f3 悬停选中 / f4 按下 / f11 空槽亮金属盘。
//      ——只是状态图，按状态换一帧，绝不按时间播放。
//   2) 滑入滑出：整块槽位（金属板 + 按钮 + 文字）作为一个整体做**位置**动画，
//      从右列外侧（x+168）滑进来 / 滑出去，逐槽错位 42ms，位移时长 180ms。
//      这才是「按钮滑入滑出」，跟 sdbtnanm 的帧毫无关系。
//
// 三层，只有两层会动：
//   固定层  sdbtnbkgd 168x42 —— 垫层，永远不动
//   动层1   sdbtnanm 156x42  —— 金属板（状态帧 f2/f3/f4 占用，f11 空槽）
//   动层2   Face + 文字       —— 按钮拾取面与文字
//
// Hierarchy:
//   ButtonPanel (MenuButtonPanel)
//     Background_0..8          ← 固定的垫层 sdbtnbkgd 168x42 (20)，永不动
//     SlotGroup_0..8           ← 动的只有这一层（localPosition.x）
//       Plate       sdbtnanm 156x42  (24) 状态帧（金属板）
//       Face_<label> 透明拾取面 126x25 (30) + 文字（按钮）
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Menu
{
    public class MenuButtonPanel : MonoBehaviour
    {
        // ---- 几何常量（对照参考 k*）----
        public const int SlotCount = 8;
        public const int BottomSlot = 8;
        public const int PlateCount = 9;
        public const int SlotTop = 199;
        public const int SlotHeight = 42;
        public const int SidebarLeft = 632;
        public const int SidebarWidth = 168;
        public const int PlateLeft = 644;
        public const int PlateWidth = 156;
        public const int ButtonLeft = 653;
        public const int ButtonWidth = 126;
        public const int ButtonHeight = 25;
        public const int MenuButtonTop = 207;

        // ---- 滑入滑出时序 ----
        public const int StaggerMs = 42;   // 逐槽错位
        public const int SlideMs = 180;    // 单槽位移时长
        public const int SlideDistance = SidebarWidth;  // 从右列外侧滑入

        // ---- Idle 状态帧（sdbtnanm 实测）----
        public const int StateEmpty = 11;
        public const int StateNormal = 2;
        public const int StateHover = 3;
        public const int StatePressed = 4;

        public Texture2D SlotBackground;
        /// <summary>mnbttn.shp 的 3 帧：常态 / 悬停 / 按下。这才是按钮本体。</summary>
        public List<Texture2D> ButtonFrames = new List<Texture2D>();
        /// <summary>sdbtnanm 的 17 帧，保留给槽位带（暂未使用）。</summary>
        public List<Texture2D> Frames = new List<Texture2D>();

        public Action<int, string, string> Clicked;

        private readonly List<MenuSlot> _slots = new List<MenuSlot>();
        private readonly List<GameObject> _slotRoots = new List<GameObject>();
        private readonly List<GameObject> _slotPlate = new List<GameObject>();
        private readonly List<Vector3> _slotPlateBase = new List<Vector3>();
        private bool _slotsBuilt;

        private enum Phase { SlideIn, Idle, SlideOut }
        private Phase _phase = Phase.Idle;
        private float _phaseStartMs;
        private Action _slideOutCallback;

        public int Count { get { return _slots.Count; } }
        public IEnumerable<MenuSlot> Slots { get { return _slots; } }
        public bool IsIdle { get { return _phase == Phase.Idle; } }

        private static int InDurationMs { get { return PlateCount * StaggerMs + SlideMs; } }

        public MenuSlot SlotAt(int index)
        {
            return index >= 0 && index < _slots.Count ? _slots[index] : null;
        }

        // ------------------------------------------------------------ 构建

        public MenuSlot Add(string label, int slot, string key)
        {
            return Add(label, slot, key, DefaultTextColor);
        }

        /// <summary>按钮文字默认色（原版黄 255,222,25）；不可用项传红字。</summary>
        public static readonly Color32 DefaultTextColor = new Color32(255, 222, 25, 255);

        /// <summary>不可用按钮的红字（原版「載入儲存遊戲」无存档时）。</summary>
        public static readonly Color32 DisabledTextColor = new Color32(214, 24, 24, 255);

        public MenuSlot Add(string label, int slot, string key, Color32 textColor)
        {
            EnsureSlots();
            var index = _slots.Count;
            int slotIndex = slot < 0 ? BottomSlot : Mathf.Clamp(slot, 0, BottomSlot);
            int top = MenuButtonTop + slotIndex * SlotHeight;

            // 按钮本体挂在该槽的 SlotGroup 下，这样滑入滑出时跟着整块一起走。
            var root = _slotRoots[slotIndex];
            var face = MenuSceneObjects.Quad(root.transform, "Face_" + (string.IsNullOrEmpty(label) ? index.ToString() : label),
                null, ButtonLeft, top, ButtonWidth, ButtonHeight, 30, true);

            var labelCanvas = MenuSceneObjects.AttachLabelCanvas(face, ButtonWidth, ButtonHeight, 900 + index);
            MenuSceneObjects.AddCenteredText(labelCanvas, label,
                ButtonWidth, ButtonHeight, 14, textColor);

            var slotComp = face.AddComponent<MenuSlot>();
            slotComp.Configure(index, slotIndex, key, label, face,
                face.GetComponentInChildren<Text>(), null, null, null, OnSlotClicked);
            slotComp.StateChanged = OnSlotStateChanged;
            slotComp.SetFaceVisible(true);
            slotComp.Interactive = _phase == Phase.Idle && !_disabled.Contains(index);

            _slots.Add(slotComp);
            ApplyFrame(slotIndex, IdleFrame(slotIndex));
            return slotComp;
        }

        public void Clear()
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                var slot = _slots[i];
                _slots.RemoveAt(i);
                if (slot != null && slot.gameObject != null) Destroy(slot.gameObject);
            }
            RefreshIdlePlates();
        }

        // ------------------------------------------------------------ 滑入 / 滑出

        /// <summary>点击后：整列逐槽滑出，全部出去后回调（回调里换页，再调 RevealAll 滑入）。</summary>
        public void BeginSlideOut(Action onCompleted)
        {
            EnsureSlots();
            _slideOutCallback = onCompleted;
            _phase = Phase.SlideOut;
            _phaseStartMs = Time.unscaledTime * 1000f;
            foreach (var s in _slots) if (s != null) s.Interactive = false;
        }

        /// <summary>换页/进菜单：整列逐槽从右列外侧滑入。</summary>
        public void RevealAll()
        {
            EnsureSlots();
            RefreshIdlePlates();
            _phase = Phase.SlideIn;
            _phaseStartMs = Time.unscaledTime * 1000f;
            foreach (var s in _slots) if (s != null) s.Interactive = false;
            ApplyOffsets(true, 0f);
        }

        /// <summary>立即停到位（不播动画）。</summary>
        public void ShowIdleImmediately()
        {
            EnsureSlots();
            _phase = Phase.Idle;
            ApplyOffsets(false, 0f);
            foreach (var s in _slots)
            {
                if (s == null) continue;
                s.SetFaceVisible(true);
            }
            SyncInteractivity();
        }

        public void SetSelected(int slot)
        {
            foreach (var s in _slots)
            {
                if (s == null) continue;
                s.SetFaceState(s.Slot == slot ? 1 : 0);
            }
        }

        public void SetInteractable(bool value)
        {
            if (value) _allLocked = false; else _allLocked = true;
            SyncInteractivity();
        }

        private bool _allLocked;
        private readonly HashSet<int> _disabled = new HashSet<int>();

        /// <summary>单个按钮的可用状态（false = 红字不可点，如无存档的「載入儲存遊戲」）。</summary>
        public void SetSlotEnabled(int index, bool enabled)
        {
            if (enabled) _disabled.Remove(index); else _disabled.Add(index);
            SyncInteractivity();
        }

        /// <summary>把可用状态与相位合成到每个槽：只有空闲相位且未被禁用才可点。</summary>
        private void SyncInteractivity()
        {
            bool idle = _phase == Phase.Idle && !_allLocked;
            foreach (var s in _slots)
            {
                if (s == null) continue;
                s.Interactive = idle && !_disabled.Contains(s.Index);
            }
        }

        // ------------------------------------------------------------ 帧 / 位移

        private void EnsureSlots()
        {
            if (_slotsBuilt) return;
            _slotsBuilt = true;

            for (var i = 0; i < PlateCount; i++)
            {
                int top = SlotTop + i * SlotHeight;

                // 固定层 = sdbtnbkgd 托架（168×42，shell2.pal），钉死不动。
                MenuSceneObjects.Quad(transform, "Tray_" + i,
                    SlotBackground, SidebarLeft, top, SidebarWidth, SlotHeight, 20, false);

                var root = new GameObject("SlotGroup_" + i);
                root.transform.SetParent(transform, false);
                _slotRoots.Add(root);

                // 动层 = 槽位板 sdbtnanm（156×42），空槽隐藏，露出固定托架。
                var plate = MenuSceneObjects.Quad(root.transform, "Plate",
                    StateTexture(StateEmpty), PlateLeft, top, PlateWidth, SlotHeight, 24, false);
                _slotPlate.Add(plate);
                _slotPlateBase.Add(plate.transform.localPosition);
            }
        }

        private void Start()
        {
            EnsureSlots();
            RefreshIdlePlates();
        }

        private void Update()
        {
            if (_phase == Phase.Idle) return;

            float elapsed = Time.unscaledTime * 1000f - _phaseStartMs;

            if (_phase == Phase.SlideIn)
            {
                ApplyOffsets(true, elapsed);
                if (elapsed >= InDurationMs)
                {
                    _phase = Phase.Idle;
                    ApplyOffsets(false, 0f);
                    Fire(_slideOutCallback);
                    _slideOutCallback = null;
                    foreach (var s in _slots)
                    {
                        if (s == null) continue;
                        s.SetFaceVisible(true);
                    }
                    SyncInteractivity();
                }
                return;
            }

            // SlideOut
            ApplyOffsets(false, elapsed);
            if (elapsed < InDurationMs) return;

            _phase = Phase.Idle;
            RefreshIdlePlates();
            var cb = _slideOutCallback;
            _slideOutCallback = null;
            Fire(cb);
        }

        private static void Fire(Action a)
        {
            if (a != null) a();
        }

        /// <summary>
        /// 逐槽错位位移：第 i 槽在 i*42ms 后开始，180ms 内从 +168 走到 0（滑入），
        /// 滑出方向相反。用平滑收尾，避免看起来像瞬移。
        /// </summary>
        private void ApplyOffsets(bool slidingIn, float elapsedMs)
        {
            for (var i = 0; i < _slotRoots.Count; i++)
            {
                var root = _slotRoots[i];
                if (root == null) continue;

                float local = elapsedMs - i * StaggerMs;
                float t = local <= 0f ? 0f : Mathf.Clamp01(local / SlideMs);
                float eased = 1f - (1f - t) * (1f - t);          // ease-out

                // 滑入：从 SlideDistance → 0；滑出：0 → SlideDistance
                float offset = slidingIn
                    ? SlideDistance * (1f - eased)
                    : SlideDistance * eased;

                var p = root.transform.localPosition;
                root.transform.localPosition = new Vector3(offset, p.y, p.z);
            }
        }

        private void RefreshIdlePlates()
        {
            for (var i = 0; i < PlateCount; i++) ApplyFrame(i, IdleFrame(i));
        }

        private int IdleFrame(int slot)
        {
            var s = FindSlot(slot);
            if (s == null) return StateEmpty;
            switch (s.FaceState)
            {
                case 1: return StateHover;
                case 2: return StatePressed;
                default: return StateNormal;
            }
        }

        private void ApplyFrame(int slot, int frame)
        {
            if (slot < 0 || slot >= _slotPlate.Count) return;
            var plate = _slotPlate[slot];
            if (plate == null) return;

            // 空槽：不画按钮，露出底下固定的托架。
            if (frame == StateEmpty)
            {
                MenuSceneObjects.SetVisible(plate, false);
                return;
            }

            var tex = StateTexture(frame);
            if (tex == null)
            {
                MenuSceneObjects.SetVisible(plate, false);
                return;
            }
            MenuSceneObjects.SetVisible(plate, true);
            MenuSceneObjects.SetTexture(plate, tex);
        }

        /// <summary>mnbttn.shp 的按钮帧：0 常态 / 1 悬停 / 2 按下。</summary>
        private Texture2D ButtonTexture(int frame)
        {
            if (ButtonFrames == null || ButtonFrames.Count == 0) return null;
            if (frame < 0) frame = 0;
            if (frame >= ButtonFrames.Count) frame = ButtonFrames.Count - 1;
            return ButtonFrames[frame];
        }

        private Texture2D StateTexture(int frame)
        {
            if (Frames == null || Frames.Count == 0) return null;
            if (frame < 0) frame = 0;
            if (frame >= Frames.Count) frame = Frames.Count - 1;
            return Frames[frame];
        }

        private MenuSlot FindSlot(int slot)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s != null && s.Slot == slot) return s;
            }
            return null;
        }

        private void OnSlotStateChanged(MenuSlot slot)
        {
            if (slot == null) return;
            ApplyFrame(slot.Slot, IdleFrame(slot.Slot));
        }

        private void OnSlotClicked(MenuSlot slot)
        {
            if (Clicked != null) Clicked(slot.Index, slot.Key, slot.Label);
        }
    }
}
