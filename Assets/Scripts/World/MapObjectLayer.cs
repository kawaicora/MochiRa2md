// MapObjectLayer.cs —— 地图上的“独立对象层”（overlay 覆盖物 / 单位 / 建筑），一个对象一个 GameObject。
//
// 为什么是 GameObject 而不是往 Surface16 里贴图：
//   · 用户口径：覆盖物、单位、建筑都要能单独选中/移动/删除，地图编辑器直接拿这些对象做手柄；
//   · 地形仍然走 Surface16（1:1 像素面），对象层是 ScreenSpaceOverlay 上 sortingOrder=1 的 Canvas，
//     压在呈现面之上，坐标每帧按 TerrainRenderer 的等距投影换算，跟随窗口变化。
// 一个对象的组成：GameObject(MapObjectView) + RectTransform(pivot=0.5,0 踩在格子底角) + Image(Texture2D)。
//   · 贴图 = SHP 帧 → isosno/isotem/isourb 调色板 → RGBA32，索引 0 透明；
//   · 遮挡顺序 = (cellX + cellY) 升序（等距深度），同深度按创建顺序。
using System;
using System.Collections.Generic;
using Ra2md.Assets;
using Ra2md.Core;
using Ra2md.Gfx;
using Ra2md.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.World
{
    /// <summary>单个地图对象的视图（保持 cell 坐标与来源信息，供地图编辑器直接改）。</summary>
    public sealed class MapObjectView : MonoBehaviour
    {
        public int CellX;
        public int CellY;
        public int Level;                // Height 层级（地形抬升）
        public string Kind;              // overlay / unit / building
        public string TypeName;          // overlay 类型名 / 单位类型 ID / 建筑类型 ID
        public string ImageName;         // 解析后的 SHP 名（不带 .shp）
        public int FrameIndex;
        public int SpriteWidth;
        public int SpriteHeight;
        public bool TopAnchored;        // true = TMP tile（锚在格子上顶点）；false = SHP 物件（锚在格心）
        // TMP 子图相对格子左上角的偏移（extra 图形可能向上/向左伸出）；SHP 为 0。
        public int ImageOffsetX;
        public int ImageOffsetY;
        // 渲染锚定格：多格 foundation（CARUS06 4×4、GAGATE_A 3×1）画布中心要压在整个地基中心，
        // 而 CellX/CellY 仍是地图数据里的原值（编辑器移动/删除用），两者分开存。
        public int AnchorCellX;
        public int AnchorCellY;
        // 地基中心相对 origin 格心的像素偏移（偶数尺寸地基落在半格上）
        public float AnchorOffsetX;
        public float AnchorOffsetY;
        public int SortBias;            // 同格叠加次序：0 = 主体，1.. = art 里的动画层
        public int OrderIndex;
        public int YAdjust;             // art 的 ZAdjust（像素，正值向上）
        // unit 表重染用的阵营色（16..31 remap 区）；建筑/覆盖物不用，恒 0。
        public int ColorR;
        public int ColorG;
        public int ColorB;

        // ---- art 动画（ActiveAnim / ActiveAnimTwo / IdleAnim …），通用 SpriteAnim 驱动 ----
        public string AnimImage;
        public string AnimDamagedImage;
        public Ra2md.Assets.SpriteAnim Anim;
        public Ra2md.Assets.SpriteAnim AnimNormal;
        public Ra2md.Assets.SpriteAnim AnimDamaged;
        public AnimRole Role;
        public bool RequiresPower = true;   // <Key>Powered=no 时停电也不停
        public bool PoweredSpecial;        // <Key>PoweredSpecial=yes：停电时隐藏，由 LowPower 槽接替

        [NonSerialized] public RectTransform Rect;
        [NonSerialized] public Image Image;
        [NonSerialized] public int DepthKey;
        // 视图 ↔ 逻辑对象的绑定（选中/移动/展开时直接改位置，不重建）
        [NonSerialized] public UnitClass BoundUnit;
        [NonSerialized] public BuildingClass BoundBuilding;
        [NonSerialized] private RectTransform _selectionFrame;

        public bool Selected { get; private set; }

        /// <summary>选中框：四条 2px 边（原版是单位脚下的括号，这里先给一个同色描边矩形）。</summary>
        public void SetSelected(bool on)
        {
            if (Selected == on) return;
            Selected = on;
            if (!on)
            {
                if (_selectionFrame != null) Destroy(_selectionFrame.gameObject);
                _selectionFrame = null;
                return;
            }
            if (Rect == null) return;
            if (_selectionFrame != null) return;

            var go = new GameObject("SelectionFrame");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(Rect, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(-3f, -3f);
            rt.offsetMax = new Vector2(3f, 3f);
            _selectionFrame = rt;

            var color = new Color(0.35f, 1f, 0.35f, 0.9f);
            AddBar(rt, 0f, 1f, 1f, 1f, color);              // 顶
            AddBar(rt, 0f, 0f, 1f, 0f, color);              // 底
            AddBar(rt, 0f, 0f, 0f, 1f, color);              // 左
            AddBar(rt, 1f, 0f, 1f, 1f, color);              // 右
        }

        private static void AddBar(RectTransform parent, float ax0, float ay0, float ax1, float ay1, Color color)
        {
            var go = new GameObject("Bar");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(ax0, ay0);
            rt.anchorMax = new Vector2(ax1, ay1);
            var vertical = Mathf.Approximately(ax0, ax1);
            rt.sizeDelta = vertical ? new Vector2(2f, 0f) : new Vector2(0f, 2f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }
    }

    /// <summary>图像锚点口径：TMP 格子上顶点 + 子图偏移；SHP 画布中心。</summary>
    public struct SpriteAnchor
    {
        public bool TopAnchored;
        public int OffsetX;
        public int OffsetY;
    }

    /// <summary>建筑动画槽位角色（对齐参考 21 槽位：3..6 Active、7 PreProduction、8 Production、
    /// 9 Turret、10..13 Special、14..17 Super、18 Idle、19 LowPower、20 SuperLowPower）。</summary>
    public enum AnimRole
    {
        Active = 0,
        PreProduction = 1,
        Production = 2,
        Special = 3,
        Super = 4,
        Idle = 5,
        LowPower = 6,
        SuperLowPower = 7,
        Turret = 8,
    }

    public sealed class MapObjectLayer : MonoBehaviour
    {
        private static MapObjectLayer _instance;

        private RectTransform _root;
        private readonly List<MapObjectView> _views = new List<MapObjectView>();
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _imageErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SpriteAnchor> _anchorCache = new Dictionary<string, SpriteAnchor>(StringComparer.OrdinalIgnoreCase);
        private bool _dirtySort;
        private IsoMapData _terrain;

        public static MapObjectLayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Ra2md.MapObjectLayer");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MapObjectLayer>();
                }
                return _instance;
            }
        }

        public int Count => _views.Count;
        public IReadOnlyList<MapObjectView> Views => _views;
        public string Status { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // 对象层不再自建 Canvas：直接挂进整图画布容器（MapRoot），
            // 位置用画布像素坐标，视口平移时随画布一起移动，不需要重算屏幕坐标。
            var rootGo = new GameObject("Objects");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0f, 0f);
            _root.anchorMax = new Vector2(0f, 0f);
            _root.pivot = new Vector2(0f, 0f);
            _root.anchoredPosition = Vector2.zero;
            MapCanvasView.Instance.Attach(_root);
        }

        // ---- 构建 ----

        /// <summary>清空并重建整层（overlay 覆盖物 + 单位 + 建筑）。返回对象数。</summary>
        public int Build(SkirmishMatch match)
        {
            Clear();
            _imageErrors.Clear();
            if (match == null) return 0;
            _terrain = match.Terrain;

            var overlays = 0;
            var terrains = 0;
            var things = 0;
            var failed = 0;

            List<OverlayCell> cells;
            string note;
            if (match.Document != null && match.Document.Ini != null)
            {
                cells = OverlayMap.Decode(match.Document.Ini, RulesDatabase.Current, out note);
            }
            else
            {
                cells = new List<OverlayCell>();
                note = "无地图 INI";
            }

            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (string.IsNullOrEmpty(cell.ImageName)) continue;

                // 每个覆盖物都读自己的 art(md).ini 段：Image= 改写贴图名，ZAdjust / YSortAdjust 定偏移，
                // Palette= 在 ResolvePalette 里生效。art 段没有才用 rules 的 Image。
                var art = ArtDatabase.Current != null ? ArtDatabase.Current.Get(cell.TypeName) : null;
                var imageName = art != null ? art.ResolvedImage(cell.TypeName) : cell.ImageName;
                if (string.IsNullOrEmpty(imageName)) imageName = cell.ImageName;

                var view = Spawn("overlay", cell.TypeName, imageName, cell.Frame, cell.X, cell.Y,
                    match.Terrain.HeightAt(cell.X, cell.Y), 0, 0, 0, true);
                if (view == null) { failed++; continue; }
                if (art != null && art.Raw != null) view.YAdjust = art.Raw.GetInt("ZAdjust", 0);
                overlays++;
            }

            // 地形对象（[Terrain] 里的树/路灯/石头）：每个都是独立 TerrainClass 实例
            for (var i = 0; i < TerrainClass.Array.Count; i++)
            {
                var t = TerrainClass.Array[i];
                if (t == null) continue;
                if (string.IsNullOrEmpty(t.ImageName)) continue;
                var view = Spawn("terrain", t.TypeName, t.ImageName, 0, t.CellX, t.CellY, match.Terrain.HeightAt(t.CellX, t.CellY));
                if (view == null) { failed++; continue; }
                terrains++;
            }

            // 单位 / 建筑：类型对象里存着自己那批实例，直接遍历
            for (var i = 0; i < UnitClass.Array.Count; i++)
            {
                var unit = UnitClass.Array[i];
                if (unit == null || !unit.IsAlive) continue;
                var image = unit.Type != null ? unit.Type.ResolvedImage : null;
                if (string.IsNullOrEmpty(image)) continue;
                var view = SpawnUnit(unit);
                if (view == null) { failed++; continue; }
                things++;
            }

            // 步兵 / 飞机：同一套通用视图与动画口径，只是分类标签不同。
            for (var i = 0; i < InfantryClass.Array.Count; i++)
            {
                var inf = InfantryClass.Array[i];
                if (inf == null || !inf.IsAlive) continue;
                var view = SpawnTechno(inf, "infantry");
                if (view == null) { failed++; continue; }
                things++;
            }

            for (var i = 0; i < AircraftClass.Array.Count; i++)
            {
                var air = AircraftClass.Array[i];
                if (air == null || !air.IsAlive) continue;
                var view = SpawnTechno(air, "aircraft");
                if (view == null) { failed++; continue; }
                things++;
            }

            for (var i = 0; i < BuildingClass.Array.Count; i++)
            {
                var b = BuildingClass.Array[i];
                if (b == null || !b.IsAlive) continue;
                var image = b.Type != null ? b.Type.ResolvedImage : null;
                if (string.IsNullOrEmpty(image)) continue;
                var view = Spawn("building", b.Type != null ? b.Type.ID : image, image, 0,
                    b.CellX, b.CellY, match.Terrain.HeightAt(b.CellX, b.CellY));
                if (view == null) { failed++; continue; }
                things++;
                // 建筑落位只认 origin 格中心：WAE BuildingRenderer 的 drawPoint 走 ObjectRenderer.GetDrawPoint
                // （结构 Position 的格左上角），再由 GetTextureDrawCoords 加 CellSize/2 → **画布中心 = origin 格中心**。
                // GetBuildingCenterPoint（地基中心）只用于深度排序，不参与绘制。画布内的 SHP 帧偏移已在
                // ShpReader.TryDecodeCanvas 里按 frame.OffsetX/OffsetY 贴进画布，不再叠加。
                ApplyFoundationDepth(view, b.OccupyWidth, b.OccupyHeight);
                // artmd 里的分层动画（油井抽油机 ActiveAnim、船厂/基地的 ActiveAnimTwo …）
                var art = b.Type != null ? b.Type.Art : null;
                var parts = SpawnBuildingParts(b, art, view);
                things += parts;
            }

            _dirtySort = true;
            Sort();

            var detail = new System.Text.StringBuilder();
            foreach (var kv in _imageErrors)
                detail.Append(" | ").Append(kv.Key).Append(" => ").Append(kv.Value);
            Status = $"对象 overlay={overlays} 地形={terrains} 单位建筑={things} 失败={failed} · "
                + (note ?? string.Empty) + detail;
            BootLog.Log("[对象层] " + Status);
            return _views.Count;
        }

        /// 深度按地基最后一格算（WAE MapView 用 GetBuildingCenterPoint 只做深度比较）。
        /// 绘制锚点仍是 origin 格中心，不做任何像素偏移。
        private static void ApplyFoundationDepth(MapObjectView view, int width, int height)
        {
            if (view == null) return;
            if (width < 1) width = 1;
            if (height < 1) height = 1;
            view.AnchorCellX = view.CellX;
            view.AnchorCellY = view.CellY;
            view.AnchorOffsetX = 0f;
            view.AnchorOffsetY = 0f;
            view.DepthKey = view.CellX + view.CellY + (width - 1) + (height - 1);
        }

        /// <summary>建一个对象；拿不到贴图就返回 null（不建空壳，避免编辑器里一堆看不见的对象）。</summary>
        private MapObjectView Spawn(string kind, string typeName, string imageName, int frame,
            int cellX, int cellY, int level, int colorR = 0, int colorG = 0, int colorB = 0,
            bool preferTile = false, bool voxel = false, int facing = 0)
        {
            // 调色板：先读 art(md).ini 这一段的 Palette=，没有才回落到类别默认表
            // （地表表 = terrain/overlay；object/unit 表 = 建筑/单位，unit 还带阵营重染 16..31）。
            var palette = ResolvePalette(kind, typeName, colorR, colorG, colorB);
            var paletteTag = (kind ?? string.Empty) + ":" + (typeName ?? string.Empty);
            var sprite = LoadSprite(imageName, frame, paletteTag, palette, out var width, out var height,
                out var anchor, colorR, colorG, colorB, preferTile, voxel, facing);
            if (sprite == null) return null;

            var go = new GameObject($"{kind}:{typeName}@{cellX},{cellY}");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_root, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            // 地形 TMP tile 用格子上顶点锚；SHP 物件（树画布/单位/建筑）用**画布中心**对格子中心
            rt.pivot = anchor.TopAnchored ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = false;

            var view = go.AddComponent<MapObjectView>();
            view.CellX = cellX;
            view.CellY = cellY;
            view.AnchorCellX = cellX;
            view.AnchorCellY = cellY;
            view.Level = level;
            view.Kind = kind;
            view.TypeName = typeName ?? string.Empty;
            view.ImageName = imageName;
            view.FrameIndex = frame;
            view.SpriteWidth = width;
            view.SpriteHeight = height;
            view.TopAnchored = anchor.TopAnchored;
            view.ImageOffsetX = anchor.OffsetX;
            view.ImageOffsetY = anchor.OffsetY;
            view.YAdjust = 0;
            view.SortBias = 0;
            view.OrderIndex = _views.Count;
            view.ColorR = colorR;
            view.ColorG = colorG;
            view.ColorB = colorB;
            view.Rect = rt;
            view.Image = img;
            view.DepthKey = cellX + cellY;
            _views.Add(view);
            return view;
        }

        /// <summary>
        /// 运行时新增一个 overlay 对象（爆炸弹坑、矿石再生、桥损毁后的残骸 …）。
        /// 走与装载完全同一个 Spawn 口径：独立 GameObject + 画布坐标，底图纹理不动。
        /// </summary>
        public MapObjectView CreateOverlay(string typeName, string imageName, int frame, int cellX, int cellY)
        {
            var level = _terrain != null ? _terrain.HeightAt(cellX, cellY) : 0;
            var view = Spawn("overlay", typeName, imageName, frame, cellX, cellY, level);
            if (view != null) _dirtySort = true;
            return view;
        }

        /// <summary>运行时为已有 UnitClass 建视图（开局 MCV、建造出来的单位都走这里）。</summary>
        public MapObjectView SpawnUnit(UnitClass unit)
        {
            if (unit == null || !unit.IsAlive || unit.Type == null) return null;
            var view = SpawnTechno(unit, "unit");
            if (view != null) view.BoundUnit = unit;
            return view;
        }

        /// <summary>单位 / 步兵 / 飞机共用的视图装配：阵营重染 + 身体自身动画（art 段声明帧范围才动）。</summary>
        private MapObjectView SpawnTechno(TechnoObjectClass entity, string kind)
        {
            if (entity == null || !entity.IsAlive || entity.Type == null) return null;
            var image = entity.Type.ResolvedImage;
            if (string.IsNullOrEmpty(image)) return null;
            var level = _terrain != null ? _terrain.HeightAt(entity.CellX, entity.CellY) : 0;
            var voxel = entity.Type.Art != null && entity.Type.Art.Voxel;
            var view = Spawn(kind, entity.Type.ID, image, 0, entity.CellX, entity.CellY, level,
                entity.Owner != null ? entity.Owner.ColorR : 0,
                entity.Owner != null ? entity.Owner.ColorG : 0,
                entity.Owner != null ? entity.Owner.ColorB : 0,
                false, voxel, 0);
            if (view == null) return null;

            AttachBodyAnim(view, entity.Type.Art);
            _dirtySort = true;
            Sort();
            return view;
        }

        /// <summary>把身体 SHP 自身的动画参数挂到通用状态机；参数不足时保持静态首帧。</summary>
        private static void AttachBodyAnim(MapObjectView view, ArtRecord art)
        {
            if (view == null) return;
            view.AnimNormal = Ra2md.Assets.SpriteAnim.FromArt(art);
            view.Anim = view.AnimNormal;
            view.AnimImage = view.ImageName;
            view.FrameIndex = view.Anim != null && view.Anim.Active ? view.Anim.Start : 0;
        }

        /// <summary>运行时为已有 BuildingClass 建视图（基地车展开成建造厂走这里）。</summary>
        public MapObjectView SpawnBuilding(BuildingClass b)
        {
            if (b == null || !b.IsAlive || b.Type == null) return null;
            var image = b.Type.ResolvedImage;
            if (string.IsNullOrEmpty(image)) return null;
            var level = _terrain != null ? _terrain.HeightAt(b.CellX, b.CellY) : 0;
            var view = Spawn("building", b.Type.ID, image, 0, b.CellX, b.CellY, level);
            if (view == null) return null;
            view.BoundBuilding = b;
            ApplyFoundationDepth(view, b.OccupyWidth, b.OccupyHeight);
            SpawnBuildingParts(b, b.Type.Art, view);
            _dirtySort = true;
            Sort();
            return view;
        }

        /// <summary>对象被摧毁：销毁它自己的 GameObject（⇒ 爆破碎片/弹坑由另外的对象表现）。</summary>
        public void Remove(MapObjectView view)
        {
            if (view == null) return;
            _views.Remove(view);
            if (view.gameObject != null) Destroy(view.gameObject);
            _dirtySort = true;
        }

        /// <summary>一条建筑动画槽位（normal / damaged 两个变体，damaged 缺省回落到 normal）。</summary>
        private sealed class AnimSlot
        {
            public string Key;
            public string Normal;
            public string Damaged;
            public AnimRole Role;
            public bool Powered = true;
            public bool PoweredSpecial;
        }

        /// <summary>
        /// 按参考槽位表枚举一栋建筑 art 段里声明的全部动画槽：
        /// ActiveAnim/Two/Three/Four(3..6)、PreProductionAnim(7)、ProductionAnim(8)、
        /// TurretAnim(9)、SpecialAnim/Two/Three/Four(10..13)、SuperAnim/Two/Three/Four(14..17)、
        /// IdleAnim(18)、LowPower(19)、SuperLowPower(20)。
        /// </summary>
        private static List<AnimSlot> EnumerateBuildingAnimSlots(IniSection raw, IniSection rulesRaw)
        {
            var list = new List<AnimSlot>();
            AddSlot(raw, list, "ActiveAnim", new[] { "", "Two", "Three", "Four" }, AnimRole.Active);
            AddSlot(raw, list, "PreProductionAnim", new[] { "" }, AnimRole.PreProduction);
            AddSlot(raw, list, "ProductionAnim", new[] { "" }, AnimRole.Production);
            AddSlot(raw, list, "SpecialAnim", new[] { "", "Two", "Three", "Four" }, AnimRole.Special);
            AddSlot(raw, list, "SuperAnim", new[] { "", "Two", "Three", "Four" }, AnimRole.Super);
            AddSlot(raw, list, "IdleAnim", new[] { "" }, AnimRole.Idle);
            AddSlot(raw, list, "LowPower", new[] { "" }, AnimRole.LowPower);
            AddSlot(raw, list, "SuperLowPower", new[] { "" }, AnimRole.SuperLowPower);

            // 炮塔槽 9：参考从 rules 段读 TurretAnim / TurretAnimDamaged（art 段不声明）。
            var turretRaw = rulesRaw ?? raw;
            var turret = turretRaw.GetString("TurretAnim", string.Empty);
            if (!string.IsNullOrEmpty(turret))
            {
                list.Add(new AnimSlot
                {
                    Key = "TurretAnim",
                    Normal = turret,
                    Damaged = turretRaw.GetString("TurretAnimDamaged", turret),
                    Role = AnimRole.Turret,
                });
            }
            return list;
        }

        private static void AddSlot(IniSection raw, List<AnimSlot> list, string baseKey, string[] suffixes, AnimRole role)
        {
            for (var i = 0; i < suffixes.Length; i++)
            {
                var key = baseKey + suffixes[i];
                var normal = raw.GetString(key, string.Empty);
                if (string.IsNullOrEmpty(normal)) continue;
                list.Add(new AnimSlot
                {
                    Key = key,
                    Normal = normal,
                    Damaged = raw.GetString(key + "Damaged", normal),
                    Role = role,
                    Powered = raw.GetBool(key + "Powered", true),
                    PoweredSpecial = raw.GetBool(key + "PoweredSpecial", false),
                });
            }
        }

        /// <summary>
        /// artmd 的分层：一栋建筑 = 主体 SHP + 若干动画 SHP（油井 CAOILD 的 ActiveAnim 抽油机、
        /// ActiveAnimTwo 火焰；炮塔槽 9、SpecialAnim 围墙板片 …）。每层独立 GameObject，
        /// ZAdjust 直接作用在屏幕 Y 上，SortBias 保证动画压在主体之上。
        /// </summary>
        private int SpawnBuildingParts(BuildingClass b, ArtRecord art, MapObjectView body)
        {
            if (art == null || art.Raw == null || body == null) return 0;
            var raw = art.Raw;
            var rulesRaw = RulesDatabase.Current != null && b.Type != null
                ? RulesDatabase.Current.GetSection(b.Type.ID) : null;
            var cellX = body.CellX;
            var cellY = body.CellY;
            var level = _terrain != null ? _terrain.HeightAt(cellX, cellY) : 0;
            var spawned = 0;
            var lowPower = b.Owner != null && b.Owner.HasLowPower();
            var damaged = IsDamaged(b);

            var slots = EnumerateBuildingAnimSlots(raw, rulesRaw);
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (string.IsNullOrEmpty(slot.Normal)) continue;

                // 建造阶段动画（起重机/预制件）：落位后不再显示；低功耗动画只在停电时显示。
                if ((slot.Role == AnimRole.Production || slot.Role == AnimRole.PreProduction) && !b.IsOnHold) continue;
                if ((slot.Role == AnimRole.LowPower || slot.Role == AnimRole.SuperLowPower) && !lowPower) continue;

                var normalArt = ArtDatabase.Current != null ? ArtDatabase.Current.Get(slot.Normal) : null;
                var normalImage = normalArt != null ? normalArt.ResolvedImage(slot.Normal) : slot.Normal;
                var damagedName = string.IsNullOrEmpty(slot.Damaged) ? slot.Normal : slot.Damaged;
                var hasDamaged = !string.Equals(damagedName, slot.Normal, StringComparison.OrdinalIgnoreCase);
                var damagedArt = hasDamaged && ArtDatabase.Current != null ? ArtDatabase.Current.Get(damagedName) : null;
                var damagedImage = damagedArt != null ? damagedArt.ResolvedImage(damagedName) : damagedName;

                var view = Spawn("building-anim", slot.Normal, normalImage, 0, cellX, cellY, level,
                    body.ColorR, body.ColorG, body.ColorB);
                if (view == null) continue;

                view.BoundBuilding = b;
                view.SortBias = 1 + spawned;
                var offsetRaw = slot.Role == AnimRole.Turret ? (rulesRaw ?? raw) : raw;
                view.AnchorOffsetX = body.AnchorOffsetX + offsetRaw.GetInt(slot.Key + "X", 0);
                view.AnchorOffsetY = body.AnchorOffsetY + offsetRaw.GetInt(slot.Key + "Y", 0);
                view.DepthKey = body.DepthKey;
                view.YAdjust = offsetRaw.GetInt(slot.Key + "ZAdjust", 0);
                view.Role = slot.Role;
                view.RequiresPower = slot.Powered;
                view.PoweredSpecial = slot.PoweredSpecial;

                view.AnimNormal = Ra2md.Assets.SpriteAnim.FromArt(normalArt);
                view.AnimImage = normalImage;
                if (hasDamaged)
                {
                    view.AnimDamaged = Ra2md.Assets.SpriteAnim.FromArt(damagedArt);
                    view.AnimDamagedImage = damagedImage;
                }

                ApplyVariant(view, damaged);
                spawned++;
            }
            return spawned;
        }

        /// <summary>受损态阈值：血量 ≤ 50%（规则 ConditionYellow=50%）切到 Damaged 变体。</summary>
        private static bool IsDamaged(BuildingClass b)
        {
            return b != null && b.MaxHealth > 0 && b.Health * 2 <= b.MaxHealth;
        }

        /// <summary>按当前受损态选择 normal/damaged 变体；切换时回到该变体起始帧。返回是否发生切换。</summary>
        private static bool ApplyVariant(MapObjectView view, bool damaged)
        {
            var useDamaged = damaged && view.AnimDamaged != null;
            var next = useDamaged ? view.AnimDamaged : view.AnimNormal;
            if (next == null) return false;
            if (view.Anim == next) return false;
            view.Anim = next;
            view.AnimImage = useDamaged ? view.AnimDamagedImage : view.ImageName;
            view.FrameIndex = next.Active ? next.Start : 0;
            return true;
        }

        /// <summary>
        /// 每帧刷新建筑部件：停电时 PoweredSpecial 槽隐藏、需电槽暂停；受损态切换变体并重载首帧。
        /// 返回 true 表示本帧应暂停推进该动画。
        /// </summary>
        private bool RefreshBuildingPart(MapObjectView view)
        {
            var b = view.BoundBuilding;
            if (b == null) return false;

            var lowPower = b.Owner != null && b.Owner.HasLowPower();

            if (view.Role == AnimRole.LowPower || view.Role == AnimRole.SuperLowPower || view.PoweredSpecial)
            {
                var visible = view.PoweredSpecial ? !lowPower : lowPower;
                if (view.gameObject != null && view.gameObject.activeSelf != visible)
                    view.gameObject.SetActive(visible);
                if (!visible) return true;
            }

            var paused = view.RequiresPower && lowPower;

            if (ApplyVariant(view, IsDamaged(b)))
            {
                var palette = ResolvePalette(view.Kind, view.TypeName, view.ColorR, view.ColorG, view.ColorB);
                var sprite = LoadSprite(view.AnimImage, view.FrameIndex,
                    (view.Kind ?? string.Empty) + ":" + (view.TypeName ?? string.Empty),
                    palette, out var w, out var h, out _,
                    view.ColorR, view.ColorG, view.ColorB);
                if (sprite != null)
                {
                    view.Image.sprite = sprite;
                    view.Rect.sizeDelta = new Vector2(w, h);
                }
            }
            return paused;
        }

        private void AdvanceAnim(MapObjectView view, float dt)
        {
            if (view.Anim == null || !view.Anim.Active) return;
            var frame = view.Anim.Step(dt);
            if (frame < 0 || frame == view.FrameIndex) return;

            var palette = ResolvePalette(view.Kind, view.TypeName, view.ColorR, view.ColorG, view.ColorB);
            var sprite = LoadSprite(view.AnimImage, frame, (view.Kind ?? string.Empty) + ":" + (view.TypeName ?? string.Empty),
                palette, out var w, out var h, out _,
                view.ColorR, view.ColorG, view.ColorB);
            if (sprite == null) { view.Anim.Active = false; return; }
            view.FrameIndex = frame;
            view.Image.sprite = sprite;
            view.Rect.sizeDelta = new Vector2(w, h);
        }

        /// <summary>逻辑侧改了格子 / 新增对象后，让对象层下一帧重排深度。</summary>
        public void MarkDirty() => _dirtySort = true;

        public void Clear()
        {
            for (var i = 0; i < _views.Count; i++)
            {
                if (_views[i] != null && _views[i].gameObject != null) Destroy(_views[i].gameObject);
            }
            _views.Clear();
            _dirtySort = false;
            Status = null;
        }

        // ---- 每帧：按地形投影摆位 + 深度排序 ----

        private void LateUpdate()
        {
            if (_views.Count == 0) return;
            if (_dirtySort) Sort();

            var dt = Time.deltaTime;
            for (var i = 0; i < _views.Count; i++)
            {
                var view = _views[i];
                if (view == null || view.Rect == null) continue;
                if (!MapCanvas.TryCellToCanvas(view.AnchorCellX, view.AnchorCellY, view.Level,
                        out var canvasX, out var canvasY))
                    continue;

                // pivot 已在 Spawn 里定死：TMP=上顶点(0.5,1)，SHP=画布中心(0.5,0.5)。
                // canvasY 是格子菱形上顶点的画布 y（左下原点、y 向上），格心再低 HalfH，所以 SHP 减 HalfH。
                var anchorY = view.TopAnchored ? canvasY : canvasY - IsoProjection.HalfH;
                // TMP 子图偏移：OffsetY<0 表示图形在格子之上（悬崖壁/树冠），画布 y 轴向上，所以是减
                anchorY -= view.ImageOffsetY;
                view.Rect.anchoredPosition = new Vector2(canvasX + view.AnchorOffsetX,
                    anchorY + view.YAdjust - view.AnchorOffsetY);
                var paused = RefreshBuildingPart(view);
                if (!paused && view.Anim != null && view.Anim.Active) AdvanceAnim(view, dt);
            }
        }

        /// <summary>等距深度：cellX+cellY 小的先画，同深度按创建顺序。</summary>
        private void Sort()
        {
            _views.Sort((a, b) =>
            {
                if (a == null) return b == null ? 0 : -1;
                if (b == null) return 1;
                var byDepth = a.DepthKey.CompareTo(b.DepthKey);
                if (byDepth != 0) return byDepth;
                var byBias = a.SortBias.CompareTo(b.SortBias);      // 主体在动画层之下
                return byBias != 0 ? byBias : a.OrderIndex.CompareTo(b.OrderIndex);
            });
            for (var i = 0; i < _views.Count; i++)
            {
                if (_views[i] != null && _views[i].Rect != null) _views[i].Rect.SetSiblingIndex(i);
            }
            _dirtySort = false;
        }

        // ---- SHP → Sprite ----

        /// <summary>
        /// 对象取图：解码统一走 IndexedImageLoader，转 Sprite 统一走 SpriteFactory，
        /// 行序翻转只发生在 SpriteFactory 一处（历史上“覆盖物倒置”重犯就在两份手写翻转）。
        /// </summary>
        /// <summary>
        /// 哪些对象用剧场地表调色板：树木/路灯/石头（[Terrain] 的 TerrainClass）与覆盖物（tiberium/路面标记）
        /// 是地图装饰，原版与地表同表；单位与建筑走 unit 表（unitsno/unittem/uniturb）。
        /// </summary>
        // 地表表：terrain / overlay（地图装饰）；建筑与单位走 object/unit 表，
        // 但两者都可以被 art(md).ini 的 Palette= 覆盖（覆盖物逐个读 art 字段）。
        private static bool UseTheaterPalette(string kind)
            => string.Equals(kind, "terrain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "overlay", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 按 art(md).ini 逐字段决定调色板（ART.INI 头注释的口径）：
        ///   Palette=        显式指定那张 pal，最高优先级；
        ///   TerrainPalette= yes → 走地表表（iso），默认 no；
        ///   Remapable=      yes → 才做 16..31 阵营重染，默认 no（所以建筑不会被染）。
        /// 都没写时才按类别回落：terrain/overlay = 地表表；building = building 表；unit = unit 表。
        /// </summary>
        private static byte[] ResolvePalette(string kind, string typeName, int colorR, int colorG, int colorB)
        {
            var art = ArtDatabase.Current != null ? ArtDatabase.Current.Get(typeName) : null;
            var raw = art != null ? art.Raw : null;

            var terrainPalette = raw != null && raw.GetBool("TerrainPalette");
            var remapable = raw != null && raw.GetBool("Remapable");

            // 原版对象 SHP（单位/建筑）吃 theater 的 unit 表（unitsno/unittem/uniturb）；
            // 地图上的覆盖物/地形对象（树、石头、矿、桥片段，TMP 与 SHP 都在内）画在地表上，
            // 必须走地表 iso 表（isosno/isourb/isotem），否则就是那身洋红噪点。
            var isMapDecoration = string.Equals(kind, "terrain", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "overlay", StringComparison.OrdinalIgnoreCase);

            var baseTable = (terrainPalette || isMapDecoration)
                ? TerrainRenderer.ScenePalette
                : TerrainRenderer.ObjectPalette;

            return remapable
                ? TerrainRenderer.RemapPalette(baseTable, colorR, colorG, colorB)
                : baseTable;
        }

        private Sprite LoadSprite(string imageName, int frame, string paletteTag, byte[] palette,
            out int width, out int height, out SpriteAnchor anchor,
            int colorR = 0, int colorG = 0, int colorB = 0, bool preferTile = false,
            bool voxel = false, int facing = 0)
        {
            width = 0;
            height = 0;
            anchor = new SpriteAnchor { TopAnchored = true };
            if (string.IsNullOrEmpty(imageName)) return null;

            var key = paletteTag + "#" + imageName + "#" + frame + "@" + colorR + "_" + colorG + "_" + colorB
                + (voxel ? "$v" + facing : string.Empty);
            if (_spriteCache.TryGetValue(key, out var cached))
            {
                width = (int)cached.rect.width;
                height = (int)cached.rect.height;
                anchor = _anchorCache.TryGetValue(key, out var a) ? a : new SpriteAnchor { TopAnchored = true };
                return cached;
            }
            if (_imageErrors.ContainsKey(key)) return null;

            if (palette == null)
            {
                _imageErrors[key] = "调色板未就绪";
                return null;
            }

            IndexedImage image;
            if (voxel)
            {
                image = VoxelImage.Render(imageName, facing);
                // 体素取不到时再按 SHP 找一遍（少数单位 art 写 Voxel 但确实配了 SHP）
                if (image == null || !image.Valid)
                {
                    var fallback = IndexedImageLoader.LoadObject(imageName, frame, TerrainRenderer.TileExtension);
                    if (fallback != null && fallback.Valid) image = fallback;
                }
            }
            else
            {
                image = preferTile
                    ? IndexedImageLoader.LoadOverlay(imageName, frame, TerrainRenderer.TileExtension)
                    : IndexedImageLoader.LoadObject(imageName, frame, TerrainRenderer.TileExtension);
            }
            if (image == null || !image.Valid)
            {
                _imageErrors[key] = "贴图缺失 " + imageName + "（" + (image != null ? image.Error : "null") + "）";
                BootLog.Log("[对象层] " + _imageErrors[key] + " kind=" + paletteTag);
                return null;
            }

            width = image.Width;
            height = image.Height;
            anchor = new SpriteAnchor
            {
                TopAnchored = image.TopAnchored,
                OffsetX = image.OffsetX,
                OffsetY = image.OffsetY,
            };

            var sprite = SpriteFactory.Make(image.Pixels, image.Width, image.Height, palette, imageName + "#" + frame);
            if (sprite == null)
            {
                _imageErrors[key] = "Sprite 生成失败 " + imageName;
                return null;
            }

            _spriteCache[key] = sprite;
            _anchorCache[key] = anchor;
            return sprite;
        }
    }
}