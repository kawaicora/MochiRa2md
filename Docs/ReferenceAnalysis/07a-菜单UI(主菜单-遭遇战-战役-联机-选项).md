# 07a 菜单 UI：主菜单 / 遭遇战 / 战役 / 局域网 / 互联网 / 选项 / 结算 / 加载

## 本章覆盖范围

本章梳理参考引擎外壳菜单（Shell / Front-end）一侧的导航结构、控件树、几何常量、三态按钮与动画、位图文字排版、输入路由、会话结果契约、菜单音乐与音效接入，以及菜单绘制命令与 CPU/GPU 两条提交路径。

- 入口结论：主菜单退出枚举 `RuntimeMainMenuResult`，遭遇战返回 `RuntimeSkirmishSelection`，战役返回 `RuntimeCampaignSelection`，联机返回 `RuntimeMultiplayerMenuResult`（`src/runtime/contracts/RuntimeMainMenuResult.h:3`、`src/runtime/contracts/RuntimeSkirmishSelection.h:18`、`src/runtime/contracts/RuntimeCampaignSelection.h:7`、`src/runtime/contracts/RuntimeMultiplayerMenuTypes.h:34`）。
- 解码细节（CSF、FNT、Bink/VQA）不在本章重复展开，统一引用 [05b-文字与视频解码(CSF-FNT-Bink).md](05b-文字与视频解码(CSF-FNT-Bink).md)。

### 对应源码目录（本章负责域）

| 域 | 路径 |
| --- | --- |
| 菜单控制器 | `src/runtime/menu/` |
| 主菜单视图 | `src/ui/main_menu/` |
| 遭遇战视图 | `src/ui/skirmish/` |
| 战役视图 | `src/ui/campaign/` |
| 联机视图 | `src/ui/network/` |
| 选项持久化 | `src/ui/settings/` |
| 结算页 | `src/ui/results/` |
| 加载页 | `src/ui/loading/` |
| 绘制命令 / 表面 / 颜色 / 时钟 / 资源 | `src/ui/drawing/`、`src/ui/surface/`、`src/ui/values/`、`src/ui/time/`、`src/ui/resources/` |
| 菜单文字 | `src/ui/text/`（菜单部分） |
| 菜单音频 | `src/ui/audio/`、`src/audio/menu/` |
| 菜单位图资源 | `src/assets/menu/`、`src/assets/preview/` |

### 接口边界（归另一代理，本章仅标注）

- `src/ui/sidebar/`、`src/ui/hud/`、`src/ui/selection/`、`src/ui/radar/`、`src/ui/cursor/`、`src/ui/input/` 归战斗内 UI 域。
- 本章仅引用其类型：像素点 / 矩形 `RuntimeInputPixelPoint`、`RuntimeInputPixelRect` 定义于 `src/ui/input/RuntimeInputPixelValues.h`；鼠标光标判断在主菜单/遭遇战控制器中以 `mouseVisible`、`hover`、`pressed` 三个状态量表达，光标资源句柄的归属与绘制由输入域负责。

---

## 1. 菜单整体层级与导航树

### 1.1 顶层导航树

```text
主菜单 Home (MenuPage::Home)
├─ 单人 Single Player (MenuPage::SinglePlayer)
│   ├─ 战役 Campaign            → RuntimeMainMenuResult::Campaign
│   ├─ 载入存档 Load Game        → RuntimeMainMenuResult::LoadGame
│   └─ 遭遇战 Skirmish           → RuntimeMainMenuResult::Skirmish
├─ 互联网 Internet              → RuntimeMainMenuResult::Internet
├─ 局域网 Network                → RuntimeMainMenuResult::Network
├─ 影片与致谢 Movies & Credits (MenuPage::Movies)
│   └─ 致谢 Credits (MenuPage::Credits)
├─ 选项 Options (MenuPage::Options)
└─ 退出 Exit                    → RuntimeMainMenuResult::Exit
```

页面枚举 `MenuPage`：`Home / SinglePlayer / Movies / Credits / Options`（`src/runtime/menu/RuntimeMainMenuController.h:43`）。
动作枚举 `MenuAction`：`OpenSinglePlayer / OpenHome / OpenMovies / OpenCredits / OpenOptions / PreviousMovie / PlayMovie / NextMovie / PreviousCreditsPage / NextCreditsPage / CycleResolution / CycleTargetFps / CycleGameSpeed / ApplyOptions / Campaign / LoadGame / Skirmish / Network / Internet / Exit`（`src/runtime/menu/RuntimeMainMenuController.h:51`）。

### 1.2 主菜单控制器状态机

控制器结构 `RuntimeMainMenuController`，持有服务端口 `RuntimeMainMenuServices`、演示时钟 `RuntimeUiClock*`、配置 `RuntimeConfig`、结果 `RuntimeMainMenuResult`、页面 `page`、阶段 `phase`、挂起动作 `pending`、焦点 `focus` / 悬停 `hover` / 按下 `pressed`、视口 `viewLeft/Top/Width/Height` 等状态（`src/runtime/menu/RuntimeMainMenuController.h:116`）。

**阶段（动画）状态机 `MenuPhase`**：`SlideIn → Idle → SlideOut`（`src/runtime/menu/RuntimeMainMenuController.h:50`）。

阶段迁移与切换条件（`src/runtime/menu/RuntimeMainMenuController.cpp`）：

| 步骤 | 函数 / 行号 | 行为 |
| --- | --- | --- |
| 1 入口初始化 | `resetMainMenuForEntry` `:7` | 载入 config；按 `startPage` 选 `Home` / `SinglePlayer`；初始化选项副本（窗口尺寸、目标 FPS、游戏速度）；音乐音量钳制 0..100 |
| 2 动作挂起 | `beginAction` `:331` | 仅 `Idle` 且未关闭时进入；`Skirmish` 先 `prewarmSkirmish()`；置 `pending`、`phase = SlideOut`，记录 `phaseStarted`，播放 `MenuSlideOut` |
| 3 即时动作 | `performImmediateAction` `:297` | 影片上一条/下一条、影片播放、致谢翻页、分辨率/FPS/速度循环，不离开当前页；其余动作落到 `beginAction` |
| 4 阶段推进 | `updateAnimation` `:358` | 按 `kAnimationFrameMs`（42 ms）计数帧；时长 = `(slotCount - 1 + kAnimationFrames) * kAnimationFrameMs`；`SlideIn` 结束转 `Idle`；`SlideOut` 结束后按 `pending` 提交结果或切页 |
| 5 结果提交 | `finishMenu` `:210` | 置 `closing`、写 `result`、`stopVideo()`、`stopMusic()`、`destroyWindow()`，幂等（`closing` 已真则直接返回） |
| 6 切页回放 | `updateAnimation` `:371-451` | 不关闭的动作（`OpenSinglePlayer/OpenMovies/OpenCredits/OpenOptions/OpenHome`）改写 `page`，清焦点/悬停，回 `SlideIn`，`startPageMusic()` 并播放 `MenuSlideIn`；`ApplyOptions` 先 `savePreferences`，成功后 `resizeHost()` 回 `Home`，失败把错误写入 `pageStatus` |

Esc 动作按页面回退：`Home → Exit`；`Credits → OpenMovies`；`SinglePlayer / Movies / Options → OpenHome`（`escapeActionForPage` `:223`）。

**主菜单退出结果枚举**（`src/runtime/contracts/RuntimeMainMenuResult.h:3`）：

| 字段 | 取值 | 含义 |
| --- | --- | --- |
| `RuntimeMainMenuResult` | `Exit` | 退出外壳 |
| | `Campaign` | 进入战役选择器 |
| | `LoadGame` | 进入存档浏览器 |
| | `Skirmish` | 进入遭遇战大厅 |
| | `Network` | 进入局域网联机 |
| | `Internet` | 进入互联网联机 |
| `RuntimeMainMenuStartPage` | `Home` / `SinglePlayer` | 外壳起始页（`RuntimeMainMenuResult.h:12`） |

### 1.3 遭遇战控制器子页面状态机

`SkirmishPage`：`Lobby / MapSelection / RandomMap / RandomMapLoad / RandomMapPreview`（`src/runtime/menu/RuntimeSkirmishController.h:126`）。
外壳阶段 `ShellMenuPhase`：`SlideIn / Idle / SlideOut`（`src/runtime/menu/RuntimeSkirmishController.h:134`），推进逻辑与主菜单同构（`src/runtime/menu/RuntimeSkirmishController.cpp:547`）。

- 大厅 `Lobby`：模式列表、地图列表、玩家席位表、选项滑块与开关、开始 / 返回。
- 选图 `MapSelection`：地图预览、起始点标记、确认使用该图或返回。
- 随机地图 `RandomMap`：参数选项、产生 / 替我挑选 / 使用 / 保存 / 载入 / 预览 / 取消。
- `RandomMapLoad`：已存随机地图列表；`RandomMapPreview`：生成图大图预览。

### 1.4 战役控制器子页面状态机

`CampaignPage`：`Campaigns / Missions / Difficulty / Briefing / Saves`（`src/runtime/menu/RuntimeCampaignPresentationData.h:25`）。

页面推进（`src/runtime/menu/RuntimeCampaignMenuController.cpp`）：

