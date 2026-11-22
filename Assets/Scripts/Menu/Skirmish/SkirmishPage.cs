// SkirmishPage.cs —— 遭遇战大厅（Lobby）+ 选择地图页（Map Selection）的 UI 复刻。
//
// 几何来源：原版对话框资源 258 SkirmishDialog（ra2de_aaa\__\Ares\src\UI\SkirmishDialog.rc，
// 533×369 DLU，DLU→px：X=1.5、Y=1.625），不是照截图目测：
//   表头 GUI:Players/Side/Color/StartPosition/Team : y=21 DLU → 34，x = 39/191/283/325/363 → 58/286/424/487/544
//   玩家行  EDIT 38,36,100,14   → 57,58.5,150×22.75；行距 16 DLU → 26 px，8 行
//   国旗位  SS_GRAYRECT 150,36,32,12 → 225..273
//   阵营    COMBOBOX 191,36,78  → 286..403
//   颜色    COMBOBOX 282,36,29  → 423..466（只有色块 + 三角，无文字）
//   位置    COMBOBOX 324,36,25  → 486..523
//   小队    COMBOBOX 364,36,25  → 546..583
//   选项    复选框 48,176/193/210/228 → 72,286/314/341/370；BuildOffAlly 201,227 → 301,369
//   滑条    trackbar 269,176/193/210,85 → 403..531；数值格画在轨道右端
//   右列    黑底预览 429,23,96,69 → 644,37,144×112；地图类型 432,103 → 648,167；地图名 432,116 → 648,188
//   按钮    StartGame 425,149 → 槽 1；ChooseMap 425,176 → 槽 2；Back 425,346 → 底部槽
//   drawMapSelectionPage : 双列 86..236 / 256..506，y 143..443，行高 31，9 行可见，滚动条 488
//
// 与参考实现的差异（按本轮要求）：
//   1. 玩家槽总数不再固定 8，容量取 SkirmishLimits.SlotCapacity（0x40）；
//   2. 本局可加入人数完全由地图决定（SkirmishMapInfo.SlotLimit）；
//   3. 玩家列表是滚动窗口（MenuListScroll），国家/颜色/起始/队伍下拉同样滚动。
// 目前只做 UI 与选择状态；本页的进入由 GameStatics.GameMode == Skirmish 决定，
// 点「开始游戏」只输出装配日志（进局需要对象世界，标 [未实现]）。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ra2md.Core;
using Ra2md.World;

namespace Ra2md.Menu
{
    public sealed class SkirmishPage : MonoBehaviour
    {
        // ---- 大厅几何：逐个控件取自原版对话框资源 258 SkirmishDialog
        //      （ra2de_aaa\__\Ares\src\UI\SkirmishDialog.rc，533×369 DLU）。
        //      DLU→像素沿用 RA2 口径：X = 1.5、Y = 1.625（533*1.5=799.5、369*1.625=600，正好铺满屏）。
        //      例：玩家 EDIT 38,36,100,14 → 57,58.5,150,22.75；行距 16 DLU → 26 px。
        private const int HeaderY = 34;                 // STATIC 39/191/283/325/363,21
        private const int RowTop = 57;                  // EDIT / COMBOBOX y=36 DLU → 36*1.625=58.5
        private const int RowHeight = 26;               // 行距 16 DLU → 26
        private const int CellHeight = 23;              // 组合框/编辑框可见高 14 DLU → 22.75
        private const int RowsVisible = SkirmishLimits.VisibleRows;

        // 列表外框（原版逐控件摆放、无整块框；这里只包住 8 行可见区）
        private const int ListLeft = 52;
        private const int ListTop = 52;
        private const int ListWidth = 540;
        private const int ListHeight = 222;
        private const int ScrollLeft = 590;
        private const int ScrollTop = 54;
        private const int ScrollHeight = 214;

        private const int ColPlayer = 57;               // EDIT 38,36,100,14 → 56.8
        private const int ColPlayerRight = 207;         // +100 DLU
        private const int ColFlag = 224;                // STATIC 150,36,32,12 → 224.1
        private const int ColFlagRight = 272;
        private const int ColCountry = 285;             // COMBOBOX 191,36,78 → 285.4
        private const int ColCountryRight = 402;
        private const int ColColor = 421;               // COMBOBOX 282,36,29 → 421.3
        private const int ColColorRight = 464;
        private const int ColStart = 484;               // COMBOBOX 324,36,25 → 484.1
        private const int ColStartRight = 521;
        private const int ColTeam = 544;                // COMBOBOX 364,36,25 → 543.8
        private const int ColTeamRight = 581;

        private const int OptionLeft = 72;              // BUTTON 48,176 → 71.7
        private const int OptionRight = 300;           // BUTTON 201,227 → 300.3
        private const int StepperTop = 286;             // BUTTON/STATIC y=176 → 286
        private const int StepperStep = 28;             // 17 DLU → 27.6
        private const int OptionToggleStep = 27;
        private const int ToggleSize = 16;              // 按钮高 10 DLU → 16
        private const int StepperLabelWidth = 90;       // STATIC 201,xxx,60,10 → 300..390
        private const int StepperLeft = 403;
        private const int StepperWidth = 127;            // 轨道 77 + 数值板 50（sub_61D950：数值板固定 50px 贴右）
        private const int StepperHeight = 22;

        // ---- 右列：STATIC 0x468 429,23,96,69 → 641,37,143×112（小地图/雷达框）
        //      STATIC 0x6ec 432,103,90,10 → 645,167（游戏类型）
        //      STATIC 0x5a8 432,116,90,20 → 645,188（地图名）
        private const int SidebarBoxLeft = 641;
        private const int SidebarBoxWidth = 143;
        private const int SidebarPreviewTop = 37;
        private const int SidebarPreviewHeight = 112;
        private const int SidebarInfoLeft = 641;
        private const int SidebarInfoTop = 165;
        private const int SidebarInfoWidth = 145;
        private const int SidebarInfoHeight = 54;

        // ---- 选图页几何 ----
        private const int MapPageTitleTop = 78;
        private const int MapPageLabelTop = 100;
        private const int MapPageLeft = 300;
        private const int MapPageModeLeft = 116;
        private const int MapPageModeRight = 312;
        private const int MapPageListLeft = 314;
        private const int MapPageListRight = 514;
        private const int MapPagePanelTop = 126;
        private const int MapPagePanelBottom = 455;
        private const int MapPageRowHeight = 31;
        private const int MapPageRows = 9;
        private const int MapPageScrollLeft = 516;
        private const int MapPageInfoTop = 462;

        private static readonly Color32 TextGold = new Color32(255, 232, 0, 255);
        private static readonly Color32 TextDim = new Color32(112, 112, 112, 255);
        private static readonly Color32 TextPale = new Color32(206, 206, 187, 255);
        private static readonly Color32 TextNotice = new Color32(199, 174, 98, 255);
        private static readonly Color32 CellFillOn = new Color32(20, 27, 23, 255);
        private static readonly Color32 CellFillOff = new Color32(10, 13, 12, 255);
        private static readonly Color32 PanelFill = new Color32(3, 3, 3, 255);
        private static readonly Color32 SelectFill = new Color32(74, 7, 5, 255);
        private static readonly Color32 FrameRed = new Color32(117, 17, 11, 255);
        private static readonly Color32 HeaderGold = new Color32(171, 163, 125, 255);

        private static Texture2D _texOn;
        private static Texture2D _texOff;
        private static Texture2D _texGold;
        private readonly Dictionary<string, Texture2D> _swatchCache = new Dictionary<string, Texture2D>();

        private enum Cell { Player, Country, Color, Start, Team }
        private enum View { Lobby, MapSelection }

        private sealed class Row
        {
            public GameObject PlayerCheck;
            
            public GameObject Swatch;
            public Text Name, Country, Start, Team;
        }

        private Transform _root;
        private GameObject _lobbyRoot;
        private GameObject _mapRoot;
        private RectTransform _lobbyCanvas;
        private RectTransform _mapCanvas;

