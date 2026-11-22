// PlayerController.cs —— 玩家控制层：选中 / 移动 / 基地车展开（世界逻辑与视图的桥）。
//
// 口径：
//   · 左键单击 = 选中自己的一台单位（拖动是平移视口，所以按下与抬起的位移 < 6px 才算单击）；
//   · 右键 = 给选中单位下达移动指令（按格推进，一格 0.1s；寻路 A* 尚未接入，先走直达线）；
//   · D 键 = 展开基地车（rules 的 DeploysInto），单位销毁、原地生成建造厂并建视图；
//   · 视图位置一律由 MapObjectLayer 用 MapCanvas 的格 → 画布换算，逻辑只改 UnitClass 的格子。
using System.Collections.Generic;
using Ra2md.Core;
using UnityEngine;

namespace Ra2md.World
{
    public sealed class PlayerController : MonoBehaviour
    {
        private static PlayerController _instance;

        public static PlayerController Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Ra2md.PlayerController");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<PlayerController>();
                }
                return _instance;
            }
        }

        private readonly List<MapObjectView> _selection = new List<MapObjectView>();
        private UnitClass _mover;
        private MapObjectView _moverView;
        private readonly List<Vector2Int> _path = new List<Vector2Int>();
        private float _moveTimer;
        private const float SecondsPerCell = 0.1f;

        private Vector2 _pressPos;
        private bool _pressed;

        public IReadOnlyList<MapObjectView> Selection => _selection;
        public MapObjectView Primary => _selection.Count > 0 ? _selection[0] : null;
        public string LastOrder { get; private set; } = "待命";

        private void Update()
        {
            if (SkirmishMatch.Current == null) return;
            HandleClick();
            HandleHotkeys();
            StepMovement(Time.deltaTime);
        }

        private void HandleClick()
        {
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                _pressed = true;
                _pressPos = UnityEngine.Input.mousePosition;
            }
            if (UnityEngine.Input.GetMouseButtonUp(0) && _pressed)
            {
                _pressed = false;
                // 参考口径：拖动到 5px 才激活框选，5px 以内算单击（ftol(sqrt(dx²+dy²)) > 4）。
                if (((Vector2)UnityEngine.Input.mousePosition - _pressPos).sqrMagnitude < 25f)
                    HandleLeftClick(UnityEngine.Input.mousePosition);
            }
            // 右键留给视口拖动平移；移动指令走左键（选中后点空地）。
        }

        /// <summary>左键：点到自己的单位/建筑就选中；已选中单位时点空地 = 下达移动指令。</summary>
        private void HandleLeftClick(Vector2 screen)
        {
            if (GameHud.IsPointerOverUI(screen)) return;
            int cx, cy;
            if (!MapCanvas.TryScreenToCell(screen, out cx, out cy))
            {
                ClearSelection();
                return;
            }
            var view = FindOwnSelectableAt(cx, cy);
            if (view != null) { SetSelection(view); return; }

            // 空地：当前选中的是机动单位才给移动指令；建筑不下达移动。
            var mover = Primary != null ? Primary.BoundUnit : null;
            if (mover != null && mover.IsAlive) OrderMove(mover, Primary, cx, cy);
            else ClearSelection();
        }

        /// <summary>屏幕点 → 格 → 找该格上属于当前玩家的单位或建筑视图（单击可选建筑；框选才排除建筑）。</summary>
        public void TrySelectOnScreen(Vector2 screen) => HandleLeftClick(screen);

        private MapObjectView FindOwnSelectableAt(int cx, int cy)
        {
            var house = SkirmishMatch.Current.PlayerHouseClass;
            var views = MapObjectLayer.Instance.Views;
            MapObjectView best = null;
            for (var i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v == null) continue;
                var isUnit = v.Kind == "unit";
                var isBuilding = v.Kind == "building";
                if (!isUnit && !isBuilding) continue;

                var alive = isUnit ? v.BoundUnit != null && v.BoundUnit.IsAlive
                                    : v.BoundBuilding != null && v.BoundBuilding.IsAlive;
                if (!alive) continue;
                var owner = isUnit ? v.BoundUnit.Owner : v.BoundBuilding.Owner;
                if (house != null && owner != house) continue;

                if (v.CellX != cx || v.CellY != cy)
                {
                    // 单位/大建筑画布比一格大，隔壁格点到也算（±1 格容差）
                    if (Mathf.Abs(v.CellX - cx) > 1 || Mathf.Abs(v.CellY - cy) > 1) continue;
                }
                best = v;
                break;
            }
            return best;
        }

        public void SetSelection(MapObjectView view)
        {
            ClearSelection();
            if (view == null) return;
            _selection.Add(view);
            view.SetSelected(true);
        }

        public void ClearSelection()
        {
            for (var i = 0; i < _selection.Count; i++)
                if (_selection[i] != null) _selection[i].SetSelected(false);
            _selection.Clear();
        }

        /// <summary>右键移动：把选中单位沿直线推进到目标格。</summary>
        public void IssueMove(Vector2 screen)
        {
            var view = Primary;
            if (view == null || view.BoundUnit == null) { LastOrder = "没有选中单位"; return; }
            int cx, cy;
            if (!MapCanvas.TryScreenToCell(screen, out cx, out cy)) { LastOrder = "目标不在图上"; return; }
            OrderMove(view.BoundUnit, view, cx, cy);
        }

        public bool OrderMove(UnitClass unit, MapObjectView view, int targetX, int targetY)
        {
            if (unit == null || !unit.IsAlive) return false;
            _mover = unit;
            _moverView = view;
            BuildPath(unit.CellX, unit.CellY, targetX, targetY);
            _moveTimer = 0f;
            LastOrder = $"移动 ({unit.CellX},{unit.CellY}) → ({targetX},{targetY}) 路径 {_path.Count} 格";
            return _path.Count > 0;
        }

        /// <summary>直达线：每步沿差更大的轴走一格（等距四邻接）。</summary>
        private void BuildPath(int fromX, int fromY, int toX, int toY)
        {
            _path.Clear();
            var x = fromX;
            var y = fromY;
            var guard = Mathf.Abs(toX - fromX) + Mathf.Abs(toY - fromY) + 2;
            while ((x != toX || y != toY) && guard-- > 0)
            {
                var dx = toX - x;
                var dy = toY - y;
                if (Mathf.Abs(dx) >= Mathf.Abs(dy)) x += dx > 0 ? 1 : -1;
                else y += dy > 0 ? 1 : -1;
                _path.Add(new Vector2Int(x, y));
            }
        }

        private void StepMovement(float dt)
        {
            if (_mover == null || !_mover.IsAlive || _path.Count == 0) return;
            _moveTimer += dt;
            if (_moveTimer < SecondsPerCell) return;
            _moveTimer -= SecondsPerCell;

            var next = _path[0];
            _path.RemoveAt(0);
            _mover.Move(next.x, next.y);
            if (_moverView != null)
            {
                _moverView.CellX = next.x;
                _moverView.CellY = next.y;
                _moverView.AnchorCellX = next.x;
                _moverView.AnchorCellY = next.y;
                _moverView.DepthKey = next.x + next.y;
                MapObjectLayer.Instance.MarkDirty();
            }
            if (_path.Count == 0)
            {
                LastOrder = $"到达 ({next.x},{next.y})";
                _mover = null;
                _moverView = null;
            }
        }

        private void HandleHotkeys()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.D)) DeploySelection();
        }

        /// <summary>
        /// 基地车展开：unit 的 DeploysInto（AMCV→GACNST、SMCV→NABCNST、PCV→YACNST）。
        /// 原版口径是 UnitClass::Deploy 销毁自身、在自身格上生成 BuildingClass；这里照做。
        /// </summary>
        public bool DeploySelection()
        {
            var view = Primary;
            var unit = view != null ? view.BoundUnit : null;
            if (unit == null) { LastOrder = "没有选中基地车"; return false; }

            var rules = RulesDatabase.Current;
            var deployId = rules != null ? rules.GetString(unit.Type.ID, "DeploysInto", string.Empty) : string.Empty;
            if (string.IsNullOrEmpty(deployId))
            {
                LastOrder = unit.Type.ID + " 不能展开（rules 无 DeploysInto）";
                return false;
            }

            var buildingType = BuildingTypeClass.Find(deployId);
            if (buildingType == null) { LastOrder = "找不到建筑类型 " + deployId; return false; }

            var bx = unit.CellX;
            var by = unit.CellY;
            var owner = unit.Owner;

            unit.Destroy();
            MapObjectLayer.Instance.Remove(view);
            ClearSelection();

            var building = new BuildingClass(buildingType, owner, bx, by);
            MapObjectLayer.Instance.SpawnBuilding(building);

            LastOrder = "展开 → " + deployId + " @ (" + bx + "," + by + ")";
            BootLog.Log("[控制] MCV 展开 " + deployId + " @" + bx + "," + by + " 归属=" + (owner != null ? owner.GetId() : "无"));
            return true;
        }
    }
}