| 当前页 | 确认（Activate） | 返回（GoBack） | 出处 |
| --- | --- | --- | --- |
| `Campaigns` | 选中战役 → `Missions` | 关闭外壳 | `:675` / `:726` |
| `Missions` | 已解锁任务 → 选难度；带地图选择图时先跳过 VQA 演出 | 回 `Campaigns` | `:678-707` / `:729` |
| `Difficulty` | → `Briefing` | 回 `Missions` | `:709` / `:732` |
| `Briefing` | `acceptMission` 提交 | 回 `Difficulty` | `:711` / `:735` |
| `Saves` | `acceptSave` 提交 | 关闭外壳 | `:714` / `:725` |

战役地图选择演出阶段 `CampaignMapPresentationPhase`：`Unavailable / VqaIntro / OverlayFade / Interactive / FallbackList`（`src/runtime/menu/RuntimeCampaignPresentationData.h:44`），VQA/点击图/语音/覆盖层/动画帧均属该演示状态（`:60-102`）。

### 1.5 联机控制器页面状态机

**局域网 `MultiplayerMenuPage`**：`Browser / Lobby`（`src/runtime/menu/RuntimeLanMenuController.h:10`）。

**互联网 `InternetMenuPage`**：`Account / Community / Browser / Ladder / News / Lobby / Kicked / MatchHistory`（`src/runtime/menu/RuntimeInternetMenuController.h:15`）。页内通过 `showInternetMenuPage` 切换控件可见性（`src/runtime/menu/RuntimeInternetMenuController.cpp:15`）；未认证时从 `Lobby` / `Kicked` 回到 `Account`（`:1223-1225`）。

---

## 2. 主菜单页面控件树

### 2.1 整体几何常量（原值照抄）

| 常量 | 值 | 出处 |
| --- | --- | --- |
| `kWidth` / `kHeight` | `800` / `600` | `src/runtime/menu/RuntimeMainMenuController.h:14-15` |
| `kLeftWidth` | `632` | `:16` |
| `kStatusTop` | `568` | `:17` |
| `kSidebarLeft` | `632` | `:18` |
| `kSidebarTopHeight` | `199` | `:19` |
| `kSlotHeight` | `42` | `:20` |
| `kSlotCount` | `8` | `:21` |
| `kSidebarBottomTop` | `535` | `:22` |
| `kMenuButtonLeft` | `653` | `:23` |
| `kMenuButtonTop` | `207` | `:24` |
| `kBottomButtonTop` | `543` | `:25` |
| `kBottomHitTop` | `535` | `:26` |
| `kWarningLeft` / `kWarningTop` | `670` / `48` | `:27-30` |
| `kAnimationFrames` / `kAnimationFrameMs` | `6` / `42` | `:31-32` |
| `kCreditsLinesPerPage` | `18` | `:33` |
| 分辨率循环表 | `800,600`；`1024,640`；`1024,768`；`1280,720`；`1600,900` | `:34-40` |
| 目标 FPS 循环表 | `30 / 60 / 120 / 144` | `:41` |

命中区间：按钮列 `x ∈ [sidebar+12, sidebar+158)`；普通槽行 `y ∈ [top + slot*height, +height)`；底部 `SDBTM` 按钮 `y ∈ [kBottomHitTop, kHeight)`（`src/runtime/menu/RuntimeMainMenuController.cpp:189-208`）。

视口适配：取 `min(clientW/800, clientH/600)` 的整数倍为整数缩放，不足 1 倍用比例缩放；居中并按 `clientToBase` 反算回 800×600 基准（`src/runtime/menu/RuntimeMainMenuController.cpp:153-187`）。

### 2.2 主菜单（Home）控件树

控件来源：`buttons(state)` 按页面构造 `ButtonSpec{slot, label, tooltip, action}`（`src/runtime/menu/RuntimeMainMenuController.cpp:29`）。

| # | 控件 | 类型 | 几何（基准坐标） | 文字键（CSF 键 / 回退） | 动作 | 出处 |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | Single Player | 槽按钮 | slot 0，`y = 207` | `GUI:NewCampaign` 对应 `GUI:SinglePlayer`；提示 `STT:MainButtonSinglePlayer` | `OpenSinglePlayer` | `:99-101` |
| 1 | Internet | 槽按钮 | slot 1，`y = 249` | `GUI:Internet`；提示 `STT:MainButtonWWOnline` | `Internet` | `:102-104` |
| 2 | Network | 槽按钮 | slot 2，`y = 291` | `GUI:Network`；提示 `STT:MainButtonNetwork` | `Network` | `:105-107` |
| 3 | Movies & Credits | 槽按钮 | slot 3，`y = 333` | `GUI:MoviesAndCredits`；提示 `STT:MainButtonMovies` | `OpenMovies` | `:108-110` |
| 4 | Options | 槽按钮 | slot 4，`y = 375` | `GUI:Options`；提示 `STT:MainButtonOptions` | `OpenOptions` | `:111-113` |
| -1 | Exit Game | 底部独立按钮 | `y ∈ [535,600)`，按钮顶 `543` | `GUI:ExitGame`；提示 `STT:MainButtonExitGamemd` | `Exit` | `:114-115` |

其余固定元素：背景视频/静态回退帧 `videoFrame/staticFallback/mainShell`；状态栏 `statusBar`（`y = 568` 起）；侧栏顶图 `sideTop.front()`；警告动画 `warning[warningFrame]`（`kWarningLeft, kWarningTop`）；8 槽底图 `slotBackground`；侧栏底图 `sideBottom`；槽位扫光动画 `slotAnimation[frame]`；红色三态按钮图 `redButton[]`；鼠标光标 `mouse`；标题；底部提示条（`src/ui/main_menu/RuntimeMainMenu.cpp` 渲染段，见 `src/ui/main_menu/RuntimeMainMenu.cpp` 内 `renderBase` 调用链，状态字段见 `src/ui/main_menu/RuntimeMainMenu.cpp:561-574`）。

### 2.3 单人页（SinglePlayer）

| # | 控件 | 槽位 | 文字键 / 回退 | 动作 | 出处 |
| --- | --- | --- | --- | --- | --- |
| 0 | Campaign | slot 0 | `GUI:NewCampaign` / `Campaign`；提示 `STT:SingleButtonNewCampaign` | `Campaign` | `:35-37` |
| 1 | Load Game | slot 1 | `GUI:LoadSavedGame` / `Load Game`；提示 `STT:SingleButtonLoadSavedGame` | `LoadGame` | `:38-40` |
| 2 | Skirmish | slot 2 | `GUI:Skirmish` / `Skirmish`；提示 `STT:SingleButtonSkirmish` | `Skirmish` | `:41-43` |
| -1 | Back | 底部 | `GUI:Back`；提示 `STT:SingleButtonBack` | `OpenHome` | `:44-46` |

### 2.4 影片页（Movies）与致谢页（Credits）

**Movies 控件**（`src/runtime/menu/RuntimeMainMenuController.cpp:49-66`）：

| # | 控件 | 动作 | 备注 |
| --- | --- | --- | --- |
| 0 | Previous | `PreviousMovie` | 无影片时动作 `None` |
| 1 | Play Movie | `PlayMovie` | 无影片时 `None`；Esc 跳过当前影片 |
| 2 | Next | `NextMovie` | 无影片时 `None` |
| 3 | Credits | `OpenCredits` | — |
| -1 | Back | `OpenHome` | — |

影片目录扫描：`GameMD/Movies`、`Movies`（`src/ui/main_menu/RuntimeMainMenu.cpp:770-771`）。播放状态文案：成功 `Movie playback finished.`；失败 `Playback unavailable: <错误>`（`src/runtime/menu/RuntimeMainMenuController.cpp:269-275`）。

**Movies 正文绘制矩形**（原值照抄，`src/ui/main_menu/RuntimeMainMenu.cpp:1218-1267`）：

| 元素 | RECT (left, top, right, bottom) | 对齐 |
| --- | --- | --- |
| 标题 | `54, 62, 578, 96` | 居中 / 垂直居中 / 单行 |
| 无影片提示 | `54, 230, 578, 266` | 居中 |
| 影片名 | `54, 206, 578, 242` | 居中 |
| 序号 `n / total` | `54, 250, 578, 280` | 居中 |
| 操作提示 | `54, 300, 578, 330` | 居中 |

**Credits 控件**（`src/runtime/menu/RuntimeMainMenuController.cpp:67-77`）：Previous Page / Next Page / Back；每页 18 行（`kCreditsLinesPerPage`），行高 25，矩形 `46, y, 586, y+23`，页码 `46, 504, 586, 532`（`src/ui/main_menu/RuntimeMainMenu.cpp:1280-1300`）。致谢文本来源依次尝试 `GameMD/creditsmd.txt`、`creditsmd.txt`、`exportRoot/langmd/creditsmd.txt`、`exportRoot/lang/credits.txt`，行内 `{KEY}` 占位回 CSF 展开（`src/ui/main_menu/RuntimeMainMenu.cpp:852-861`、`:815-843`）。

### 2.5 选项页（Options）

**控件**（`src/runtime/menu/RuntimeMainMenuController.cpp:79-96`）：

| # | 控件 | 动作 |
| --- | --- | --- |
| 0 | Resolution | `CycleResolution` |
| 1 | Target FPS | `CycleTargetFps` |
| 2 | Game Speed | `CycleGameSpeed`（`optionGameSpeed = (optionGameSpeed+1) % 7`，`:320-321`） |
| 3 | Apply | `ApplyOptions` |
| -1 | Cancel | `OpenHome` |