        private Catalog _catalog;
        private SkirmishSelection _selection;
        private readonly List<Row> _rows = new List<Row>();
        private MenuListScroll _slotScroll;
        private int _slotTop;

        private Text _mapNameText;
        private Text _mapCountText;
        private Text _modeText;
        private GameObject _previewImage;
        private GameObject _mapPreviewImage;
        private int _gameModeIndex;
        private Text _mapInfoName;
        private Text _mapInfoPlayers;

        private SkinStepper _speedStep, _creditsStep, _unitsStep;
        private Text _statusText;

        private static readonly int[] CreditSteps = { 0, 500, 1000, 2000, 5000, 10000, 20000 };
        private const int SpeedMax = 6;
        private const int UnitsMax = 10;

        // 下拉
        private GameObject _dropdown;
        private RectTransform _ddCanvas;
        private readonly List<Text> _ddTexts = new List<Text>();
        private readonly List<GameObject> _ddHits = new List<GameObject>();
        private MenuListScroll _ddScroll;
        private int _ddFirst;
        private List<string> _ddOptions;
        private Action<int> _ddPick;

        // 选图页
        private readonly List<string> _modes = new List<string>();
        private readonly List<int> _filtered = new List<int>();
        private readonly List<Text> _modeTexts = new List<Text>();
        private readonly List<GameObject> _modeBars = new List<GameObject>();
        private readonly List<Text> _mapTexts = new List<Text>();
        private readonly List<GameObject> _mapBars = new List<GameObject>();
        private readonly List<GameObject> _mapHits = new List<GameObject>();
        private MenuListScroll _mapScroll;
        private int _mapTop;
        private int _modeIndex;

        private Action<string> _requestButtons;

        // ---- 加载屏（原版开局载入：GUI:Loading 文案 + 进度条 + 地图名）----
        private GameObject _loadingRoot;
        private Transform _loadingCanvas;
        private Text _loadingTitle;
        private Text _loadingMapText;
        private Text _loadingStepText;
        private Transform _loadingBarFill;
        private readonly List<string> _loadingStepNames = new List<string>();
        private int _loadingStepIndex;
        private bool _loadingActive;
        private bool _loadingDone;
        private float _loadingNextStepAt;
        private const float LoadingStepHold = 0.16f;   // 原版载入屏每阶段有可见停留，不是一帧闪过去
        private const int LoadingBarLeft = 200;
        private const int LoadingBarTop = 420;
        private const int LoadingBarWidth = 400;
        private const int LoadingBarHeight = 14;

        public SkirmishSelection Selection { get { return _selection; } }

        // ---------------------------------------------------------------- 装配

        public static SkirmishPage Create(Transform parent, Action<string> requestButtons)
        {
            var go = new GameObject("SkirmishPage");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var page = go.AddComponent<SkirmishPage>();
            page._requestButtons = requestButtons;
            page.Build();
            return page;
        }

        private void Build()
        {
            EnsureTextures();

            _catalog = new Catalog();
            _catalog.BuildCountries();
            _catalog.BuildColors();
            _catalog.BuildMaps();

            _selection = new SkirmishSelection();
            _selection.Reset(_catalog);

            _root = new GameObject("SkirmishRoot").transform;
            _root.SetParent(transform, false);
            _root.localPosition = Vector3.zero;

            _lobbyRoot = new GameObject("LobbyRoot");
            _lobbyRoot.transform.SetParent(_root, false);
            _lobbyRoot.transform.localPosition = Vector3.zero;
            _lobbyCanvas = AttachPageCanvas(_lobbyRoot, 74);

            _mapRoot = new GameObject("MapRoot");
            _mapRoot.transform.SetParent(_root, false);
            _mapRoot.transform.localPosition = Vector3.zero;
            _mapCanvas = AttachPageCanvas(_mapRoot, 74);

            BuildLobby(_lobbyRoot.transform, _lobbyCanvas.transform);
            BuildMapSelection(_mapRoot.transform, _mapCanvas.transform);
            BuildLoading();

            // 底部状态行：原版 STATIC 0x695 2,355,410,12 → 3,577,613×19（初始 GUI:Blank）
            _statusText = AddText(_lobbyCanvas.transform, "Status", MenuStrings.Get("GUI:Blank", string.Empty),
                3, 572, 610, 18, 12, TextNotice, TextAnchor.MiddleLeft);

            if (_catalog.Maps.Count > 0)
            {
                _selection.MapIndex = 0;
                ApplySelectedMap();
            }

            _mapRoot.SetActive(false);
            RefreshLobby();
            RefreshMapSelection();
        }

        private void EnsureTextures()
        {
            if (_texOn == null) _texOn = MenuWidgets.Solid(CellFillOn);
            if (_texOff == null) _texOff = MenuWidgets.Solid(CellFillOff);
            if (_texGold == null) _texGold = MenuWidgets.Solid(MenuWidgets.BoxCheckOn);
        }

        /// <summary>
        /// 给页面挂一张 800×600 的文字画布，并把它摆到平面中心。
        /// MenuSceneObjects.AttachLabelCanvas 只把 Canvas 放在宿主原点（= 平面左下角），
        /// 而 Quad 与 AddText 的坐标都以平面中心为基准，少了这 +400/+300 的位移，
        /// 整页文字会整体向左下偏移半个平面。
        /// </summary>
        private static RectTransform AttachPageCanvas(GameObject host, int sortingOrder)
        {
            var rect = MenuSceneObjects.AttachLabelCanvas(host,
                MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight, sortingOrder);
            rect.localPosition = new Vector3(
                MenuSceneObjects.PlaneWidth * 0.5f,
                MenuSceneObjects.PlaneHeight * 0.5f,
                -0.05f);
            return rect;
        }

        private Texture2D SwatchTexture(Color32 color, string id)
        {
            Texture2D texture;
            var key = id + ":" + color.r + "_" + color.g + "_" + color.b;
            if (_swatchCache.TryGetValue(key, out texture) && texture != null) return texture;
            texture = MenuWidgets.Solid(color);
            _swatchCache[key] = texture;
            return texture;
        }

        private void BuildLobby(Transform t, Transform canvas)
        {
            // 原版没有“整块红框列表底”：行/列都是各自带 2px 边框的控件，直接铺在背景上。

            // ---- 表头 ----
            AddText(canvas, "HeadPlayer", MenuStrings.Get("GUI:Player", "Player"), ColPlayer, HeaderY,
                ColPlayerRight - ColPlayer, 20, 12, HeaderGold, TextAnchor.MiddleLeft);
            AddText(canvas, "HeadCountry", MenuStrings.Get("GUI:Side", MenuStrings.Get("GUI:Country", "Side")), ColCountry, HeaderY,
                ColCountryRight - ColCountry, 20, 12, HeaderGold, TextAnchor.MiddleLeft);
            AddText(canvas, "HeadColor", MenuStrings.Get("GUI:Color", "Color"), ColColor, HeaderY,
                ColColorRight - ColColor, 20, 12, HeaderGold, TextAnchor.MiddleLeft);
            AddText(canvas, "HeadStart", MenuStrings.Get("GUI:StartPosition",
                    MenuStrings.Get("GUI:Location", "Start Position")), ColStart, HeaderY,
                ColStartRight - ColStart, 20, 12, HeaderGold, TextAnchor.MiddleLeft);
            AddText(canvas, "HeadTeam", MenuStrings.Get("GUI:Team", "Team"), ColTeam, HeaderY,
                ColTeamRight - ColTeam, 20, 12, HeaderGold, TextAnchor.MiddleLeft);

            // ---- 玩家行（滚动窗口）----
            _slotScroll = MenuListScroll.Create(t, "SlotScroll", ScrollLeft, ScrollTop,
                15, ScrollHeight, 62);
            _slotScroll.SetHitRect(ColPlayer - 4, RowTop - 2, ColTeamRight - ColPlayer + 8,
                RowsVisible * RowHeight);
            _slotScroll.TopChanged = top =>
            {
                _slotTop = top;
                RefreshLobby();
            };

            for (var i = 0; i < RowsVisible; i++) _rows.Add(BuildRow(t, canvas, i));

            BuildSidebar(t, canvas);
            BuildOptions(t, canvas);
            BuildSteppers(t, canvas);
        }

        /// <summary>
        /// <summary>
        /// 右列（632..800）：原版 0x102 的 0x468 STATIC（429,23,96,69）是“小地图预览框”，
        /// 不是 WARNING 动画；下面 0x6ec / 0x5a8 两块 STATIC 组成信息框（类型 + 地图名）。
        /// </summary>
        private void BuildSidebar(Transform t, Transform canvas)
        {
            // 小地图框：金属 2px 边框 + 黑底，里面叠地图缩略图与起始点编号（缩略图待 [PreviewPack] 解码）
            SkinCell.Create(t, "PreviewFrame", SidebarBoxLeft, SidebarPreviewTop,
                SidebarBoxWidth, SidebarPreviewHeight, 60, SkirmishSkin.MetalEdgeHi, SkirmishSkin.MetalEdgeLo);
            MenuSceneObjects.Quad(t, "PreviewBlack", MenuWidgets.Solid(new Color32(0, 0, 0, 255)),
                SidebarBoxLeft + 2, SidebarPreviewTop + 2, SidebarBoxWidth - 4,
                SidebarPreviewHeight - 4, 63, false);

            SkinCell.Create(t, "MapInfoBox", SidebarInfoLeft, SidebarInfoTop,
                SidebarInfoWidth, SidebarInfoHeight, 60, SkirmishSkin.MetalEdgeHi, SkirmishSkin.MetalEdgeLo);
            MenuSceneObjects.Quad(t, "InfoBlack", MenuWidgets.Solid(new Color32(0, 0, 0, 255)),
                SidebarInfoLeft + 2, SidebarInfoTop + 2, SidebarInfoWidth - 4,
                SidebarInfoHeight - 4, 63, false);
            // 0x6ec STATIC：原版显示的是当前地图支持的游戏模式（作战 / 自由交战 / 小队联盟…），
            // 文案按 CSF key "GUI:<Mode>" 取；点它在本图声明的模式间轮换（地图不支持就保持首项）。
            _modeText = AddText(canvas, "MapInfoType", string.Empty,
                SidebarInfoLeft + 6, SidebarInfoTop + 3, SidebarInfoWidth - 12, 16, 12, TextGold,
                TextAnchor.MiddleLeft);
            Hit(t, "MapInfoTypeHit", SidebarInfoLeft + 2, SidebarInfoTop + 1, SidebarInfoWidth - 4, 18,
                68, CycleGameMode);
            _mapNameText = AddText(canvas, "MapName", string.Empty, SidebarInfoLeft + 6,
                SidebarInfoTop + 20, SidebarInfoWidth - 12, 18, 12, TextGold, TextAnchor.MiddleLeft);
            _mapNameText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _mapCountText = AddText(canvas, "MapCount", string.Empty, SidebarInfoLeft + 6,
                SidebarInfoTop + 37, SidebarInfoWidth - 12, 14, 11, TextPale, TextAnchor.MiddleLeft);

            // 0x468 STATIC 小地图：原版由 PreviewClass::DrawStartPoints 把 [PreviewPack] 缩略图
            // 贴进这块 96×69 的框（RT_DIALOG 258: 429,23,96,69），再按比例画起始点编号。
            _previewImage = MenuSceneObjects.Quad(t, "MapPreview", MenuSceneObjects.TransparentTexture,
                SidebarBoxLeft + 2, SidebarPreviewTop + 2, SidebarBoxWidth - 4, SidebarPreviewHeight - 4,
                72, false);
        }

        private Row BuildRow(Transform t, Transform canvas, int row)
        {
            var y = RowTop + row * RowHeight;
            var item = new Row();

            // 玩家格：EDIT 38,36,100,14（2px 边框 + 点阵内部），左侧勾选方块 + 难度/名字
            SkinCell.Create(t, "PlayerCell" + row, ColPlayer, y, ColPlayerRight - ColPlayer,
                CellHeight, 61, SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);

            item.PlayerCheck = MenuSceneObjects.Quad(t, "CheckOn" + row, MenuWidgets.Solid(MenuWidgets.BoxCheckOn),
                ColPlayer + 4, y + 5, 11, 11, 65, false);
            MenuSceneObjects.Quad(t, "CheckBoxWell" + row, MenuWidgets.Solid(new Color32(0x42, 0x00, 0x00, 255)),
                ColPlayer + 3, y + 4, 13, 13, 64, false);
            MenuSceneObjects.Quad(t, "CheckBoxFrame" + row, MenuWidgets.Solid(SkirmishSkin.ToggleEdgeHi),
                ColPlayer + 2, y + 3, 15, 15, 63, false);

            Hit(t, "PlayerHit" + row, ColPlayer + 1, y + 1, 18, CellHeight - 2,
                66, () => ToggleSlot(row));
            Hit(t, "DifficultyHit" + row, ColPlayer + 19, y,
                ColPlayerRight - ColPlayer - 19, CellHeight, 67, () => OpenPlayerCell(row));

            item.Name = AddText(canvas, "Name" + row, string.Empty, ColPlayer + 22, y,
                ColPlayerRight - ColPlayer - 44, CellHeight, 12, TextGold, TextAnchor.MiddleLeft);
            AddArrow(t, "PlayerArrow" + row, ColPlayerRight, y, 68);

            // 国旗位（STATIC 150,36,32,12）：原版画阵营国旗，暂以缩略色块占位
            SkinCell.Create(t, "FlagCell" + row, ColFlag, y, ColFlagRight - ColFlag, CellHeight,
                61, SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);

            // 阵营，COMBOBOX 191,36,78,73
            SkinCell.Create(t, "CountryCell" + row, ColCountry, y, ColCountryRight - ColCountry,
                CellHeight, 61, SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);
            item.Country = AddText(canvas, "Country" + row, string.Empty, ColCountry + 5, y,
                ColCountryRight - ColCountry - 32, CellHeight, 12, TextGold, TextAnchor.MiddleLeft);
            AddArrow(t, "CountryArrow" + row, ColCountryRight, y, 68);
            Hit(t, "CountryHit" + row, ColFlag, y, ColCountryRight - ColFlag,
                CellHeight, 66, () => OpenCell(Cell.Country, row));

            // 颜色，COMBOBOX 282,36,29,73：只有色块 + 三角，不写文字
            SkinCell.Create(t, "ColorCell" + row, ColColor, y, ColColorRight - ColColor,
                CellHeight, 61, SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);
            item.Swatch = MenuSceneObjects.Quad(t, "Swatch" + row, MenuWidgets.Solid(MenuWidgets.BoxCheckOn),
                ColColor + 3, y + 3, ColColorRight - ColColor - 32, CellHeight - 6, 65, false);
            AddArrow(t, "ColorArrow" + row, ColColorRight, y, 68);
            Hit(t, "ColorHit" + row, ColColor, y, ColColorRight - ColColor,
                CellHeight, 66, () => OpenCell(Cell.Color, row));

            // 起始位置，COMBOBOX 324,36,25,73
            SkinCell.Create(t, "StartCell" + row, ColStart, y, ColStartRight - ColStart,
                CellHeight, 61, SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);
            item.Start = AddText(canvas, "Start" + row, string.Empty, ColStart + 1, y,
                ColStartRight - ColStart - 29, CellHeight, 11, TextGold, TextAnchor.MiddleCenter);
            AddArrow(t, "StartArrow" + row, ColStartRight, y, 68);
            Hit(t, "StartHit" + row, ColStart, y, ColStartRight - ColStart,
                CellHeight, 66, () => OpenCell(Cell.Start, row));

            // 小队，COMBOBOX 364,36,25,73
            SkinCell.Create(t, "TeamCell" + row, ColTeam, y, ColTeamRight - ColTeam,
                CellHeight, 61, SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);
            item.Team = AddText(canvas, "Team" + row, string.Empty, ColTeam + 1, y,
                ColTeamRight - ColTeam - 29, CellHeight, 11, TextGold, TextAnchor.MiddleCenter);
            AddArrow(t, "TeamArrow" + row, ColTeamRight, y, 68);
            Hit(t, "TeamHit" + row, ColTeam, y, ColTeamRight - ColTeam,
                CellHeight, 66, () => OpenCell(Cell.Team, row));

            return item;
        }