**正文矩形**（`src/ui/main_menu/RuntimeMainMenu.cpp:1317-1345`）：三行起点 `y = 126`，行距 `78`；标签 `74, y, 320, y+34`（左对齐）；值 `320, y, 558, y+34`（右对齐）；底部说明 `54, 418, 578, 448`；状态行 `44, 470, 588, 500`。

游戏速度文案键表：`TXT_SLOWEST / TXT_SLOWER / TXT_SLOW / TXT_MEDIUM / TXT_FAST / TXT_FASTER / TXT_FASTEST`（`src/ui/main_menu/RuntimeMainMenu.cpp:1206-1215`）。

选项持久化：`ApplyOptions` 把窗口宽高、目标 FPS、游戏速度写入候选 `RuntimeConfig`，`savePreferences` 成功后 `resizeHost()` 回 `Home`；失败文案 `Could not save options: <错误>`（`src/runtime/menu/RuntimeMainMenuController.cpp:409-423`）。落盘段与键名见第 7 节。

### 2.6 三态按钮与槽位扫光动画

- 按钮三态帧：`pressed` → 帧 2；`hover` 或键盘聚焦提示 `keyboardTooltip` → 帧 1；其余帧 0（`src/ui/main_menu/RuntimeMainMenu.cpp:1414-1424`）。
- 槽位扫光：`phaseFrameForSlot` 按阶段与槽位占用取 `slotAnimation` 帧；动画 6 帧、每帧 42 ms（`src/runtime/menu/RuntimeMainMenuController.h:31-32`，推进 `:358-369`）。
- 内容面板压暗：Movies / Credits / Options 或存在 `pageStatus` 时，对 `RECT{28, 34, 604, 542}` 逐通道乘 `2/5`（`shadeContentPanel` `src/ui/main_menu/RuntimeMainMenu.cpp:1175-1187`）。
- 警告动画帧：`warningFrame = (now / 75) % warningFrames()`（`src/runtime/menu/RuntimeMainMenuController.cpp:459-466`）。

### 2.7 绘制顺序（主菜单 CPU 合成）

`renderBase` 顺序（`src/ui/main_menu/RuntimeMainMenu.cpp:1358-1478`）：

1. 清表面；
2. 视频帧（尺寸等于 `632×568` 原位 blit，否则按 `kLeftWidth×kStatusTop+2` 缩放）或静态回退 + `mainShell`；
3. 状态栏 `statusBar`；
4. `sideTop`、警告帧、8 槽底图、`sideBottom`；
5. 槽位扫光动画帧；
6. `Idle` 阶段绘制三态红色按钮图；
7. 需要时压暗内容面板；
8. 非位图字体路径：先画鼠标光标，再上传回缓冲；
9. 标题文字；
10. 页面正文（影片 / 致谢 / 选项 / 状态）；
11. `Idle` 阶段按钮文字；
12. 底部提示条（悬停或键盘聚焦项的 tooltip）。

---

## 3. 遭遇战菜单控件树

### 3.1 几何与颜色常量（原值照抄）

| 常量 | 值 | 出处 |
| --- | --- | --- |
| `kBaseWidth` / `kBaseHeight` | `800` / `600` | `src/runtime/menu/RuntimeSkirmishController.h:17-18` |
| `kShellSlotCount` | `8` | `:19` |
| `kShellAnimationFrames` / `kShellAnimationFrameMs` | `6` / `42` | `:20-21` |
| `kPlayerSlots` | `8` | `:22` |
| `kVisibleMapRows` | `9` | `:23` |
| `kShellLeftWidth` | `632` | `src/ui/skirmish/RuntimeSkirmishMenu.cpp:79` |
| `kShellStatusTop` | `568` | `:80` |
| `kShellSideLeft` | `632` | `:81` |
| `kShellTopHeight` | `199` | `:82` |
| `kShellSlotHeight` | `42` | `:83` |
| `kShellBottomTop` | `535` | `:87` |
| `kShellButtonLeft` | `653` | `:88` |
| `kShellButtonTop` | `207` | `:89` |
| `kShellBottomButtonTop` | `543` | `:90` |

颜色（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:93-100`）：`kGold RGB(238,198,80)`、`kPaleGold RGB(255,232,145)`、`kText RGB(218,218,199)`、`kDimText RGB(112,119,109)`、`kPanel RGB(8,14,13)`、`kPanelInner RGB(18,25,22)`、`kPanelEdgeLight RGB(128,120,87)`、`kPanelEdgeDark RGB(2,4,4)`。

### 3.2 大厅（Lobby）面板与控件

**模式列表面板** `RECT{17, 44, 151, 229}`；行起点 `rowTop = 66`，`rowHeight = 17`；标题键 `GUI:GameType` / `GUI:GameMode`（回退 `GAME TYPE`）；选中行填充 `RGB(81,62,26)` + 金边，热行填充 `RGB(44,46,32)`，文本选中/热态用 `kPaleGold`，否则 `kText`；命中 `HitKind::Mode`（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:1599-1631`）。

**地图列表面板** `RECT{155, 44, 394, 229}`；行高同 17，可见 9 行；标题键 `GUI:Map` / `GUI:ChooseMap`（回退 `MAPS`）；命中 `HitKind::Map`；上下滚动 `MapScrollUp / MapScrollDown`（`:1670-1707`）。滚动箭头贴图：`uparrowr.pcx / uparrowp.pcx / dnarrowr.pcx / dnarrowp.pcx / sbgript.pcx / sbgripm.pcx / sbgripb.pcx`（`:1283-1301`）。

**玩家席位表**（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:1971-2119`）：

| 列 | 矩形 (left, top, right, bottom) | 命中类型 | 备注 |
| --- | --- | --- | --- |
| 启用勾选 | `22, top, 42, top+rowHeight`（内部小框 `25, top+2, 37, top+14`） | `SlotEnabled` | `:2005-2015` |
| 名称 | `44, top, 133, top+rowHeight-1` | — | 背景按激活态 `RGB(20,27,23)` / `RGB(10,13,12)` |
| 国家 | `135, top, 281, top+rowHeight-1` | `SlotCountry` | 国旗小图 `countryRect.left+3 .. +31`，文字 `flagRect.right+3` |
| 颜色 | `283, top, 376, top+rowHeight-1` | `SlotColor` | 色块 `left+4, top+3, left+14, bottom-3` |
| 队伍 | `378, top, 442, top+rowHeight-1` | `SlotTeam` | — |
| 难度 | `444, top, 608, top+rowHeight-1` | `SlotDifficulty` | — |
| 起始点 | `455, y, 510, y+26` | `SlotStart` | `:3223` |

内置候选值：颜色 `Gold / DarkRed / DarkBlue / DarkGreen / Orange / DarkSky / Purple / Magenta`；队伍 `- / 1 / 2 / 3 / 4`；难度 `Easy / Medium / Hard`（`src/runtime/menu/RuntimeSkirmishController.h:193-198`）。

**下拉层**：源矩形按 `DropdownKind`（`Player / Country / Color / Start / Team`）取席位单元格；下拉层优先向下展开，空间不足翻到源矩形上方；宽度按列；起始点下拉层宽 90（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:3229-3273`）。

**选项控件**：

| 控件 | 命中类型 | 出处 |
| --- | --- | --- |
| 资金 +/- | `CreditsMinus / CreditsPlus` | `src/runtime/menu/RuntimeSkirmishController.h:75-76` |
| 单位数 +/- | `UnitsMinus / UnitsPlus` | `:77-78` |
| 速度 +/- | `SpeedMinus / SpeedPlus` | `:79-80` |
| 资金 / 单位 / 速度滑块 | `CreditsSlider / UnitsSlider / SpeedSlider` | `:81-83` |
| 短游戏 | `ToggleShortGame` | `:84` |
| MCV 展开 | `ToggleMcvRepacks` | `:85` |
| 宝箱 | `ToggleCrates` | `:86` |
| 超级武器 | `ToggleSuperweapons` | `:87` |
| 盟友旁建造 | `ToggleBuildOffAlly` | `:88` |
| 电脑 AI | `ToggleComputerAi` | `:89` |
| 无冷却 | `ToggleNoCooldown` | `:90` |

滑块轨道：资金/单位/速度轨道 `trackLeft = 373`、`trackWidth = 79`（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:3357-3358`）；另一竖向轨道 `trackTop = 165`、`trackHeight = 256`（`:3537-3538`）。

**底部按钮**（GDI 大厅路径，`src/ui/skirmish/RuntimeSkirmishMenu.cpp:2354-2363`）：

| 按钮 | 矩形 | 文字键 / 回退 | 命中 |
| --- | --- | --- | --- |
| START GAME | `350, 535, 476, 560` | `GUI:StartGame` / `GUI:Play` | `Start` |
| BACK | `484, 535, 610, 560` | `GUI:Back` / `GUI:Cancel` | `Back` |

命令式外壳路径右侧按钮：`Start` 槽顶 `241`、`Back` 槽顶 `kShellBottomButtonTop`（`:3462-3467`）。

### 3.3 地图预览接入

- `MapInfo` 字段：`path / id / displayName / theater / size / minPlayers / maxPlayers / previewWidth / previewHeight / headerX/Y/Width/Height / startPoints / filters / metadataLoaded / previewAttempted / preview(MapPreviewImage) / previewError`（`src/runtime/menu/RuntimeSkirmishController.h:25-45`）。
- `MapPreviewImage`：`width / height / pixels`，自上而下紧凑 BGRA32，Alpha 恒 255；解码 Format5/LZO1X `[PreviewPack]`，尺寸取自 `[Preview] Size=x,y,width,height`（`src/data/maps/MapDatabase.h:76-93`）。
- 预览懒加载：`ensurePreview(index)` 经服务端口异步准备；`previewAttempted` 防止重复尝试；解码失败写 `previewError`（`src/runtime/menu/RuntimeSkirmishController.cpp:13`、`src/ui/skirmish/RuntimeSkirmishMenu.cpp:1058-1085`）。
- 预览绘制：按目标矩形等比缩放（`min` 缩放），`StretchDIBits` 用负 `biHeight` 保持行序（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:1760-1786`、`:2984-3005`）。起始点在预览上的映射使用 `headerX/Y/Width/Height` 归一化（`:1821-1824`）。