        /// <summary>
        /// 组合框下拉箭头：原版 arrow_dd.pcx 自然尺寸 28×24，右边缘对齐控件右边，纵向 y-1
        /// （24 高的贴图比 23 高的控件各溢 1px，这是原版 ownerdraw 的贴法）。
        /// </summary>
        private void AddArrow(Transform t, string name, int columnRight, int y, int order)
        {
            SkinPcx.Create(t, name, "arrow_dd.pcx", columnRight - 28, y - 1, 28, 24, order);
        }

        private void BuildOptions(Transform t, Transform canvas)
        {
            // 原版 BUTTON 48,176 / 48,193 / 48,210 / 48,228 与 201,227 共 5 个：
            // GUI:ShortGame / GUI:MCVRepacks / GUI:CratesAppear / GUI:SuperWeaponsAllowed / GUI:BuildOffAlly
            AddToggle(t, "ShortGame", OptionLeft, StepperTop, MenuStrings.Get("GUI:ShortGame", "Short Game"),
                _selection.ShortGame, 160, v => _selection.ShortGame = v);
            AddToggle(t, "McvRepacks", OptionLeft, StepperTop + OptionToggleStep,
                MenuStrings.Get("GUI:MCVRepacks", "MCV Repacks"), _selection.McvRepacks, 160,
                v => _selection.McvRepacks = v);
            AddToggle(t, "Crates", OptionLeft, StepperTop + OptionToggleStep * 2,
                MenuStrings.Get("GUI:CratesAppear", "Crates"), _selection.Crates, 160,
                v => _selection.Crates = v);
            AddToggle(t, "Superweapons", OptionLeft, StepperTop + OptionToggleStep * 3 + 4,
                MenuStrings.Get("GUI:SuperWeaponsAllowed", "Superweapons"), _selection.Superweapons, 200,
                v => _selection.Superweapons = v);
            AddToggle(t, "BuildOffAlly", OptionRight, StepperTop + OptionToggleStep * 3 + 2,
                MenuStrings.Get("GUI:BuildOffAlly", "Build Off Ally"), _selection.BuildOffAlly, 210,
                v => _selection.BuildOffAlly = v);
        }

        /// <summary>复选框：原版 toggle 集（紫灰 2px 框 + 红球），点方框或整行文字都可切换。</summary>
        private void AddToggle(Transform t, string name, int x, int yTop, string caption, bool initial,
            int labelWidth, Action<bool> onChange)
        {
            var canvas = _lobbyCanvas;
            SkinToggle.Create(t, name, x, yTop, ToggleSize, labelWidth, initial, onChange);
            AddText(canvas.transform, name + "Lbl", caption, x + ToggleSize + 4, yTop - 1,
                labelWidth, ToggleSize + 2, 12, TextPale, TextAnchor.MiddleLeft);
        }

        private void BuildSteppers(Transform t, Transform canvas)
        {
            _speedStep = BuildStepper(t, canvas, "Speed", StepperTop,
                MenuStrings.Get("GUI:GameSpeed", "Game Speed"), 0);
            _creditsStep = BuildStepper(t, canvas, "Credits", StepperTop + StepperStep,
                MenuStrings.Get("GUI:Credits", "Credits"), 1);
            _unitsStep = BuildStepper(t, canvas, "Units", StepperTop + StepperStep * 2,
                MenuStrings.Get("GUI:UnitCount", "Starting Units"), 2);
        }

        /// <summary>
        /// 滑条 = 轨道（2px 边框 + 点阵）+ 把手 + 数值板（trofl|trofm|trofr）+ 右侧 49px 红底读数。
        /// 几何照 code\sub_\sub_61D950.c：数值板 50px 贴右，把手 12px，可动里程 = 宽-50-13，起点 +1。
        /// 控件本体在 SkirmishControls.SkinStepper 里，这里只做摆放与数值映射。
        /// </summary>
        private SkinStepper BuildStepper(Transform t, Transform canvas, string name, int y,
            string caption, int kind)
        {
            AddText(canvas, name + "Lbl", caption, OptionRight, y, StepperLabelWidth, 20, 13,
                TextPale, TextAnchor.MiddleRight);

            var stepper = SkinStepper.Create(t, canvas, name, StepperLeft, y, StepperWidth,
                StepperHeight, NormalizedOf(kind), 60);
            stepper.ValueChanged += v => SetStepper(kind, v);
            return stepper;
        }

        private void BuildMapSelection(Transform t, Transform canvas)
        {
            AddText(canvas, "MapTitle", MenuStrings.Get("GUI:SelectEngagement", "Select Engagement"),
                MapPageLeft, MapPageTitleTop, 420, 26, 15, TextGold, TextAnchor.MiddleLeft);
            AddText(canvas, "ModeLbl", MenuStrings.Get("GUI:GameType", "Game Type"),
                MapPageModeLeft, MapPageLabelTop, 150, 22, 13, TextGold, TextAnchor.MiddleLeft);
            AddText(canvas, "MapLbl", MenuStrings.Get("GUI:Map", "Map"),
                MapPageListLeft, MapPageLabelTop, 150, 22, 13, TextGold, TextAnchor.MiddleLeft);

            SkinCell.Create(t, "ModeBox", MapPageModeLeft, MapPagePanelTop,
                MapPageModeRight - MapPageModeLeft, MapPagePanelBottom - MapPagePanelTop, 60,
                SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);
            SkinCell.Create(t, "MapBox", MapPageListLeft, MapPagePanelTop,
                MapPageListRight - MapPageListLeft, MapPagePanelBottom - MapPagePanelTop, 60,
                SkirmishSkin.CellEdgeHi, SkirmishSkin.CellEdgeLo);

            // 右列 0x468 预览框：选图页同样把当前选中地图的 [PreviewPack] 缩略图贴进去
            _mapPreviewImage = MenuSceneObjects.Quad(t, "MapPagePreview", MenuSceneObjects.TransparentTexture,
                SidebarBoxLeft + 2, SidebarPreviewTop + 2, SidebarBoxWidth - 4, SidebarPreviewHeight - 4,
                72, false);

            for (var i = 0; i < MapPageRows; i++)
            {
                var y = MapPagePanelTop + 2 + i * MapPageRowHeight;

                _modeBars.Add(MenuSceneObjects.Quad(t, "ModeBar" + i, MenuWidgets.Solid(SelectFill),
                    MapPageModeLeft + 2, y, MapPageModeRight - MapPageModeLeft - 4,
                    MapPageRowHeight, 61, false));
                MenuSceneObjects.SetVisible(_modeBars[i], false);
                _modeTexts.Add(AddText(canvas, "ModeText" + i, string.Empty, MapPageModeLeft + 7, y,
                    MapPageModeRight - MapPageModeLeft - 12, MapPageRowHeight, 13, TextPale,
                    TextAnchor.MiddleLeft, () => SelectMode(i)));

                _mapBars.Add(MenuSceneObjects.Quad(t, "MapBar" + i, MenuWidgets.Solid(SelectFill),
                    MapPageListLeft + 2, y, MapPageListRight - MapPageListLeft - 4,
                    MapPageRowHeight, 61, false));
                MenuSceneObjects.SetVisible(_mapBars[i], false);
                _mapTexts.Add(AddText(canvas, "MapText" + i, string.Empty, MapPageListLeft + 7, y,
                    MapPageListRight - MapPageListLeft - 22, MapPageRowHeight, 13, TextPale,
                    TextAnchor.MiddleLeft));
                _mapHits.Add(Hit(t, "MapHit" + i, MapPageListLeft + 2, y,
                    MapPageListRight - MapPageListLeft - 4, MapPageRowHeight, 66, () => SelectMapRow(i)));
            }

            _mapScroll = MenuListScroll.Create(t, "MapScroll", MapPageScrollLeft, MapPagePanelTop + 2,
                15, MapPagePanelBottom - MapPagePanelTop - 4, 62);
            _mapScroll.SetHitRect(MapPageListLeft, MapPagePanelTop,
                MapPageListRight - MapPageListLeft, MapPagePanelBottom - MapPagePanelTop);
            _mapScroll.TopChanged = top =>
            {
                _mapTop = top;
                RefreshMapSelection();
            };

            _mapInfoName = AddText(canvas, "MapInfoName", string.Empty, MapPageListLeft, MapPageInfoTop,
                250, 20, 13, TextPale, TextAnchor.MiddleLeft);
            _mapInfoPlayers = AddText(canvas, "MapInfoPlayers", string.Empty, MapPageListLeft,
                MapPageInfoTop + 20, 250, 20, 12, TextPale, TextAnchor.MiddleLeft);
        }