### 3.4 随机地图页（RandomMap / Load / Preview）

| 控件 | 矩形 / 位置 | 命中 | 出处 |
| --- | --- | --- | --- |
| 参数选项成对列 | `238, y, 269, y+27` / `270, y, 506, y+27` | `RandomOption` | `src/ui/skirmish/RuntimeRandomMapMenu.inl:166-167` |
| 生成图预览（大厅页） | `86, 318, 506, 500` | `RandomPreview` | `:170-174` |
| 右上角小预览 | `644, 40, 790, 152` | — | `:172` |
| 产生地图 | 槽顶 `157` | `RandomGenerate` | `:178` |
| 替我挑选 | 槽顶 `199` | `RandomSurprise` | `:179` |
| 使用地图 | 槽顶 `241` | `RandomUse` | `:181` |
| 保存地图 | 槽顶 `283` | `RandomSave` | `:182` |
| 载入地图 | 槽顶 `325` | `RandomLoad` | `:183` |
| 预览地图 | 槽顶 `367` | `RandomPreview` | `:184` |
| 取消 | `kShellBottomButtonTop` | `RandomCancel` | `:185` |
| 预览页大图 | `70, 120, 600, 540`；标题 `86, 85, 586, 112` | `RandomUse` / `RandomCancel` | `:189-193` |
| 已存列表行 | 列表行内 | `RandomSaved` / `RandomSavedScroll` / `RandomLoadSelected` / `RandomDelete` | `:205-214` |

随机图生成经 `services.activateRandomTarget(HitTarget, direction)` 与 `randomMapReady()` 轮询（`src/runtime/menu/RuntimeSkirmishController.h:168-169`）；未就绪时按钮置灰。

### 3.5 遭遇战提交校验

`canStart` 失败条件（`src/runtime/menu/RuntimeSkirmishController.cpp:907-955`）：

1. 未选地图：`Select a map before starting.`
2. 合作模式：协作数据不完整 / 场景缺失 / 席位少于 4；四个席位必须启用、仅 0 号位人类、队伍 `1,1,2,2`、起始点 `1..4`、国家允许列表通过。
3. 活跃玩家数（观察者不计）少于地图 `minPlayers`。

提交路径：`activateTarget(Start)` → `canStart` 通过则 `completeMenu(state, true)`，否则保留大厅并写 `status`；`completeMenu` 内 `syncSelection` 后置 `accepted = true`，落盘宝箱偏好与（离线时）无冷却偏好；未接受时 `prewarmMainMenu()`（`src/runtime/menu/RuntimeSkirmishController.cpp:575-597`、`:1174-1184`）。

---

## 4. 战役菜单控件树

### 4.1 几何常量（原值照抄）

| 常量 | 值 | 出处 |
| --- | --- | --- |
| `kCampaignShellWidth / Height` | `800` / `600` | `src/runtime/menu/RuntimeCampaignMenuController.h:12-13` |
| `kCampaignBackButton` | `{653, 543, 779, 569}` | `:14` |
| `kCampaignMapViewport` | `{8, 40, 624, 502}` | `:15` |
| `kCampaignSlider` | `{206, 230, 422, 254}` | `:16` |

控制动作 `CampaignControlAction`：`Back / Confirm / Edition / Campaign / Previous / Next / Row / Difficulty`（`src/runtime/menu/RuntimeCampaignMenuController.h:18`）。控件结构 `CampaignControl{rect, action, index, label, enabled, selected}`（`:20-27`）。

### 4.2 各页控件

控制由 `campaignControls(state)` 构造（`src/runtime/menu/RuntimeCampaignMenuController.cpp:786-873`）。右侧通用按钮矩形：`{653, 207 + slot*42, 779, 233 + slot*42}`；返回按钮固定 `kCampaignBackButton`。

**战役列表页 `Campaigns`**：

| 控件 | 矩形 | 动作 | 备注 | 出处 |
| --- | --- | --- | --- | --- |
| 产品版本（Edition） | 右侧槽列 | `Edition` | 去重 `sourceId`；YR 文案 `尤里的复仇`，RA2 `红色警戒 2` | `:816-820` |
| 战役条目 | `{187, top, 448, top+130}`，`top = 51 + iconSlot*286` | `Campaign` | 教程战役占槽 3，文案 `新兵训练` | `:823-828` |
| 难度条 | `x = 178 + index*71`，`{x, 267, x+69, 295}` | `Difficulty` | 四档；另四段滑块 `left = 206 + index*54`，`{left, 230, left+54, 254}` | `:859-870` |
| 继续 | slot 6，`Confirm` | — | — | `:803` |

**任务列表页 `Missions`（非地图演出态）**：

| 控件 | 矩形 | 动作 | 出处 |
| --- | --- | --- | --- |
| 任务行（最多 9 行） | `{42, 66 + row*34, 590, 98 + row*34}` | `Row` | `:838-840` |
| 上一任务 | slot 1 | `Previous` | `:842` |
| 下一任务 | slot 2 | `Next` | `:843` |

行文案：`<ordinal>. <任务名>`，未解锁追加 `  [未解锁]`；不可用任务 `enabled = false`。

**难度页 / 简报页 / 存档页**：

| 页 | 控件 | 矩形 | 出处 |
| --- | --- | --- | --- |
| `Difficulty` | 难度按钮 + 滑块 | 同 4.2 | `:859-870` |
| `Briefing` | 向上 / 向下滚动 | slot 1 / slot 2，`Next/Previous` | `:856-857` |
| `Saves` | 存档行（最多 9 行） | `{42, 66 + row*34, 590, 98 + row*34}` | `:847-851` |
| `Saves` | 上一/下一存档 | slot 1 / slot 2 | `:853-854` |

简报正文：标题 `42, 64, 590, 100`；简报区裁剪 `42, 112, 590, 536`，文本起点 `48, 118 - briefingScroll`，宽度 `585`（`src/ui/campaign/RuntimeCampaignMenuUi.inl:172-187`）。简报键优先 `mission.briefingKey`，回退 `loadBriefingKey`，再回退 `该任务没有可用的本地化简报。`（`:174-175`）。

### 4.3 战役列表 / 进度 / 影片接入

- 任务定义 `CampaignMissionDefinition`：`id / campaignId / sourceId / edition / mapFileName / mapPath / playerHouse / playerCountry / nextMissionId / startingCredits / displayNameKey / descriptionKey / briefingKey / loadMessageKey / loadBriefingKey / loadingBackground640/800 / loadingPalette800 / voiceOver / mapVq / clickMap / introVideoId / introVideoPath / postScoreVideoId / postScoreVideoPath / videoIds / videoPaths` 及可用性标志（`src/data/campaign/CampaignDatabase.h:83-153`）。
- 进度解锁：`missionUnlocked` 依据 `progress` 与任务 `available` 判断；锁定或地图不可用时 `acceptMission` 拒绝（`src/runtime/menu/RuntimeCampaignMenuController.cpp:443-446`）。
- 接受任务：写进度（`saveProgress`），`fillSelectionFromMission`，`closeMenu(true)`（`:440-463`）。
- 接受存档：遭遇战存档先 `validateSavePaths`；战役存档 `findMission` 校验 `available`，再回填（`:465-503`）。
- 地图选择图阶段：VQA 开场 → 覆盖层淡入 → 可交互；跳过演出仅在 `VqaIntro / OverlayFade` 有效；无可用点击图时重建命中面或回退列表 UI（`src/runtime/menu/RuntimeCampaignMenuController.cpp:539-559`、`src/runtime/menu/RuntimeCampaignPresentationData.h:77-81`）。
- 地图音效：`CampaignMapSelectionSound{sample, volume, volumeSpecified}`，配置段含 `overlay / targetFlyIn / enterRegion / exitRegion / clickRegion`（`src/data/campaign/CampaignDatabase.h:64-77`）；点击区域命中后播放 `clickRegion`（`src/runtime/menu/RuntimeCampaignMenuController.cpp:535`）。

### 4.4 战役结果契约 `RuntimeCampaignSelection`

| 字段 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- |
| `accepted` | `bool` | 是否接受 | `src/runtime/contracts/RuntimeCampaignSelection.h:8` |
| `loadedFromSave` | `bool` | 来自战役存档 | `:9` |
| `loadedSkirmishSave` | `bool` | 来自遭遇战存档 | `:10` |
| `loadedSkirmishFixedTickHz` | `int` | 遭遇战存档固定 Tick 率，默认 30 | `:11` |
| `loadedSkirmishTechLevel` | `int` | 遭遇战存档科技等级，默认 10 | `:12` |
| `savePath` | `std::string` | 存档路径 | `:13` |
| `skirmish` | `RuntimeSkirmishSelection` | 遭遇战回填副本 | `:14` |
| `mission` | `CampaignMissionDefinition` | 任务定义 | `:15` |
| `campaignId / missionId / sourceId` | `std::string` | 标识 | `:17-19` |
| `difficulty` | `std::string` | 默认 `Normal` | `:20` |
| `playerHouse` | `std::string` | 玩家阵营 | `:21` |
| `nextMissionId` | `std::string` | 下一任务 | `:22` |
| `introVideoPath / postScoreVideoPath` | `std::string` | 开场 / 结算影片 | `:23-24` |
| `briefingKey` | `std::string` | 简报键 | `:25` |
| `mapPath` | `std::string` | 地图路径 | `:26` |
| `playerCountry` | `std::string` | 玩家国家 | `:27` |
| `playerCountryDisplayName / playerCountryBrief` | `std::wstring` | 国家显示名 / 简报 | `:28-29` |
| `loadingScreenAssetId` | `std::string` | 加载屏资源 | `:30` |

---

## 5. 局域网与互联网菜单

### 5.1 控件 ID 与控件枚举（局域网）

控件 ID `MultiplayerMenuControlId`（`src/runtime/menu/RuntimeLanMenuController.h:14-40`）：

| ID | 常量 | 控件 |
| --- | --- | --- |
| 4101 | `PlayerNameEdit` | 玩家名输入 |
| 4102 | `HostPortEdit` | 主机端口 |
| 4103 | `DirectAddressEdit` | 直连地址 |
| 4104 | `HostButton` | 建立主机 |
| 4105 | `RefreshButton` | 刷新房间列表 |
| 4106 | `JoinButton` | 加入房间 |
| 4107 | `SpectatorCheck` | 观察者勾选 |
| 4108 | `BackButton` | 返回 |
| 4109 | `RoomList` | 房间列表 |
| 4110 | `RosterList` | 成员列表 |
| 4111 | `ReadyButton` | 准备 |
| 4112 | `StartButton` | 开始 |
| 4113 | `LeaveButton` | 离开 |
| 4114 | `StatusText` | 状态文本 |
| 4115 | `NetworkHint` | 网络提示 |
| 4116-4120 | `CountryEdit / ColorEdit / TeamEdit / DifficultyEdit / StartPositionEdit` | 本地席位选项 |
| 4121 | `ApplySlotButton` | 应用席位 |
| 4122 | `KickButton` | 踢出 |
| 4123 | `PasswordEdit` | 房间密码 |
| 4124-4125 | `ResumeHostButton / ResumeSwapButton` | 恢复主机 / 席位交换 |

视图控件枚举 `RuntimeLanMenuWidget` 与上述一一对应（`src/runtime/menu/RuntimeLanMenuController.h:41-78`）。局域网页面为原生对话框控件，坐标几何由 Win32 对话框布局承载，参考引擎菜单视图层未在 `src/ui/network/` 内提供像素矩形常量。

### 5.2 流程端口 `RuntimeLanMenuFlow` 与视图端口 `RuntimeLanMenuView`

- 流程端口：`startHost / startBrowsing / join / sendDiscoveryProbe / discoveredRooms / poll / setSlot / setSlotController / setSlotOptions / requestLocalSlotOptions / kickPeer / verifyLocalMap / localMapVerified / downloadMissingMap / hostAuthoritative / canonicalSetupPublished / resuming / resumeReady / canStart / initialized / setReady / startMatch / close / lobby / bridgePresent / sessionActive / localPeerId / members / lanRole / lanPhase / leave`（`src/runtime/menu/RuntimeLanMenuController.h:79-113`）。
- 视图端口：控件显隐 / 启用 / 文本读写 / 列表清空与追加 / 选中项 / 勾选 / 端点解析 / 加入选项构造 / 清密码 / 准备标签 / 主机设置运行 / 恢复存档选择 / 名称归一化 / 安全 U64 / 安全 ID / 主机投影 / 地图解析 / 兼容性构造 / 接受标志 / 错误串 / 被踢提示 / 销毁窗口 / 完成已开始对局（`:114-149`）。

控制器状态 `RuntimeLanMenuController`：`baseConfig / compatibility / compatibilityReady / peerId / page / rooms / rosterPeerIds / selectedRosterPeerId / displayedSetupRevision / localMapPath / hosting / directDiscoveryPending / pendingPassword / done`（`src/runtime/menu/RuntimeLanMenuController.h:151-173`）。

主要动作函数：`beginLanBrowsing`、`enterLobbyWithRoom`、`startHosting`、`joinSelectedLanRoom`、`applyLocalSlotOptions`、`kickSelectedPeer`、`pollMenu`、`toggleReady`、`startMatch`、`leaveLobby`、`handleControlClick`（`src/runtime/menu/RuntimeLanMenuController.h:195-220`，实现 `src/runtime/menu/RuntimeLanMenuController.cpp`）。轮询中对局被接受则 `closeMenu`（`:566`）；`closeMenu` 时未接受则 `flow.close()`（`:256-260`）。

### 5.3 互联网页面与控件 ID

`InternetMenuControlId` 范围 5101-5156（`src/runtime/menu/RuntimeInternetMenuController.h:25-87`）：

| 分组 | 控件 | ID |
| --- | --- | --- |
| 大厅 / 浏览器 | `InternetPlayerName / RoomName / Refresh / Host / Join / Spectator / QuickMatch / RoomList / Roster / Password` | `5101-5106, 5107, 5108, 5109, 5122` |
| 席位 | `Country / Color / Team / Difficulty / StartPosition / ApplySlot` | `5110-5115` |
| 大厅操作 | `Ready / Start / Kick / Leave / Status / Connection` | `5116-5121` |
| 导航 | `NavCommunity / NavGames / NavLadder / NavNews` | `5123-5126` |
| 账户 | `ProfileSummary / AccountHandle / AccountRegister` | `5127-5129` |
| 社区 | `ChannelList / ChannelJoin / ChatList / ChatEdit / ChatSend / ParticipantList / FriendList / FriendEdit / FriendAdd / FriendRemove` | `5130-5139` |
| 天梯 / 社区 | `LadderList / CommunityRefresh / NewsList / NewsDetail / ClanSummary / ClanMemberList / ClanInvitationList / ClanName / ClanTag / ClanCreate / ClanTarget / ClanInvite / ClanAccept / ClanRemove / ClanTransfer / ClanLeave / ClanLadderList` | `5140-5156` |
| 恢复 / 历史 | `ResumeHost / ResumeSwap / MatchHistory / MatchHistoryBack / MatchHistoryList` | `5160-5164` |

视图控件枚举 `RuntimeInternetMenuWidget`（`:88-171`）；离开按钮标签 `RuntimeInternetLeaveLabel`：`Close / LeaveRoom / MainMenu`（`:172`）。

### 5.4 服务端配置与凭据占位边界

- 流程端口 `RuntimeInternetMenuFlow` 全部为异步事务接口：`beginRoomQuery / beginMatchmaking / startHost / join / requestCommunityLogin / requestCommunityRegistration / requestCommunityRefresh / requestCommunityChannelJoin / requestCommunityChannelMessage / requestCommunityFriendAdd / requestCommunityFriendRemove / requestCommunityClanCreate / ClanInvite / InvitationAccept / MemberRemove / Leave / OwnershipTransfer / poll / mode / rooms / primaryConnectionState / communitySnapshot / communityMatchHistory / close / present / transportPresent / coordinatorAuthenticated / lobbyFlow`（`src/runtime/menu/RuntimeInternetMenuController.h:173-203`）。
- 视图端口负责凭据清除 `clearCredentials()`、熵值 `entropy(...)`、显示文本校验、选项投影与行投影；菜单控制器不持有服务端地址、令牌或持久化凭据（`:205-259`）。
- 熵结构 `RuntimeInternetMultiplayerMenuEntropy`：`queryTransactionId / hostTransactionId / joinTransactionId / matchRequestId / matchActivationTransactionId / matchHostTransactionId / matchJoinTransactionId / roomGeneration / gameplayRandomSeed / roomIdBytes[16]`（`src/runtime/contracts/RuntimeInternetMenuData.h:7-22`）。
- 房间行 `RuntimeInternetMultiplayerRoomRow{roomId, text, joinable}`；成员行 `RuntimeInternetMultiplayerRosterRow{peerId, text, local, kickable}`（`src/runtime/contracts/RuntimeInternetMenuData.h:24-43`）。
- 菜单控制器状态：`baseConfig / compatibility / selectedRankedMatchId / page / rooms / rosterRows / selectedPeerId / selectedFriendPeerId / selectedNewsIndex / localMapPath / pendingHostSlots / displayedRevision / hosting / done`（`src/runtime/menu/RuntimeInternetMenuController.h:261-281`）。

### 5.5 联机结果契约 `RuntimeMultiplayerMenuResult`

| 字段 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- |
| `accepted` | `bool` | 对局是否接受 | `src/runtime/contracts/RuntimeMultiplayerMenuTypes.h:35` |
| `launchConfig` | `RuntimeMultiplayerLaunchConfigResult` | 启动配置结果 | `:36` |
| `bridge` | `std::unique_ptr<RuntimeMultiplayerRuntimeBridge>` | 运行时桥（移动语义，禁拷贝） | `:37, 42-50` |
| `error` | `std::string` | 错误信息 | `:38` |