        // ---------------------------------------------------------------- 页面切换

        public void ShowLobby()
        {
            _mapRoot.SetActive(false);
            _lobbyRoot.SetActive(true);
            RefreshLobby();
        }

        public void ShowMapSelection()
        {
            _lobbyRoot.SetActive(false);
            _mapRoot.SetActive(true);
            _mapTop = 0;
            if (_mapScroll != null) _mapScroll.SetTop(0);
            RefreshMapSelection();
        }

        /// <summary>外壳按钮回调；按键切换时顺带换按钮列。</summary>
        public void HandleButton(string key)
        {
            switch (key)
            {
                case "GUI:ChooseMap":
                    ShowMapSelection();
                    if (_requestButtons != null) _requestButtons("map");
                    break;

                case "GUI:UseMap":
                    ApplySelectedMap();
                    ShowLobby();
                    if (_requestButtons != null) _requestButtons("lobby");
                    break;

                case "GUI:Cancel":
                    ShowLobby();
                    if (_requestButtons != null) _requestButtons("lobby");
                    break;

                case "GUI:StartGame":
                    StartGame();
                    break;
            }
        }

        private void StartGame()
        {
            // 只有 GameMode=Skirmish 才有遭遇战大厅（对照 SessionClass::GameMode）
            if (!GameStatics.IsSkirmish)
            {
                BootLog.Log($"[遭遇战] GameMode={GameStatics.GameMode} 不是 Skirmish，忽略开始请求");
                return;
            }

            var map = SelectedMap;
            if (map == null)
            {
                BootLog.Log("[遭遇战] 还没有选择地图，无法开始");
                SetStatus(MenuStrings.Get("GUI:Error", "[遭遇战] 开局被拦下，详见 bootlog"));
                return;
            }

            if (_loadingActive && !_loadingDone)
            {
                BootLog.Log("[遭遇战] 正在载入中，忽略重复点击");
                return;
            }

            BeginLoading(map);
        }

        // ---------------------------------------------------------------- 加载屏

        /// <summary>
        /// 原版开局载入屏：黑底 + GUI:Loading + 地图名 + 进度条。
        /// 分阶段执行（每帧一步），阶段文案是流程名不是装饰；失败就停在错误行，不再往下走。
        /// </summary>
        private void BuildLoading()
        {
            _loadingRoot = new GameObject("LoadingRoot");
            _loadingRoot.transform.SetParent(_root, false);
            _loadingRoot.transform.localPosition = Vector3.zero;
            _loadingCanvas = AttachPageCanvas(_loadingRoot, 96).transform;

            MenuSceneObjects.Quad(_loadingCanvas, "LoadingBlack", MenuWidgets.Solid(new Color32(0, 0, 0, 255)),
                0, 0, MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight, 96, false);

            _loadingTitle = AddText(_loadingCanvas, "LoadingTitle", MenuStrings.Get("GUI:Loading", "Loading…"),
                0, 250, MenuSceneObjects.PlaneWidth, 30, 18, TextGold, TextAnchor.MiddleCenter);
            _loadingMapText = AddText(_loadingCanvas, "LoadingMap", string.Empty,
                0, 300, MenuSceneObjects.PlaneWidth, 24, 13, TextPale, TextAnchor.MiddleCenter);
            _loadingStepText = AddText(_loadingCanvas, "LoadingStep", string.Empty,
                0, 380, MenuSceneObjects.PlaneWidth, 22, 12, TextDim, TextAnchor.MiddleCenter);

            // 进度条：外框 + 内填充（填充宽度按比例改 RectTransform.sizeDelta）
            MenuSceneObjects.Quad(_loadingCanvas.parent, "LoadingBarFrame", MenuWidgets.Solid(new Color32(40, 20, 18, 255)),
                LoadingBarLeft - 2, LoadingBarTop - 2, LoadingBarWidth + 4, LoadingBarHeight + 4, 96, false);
            var fill = MenuSceneObjects.Quad(_loadingCanvas.parent, "LoadingBarFill", MenuWidgets.Solid(new Color32(190, 24, 16, 255)),
                LoadingBarLeft, LoadingBarTop, LoadingBarWidth, LoadingBarHeight, 96, false);
            _loadingBarFill = fill.transform;
            _loadingBarFill.localScale = new Vector3(0f, 1f, 1f);

            _loadingRoot.SetActive(false);
        }

        private void BeginLoading(SkirmishMapInfo map)
        {
            _loadingActive = true;
            _loadingDone = false;
            _loadingStepIndex = 0;
            _loadingNextStepAt = Time.unscaledTime;
            _loadingStepNames.Clear();
            _loadingStepNames.Add("读取规则表 rulesmd.ini");
            _loadingStepNames.Add("载入地图 + 地图专用配置");
            _loadingStepNames.Add("创建各阵营 HouseClass");
            _loadingStepNames.Add("解析类型表 + art(md).ini");
            _loadingStepNames.Add("生成开局实体（MCV / 超武）");
            _loadingStepNames.Add("完成");

            if (_loadingMapText != null)
                _loadingMapText.text = map.DisplayName + "  (" + map.FileName + ")";
            if (_loadingTitle != null)
                _loadingTitle.text = MenuStrings.Get("GUI:Loading", "Loading…");
            if (_loadingStepText != null)
                _loadingStepText.color = TextDim;
            SetBar(0f);

            if (_lobbyRoot != null) _lobbyRoot.SetActive(false);
            if (_mapRoot != null) _mapRoot.SetActive(false);
            if (_loadingRoot != null) _loadingRoot.SetActive(true);

            BootLog.Log("[遭遇战] 进入载入屏：地图=" + map.FileName + " 模式=" + map.DisplayName);
        }

        private void Update()
        {
            if (!_loadingActive || _loadingDone) return;
            if (Time.unscaledTime < _loadingNextStepAt) return;
            _loadingNextStepAt = Time.unscaledTime + LoadingStepHold;
            AdvanceLoading();
        }