网络枚举 `RuntimeMultiplayerMenuNetwork`：`Lan / Internet`（`:10-13`）；直连端点 `RuntimeMultiplayerDirectEndpoint{host, port}`（`:15-20`）；主机投影 `RuntimeMultiplayerHostSelectionProjection{lobby, slots, localMapPath}`（`:22-32`）。

---

## 6. 选项持久化（设置落盘，非独立选项页绘制）

`src/ui/settings/RuntimeSettings.cpp` 把 `RuntimeConfig` 拆成段落盘（`preferenceSections` `:176-233`）：

| 段 | 键 | 取值来源 |
| --- | --- | --- |
| `Runtime` | `WindowWidth / WindowHeight / TargetFps` | `config.windowWidth / windowHeight / targetFps`，下限 1 |
| `Skirmish` | `GameSpeed` | `config.gameSpeed`，钳制 0..6 |
| `Skirmish` | `Crates` | 遭遇战宝箱偏好（`saveRuntimeSkirmishCratePreference` `:328-337`） |
| `Skirmish` | `NoCooldown` | 无冷却偏好（`:339-344`） |
| `Options` | `ScrollRate / DetailLevel / ToolTips / UnitActionLines / ScrollCoasting / SeeHiddenObjects` | 滚动速度 0..6、细节 0..2、布尔 `yes/no` |
| `Audio` | `MusicVolume / SoundVolume / VoiceVolume / InGameMusic / SoundEnabled` | 音量 0..100、布尔 |
| `Hotkey` | `<键位ID>` | `keyBindings`；组合位保留；可选 `ReferenceLayout = 20260913` |

写入策略：保留原 INI 其余行；重复段取最后一段；缺失键追加；配置文件上限 4 MiB；原子写（`saveRuntimePreferenceSections` `:237-319`）。布尔文本 `yes / no`（`:172-174`）。

> 说明：参考引擎选项绘制在主菜单 `MenuPage::Options` 内完成（见 2.5），独立的设置控件面板不在本章目录内；`src/ui/settings/` 仅承担偏好序列化。

---

## 7. 结算页与加载页

### 7.1 结算页（Match Score）

结算行结构 `RuntimeMatchScoreRow{statistics: RuntimeMatchScoreEntry}`，行来源 `runtimeMatchScoreRows(state)`（`src/ui/results/RuntimeMatchScoreRender.inl:1-21`）。基准画布 800×600，缩放取 `min(clientW/800, clientH/600)` 居中（`runtimeMatchScoreScaledRect` `:63-67`）。

| 元素 | 基准矩形 | 出处 |
| --- | --- | --- |
| 标题（`GUI:MultiplayerScore` / `GUI:SkirmishScore`，回退 `遭遇战积分`） | `638, 2, 798, 28` | `:141-142` |
| 继续按钮命中 | `638, 530, 800, 568` | `:78-79` |
| 继续文字（`GUI:Continue`，回退 `继续`） | `653, 537, 779, 562` | `:143` |
| 胜/败条 | `94, 117 + strip*36, 534, 153 + strip*36` | `:149` |
| 胜负文字 | `99, 121, 369, 148` | `:159` |
| 用时 | `372, 121, 530, 148` | `:160` |
| 列表列 X | `99, 220, 303, 385, 462, 530` | `:161` |
| 表头行 | 列 X，`157, 184` | `:166` |
| 数据行 | 列 X，行高 27 | `:175-181` |
| 翻页提示（页数 > 1） | `12, 572, 620, 596` | `:183-184` |

每页 8 行（`pageSize = 8`，`:168`）；位图字体可用则走 `bitFont.draw`，否则系统字体回退（`:128-130`）。左侧 632×568 积分美术 + 右侧共享按钮区，对应 GAMEMD 对话框 264（`:72` 注释）。

### 7.2 加载页（Country Loading）

国家文本投影表 `RuntimeLoadingCountryText{imageToken, countryKey, unitKey, flag}`（`src/ui/loading/RuntimeCountryLoadingRender.inl:1-6`）：

| imageToken | countryKey | unitKey | flag |
| --- | --- | --- | --- |
| `ustates` | `Name:Americans` | `Name:Para` | `usai` |
| `ukingdom` | `Name:British` | `Name:SNIPE` | `gbri` |
| `germany` | `Name:Germans` | `Name:TNKD` | `geri` |
| `france` | `Name:French` | `Name:GTGCAN` | `frai` |
| `korea` | `Name:Alliance` | `Name:BEAGLE` | `japi` |
| `russia` | `Name:Russians` | `Name:TTNK` | `rusi` |
| `libya` | `Name:Africans` | `Name:DTRUCK` | `arbi` |
| `iraq` | `Name:Arabs` | `Name:DESO` | `djbi` |
| `cuba` | `Name:Confederation` | `Name:TERROR` | `lati` |
| `yuri` | `Name:YuriCountry` | `Name:YURIPR` | `yrii` |
| 兜底 | `Name:Observer` | `GUI:Observer` | `rani` |

（`src/ui/loading/RuntimeCountryLoadingRender.inl:9-23`）。加载屏起始点投影：优先 `Header` 的 `StartX/StartY/Width/Height` 与 `Waypoint<n>`；旧图回退 `Map/Size` 与 `Map/LocalSize` 等距投影裁剪（`:26-64`）。战役模式不刷新地图起始点标记（`:68`）。

---

## 8. 绘制命令、表面与两条提交路径

### 8.1 有序菜单绘制命令 `MenuDrawList`

命令类型 `MenuDrawKind`：`Fill / Darken / Bgra / Scale / Indexed / Glyph`（`src/ui/drawing/MenuDrawCommands.h:11`）。命令结构 `MenuDrawCommand` 定长 64 字节：`left/top/right/bottom`、`kind`、`offset/stride/width/height/palette/color/flags`、`originX/originY`、保留位（`:12-19`）。

| 能力 | 约束 | 出处 |
| --- | --- | --- |
| 命令上限 | `maxCommands = 16384`；资源字节预算 `maxBytes = 32 MiB` | `:22` |
| 资源入表 | `intern(bytes)`：按指针 + 尺寸 + 内容字节比对复用；等大小原地重载不得复用旧 GPU 租约；超预算抛 `length_error` | `:29-41` |
| 命令入列 | `push`：命令数满抛 `length_error`；仅当 `right>left && bottom>top` 入列 | `:42-45` |
| `fill` | 矩形纯色；颜色打包 `B | G<<8 | R<<16 | 0xFF000000` | `:46-49` |
| `darken` | 不透明度 `255 - opacity` | `:50-53` |
| `bgra` | 不缩放 / `Scale` 缩放两路；记录 stride、宽高、原点 | `:54-60` |
| `image` | 索引色图：索引字节 + 256 项 BGRA 调色板入表；`transparentZero` 置标志 1；裁剪到索引子矩形与 `clipHeight` | `:61-72` |
| 校验 `valid()` | 代际非 0、命令非空、资源非空、不超预算；逐命令校验矩形、通道 stride、调色板边界、Glyph 位掩码跨度 | `:73-99` |

代际与修订：`generation`（资源整体重置）、`revision`（资源入表修订）、`frameRevision`（命令帧修订）；`begin()` 清命令并递增 `frameRevision`；`reset()` 清命令与资源并递增三者（`:23-28`）。

`MenuDrawFrame` 是同步借用快照：`list` 引用 + `sequence` + 宽高 + `viewport` + 三代际（`generation / resourceRevision / commandRevision`），构造时固化（`:105-114`）。

### 8.2 CPU 像素表面

`BasicMenuPixelSurfaceView<Byte>`：`bytes / width / height / rowStride / generation`；`valid()` 校验行 stride 与最后一行字节余量（`src/ui/surface/RuntimeMenuPixelSurface.h:13-27`）。`MenuPixelBuffer` 持有私有 `pixels_`、宽高、stride、代际与可替换分配钩子；`reset/release/view/owns/bytes`（`:30-51`）。

软件回退原语：`clearMenuPixels`、`fillMenuPixels`、`darkenMenuPixels`、`frameMenuPixels`、`blitMenuPixels`（`/255` 混合，保留目标 Alpha）、`scaleMenuPixels`（最近邻、目标裁剪、Alpha 强制 255）（`src/ui/surface/RuntimeMenuPixelSurface.h:52-62`）。

### 8.3 GPU 提交与软件回退边界（仅菜单部分）

- 遭遇战菜单同时持有命令表 `menuCommands` 与可选 D3D11 呈现器 `menuGpu`、探针 `menuProbe`、`menuGpuAttempted`、序号 `menuSequence`、软件合成计数 `softwareCompositions`（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:738-746`）。
- GPU 路径：后端请求为 D3D11 且首次尝试时 `D3D11FramePresenterWin32::create`；`tryRecoverDevice()`；设备代际变化时 `fallbackToSoftware("menu-page-outside-S40")`；成功路径 `menuCommands.begin()` → `ShellCanvas` 命令合成 → `composeShellPixels` → `presentMenu(MenuDrawFrame{...})`，记录准备耗时与上传/呈现耗时（`:3618-3638`）。
- GPU 失败：捕获异常或呈现器不活跃后回退软件；软件路径先确保回缓冲、`backDirty` 时重渲染回缓冲（`:3641-3648`）。
- 菜单呈现校验（`src/runtime/presentation/win32/D3D11MenuPresentWin32.inl:2-52`）：必须在所属线程；设备代际匹配；窗口活跃、非最小化、可见；客户区尺寸与帧一致；视口合法；序号递增；`list.valid()`；三代际与快照一致；命令矩形裁剪到 800×600；瓦片预算超限记 `budgetRejected` 并失败；`beginFrame` → `collect` → `MenuRasterD3D11::draw` → `finishSubmission` → `capture` → `Present(1, 0)`；设备移除、上传失败、着色器失败、呈现失败均走探针/错误路径。
- 回退 API：`active()`（选中后端是否 D3D11）、`fallbackToSoftware(reason, code)`、`report()`（含 `deviceGeneration / resourceGeneration / staleRejected / softwareDevice` 等）（`src/runtime/presentation/win32/D3D11FramePresenterWin32.cpp:205-225`、`src/runtime/presentation/win32/D3D11FramePresenterWin32.h:28,51,56,61`）。

> 主菜单默认走 CPU 私有字节向量 + GDI 回缓冲上传路径；位图字体有效时直接在 CPU 表面绘制，无效才上传回缓冲后用系统字体回退（`src/ui/main_menu/RuntimeMainMenu.cpp:1434-1442`）。地图战斗内 GPU 瓦片路径不在本章范围。

### 8.4 资源代际与借用

- 菜单资源所有者 `MenuImageResources`：`configureRoots / roots / loadPalette / loadFrame`；根目录变更不清空已有键缓存；返回独立拥有的图像/调色板（`src/assets/menu/MenuImageResources.h:14-31`）。
- 两种松文件策略：`loadMenuLooseFrame`（主菜单：帧 0、全局画布/偏移、原始运行时 PAL 转换）；`loadMenuFrameLocalShp`（遭遇战：帧局部画布、源路径留空、输出先重置）（`:39-46`）。
- `MenuImage`：BGRA 解 `bgra` 与索引色 `indices / paletteBgra[256] / indexWidth/Height/Left/Top / transparentZero` 双路（`src/assets/menu/MenuImage.h:11-27`）。
- 资源新鲜度观察 `RuntimeMenuResourceWatch`：仅判断根路径与内容是否未变；不负责加载、重 armed、预热或生命周期；平台未实现或通知缺失一律视为需要重载；偏好不纳入观察（`src/ui/resources/RuntimeMenuResourceWatch.h:15-31`）。
- BMP 读取能力端口 `MenuBmpReader`：`capability()` 区分 `Unsupported / Bmp`，`read(path, output)` 成功后输出拥有像素（`src/assets/menu/MenuBmpReader.h:8-18`）。

### 8.5 菜单时钟

`RuntimeUiClock` 为借用端口，`nowMs()` 返回 `RuntimeUiTimeMs(uint64)`（`src/ui/time/RuntimeUiClock.h:9-13`）。策略函数：双击窗口 500 ms；悬停延迟 `now - start >= delay`；横幅时长；准备闪烁周期 `now / 500`、亮暗由奇偶；计分耗时以 0 为哨兵、回退 2000/3000 ms（`:17-45`）。

### 8.6 颜色值

`RuntimeRgbColorValue`：内部 32 位，掩码 `0x00ffffff`；`fromPackedRgb` / `fromChannels`；通道取 `red = 低字节`、`green = 中`、`blue = 高`；位图路径按 `0xAABBGGRR` 读取，高标志位忽略，Alpha 由像素写入器补（`src/ui/values/RuntimeColorValues.h:7-25`）。

---

## 9. 输入路由

### 9.1 主菜单

| 输入 | 处理函数 | 路由规则 | 出处 |
| --- | --- | --- | --- |
| 鼠标移动 | `handleMainMenuMouseMove` | `clientToBase` 反算基准点；更新 `mousePoint/mouseVisible/hover`；清键盘 tooltip；状态变化触发事件窗重绘 | `src/runtime/menu/RuntimeMainMenuController.cpp:473-491` |
| 鼠标离开 | `handleMainMenuMouseLeave` | 隐藏鼠标、清 hover；未按下则重绘 | `:493-499` |
| 左键按下 | `handleMainMenuLeftDown` | 命中则 `capture()`、置 `focus/hover/pressed`，播放 `MenuClick`，事件窗重绘 | `:501-513` |
| 左键松开 | `handleMainMenuLeftUp` | 释放捕获；命中且与按下项一致才 `activate(hit, false)` | `:515-524` |
| 捕获丢失 | `handleMainMenuCaptureLost` | 清 `pressed`，重绘 | `:526-531` |
| 键盘 | `handleMainMenuKey` | 仅 `Idle`；`Up` / `Tab+Shift` 上移，`Down` / `Tab` 下移；`Return/Space` 激活聚焦项；`Escape` 按页回退动作；循环取模 | `:533-562` |

键枚举 `MainMenuKey`：`Other / Up / Down / Tab / Return / Space / Escape`（`src/runtime/menu/RuntimeMainMenuController.h:82`）。激活：`activate` 取当前按钮动作，`playClick` 时播 `MenuClick`；非即时动作走 `beginAction`（`src/runtime/menu/RuntimeMainMenuController.cpp:349-356`）。

### 9.2 遭遇战

输入函数：`handleSkirmishMouseMove / LeftDown / LeftUp / DoubleClick / RightUp / MouseLeave / CaptureLost / Key / Wheel`（`src/runtime/menu/RuntimeSkirmishController.h:314-322`）。键枚举 `RuntimeSkirmishKey`：`Other / Escape / Return / Space / Up / Down / Home / End`（`:313`）。

命中体系：`HitKind` 枚举覆盖模式/地图/滚动/席位各列/下拉/颜色/起始点/队伍/难度/加减号/滑块/开关/随机图各动作/使用/取消/开始/返回（`:62-98`）；`HitTarget{kind, index}`；`HitRegion{rect, target}`（`:109-124`）。命中表 `state.hits` 由视图在合成时 `addHit` 累积；`hitTest` 按矩形取目标（`:323`）。

- 下拉开合：`DropdownKind {Player, Country, Color, Start, Team}`；`dropdownCanOpen`、`toggleDropdown`、`selectDropdownOption`、`closeDropdown`、`dropdownContainsTarget`（`:276-292`）。
- 滑块拖拽：`draggingSlider` 记录当前滑块类型；`setSliderFromBaseX` 按基准 X 落值；`isSliderTarget / sliderMaximum`（`:212, 300-302`）。
- 右键松开 `handleSkirmishRightUp`：用于取消/关闭下拉或返回（视图端口行为）；滚轮 `handleSkirmishWheel` 滚动地图/存档列表。

### 9.3 战役

输入函数：`campaignMouseMove / Leave / Down / Up / CaptureChanged / KeyDown / Wheel`（`src/runtime/menu/RuntimeCampaignMenuController.h:192-196`）。键枚举 `CampaignMenuKey`：`Other / Escape / Tab / Up / Down / Left / Right / Confirm`（`:179`）。命中 `campaignControlAt` 返回控件下标；`activateCampaignControl` 执行动作；滚轮翻任务/存档页或滚简报（`:185-191`）。

### 9.4 联机

- 局域网：`handleControlClick(control)` 按控件 ID 分发（`src/runtime/menu/RuntimeLanMenuController.h:220`）；列表选中经 `listSelected / listSelect`；双击加入在视图层处理。
- 互联网：`handleInternetSelection / handleInternetDoubleClick / handleInternetClick` 三入口（`src/runtime/menu/RuntimeInternetMenuController.h:365-367`）；事务 ID 由 `nextCommunityTransaction` 分配（`:300-304`）。

### 9.5 选择结果契约字段总表

**遭遇战 `RuntimeSkirmishSelection`**（`src/runtime/contracts/RuntimeSkirmishSelection.h:18-43`）：

| 字段 | 类型 | 默认 | 含义 |
| --- | --- | --- | --- |
| `accepted` | `bool` | `false` | 接受标志 |
| `mapPath` | `std::string` | — | 地图路径 |
| `mapStartingPointLimit` | `int` | `0` | 起始点上限（取自地图玩家数） |
| `gameMode` | `std::string` | `Battle` | 模式键 |
| `gameModeRulesOverride` | `std::string` | `MPBattleMD.ini` | 规则覆盖 INI |
| `playerCountry` | `std::string` | `Americans` | 玩家国家 |
| `playerColor` | `std::string` | `Gold` | 玩家颜色 |
| `playerCountryDisplayName` | `std::wstring` | — | 国家显示名 |
| `playerCountryBrief` | `std::wstring` | — | 国家简报 |
| `loadingScreenAssetId` | `std::string` | — | 加载屏资源 |
| `startingCredits` | `int` | `200000` | 初始资金 |
| `unitCount` | `int` | `10` | 初始单位数 |
| `gameSpeed` | `int` | `4` | 游戏速度 |
| `shortGame` | `bool` | `true` | 短游戏 |
| `mcvRepacks` | `bool` | `true` | MCV 可展开 |
| `crates` | `bool` | `true` | 宝箱 |
| `superweapons` | `bool` | `false` | 超级武器 |
| `buildOffAlly` | `bool` | `false` | 盟友旁建造 |
| `computerAiEnabled` | `bool` | `false` | 电脑 AI |
| `noCooldown` | `bool` | `false` | 无冷却 |
| `slots` | `vector<RuntimeSkirmishPlayerSlot>` | — | 席位表 |

`RuntimeSkirmishPlayerSlot`（`:6-16`）：`enabled / human / name / country="Random" / color="Random" / team="-" / difficulty="Medium" / startPosition=0`（0 随机，正整数为一号起始点）。

战役与联机契约见 4.4、5.5 节。

---

## 10. 菜单音乐、音效与文字接入

### 10.1 音效事件映射

主菜单采样表（`src/ui/main_menu/RuntimeMainMenu.cpp:409-413`）：

| 事件名 | 默认采样 | 出处 |
| --- | --- | --- |
| `MenuClick` | `umenucl1` | `:410` |
| `MenuSlideIn` | `uslide1` | `:411` |
| `MenuSlideOut` | `uslide2` | `:412` |

采样可由声音 INI 的 `[Sounds]` 段同名键覆盖；去掉 `$` / `#` 前缀、取逗号/空格/制表符前的首段（`src/ui/main_menu/RuntimeMainMenu.cpp:425-441`）。解析出的 WAV 路径缓存于 `waves`，经异步声音设备播放（`:443-462`）。