        private void AdvanceLoading()
        {
            var index = _loadingStepIndex;
            var total = _loadingStepNames.Count;
            if (index < total && _loadingStepText != null) _loadingStepText.text = _loadingStepNames[index];

            bool ok = true;
            string failure = null;
            switch (index)
            {
                case 0:
                    // 先只加载规则（还没有地图，地图专用配置下一步随地图一起叠）
                    RulesDatabase.Load(null);
                    ArtDatabase.Load(null);
                    break;

                case 1:
                {
                    var map = SelectedMap;
                    if (map == null) { ok = false; failure = MenuStrings.Get("GUI:Error", "no map"); break; }
                    // 装配层一次做到底：地图装载 → 规则/地图覆盖 → HouseClass → 类型表 → 实体
                    var match = SkirmishMatch.Start(_selection, map, _catalog);
                    if (match == null) { ok = false; failure = MenuStrings.Get("GUI:Error", "[遭遇战] 开局被拦下，详见 bootlog"); }
                    else SetStatus(MenuStrings.Get("GUI:Blank", ""));
                    break;
                }

                case 2:
                    BootLog.Log("[载入屏] 阵营 HouseClass=" + (SkirmishMatch.Current != null ? SkirmishMatch.Current.RuntimeHouses.Count : 0)
                        + " 席，当前玩家=" + (SkirmishMatch.Current != null && SkirmishMatch.Current.PlayerHouseClass != null
                            ? "#" + SkirmishMatch.Current.PlayerHouseClass.ArrayIndex : "无"));
                    break;

                case 3:
                    BootLog.Log("[载入屏] 类型表 单位/建筑/子弹/特效/超武=" + TypeTables.UnitCount + "/" + TypeTables.BuildingCount
                        + "/" + TypeTables.BulletCount + "/" + TypeTables.AnimCount + "/" + TypeTables.SuperCount
                        + "，art 记录=" + (ArtDatabase.Current != null ? "已载入" : "缺失"));
                    break;

                case 4:
                    BootLog.Log("[载入屏] 实体 " + WorldObjects.Summary());
                    break;
            }

            if (!ok)
            {
                _loadingActive = false;
                _loadingDone = true;
                if (_loadingStepText != null)
                {
                    _loadingStepText.color = new Color32(220, 60, 48, 255);
                    _loadingStepText.text = failure ?? MenuStrings.Get("GUI:Error", "error");
                }
                return;
            }

            _loadingStepIndex = index + 1;
            if (_loadingStepIndex >= total - 1)
            {
                _loadingDone = true;
                _loadingActive = false;
                SetBar(1f);
                var matchNow = SkirmishMatch.Current;
                if (matchNow != null)
                {
                    TerrainRenderer.Init(matchNow);
                    // 整张地图一次性光栅化成一张纹理，之后视口只平移画布（MapRoot），不重绘。
                    if (World.MapCanvas.Build(matchNow))
                    {
                        var view = World.MapCanvasView.Instance;
                        view.SetTexture(World.MapCanvas.Texture, World.MapCanvas.Width, World.MapCanvas.Height);
                        view.CenterOn(World.MapCanvas.CenterPoint());
                        var presenter = Gfx.FramePresenter.Instance;
                        if (presenter != null) presenter.SetVisible(false);
                        World.MapObjectLayer.Instance.Build(matchNow);
                        // 控制层 + 覆盖 UI / 小地图：此刻 HouseClass 与 MCV 都已建好、视图已挂进画布
                        var _ = World.PlayerController.Instance;
                        World.GameHud.Instance.gameObject.SetActive(true);
                        // 视口开局对准玩家的基地车（没有出生点才落回地图中心）
                        if (matchNow.PlayerHouse != null && matchNow.PlayerHouse.StartX >= 0)
                            view.CenterOn(new Vector2(
                                World.MapCanvas.CanvasX(matchNow.PlayerHouse.StartX, matchNow.PlayerHouse.StartY),
                                World.MapCanvas.CanvasY(matchNow.PlayerHouse.StartX, matchNow.PlayerHouse.StartY,
                                    World.MapCanvas.LevelAt(matchNow.PlayerHouse.StartX, matchNow.PlayerHouse.StartY))));
                    }
                }
                if (_loadingStepText != null)
                {
                    _loadingStepText.color = TextNotice;
                    _loadingStepText.text = "载入完成：" + WorldObjects.Summary() + " · " + TerrainRenderer.Status
                        + " · " + World.MapObjectLayer.Instance.Status;
                }
                BootLog.Log("[载入屏] 完成 " + WorldObjects.Summary() + " · " + TerrainRenderer.Status);
                return;
            }

            SetBar(_loadingStepIndex / (float)(total - 1));
        }

        private void SetBar(float ratio)
        {
            if (_loadingBarFill == null) return;
            var r = Mathf.Clamp01(ratio);
            _loadingBarFill.localScale = new Vector3(r, 1f, 1f);
            // Quad 以父平面左下角为原点、中心为缩放轴：左边界固定，位置随宽度右移半宽。
            _loadingBarFill.localPosition = new Vector3(
                LoadingBarLeft + LoadingBarWidth * r * 0.5f,
                MenuSceneObjects.PlaneHeight - LoadingBarTop - LoadingBarHeight * 0.5f,
                0f);
        }

        /// <summary>底部状态行（DLU 2,355 410×12）。文案一律走 CSF，缺条才用兜底。</summary>
        private void SetStatus(string text)
        {
            if (_statusText == null) return;
            _statusText.text = text ?? string.Empty;
        }

        private SkirmishMapInfo SelectedMap
        {
            get
            {
                if (_selection.MapIndex < 0 || _selection.MapIndex >= _catalog.Maps.Count) return null;
                return _catalog.Maps[_selection.MapIndex];
            }
        }

        private int MapSlotLimit
        {
            get
            {
                var map = SelectedMap;
                return map != null ? map.SlotLimit : SkirmishLimits.SlotCapacity;
            }
        }

        private void ApplySelectedMap()
        {
            var map = SelectedMap;
            if (map == null) return;

            _selection.ApplyMapLimit(map.SlotLimit);
            _gameModeIndex = 0;
            _selection.GameMode = map.Modes.Count > 0 ? map.Modes[0] : null;
            if (_slotScroll != null) _slotScroll.SetTop(0);
            BootLog.Log($"[遭遇战] 选图 {map.FileName} 可加入人数 {map.SlotLimit}（起始点 {map.StartPointCount}）");
        }

        // ---------------------------------------------------------------- 大厅刷新

        private void RefreshLobby()
        {
            if (_lobbyRoot == null || !_lobbyRoot.activeSelf) return;

            var limit = MapSlotLimit;
            if (_slotScroll != null) _slotScroll.SetCount(SkirmishLimits.SlotCapacity, RowsVisible);

            for (var row = 0; row < RowsVisible; row++)
            {
                var index = _slotTop + row;
                var widget = _rows[row];
                var has = index >= 0 && index < _selection.Slots.Count;

                SetVisible(widget.PlayerCheck, has);
                SetVisible(widget.Name.gameObject, has);
                SetVisible(widget.Country.gameObject, has);
                SetVisible(widget.Swatch, has);
                SetVisible(widget.Start.gameObject, has);
                SetVisible(widget.Team.gameObject, has);
                if (!has) continue;

                var slot = _selection.Slots[index];
                var available = index < limit;
                var active = available && slot.Enabled;

                MenuSceneObjects.SetVisible(widget.PlayerCheck, active);

                var color = active ? TextGold : TextDim;

                // 玩家格：0 号本地玩家；其余是电脑（显示难度）；未开放显示 ---
                string name;
                if (!available) name = "---";
                else if (slot.Human) name = MenuStrings.Get("GUI:You", "Commander");
                else if (!active) name = MenuStrings.Get("GUI:None", "---");
                else name = DifficultyName(slot.Difficulty);
                widget.Name.text = name;
                widget.Name.color = color;

                var country = _catalog.FindCountry(slot.CountryId);
                widget.Country.text = active && country != null ? country.DisplayName : string.Empty;
                widget.Country.color = color;

                var swatch = _catalog.FindColor(slot.ColorId);
                MenuSceneObjects.SetTexture(widget.Swatch,
                    active && swatch != null ? SwatchTexture(swatch.Swatch, swatch.Id) : _texGold);

                widget.Start.text = active
                    ? (slot.StartPosition <= 0 ? MenuStrings.Get("GUI:Unknown", "???")
                        : slot.StartPosition.ToString())
                    : string.Empty;
                widget.Start.color = color;

                widget.Team.text = active ? TeamLabel(slot.Team) : string.Empty;
                widget.Team.color = color;
            }

            var map = SelectedMap;
            if (_modeText != null)
                _modeText.text = map != null && map.Modes.Count > 0
                    ? MenuStrings.Get("GUI:" + map.Modes[_gameModeIndex], map.Modes[_gameModeIndex])
                    : MenuStrings.Get("GUI:None", "--");
            if (_previewImage != null)
                MenuSceneObjects.SetTexture(_previewImage,
                    map != null && map.Preview != null ? map.Preview : MenuSceneObjects.TransparentTexture);
            if (_mapNameText != null)
                _mapNameText.text = map != null ? map.DisplayName : MenuStrings.Get("GUI:None", "---");
            if (_mapCountText != null)
                _mapCountText.text = map != null
                    ? map.MinPlayers + "-" + map.SlotLimit + " " + MenuStrings.Get("GUI:Players", "Players")
                    : string.Empty;

            if (_speedStep != null) _speedStep.SetLabel(_selection.GameSpeed.ToString());
            if (_creditsStep != null) _creditsStep.SetLabel(_selection.StartingCredits.ToString());
            if (_unitsStep != null) _unitsStep.SetLabel(_selection.UnitCount.ToString());
        }