遭遇战音效表首项 `{"MenuClick", "umenucl1"}`（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:542`）；播放端口 `playSound(event, beepOnFailure)`，失败可蜂鸣提示（`src/runtime/menu/RuntimeSkirmishController.h:149`）。

### 10.2 菜单音乐

- 主菜单音乐设备 `MainMenuMusicDevice`（Win32 实现 `createMainMenuMusicDeviceWin32`）；策略：已打开且同路径不重开；音量百分比；一次播放、调用方负责 EOF 重启；`ensureMusicPlaying` 每秒探测一次，未在播放则从头播放或重开（`src/ui/main_menu/RuntimeMainMenu.cpp:464-510`）。
- 遭遇战音乐策略 `SkirmishMusicPolicy`（纯呈现策略，非权威时钟或物理设备）：`start / poll / stop / isOpen / path / nextProbe`，时钟与文件探测为借用注入（`src/ui/audio/RuntimeSkirmishMusicPolicy.h:8-25`）；设备 `createSkirmishMusicDeviceWin32`（`src/ui/skirmish/RuntimeSkirmishMenu.cpp:528`）。
- 战役地图音频设备 `RuntimeCampaignMapAudioDevice`，含不支持实现回退（`src/audio/menu/RuntimeCampaignMapAudioDevice.h`、`RuntimeUnsupportedCampaignMapAudioDevice.cpp`）。
- 主题路径按页面选择：`mainThemePath / moviesThemePath / creditsThemePath / optionsThemePath / themeCatalogPath`（`src/ui/main_menu/RuntimeMainMenu.cpp:576-580`）；切页后 `startPageMusic()`（`src/runtime/menu/RuntimeMainMenuController.cpp:449`）。
- 菜单退出时 `stopMusic()`（`src/runtime/menu/RuntimeMainMenuController.cpp:215`、`src/runtime/menu/RuntimeSkirmishController.cpp:543`）。

### 10.3 CSF / FNT 取值与排版

> CSF 编码、FNT 位图字体字形解码细节见 [05b-文字与视频解码(CSF-FNT-Bink).md](05b-文字与视频解码(CSF-FNT-Bink).md)，本节只记菜单侧接入点。

- CSF 目录：主菜单依次尝试 `GameMD/ra2md.csf`、`langmd/ra2md.csf`、`lang/ra2.csf`（`src/ui/main_menu/RuntimeMainMenu.cpp:895-900`）；解码后回车策略 `Space`（`src/runtime/menu/detail/RuntimeSkirmishMapResources.h:23-26`）。菜单取值端口 `text(key, fallback)`，多键回退 `label(keys, fallback)`（`src/runtime/menu/RuntimeMainMenuController.h:86`、`src/runtime/menu/RuntimeSkirmishController.h:158-159`）。
- 位图字体（FNT）视图 `MainBitmapFontView{bytes, metrics}`：`valid()` 要求字节非空、`recordSize > 1`、`lineHeight > 0`；主菜单把有效 UTF-16 代理对折成 `?`，遭遇战不折（`src/ui/text/RuntimeMainBitmapText.h:10-16`）。
- 三 Ramp 颜色：亮 `(255,190,97)`、中 `(245,172,68)`、暗 `(97,59,4)`（`:17-21`）；测量 `measureMainBitmapText`，绘制 `drawMainBitmapText`（宽/UTF-16 两路，居中/垂直居中，半开裁剪）（`:22-31`）。
- 系统字体回退角色 `MainSystemFontRole {Button, Title, Small}`；能力 `MainSystemFontCapability {Unsupported, NativeText}`；请求 `MainSystemWideTextRequest{text, rect, format, colors}`（`src/ui/text/RuntimeMainSystemFont.h:7-31`）。菜单绘制顺序：位图字体有效则 CPU 表面绘制，否则回退系统字体 `DrawText`（`src/ui/main_menu/RuntimeMainMenu.cpp:1150-1172`）。
- 战役 / 遭遇战 / 结算各自系统字体端口：`CampaignSystemWideTextRequest`、`SkirmishSystemFontRole`、结算 `bitFont` 与系统字体回退（`src/ui/campaign/RuntimeCampaignMenuUi.inl:97-107`、`src/ui/results/RuntimeMatchScoreRender.inl:120-130`）。

### 10.4 影片接入

- 主菜单 Bink 动画 `BinkMenuAnimation`：`poll(frame, waitMs)`、`stop()`、`failure()`、`sequence()`、`finished()`（`src/ui/main_menu/RuntimeMainMenu.cpp:380-396`）；首帧截止 `kMainMenuFirstFrameDeadlineMs = 60000`（`:598`）；失败回退静态首帧（`:600-606`）。
- 影片播放端口 `playMovie(path, error)`、`prepareMoviePlayback`、`stopVideo`、`pollVideo`、`restartMainVideo`（`src/runtime/menu/RuntimeMainMenuController.h:89,101-105`）；战役开场/结算影片路径取自任务定义（4.3），跳过仅在对应阶段有效（`src/runtime/menu/RuntimeCampaignMenuController.cpp:539-559`）。
- 解码器桥不可用时调用方仍可继续进入任务，错误经 `error` 返回（`src/ui/main_menu/RuntimeMainMenu.h:36-58`）。

---

## 11. 其他边界与备注

- 主菜单/遭遇战/战役共享持久顶层外壳窗口；`hostWindow` 为空时保留独立 smoke/开发用法（`src/ui/main_menu/RuntimeMainMenu.h:20-26`、`src/ui/skirmish/RuntimeSkirmishMenu.h:21-28`）。
- 预热：`prewarmRuntimeSkirmishMenu` 在工作线程准备资源，不阻塞消息循环；HWND 与绘制均留在 UI 线程；已存在资源跨菜单访问保留（`src/ui/skirmish/RuntimeSkirmishMenu.h:35-39`）。
- 遭遇战另提供 SDL3 独立位图场景入口 `runRuntimeSkirmishMenuSdl3`，失败显式报错，不替换其他窗口（`src/ui/skirmish/RuntimeSkirmishMenu.h:32-33`）。
- 战斗内 HUD / 侧栏 / 选择框 / 雷达 / 光标 / 输入细节归另一代理章节；本章命中与光标状态仅到控制器端口为止。

---

## 本章缺口 / 参考引擎未覆盖项

1. **联机对话框像素几何**：局域网与互联网页面控件由 Win32 原生对话框承载，`src/ui/network/` 下未提供基准坐标矩形常量；本章只列控件 ID、枚举与流程/视图端口，未抄录对话框布局坐标。
2. **互联网服务端配置**：服务地址、TLS 策略、登录令牌、凭据持久化均在流程所有者侧；菜单控制器只持有事务 ID 与熵值，参考引擎菜单内未见服务端地址配置界面。
3. **主菜单视频/音乐资源清单**：`mainShell / sideTop / warning / slotBackground / slotAnimation / sideBottom / redButton / mouse` 等美术资源的具体资源名与帧序列在视图资源装配段引用，本章未逐项列出 SHP/PAL 文件名；需结合 `src/assets/menu/` 装配代码与 05a 图像解码章节补全。
4. **随机地图参数项完整清单**：`RandomOption` 成对选项的标签、取值范围与生成参数映射未在本章逐项抄录；仅记录控件矩形与动作枚举。
5. **结算页统计口径**：`RuntimeMatchScoreEntry` 各统计字段（建造数、分数等）的采集与累加属战斗内结算域，本章未展开；结算行仅记录展示矩形与列定义。
6. **诊断 / Smoke 夹具**：`src/ui/**/diagnostics/`、`*.Smoke.*`、`*Trace.inl` 为测试/诊断钩子，不作为生产行为依据；本章引用的生产结论均来自 `src/` 下 `.h/.cpp/.inl` 生产源码。
7. **主菜单选项页之外的设置面板**：参考引擎未在外壳菜单内实现独立的图形/音效/键位设置绘制面板；`src/ui/settings/` 仅做偏好序列化，图形质量、滚动、提示等选项的交互控件未见生产菜单实现。
8. **战役教程/合作任务分支**：教程战役（`isTutorialCampaign`）与合作场景（`CooperativeScenarioDefinition`）的完整任务表与解锁条件依赖 `data/campaign`、`data/maps` 数据库章节，本章仅记录菜单接入点。