        /// <summary>0x6ec 的模式轮换：只在本图 [Basic] GameMode 声明的模式里转，不越界。</summary>
        private void CycleGameMode()
        {
            var map = SelectedMap;
            if (map == null || map.Modes.Count == 0) return;
            _gameModeIndex = (_gameModeIndex + 1) % map.Modes.Count;
            _selection.GameMode = map.Modes[_gameModeIndex];
            BootLog.Log("[遭遇战] 模式 -> " + _selection.GameMode);
            RefreshLobby();
        }

        private string DifficultyName(int difficulty)
        {
            switch (difficulty)
            {
                case 0: return MenuStrings.Get("GUI:Easy", "Easy");
                case 2: return MenuStrings.Get("GUI:Hard", "Hard");
                default: return MenuStrings.Get("GUI:Normal", "Normal");
            }
        }

        private string TeamLabel(string team)
        {
            var value = 0;
            int.TryParse(team, out value);
            // 原版未组队时该格显示「--」（不是 CSF 文案，是符号占位）
            if (value <= 0) return "--";
            return ((char)('A' + Math.Min(3, value - 1))).ToString();
        }

        // ---------------------------------------------------------------- 选图页刷新

        private void RefreshMapSelection()
        {
            if (_mapRoot == null || !_mapRoot.activeSelf) return;

            RebuildModeFilter();

            for (var row = 0; row < MapPageRows; row++)
            {
                var hasMode = row < _modes.Count;
                SetVisible(_modeTexts[row].gameObject, hasMode);
                SetVisible(_modeBars[row], hasMode && row == _modeIndex);
                if (hasMode)
                {
                    _modeTexts[row].text = _modes[row];
                    _modeTexts[row].color = row == _modeIndex ? TextGold : TextPale;
                }

                var filtered = _mapTop + row;
                var hasMap = filtered >= 0 && filtered < _filtered.Count;
                SetVisible(_mapTexts[row].gameObject, hasMap);
                SetVisible(_mapHits[row], hasMap);
                var selected = hasMap && _filtered[filtered] == _selection.MapIndex;
                SetVisible(_mapBars[row], selected);
                if (!hasMap) continue;

                var map = _catalog.Maps[_filtered[filtered]];
                _mapTexts[row].text = map.DisplayName;
                _mapTexts[row].color = selected ? TextGold : TextPale;
            }

            if (_mapScroll != null) _mapScroll.SetCount(_filtered.Count, MapPageRows);

            if (_mapPreviewImage != null)
            {
                var cur = SelectedMap;
                MenuSceneObjects.SetTexture(_mapPreviewImage,
                    cur != null && cur.Preview != null ? cur.Preview : MenuSceneObjects.TransparentTexture);
            }

            var current = SelectedMap;
            if (_mapInfoName != null) _mapInfoName.text = current != null ? current.DisplayName : string.Empty;
            if (_mapInfoPlayers != null)
            {
                _mapInfoPlayers.text = current != null
                    ? current.MinPlayers + "-" + current.SlotLimit + " " +
                      MenuStrings.Get("GUI:Players", "Players")
                    : string.Empty;
            }
        }

        /// <summary>模式列来自地图 [Basic] GameMode；模式集合变了才重建过滤结果。</summary>
        private void RebuildModeFilter()
        {
            var modes = new List<string>();
            for (var i = 0; i < _catalog.Maps.Count; i++)
            {
                var map = _catalog.Maps[i];
                for (var m = 0; m < map.Modes.Count; m++)
                {
                    if (!modes.Contains(map.Modes[m])) modes.Add(map.Modes[m]);
                }
            }
            if (modes.Count == 0) modes.Add(MenuStrings.Get("GUI:Standard", "Standard"));

            var same = modes.Count == _modes.Count;
            for (var i = 0; i < _modes.Count && same; i++)
            {
                if (_modes[i] != modes[i]) same = false;
            }
            if (!same)
            {
                _modes.Clear();
                _modes.AddRange(modes);
            }
            _modeIndex = Mathf.Clamp(_modeIndex, 0, _modes.Count - 1);

            _filtered.Clear();
            for (var i = 0; i < _catalog.Maps.Count; i++)
            {
                var map = _catalog.Maps[i];
                if (map.Modes.Count == 0 || map.Modes.Contains(_modes[_modeIndex])) _filtered.Add(i);
            }
        }

        // ---------------------------------------------------------------- 交互

        private void ToggleSlot(int row)
        {
            var index = _slotTop + row;
            if (index < 0 || index >= _selection.Slots.Count) return;
            if (index >= MapSlotLimit) return;              // 地图未开放该席

            var slot = _selection.Slots[index];
            if (slot.Human) return;                          // 本地玩家不能关
            slot.Enabled = !slot.Enabled;
            _selection.ApplyMapLimit(MapSlotLimit);
            RefreshLobby();
        }

        private void OpenPlayerCell(int row)
        {
            var index = _slotTop + row;
            if (index < 0 || index >= _selection.Slots.Count) return;
            var slot = _selection.Slots[index];
            if (slot.Human || !slot.Enabled) return;

            OpenDropdown(Cell.Player, index, ColPlayer + 20, RowTop + row * RowHeight,
                ColPlayerRight - ColPlayer - 24);
        }

        private void OpenCell(Cell cell, int row)
        {
            var index = _slotTop + row;
            if (index < 0 || index >= _selection.Slots.Count) return;
            var slot = _selection.Slots[index];
            if (!slot.Enabled || index >= MapSlotLimit) return;

            int left, width;
            switch (cell)
            {
                case Cell.Country: left = ColFlag; width = ColCountryRight - ColFlag; break;
                case Cell.Color: left = ColColor; width = ColColorRight - ColColor; break;
                case Cell.Start: left = ColStart; width = ColStartRight - ColStart; break;
                default: left = ColTeam; width = ColTeamRight - ColTeam; break;
            }
            OpenDropdown(cell, index, left, RowTop + row * RowHeight, width);
        }

        private void SelectMode(int row)
        {
            if (row < 0 || row >= _modes.Count) return;
            _modeIndex = row;
            RebuildModeFilter();
            _mapTop = 0;
            if (_mapScroll != null) _mapScroll.SetTop(0);
            RefreshMapSelection();
        }

        private void SelectMapRow(int row)
        {
            var filtered = _mapTop + row;
            if (filtered < 0 || filtered >= _filtered.Count) return;
            _selection.MapIndex = _filtered[filtered];
            RefreshMapSelection();
        }

        // ---------------------------------------------------------------- 下拉

        private void OpenDropdown(Cell cell, int slotIndex, int left, int top, int width)
        {
            CloseDropdown();

            var slot = _selection.Slots[slotIndex];
            var options = new List<string>();
            var selected = 0;

            switch (cell)
            {
                case Cell.Player:
                    options.Add(MenuStrings.Get("GUI:Easy", "Easy"));
                    options.Add(MenuStrings.Get("GUI:Normal", "Normal"));
                    options.Add(MenuStrings.Get("GUI:Hard", "Hard"));
                    selected = Mathf.Clamp(slot.Difficulty, 0, options.Count - 1);
                    break;

                case Cell.Country:
                    for (var i = 0; i < _catalog.Countries.Count; i++)
                        options.Add(_catalog.Countries[i].DisplayName);
                    selected = Mathf.Max(0, _catalog.IndexOfCountry(slot.CountryId));
                    break;

                case Cell.Color:
                    for (var i = 0; i < _catalog.Colors.Count; i++)
                        options.Add(_catalog.Colors[i].DisplayName);
                    selected = Mathf.Max(0, _catalog.IndexOfColor(slot.ColorId));
                    break;

                case Cell.Start:
                    options.Add(MenuStrings.Get("GUI:Unknown", "???"));
                    for (var i = 1; i <= MapSlotLimit; i++) options.Add(i.ToString());
                    selected = Mathf.Clamp(slot.StartPosition, 0, options.Count - 1);
                    break;

                default:
                    options.Add(MenuStrings.Get("GUI:None", "None"));
                    options.Add("A"); options.Add("B"); options.Add("C"); options.Add("D");
                    var team = 0;
                    int.TryParse(slot.Team, out team);
                    selected = Mathf.Clamp(team, 0, options.Count - 1);
                    break;
            }

            _ddOptions = options;
            _ddPick = idx => PickOption(cell, slotIndex, idx);

            var rowHeight = 20;
            var visible = Mathf.Min(options.Count, 8);
            var height = visible * rowHeight + 2;
            var ddTop = top + RowHeight;
            if (ddTop + height > MenuSceneObjects.PlaneHeight - 6) ddTop = Mathf.Max(6, top - height);

            _dropdown = new GameObject("SkirmishDropdown");
            _dropdown.transform.SetParent(_root, false);
            _dropdown.transform.localPosition = Vector3.zero;
            var dd = _dropdown.transform;
            _ddCanvas = AttachPageCanvas(_dropdown, 80);

            // 空白处点击关闭
            Hit(dd, "Backdrop", 0, 0, MenuSceneObjects.PlaneWidth, MenuSceneObjects.PlaneHeight,
                76, CloseDropdown);

            MenuWidgets.Panel(dd, "Frame", left, ddTop, width, height,
                MenuWidgets.FrameLight, PanelFill, 78);

            _ddTexts.Clear();
            _ddHits.Clear();
            for (var i = 0; i < visible; i++)
            {
                var y = ddTop + 1 + i * rowHeight;
                _ddTexts.Add(AddText(_ddCanvas, "Opt" + i, string.Empty, left + 5, y, width - 20,
                    rowHeight, 12, TextPale, TextAnchor.MiddleLeft));
                var rowIndex = i;
                _ddHits.Add(Hit(dd, "OptHit" + i, left + 1, y, width - 18, rowHeight, 82,
                    () => PickDropdownRow(rowIndex)));
            }

            _ddScroll = null;
            _ddFirst = 0;
            if (options.Count > visible)
            {
                _ddScroll = MenuListScroll.Create(dd, "DdScroll", left + width - 16, ddTop + 2,
                    15, height - 4, 84);
                _ddScroll.SetHitRect(left, ddTop, width, height);
                _ddScroll.SetCount(options.Count, visible);
                _ddScroll.SetTop(Mathf.Clamp(selected - visible / 2, 0, Mathf.Max(0, options.Count - visible)));
                _ddFirst = _ddScroll.Top;
                _ddScroll.TopChanged = first =>
                {
                    _ddFirst = first;
                    RefreshDropdown();
                };
            }

            RefreshDropdown();
        }

        private void RefreshDropdown()
        {
            if (_ddOptions == null) return;
            for (var i = 0; i < _ddTexts.Count; i++)
            {
                var index = _ddFirst + i;
                var has = index >= 0 && index < _ddOptions.Count;
                SetVisible(_ddTexts[i].gameObject, has);
                SetVisible(_ddHits[i], has);
                if (has) _ddTexts[i].text = _ddOptions[index];
            }
        }

        private void PickDropdownRow(int row)
        {
            var index = _ddFirst + row;
            if (_ddPick == null || _ddOptions == null) return;
            if (index < 0 || index >= _ddOptions.Count) return;

            var pick = _ddPick;
            CloseDropdown();
            pick(index);
        }

        private void PickOption(Cell cell, int slotIndex, int index)
        {
            var slot = _selection.Slots[slotIndex];
            switch (cell)
            {
                case Cell.Player:
                    slot.Difficulty = Mathf.Clamp(index, 0, 2);
                    break;
                case Cell.Country:
                    if (index >= 0 && index < _catalog.Countries.Count) slot.CountryId = _catalog.Countries[index].Id;
                    break;
                case Cell.Color:
                    if (index >= 0 && index < _catalog.Colors.Count) slot.ColorId = _catalog.Colors[index].Id;
                    break;
                case Cell.Start:
                    slot.StartPosition = index;
                    break;
                case Cell.Team:
                    slot.Team = index.ToString();
                    break;
            }
            RefreshLobby();
        }

        private void CloseDropdown()
        {
            if (_dropdown != null)
            {
                Destroy(_dropdown);
                _dropdown = null;
            }
            _ddCanvas = null;
            _ddTexts.Clear();
            _ddHits.Clear();
            _ddOptions = null;
            _ddPick = null;
            _ddScroll = null;
            _ddFirst = 0;
        }

        private void OnDestroy()
        {
            CloseDropdown();
        }

        // ---------------------------------------------------------------- 选项数值

        private void SetStepper(int kind, float ratio)
        {
            switch (kind)
            {
                case 0:
                    _selection.GameSpeed = Mathf.RoundToInt(Mathf.Clamp01(ratio) * SpeedMax);
                    break;
                case 1:
                    var index = Mathf.RoundToInt(Mathf.Clamp01(ratio) * (CreditSteps.Length - 1));
                    _selection.StartingCredits = CreditSteps[Mathf.Clamp(index, 0, CreditSteps.Length - 1)];
                    break;
                default:
                    _selection.UnitCount = Mathf.RoundToInt(Mathf.Clamp01(ratio) * UnitsMax);
                    break;
            }
            if (_speedStep != null) _speedStep.SetLabel(_selection.GameSpeed.ToString());
            if (_creditsStep != null) _creditsStep.SetLabel(_selection.StartingCredits.ToString());
            if (_unitsStep != null) _unitsStep.SetLabel(_selection.UnitCount.ToString());
        }

        private float NormalizedOf(int kind)
        {
            switch (kind)
            {
                case 0: return Mathf.Clamp01(_selection.GameSpeed / (float)SpeedMax);
                case 1:
                    var index = 0;
                    for (var i = 0; i < CreditSteps.Length; i++)
                        if (CreditSteps[i] <= _selection.StartingCredits) index = i;
                    return index / (float)(CreditSteps.Length - 1);
                default: return Mathf.Clamp01(_selection.UnitCount / (float)UnitsMax);
            }
        }

        // ---------------------------------------------------------------- 小工具

        private Text AddText(Transform canvasParent, string name, string content, int x, int yTop,
            int width, int height, int size, Color32 color, TextAnchor anchor, Action onClick = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvasParent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(
                x + width * 0.5f - MenuSceneObjects.PlaneWidth * 0.5f,
                MenuSceneObjects.PlaneHeight * 0.5f - yTop - height * 0.5f);

            var text = go.AddComponent<Text>();
            text.text = content;
            text.font = MenuFonts.Chinese();
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            if (onClick != null)
            {
                var hit = MenuSceneObjects.Quad(canvasParent.parent, name + "_Hit",
                    MenuSceneObjects.TransparentTexture, x, yTop, width, height, 76, true);
                var drag = hit.AddComponent<MenuDragArea>();
                drag.Configure(x, yTop, width, height, false);
                drag.OnClicked = onClick;
            }
            return text;
        }

        private static GameObject Hit(Transform parent, string name, int left, int top, int width, int height,
            int order, Action onClick)
        {
            var go = MenuSceneObjects.Quad(parent, name, MenuSceneObjects.TransparentTexture,
                left, top, width, height, order, true);
            var drag = go.AddComponent<MenuDragArea>();
            drag.Configure(left, top, width, height, false);
            drag.OnClicked = onClick;
            return go;
        }

        private static void SetVisible(GameObject go, bool visible)
        {
            if (go != null) MenuSceneObjects.SetVisible(go, visible);
        }
    }
}
















