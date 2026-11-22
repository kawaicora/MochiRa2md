# 07b · 战局内 HUD、侧栏与输入路由（侧栏 / 选中 / 编组 / 雷达 / 光标）

## 本章覆盖范围

- 战局内右侧栏：宽度与纵向分段、四类生产分页（建筑 / 防御 / 步兵 / 载具）、两列按钮网格、分页按钮与滚轮累计、按钮可用性 / 就绪 / 生产中 / 挂起 / 悬停 / 多队列计数等状态绘制，以及 cameo 图标、进度时钟遮罩、就绪闪烁与边框配色。
- 选中与编组：左键单击 / 框选 / 追加选择 / 强制攻击 / 攻击移动 / 跟随 / 集结点 / 信标点击的判定与命令接入；选中高亮框、框选虚线、归属与血量 / 勋章反馈；数字编组（创建 / 选择 / 追加 / 居中）与快速选择（同类 / 战斗单位 / 老兵 / 血量）。
- 雷达小地图：投影与坐标映射、地形采样、视野遮蔽、资源与归属点绘制、点击导航。
- 光标：语境判定顺序与原生库存光标资源号；自定义 `MOUSE.SHP` 语境光标在参考引擎中的落地情况。
- 输入路由：指针按下 / 松开 / 捕获 / 取消、左右键手势、键盘准入与快捷键表、聊天文本输入、输入值到世界命令 / UI 命令的提交时机、同步重入校验与偏好保存边界。
- HUD 其余元素：资源计数行、电力段条、超武倒计时列表、聊天 / 提示文字、外交面板、信标 / 叫嚣 / 欢呼。
- 几何常量与颜色值原样列出，附 `src/...:行号`。

> 边界：主菜单 / 遭遇战 / 战役 / 结算页的控件与绘制命令机制由菜单章负责；本章只在战局内 HUD 引用到这些通用机制处标注边界，不重复展开。文字（FNT/CSF）解码与通用位图文字绘制见 `05b-文字与视频解码(CSF-FNT-Bink).md`。

## 对应源码目录

生产源码（`src/ui/` 下本章域）：

| 文件 | 职责 |
| --- | --- |
| `src/ui/sidebar/RuntimeSidebarPageController.h` / `.cpp` | 分页槽位、页计数、页码钳制、滚轮累计翻页与翻页状态回调 |
| `src/ui/sidebar/RuntimeSidebarPageUi.inl` | 分页 / 滚轮控制器的本地 UI 宿主适配（读 `RuntimeWindowState`、原生滚轮 delta、重绘） |
| `src/ui/sidebar/RuntimeHealthPipStrip.inl` | 血量 pip 条（按填充帧数 / 像素数绘制）内联实现 |
| `src/ui/sidebar/RuntimePaintSecretLabPhase.inl` | 秘密实验室相位绘制占位（当前为空桩，见缺口） |
| `src/ui/selection/RuntimeSelectionFrameRaster.h` | 选中框光栅写入统计与原生 GDI 写标记接口 |
| `src/ui/selection/RuntimeSelectedPathBatch.h` | 选中单位路径线批量数据（含老兵帧缓存标记） |
| `src/ui/selection/RuntimeSelectedPathRaster.h` | 选中路径线光栅接口 |
| `src/ui/selection/RuntimeIdleVariantSelection.inl` | 空闲变体选择缓存占位（当前为空桩） |
| `src/ui/selection/RuntimeChronosphereSelection.inl` | 两阶段超武（时空门）本地落点标记动画与音频启停 |
| `src/ui/radar/RuntimeRadarCellColors.h` | 雷达格点颜色 / 资源位稠密 + 回退存储（512×512 稠密表） |
| `src/ui/radar/RuntimeRadarPixelStore.h` | 雷达像素存储（内存 DC / DIB / 顶底位图写入） |
| `src/ui/radar/RuntimeRadarRulesColorsMemo.h` | 规则归属色表记忆化（INI 源身份 + 字节级命中校验） |
| `src/ui/radar/RuntimeRadarTerrainColorsMemo.h` | 地形雷达色记忆化 |
| `src/ui/radar/RuntimeRadarSampledTerrain.h` | 雷达采样地形键集合（采样禁用 / 容量回退） |
| `src/ui/hud/RuntimeSuperWeaponTimerText.inl` | 超武倒计时文字绘制接口 |
| `src/ui/diplomacy/RuntimeDiplomacyPage.inl` | 外交页占位（当前为空桩，交互在指针控制器内） |
| `src/ui/cursor/RuntimePanCursor.inl` | 平移光标占位（当前为空桩） |
| `src/ui/cursor/RuntimeHarvesterCursor.inl` | 采矿车光标占位（当前为空桩） |
| `src/ui/input/RuntimeReferencePresentationController.h` | `RuntimeBuildKind` / `RuntimeSidebarTool` 枚举与呈现输入结构 |
| `src/ui/input/RuntimeInputPixelValues.h` | 与设备无关的像素点 / 矩形 / 视口 / 侧栏矩形与命中判定 |
| `src/ui/input/RuntimeCursorController.h` / `.cpp` / `RuntimeCursorUi.inl` | 光标语境判定与原生库存光标加载 / 设置 |
| `src/ui/input/RuntimePointerIngressController.h` / `.cpp` / `RuntimePointerIngressUi.inl` | 指针事件准入、外交 / 聊天抢占、命中控件与外交面板交互 |
| `src/ui/input/RuntimeMapLeftPointerController.cpp` | 左键手势：菜单 / 侧栏 / 雷达跳转 / 超武 / 工具 / 分页 / 按钮 / 世界选中与目标命令 |
| `src/ui/input/RuntimeMapRightPointerController.cpp` | 右键手势：拖拽平移、松开单击取消 / 跟随 / 集结 / 目标命令、取消事件 |
| `src/ui/input/RuntimeKeyboardRouterController.cpp` | 键盘准入与子控制器路由顺序、强制攻击（Ctrl）按住态 |
| `src/ui/input/RuntimeKeyboardIngressController.h` / `.cpp` | 键盘事件准入与原始键码 / 重复态适配 |
| `src/ui/input/RuntimeSelectionKeyController.h` | 选中类快捷键接口声明 |
| `src/ui/input/RuntimeTeamKeyController.h` / `.cpp` | 数字编组创建 / 选择 / 追加 / 居中 |
| `src/ui/input/RuntimeCameraKeyController.h` | 镜头 / 视图快捷键接口声明 |
| `src/ui/input/RuntimeLocalNavigationController.cpp` | 对象循环、视图位存 / 取、快速选择（同类 / 战斗 / 老兵 / 血量）、雷达事件居中 |
| `src/ui/input/RuntimeReferenceCommunicationController.cpp` | 外交 / 结盟 / 聊天 / 信标 / 欢呼 / 叫嚣快捷键与外交页导航 |
| `src/ui/input/RuntimeChatTextController.h` / `.cpp` | 聊天 / 信标文本草稿编辑、退格代理项处理、回车提交与 UTF-8 编码 |
| `src/ui/input/RuntimePlayerCommands.h` | 快捷键默认绑定表、修饰键位掩码、编组 / 快速选择本地命令、目标命令种类判定 |
| `src/ui/input/RuntimePlayerCommunication.h` / `RuntimePlayerCommunication.inl` | 聊天行 / 信标 / 欢呼 / 叫嚣呈现状态、可见性与命令校验落地 |
| `src/ui/input/RuntimePlayerHotkeysUi.inl` | 快捷键绑定查询宿主、聊天命令同步重入校验与派发适配 |
| `src/ui/input/RuntimeInputCancellationController.h` | 输入取消事件（失焦 / 取消模式 / 应用停用）接口 |
| `src/ui/input/RuntimeLocalNavigationController.h` | 本地导航快捷键宿主接口 |
| `src/ui/input/RuntimePlanningFactoryKeyController.h` | 规划 / 工厂快捷键接口 |
| `src/ui/input/RuntimeSessionKeyController.h` | 结算 / 选项 / 存读档会话级键盘接口 |
| `src/ui/time/RuntimeUiClock.h` | UI 毫秒时钟类型、双击 / 悬停 / 横幅 / 就绪闪烁等时间谓词 |
| `src/ui/values/RuntimeColorValues.h` | 与设备无关的 RGB 颜色值封装（打包 0xAABBGGRR 通道拆分） |

消费方（非 `ui/` 目录，属于本章链路）：

| 文件 | 职责 |
| --- | --- |
| `src/runtime/host/RuntimeShell.cpp` | 侧栏 / 雷达 / 选中框 / 血量条 / 勋章 / 超武倒计时等实际 GDI 绘制与几何函数；本章绝大多数 `src/runtime/host/RuntimeShell.cpp:行号` 出处 |
| `src/runtime/session/detail/RuntimeProductionOperations.h` | 侧栏宽度、电力显示结构、雷达投影结构、电力段计数等会话侧常量与结构 |
| `src/runtime/session/RuntimeSessionFeedback.cpp` | 电力显示 produced/used 与动画推进 |

---

## 1. 侧栏整体布局

### 1.1 画布划分与宽度

战局内客户区被切成左侧地图视口与右侧固定宽度侧栏；侧栏宽度为 **168 像素**（`src/runtime/session/detail/RuntimeProductionOperations.h:333`）。

| 元素 | 几何 / 计算 | 出处 |
| --- | --- | --- |
| 地图视口 | 客户区右边界减去侧栏宽度；右边界被裁到 `client.left` 与 `client.right - sidebarWidth` 之间 | `src/ui/input/RuntimeInputPixelValues.h:58-63` |
| 侧栏矩形 | 客户区左边界取 `client.right - sidebarWidth` 与原值的较大者 | `src/ui/input/RuntimeInputPixelValues.h:65-70`；`src/runtime/host/RuntimeShell.cpp:3180-3185` |
| 命中判定 | 点在矩形内为**闭区间**（含右 / 下边界） | `src/ui/input/RuntimeInputPixelValues.h:74-80` |
| 矩形相交 | 半开区间相交判定（框选用） | `src/ui/input/RuntimeInputPixelValues.h:82-88` |

### 1.2 侧栏纵向分段（自上而下）

| 分段 | 高度（px） | 偏移计算 | 出处 |
| --- | --- | --- | --- |
| 资源计数行 `credits` | 16 | `sidebar.top` | `src/runtime/host/RuntimeShell.cpp:1981` |
| 顶部条 `top` | 32 | `topOffset = creditsHeight` | `src/runtime/host/RuntimeShell.cpp:1982,1991` |
| 雷达区 | 110 | `radarOffset = topOffset + topHeight` | `src/runtime/host/RuntimeShell.cpp:1983,1992-1993` |
| 上方面板 `side1`（工具按钮 + 分页标签） | 69 | `side1Offset = radarOffset + radarHeight` | `src/runtime/host/RuntimeShell.cpp:1984,1994-1995` |
| 按钮列表行 `side2`（每行 50） | 50 | `listOffset = side1Offset + side1Height` | `src/runtime/host/RuntimeShell.cpp:1985,1996-1997` |
| 底部分页 / 工具条 `side3` | 26 | 列表底部之上 | `src/runtime/host/RuntimeShell.cpp:1986` |
| 装饰帽 `addon` | 63 | `side3` 之下；不引入第二条按钮带 | `src/runtime/host/RuntimeShell.cpp:1987-1990` |

列表可容纳的 `side2` 行数由剩余高度除以 50 得到（`src/runtime/host/RuntimeShell.cpp:3205-3216`）；列表底部取 `side3` 顶与 `addon` 顶的较小者（`src/runtime/host/RuntimeShell.cpp:3218-3219`）。

### 1.3 皮肤帧资源

侧栏静态层先绘制到离屏缓冲，再整幅 BitBlt（`src/runtime/host/RuntimeShell.cpp:19630-19675`）。各面板帧名与矩形：

| 面板 | SHP 逻辑名 | 调色板 | 出处 |
| --- | --- | --- | --- |
| 资源计数行 | `credits` frame 0 | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19437-19453` |
| 顶部条 | `top` frame 0 | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19455-19471` |
| 上方面板 | `side1` frame 0 | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19473-19489` |
| 按钮行面板 | `side2` frame 0（按行重复平铺） | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19491-19511` |
| 底部面板 | `side3` frame 0 | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19514-19530` |
| 装饰帽 | `addon` frame 0 | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19532-19553` |
| 外交按钮 | `diplobtn` frame 0 | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19600-19609` |
| 选项按钮 | `optbtn` frame 0 | `sidebar` | `src/runtime/host/RuntimeShell.cpp:19611-19627` |

### 1.4 分页结构与标签

生产目录按四类分页，枚举顺序即标签槽位顺序（`src/ui/input/RuntimeReferencePresentationController.h:13-18`）：

| 标签槽位 | `RuntimeBuildKind` | 含义 |
| --- | --- | --- |
| 0 | `Building` | 建筑 |
| 1 | `Defense` | 防御（含超武按钮槽位） |
| 2 | `Infantry` | 步兵 |
| 3 | `Unit` | 载具 |

标签索引映射（`src/ui/sidebar/RuntimeSidebarPageController.cpp:6-18`）。每页按钮数为两列 × 列表行数，下限 2（`src/runtime/host/RuntimeShell.cpp:19235-19241`）。

按钮网格几何（`src/runtime/host/RuntimeShell.cpp:19335-19350`）：

| 项 | 值 |
| --- | --- |
| 列数 | 2 |
| 按钮宽 / 高 | 60 / 48 |
| 行高 | 50 |
| 左列 left | `sidebar.left + 22` |
| 右列 left | `sidebar.left + 85` |
| 按钮顶 | `sidebar.top + listOffset + 1 + row * 50` |

超武按钮只出现在 `Defense` 页，排在本页普通目录条目之后，共用同一两列网格（`src/runtime/host/RuntimeShell.cpp:19353-19378`；`src/runtime/host/RuntimeShell.cpp:19291-19333`）。

### 1.5 分页切换：按钮与滚轮

翻页按钮几何（`src/runtime/host/RuntimeShell.cpp:19380-19386`）：宽 46、高 25，顶为 `side3Top + 7`；上一页 left = `sidebar.left + 84`，下一页 left = `sidebar.left + 38`。

- 按钮命中：上一页仅在 `page > 0` 时返回 -1，下一页仅在 `page + 1 < pageCount` 时返回 +1，否则 0（`src/runtime/host/RuntimeShell.cpp:19388-19407`）。
- 页计数：`max(1, ceil((过滤后目录数 + 防御页超武数) / 每页按钮数))`（`src/ui/sidebar/RuntimeSidebarPageController.cpp:36-47`）。
- 翻页状态按标签分别记忆：`buildPageByTab[4]`（`src/ui/sidebar/RuntimeSidebarPageUi.inl:7-11,47-49`）。
- 滚轮：以 120 为一个步长累计余数 `buildWheelRemainderByTab`；累计 `/120` 得步数，页码 `clamp(previous - steps, 0, pages-1)`；菜单打开时不响应（`src/ui/sidebar/RuntimeSidebarPageController.cpp:60-93`；滚轮 delta 取自 `GET_WHEEL_DELTA_WPARAM`，`src/ui/sidebar/RuntimeSidebarPageUi.inl:61`）。
- 侧栏左键翻页按钮流程见 `src/ui/input/RuntimeMapLeftPointerController.cpp:181-190`。

### 1.6 按钮状态与绘制

每个可见条目先解析可用性（`src/runtime/host/RuntimeShell.cpp:20475-20497`）：

| 状态 | 判定 | 出处 |
| --- | --- | --- |
| `enabled` 可排队 | `availability.canQueue` | `src/runtime/host/RuntimeShell.cpp:20480` |
| `ready` 可开始 | `availability.canStart` | `src/runtime/host/RuntimeShell.cpp:20481` |
| `busy` 生产中 | 存在活跃生产订单 | `src/runtime/host/RuntimeShell.cpp:20483-20485` |
| `onHold` 挂起 | 所在生产线 `onHold` | `src/runtime/host/RuntimeShell.cpp:20486-20489` |
| `placementReady` 待摆放 | 存在就绪待部署订单 | `src/runtime/host/RuntimeShell.cpp:20490-20492` |
| `hovered` 悬停 | 鼠标有效且点在按钮矩形内 | `src/runtime/host/RuntimeShell.cpp:20493-20495` |
| 多队列计数 | 步兵 / 载具类；显示数 = 排队数 +（生产中 ? 1 : 0） | `src/runtime/host/RuntimeShell.cpp:20496-20504` |

图标绘制：

| 元素 | 实现 / 资源 | 出处 |
| --- | --- | --- |
| cameo 图标 | 索引图像按调色板绘制；`enabled/ready/placementReady` 任一为真时按可用态上色 | `src/runtime/host/RuntimeShell.cpp:18695-18718,20506-20515` |
| 缺图标回退 | 对角叉线；可用笔色 `RGB(84,96,108)`，禁用 `RGB(54,60,66)` | `src/runtime/host/RuntimeShell.cpp:18720-18739` |
| 生产进度时钟遮罩 | 47 点多边形轮廓；进行中填充 `RGB(28,188,232)`，挂起填充 `RGB(218,160,64)` | `src/runtime/host/RuntimeShell.cpp:18750-18854` |
| 时钟 SHP 帧 | `gclock2` frame 0 为空哨兵，按进度阶段映射到 authored 帧 1..54 | `src/runtime/host/RuntimeShell.cpp:18856-18875` |
| 就绪闪烁 | 500 ms 周期，半周期亮 | `src/ui/time/RuntimeUiClock.h:32-38`；`src/runtime/host/RuntimeShell.cpp:20552-20566` |
| 就绪文字 | “就绪”，黑描边阴影 + 白字，居中单行 | `src/runtime/host/RuntimeShell.cpp:20553-20565` |
| 边框 | 仅 `hovered / placementReady / busy / onHold / ready` 之一成立时描边 | `src/runtime/host/RuntimeShell.cpp:20568-20582` |
| 多队列计数角标 | 底框 `RGB(2,5,8)`，边框 `RGB(180,228,252)`，数字 `RGB(232,248,255)`；矩形 `right-25..right-3, top+3..top+19` | `src/runtime/host/RuntimeShell.cpp:20584-20604` |

边框配色优先级（`src/runtime/host/RuntimeShell.cpp:20569-20576`）：

| 状态 | 颜色 |
| --- | --- |
| 待摆放 `placementReady` | `RGB(120,242,220)` |
| 挂起 `onHold` | `RGB(214,182,96)` |
| 生产中 `busy` | `RGB(92,204,236)` |
| 悬停 `hovered` | `RGB(152,204,236)` |
| 就绪 `ready` | `RGB(96,126,156)` |

分页标签三态帧：不可用 → frame 2，激活 / 高亮 → frame 1，否则 frame 0（`src/runtime/host/RuntimeShell.cpp:20460-20465`）。

> 说明：参考引擎按钮态以“可用性布尔 + 边框色 + 进度遮罩 + 闪烁”表达；未发现原生按下（pressed）下陷帧，也未发现沿按钮横向扫光 / 流光相位实现。

### 1.7 健康值条 / 等级勋章条

选中单位的血量条与勋章绘制在**世界投影物之上**（侧栏 `side1` 面板内不另画选中物血条）：

| 元素 | 实现 | 出处 |
| --- | --- | --- |
| 单位 / 建筑血条入口 | `drawRuntimeSelectedHealthBar`；建筑走足迹分段条，单位走 pip 条 | `src/runtime/host/RuntimeShell.cpp:22798-22807,22858-22874` |
| 血条资源 | `sidebar` / `pips`，frame 0 背景；绿 1 / 黄 2 / 红 4（按 `conditionYellow`、`conditionRed` 阈值选帧） | `src/runtime/host/RuntimeShell.cpp:22889-22906` |
| 绿段配色 | 暗 `RGB(0,50,0)` / 中 `RGB(0,128,0)` / 亮 `RGB(0,255,0)` | `src/runtime/host/RuntimeShell.cpp:22876-22878` |
| 黄段配色 | 暗 `RGB(50,50,0)` / 中 `RGB(128,128,0)` / 亮 `RGB(255,255,0)` | `src/runtime/host/RuntimeShell.cpp:22881-22883` |
| 红段配色 | 暗 `RGB(134,17,34)` / 中 `RGB(204,34,34)` / 亮 `RGB(238,68,51)` | `src/runtime/host/RuntimeShell.cpp:22885-22887` |
| 血量 pip 条内联 | 按填充帧数 / 像素数逐段绘制，资源 / 回退计数由窗口状态统计 | `src/ui/sidebar/RuntimeHealthPipStrip.inl`；`src/runtime/host/RuntimeShell.cpp:22961-23005` |
| 老兵勋章 | `veterancy > 0` 才画；`veterancy >= 2` 为精英；帧来自 `runtimeVeterancyPipFrames` 缓存，缺帧回退 14×14 角标 | `src/runtime/host/RuntimeShell.cpp:23128-23181` |
| 勋章帧解析 | 纯函数 + 每投影批 memo 标记，命中后复用 | `src/runtime/host/RuntimeShell.cpp:22792-22794,23084-23125` |

秘密实验室相位：`src/ui/sidebar/RuntimePaintSecretLabPhase.inl` 当前为空桩（见缺口）。

### 1.8 侧栏工具按钮

工具枚举：`None / Repair / Sell`（`src/ui/input/RuntimeReferencePresentationController.h:20-24`）。

- 命中与切换：左键点到工具槽则在“激活 ↔ 取消”间切换，状态文本回写 `interactionStatus`，播放 `Tab` 音（`src/ui/input/RuntimeMapLeftPointerController.cpp:163-171`）。
- 世界落点：激活工具后左键点中建筑才派发维修 / 出售；非建筑给出“needs a structure”提示；出售完成后不自动退出，维修完成后退出工具（`src/ui/input/RuntimeMapLeftPointerController.cpp:420-439`）。
- 右键取消工具（`src/ui/input/RuntimeMapRightPointerController.cpp:32-38,136-140`）。

---

## 2. 选中与编组

### 2.1 选中状态与手势状态

左键手势状态 `leftPointer`：`active / moved / start / current`；右键手势 `rightPointer`：`active / moved / start / last / cursorDelta`（宿主接口见 `src/ui/input/RuntimeMapLeftPointerController.h`、`src/ui/input/RuntimeMapRightPointerController.h`）。

- 按下：世界已加载才继续；菜单打开走菜单路径；点在侧栏走侧栏路径；观察员拒绝选中；聚焦窗口并 `captureWindow()`；记录起点，状态置 `selecting`（`src/ui/input/RuntimeMapLeftPointerController.cpp:358-379`）。
- 移动：起点位移平方和 > 16 才标记 `moved`（框选），否则视为单击（`src/ui/input/RuntimeMapLeftPointerController.cpp:397-402`；右键同阈值 `src/ui/input/RuntimeMapRightPointerController.cpp:84-87`）。
- 松开：先释放捕获；依次尝试超武落点 → 工具落点 → 待摆放确认 → 目标点击（跟随 / 集结 / 信标）→ 框选或单选（`src/ui/input/RuntimeMapLeftPointerController.cpp:405-456,457-525`）。

### 2.2 修饰键与目标命令种类

松开时按键位掩码解析（`src/ui/input/RuntimeMapLeftPointerController.cpp:460-464`）：

| 位 | 掩码 | 语义 |
| --- | --- | --- |
| Shift | `rawButtons & 4` | 追加 `append` |
| Ctrl | `rawButtons & 8` | 切换 `toggle` / 强制攻击 |
| Alt | `modifierDown(Alt)` | 移动 / 跟随 / 护卫目的地 |

目标命令种类判定（`src/ui/input/RuntimePlayerCommands.h:167-175`）：

| 组合 | 命令 |
| --- | --- |
| Alt | 有目标 → `Follow`；Ctrl+Alt → `GuardDestination`；否则 `Move` |
| Ctrl + Shift（无 Alt） | `AttackMove` |
| Ctrl | 有目标 → `Attack`；否则 `ForceFireCell` |
| 其余 | `Primary` |

### 2.3 框选

- 拖拽矩形由 `start` / `current` 归一化得到半开相交判定（`src/ui/input/RuntimeMapLeftPointerController.cpp:472-476`）。
- 遍历投影包围盒，仅当 `objectCanBeBoxSelected` 且矩形相交才入列；`applySelection(selected, append, toggle)` 落地并清空选中信标（`src/ui/input/RuntimeMapLeftPointerController.cpp:477-482`）。
- 框选反馈：左键拖拽且未强制攻击时，画 1 px 点线框，颜色 `RGB(228,238,250)`（`src/runtime/host/RuntimeShell.cpp:23992-24000`）。

### 2.4 单选 / 追加 / 双击同类

- 未移动（单击）：先尝试目标点击（信标 / 跟随 / 集结）；否则投影拾取点中物，有则单选加入 `selected`，无则清空选择（`src/ui/input/RuntimeMapLeftPointerController.cpp:483-521`）。
- 追加 / 切换：`applySelection(..., append, toggle)`；右键或空点清除信标（同上）。
- **双击同类**：参考引擎未把 `WM_LBUTTONDBLCLK` 接入战局内“双击选中同类”路径；同类选择由快捷键 `T`（`TypeSelect`）完成。两次相同选择键在 500 ms 内触发“全图”模式（`src/ui/input/RuntimeLocalNavigationController.cpp:54-80`；双击时间窗 `src/ui/time/RuntimeUiClock.h:17-20`）。

### 2.5 选中高亮 / 归属 / 血量反馈

| 反馈 | 几何 / 颜色 | 出处 |
| --- | --- | --- |
| 单位选中框 | 宽 36、高 40、顶偏移 30；锚点水平居中 | `src/runtime/host/RuntimeShell.cpp:9705-9719` |
| 选中笔 | 2 px 实线 `RGB(113,181,255)` | `src/runtime/host/RuntimeShell.cpp:23843` |
| 建筑选中笔 | 2 px 实线 `RGB(236,242,238)` | `src/runtime/host/RuntimeShell.cpp:23844` |
| 血条框宽 | 单位 36，其余 18；框高 4，位于包围盒顶 -8（含命中像素 Y 偏移） | `src/runtime/host/RuntimeShell.cpp:9721-9737` |
| 精灵包围盒 | 四周各膨胀 3 px | `src/runtime/host/RuntimeShell.cpp:9739-9745` |
| 选中路径线 | 批量路径数据与光栅写入由 `selection/` 两个头文件持有 | `src/ui/selection/RuntimeSelectedPathBatch.h`、`src/ui/selection/RuntimeSelectedPathRaster.h` |
| 时空门落点标记 | 两阶段超武第一目标后本地挂 `LocalChronosphereSelection` 动画，独立 SHP 循环与音频 | `src/ui/selection/RuntimeChronosphereSelection.inl:5-43` |

### 2.6 数字编组

本地编组存储为 10 组 `std::vector<int>`（`src/ui/input/RuntimePlayerCommands.h:68`）。快捷键（`src/ui/input/RuntimeTeamKeyController.cpp:14-45`）：

| 操作 | 键（默认） | 行为 |
| --- | --- | --- |
| 创建编组 | Ctrl + 数字 1..0 | 当前可选单位写入组槽位；不重复触发 |
| 选择编组 | 数字 1..0 | 从组内过滤不可选后选中 |
| 追加编组 | Shift + 数字 | 在当前选择上并入组成员 |
| 居中编组 | Alt + 数字 | 选中并把镜头移到成员质心 |
| 双击 / 再按 | 同数字 500 ms 内 | 居中到成员质心 |

编组选择会清除信标；质心按成员格点坐标平均后 `centerCamera`（`src/ui/input/RuntimeTeamKeyController.cpp:27-42`）。

### 2.7 快速选择与对象循环

| 快捷键 | 动作 | 出处 |
| --- | --- | --- |
| `T` TypeSelect | 选当前已选类型（屏内；同键双击全图） | `src/ui/input/RuntimeLocalNavigationController.cpp:60,66-76` |
| `P` CombatantSelect | 选战斗单位 | 同上 `:61` |
| `Y` VeterancyNav | 按老兵等级循环三档 | 同上 `:62`；分档逻辑 `src/ui/input/RuntimePlayerCommands.h:113-128` |
| `U` HealthNav | 按血量三档循环（>2/3 绿、>1/3 黄、其余红） | 同上 `:63`；分档 `src/ui/input/RuntimePlayerCommands.h:81-85` |
| `M` / `N` Previous/NextObject | 按 ID 排序在可移动单位间前后循环，跟随镜头 | `src/ui/input/RuntimeLocalNavigationController.cpp:6-29` |

---

## 3. 雷达小地图

### 3.1 区域与内框

| 元素 | 几何 | 出处 |
| --- | --- | --- |
| 雷达外框 | 侧栏顶部偏移起，高 110，与侧栏同宽 | `src/runtime/host/RuntimeShell.cpp:3187-3194` |
| 雷达内框 | 左 +13、右 -15，上下不变 | `src/runtime/host/RuntimeShell.cpp:3196-3203` |
| 背景填充 | `RGB(2,8,10)` 实色刷 | `src/runtime/host/RuntimeShell.cpp:19742-19744` |

### 3.2 投影与坐标映射

投影结构 `RuntimeRadarProjection`（`src/runtime/session/detail/RuntimeProductionOperations.h:483` 起），布局结构 `RuntimeRadarLayout{rect, scaleX, scaleY}`（`src/runtime/host/RuntimeShell.cpp:3479-3483`）。

- 缩放：两轴独立填满内框，`scaleX = 宽 / spanX`，`scaleY = 高 / spanY`，避免长 / 宽地图带信箱黑边（`src/runtime/host/RuntimeShell.cpp:3506-3532`）。
- 世界 → 屏幕：`x = left + floor((dxy.x - minX) * scaleX)`，clamp 到内框（`src/runtime/host/RuntimeShell.cpp:3534-3563`）。
- 屏幕 → 世界：点击点反算 `dxy = min + ((pixel - rect.left) + 0.5) / scale`，再判投影包含（`src/runtime/host/RuntimeShell.cpp:16776-16786`）。
- 不可用条件：雷达未解锁、过渡动画中、投影无效、缩放 ≤ 0、点不在内框，均返回空（`src/runtime/host/RuntimeShell.cpp:16767-16774`）。

### 3.3 绘制输入

| 输入 | 处理 | 出处 |
| --- | --- | --- |
| 地形采样 | 逐像素采样 Dxy，转经典格点键；采样集合禁用时整路回退 | `src/runtime/host/RuntimeShell.cpp:19803-19833` |
| 视野遮蔽 | 受限视野下走 `vision.gridViewFor` / `visibilityFor`；未受限视为 Visible | `src/runtime/host/RuntimeShell.cpp:19798-19801,19862-19871` |
| 地形色 | 规则色表解析左右半色，按左半色半亮度落格；缺色回退按水面 / 悬崖 / 岸坡 | `src/runtime/host/RuntimeShell.cpp:19881-19903` |
| 归属色表 | Rules INI 归属 remap 色表，经 memo 按源身份 + 字节命中复用 | `src/ui/radar/RuntimeRadarRulesColorsMemo.h:78-159`；加载 `src/runtime/host/RuntimeShell.cpp:19756-19766` |
| 格点存储 | 512×512 稠密颜色表 + present/resource 位；越界走 `unordered_map/set` 回退 | `src/ui/radar/RuntimeRadarCellColors.h:63-132` |
| 像素落盘 | 内存 DC / DIB / 顶底位图接口 | `src/ui/radar/RuntimeRadarPixelStore.h` |

回退地形色（`src/runtime/host/RuntimeShell.cpp:19875,19897-19902`）：

| 地形 | 颜色 |
| --- | --- |
| 默认点 | `RGB(84,116,70)` |
| 水面 | `RGB(38,70,120)` |
| 悬崖 | `RGB(128,92,54)` |
| 岸 / 坡道 | `RGB(112,126,78)` |

### 3.4 点击导航

左键点中雷达内框格点 → `centerCamera` 到该格，刷新投影，状态文本 `radar jump x,y`，结束本次手势（`src/ui/input/RuntimeMapLeftPointerController.cpp:150-159`）。雷达事件快捷键空格居中到最近事件格（`src/ui/input/RuntimeLocalNavigationController.cpp:82-89`）。

---

## 4. 光标

### 4.1 语义请求与原生资源

语义枚举（`src/ui/input/RuntimeCursorController.h:9-11`）：

| `RuntimeCursorRequest` | 原生加载 | 资源号 | 出处 |
| --- | --- | --- | --- |
| `Hidden` | 空光标（`SetCursor(nullptr)`） | — | `src/ui/input/RuntimeCursorUi.inl:40` |
| `SystemArrow` | 系统箭头 | 32512 | `src/ui/input/RuntimeCursorUi.inl:41-45` |
| `StockArrow` | 库存箭头 | 32512 | 同上 |
| `StockAction` | 库存行动（箭头/操作） | 32515 | `src/ui/input/RuntimeCursorUi.inl:41` |
| `StockForbidden` | 库存禁止 | 32648 | `src/ui/input/RuntimeCursorUi.inl:42` |
| `StockHand` | 库存手型 | 32649 | `src/ui/input/RuntimeCursorUi.inl:43` |

库存光标经 `LoadCursorW(nullptr, MAKEINTRESOURCEW(id))` 加载（`src/runtime/host/RuntimeShell.cpp:24007-24009`；`src/ui/input/RuntimeCursorUi.inl:44-48`）。

### 4.2 语境判定顺序

`handleRuntimeCursor`（`src/ui/input/RuntimeCursorController.cpp:54-101`）：

1. 非客户区直接返回；拿不到窗口状态或世界未加载返回。
2. 菜单打开 / 已结算 / 外交页打开 → `SystemArrow`。
3. 光标在地图视口内：
   - 本地超武瞄准中 → `Hidden`；
   - 遍历超武快照，命中本地或路由激活中的目标 → 预览落点有效 `StockAction`，否则 `StockForbidden`；
   - 其余视口内情况 → `Hidden`。
4. 不在视口内 → 走 `runtimeCursorForInputPoint` 点语境判定。

点语境判定（`src/ui/input/RuntimeCursorController.cpp:29-52`）：

| 区域 / 条件 | 光标 |
| --- | --- |
| 侧栏内：超武按钮 / 生产按钮 / 翻页按钮 / 分页标签 / 工具槽 / 雷达格点任一命中 | `StockHand` |
| 侧栏其余位置 | `StockArrow` |
| 视口外 | `StockArrow` |
| 有待摆放物件：摆放有效 | `StockAction`；无效 `StockForbidden` |
| 激活侧栏工具 | `StockAction` |
| 选中含可移动单位 | `StockAction` |
| 其余 | `StockArrow` |

> 边界：参考引擎战局内光标未走自定义 `MOUSE.SHP` 逐语境帧（攻击 / 移动 / 部署 / 无效 / 悬停按钮的西武原图帧位）；`src/ui/cursor/` 下 `RuntimePanCursor.inl`、`RuntimeHarvesterCursor.inl` 为空桩（见缺口）。

---

## 5. 输入路由

### 5.1 事件准入与抢占层

指针事件枚举与 Win32 消息映射（`src/ui/input/RuntimePointerIngressController.h:13-16`；`src/ui/input/RuntimePointerIngressUi.inl:122-131`）。准入控制器 `handleRuntimePointerIngress`（`src/ui/input/RuntimePointerIngressController.cpp:154-219`）：

1. 已结算：仅响应得分页继续按钮的按下 / 松开 / 悬停；其余终态指针消息吞掉（`:157-180`）。
2. 菜单未开且（聊天或外交打开）：
   - 外交打开：捕获变更 / 取消模式 / 失焦 / 应用停用为取消消息；其余指针消息进外交面板；左键按下捕获，左键 / 右键松开释放捕获（`:183-202`）。
   - 聊天打开：左键 / 右键 / 双击 / 滚轮消息被吞；右键松开关闭外交、聊天并清空草稿（`:204-216`）。
3. 未被抢占则放行到战局地图控制器。

### 5.2 左键手势流程

| 阶段 | 处理 | 出处 |
| --- | --- | --- |
| 按下 | 菜单 → 菜单按下；侧栏 → 雷达跳转 / 超武 / 工具 / 标签 / 翻页 / 生产按钮；世界 → 捕获、记录手势起点 | `src/ui/input/RuntimeMapLeftPointerController.cpp:358-379` |
| 侧栏-雷达 | 命中格点即 `centerCamera` 并结束手势 | `:150-159` |
| 侧栏-超武 | 按规则目标数（0 / 1 / 2）开始瞄准；0 目标直接确认 | `:114-147` |
| 侧栏-工具 | 激活 / 取消工具，状态文本 + Tab 音 | `:163-171` |
| 侧栏-标签 | 切换 `BuildTab` 并钳制页码 | `:172-180` |
| 侧栏-翻页 | 页码 clamp 后切换 | `:181-190` |
| 侧栏-生产按钮 | 可就绪则进入摆放；否则排队（Shift 追加到队首） | `:191-203` |
| 移动 | 摆放预览更新；手势位移超阈值标记 moved | `:381-404` |
| 松开-超武 | 路由 / 离线两路提交目标（格点 + 可拾取目标）；两阶段落点挂时空门标记 | `:206-271` |
| 松开-目标点击 | 信标 / 路径点 / 跟随 / 集结点优先 | `:273-356` |
| 松开-工具 | 对建筑派发维修 / 出售 | `:420-439` |
| 松开-摆放 | 路由校验后确认摆放；离线直接落建 | `:441-455` |
| 松开-选中 / 命令 | 修饰键解析 → 框选 / 单选 / 目标命令 / 工厂主副行为 | `:457-525` |

### 5.3 右键手势流程

| 阶段 | 处理 | 出处 |
| --- | --- | --- |
| 按下 | 世界未加载 / 菜单打开直接返回；视口外先取消超武瞄准；侧栏内：生产按钮副行为、清除工具；视口内捕获并记录平移手势 | `src/ui/input/RuntimeMapRightPointerController.cpp:6-53` |
| 移动 | 累计总位移超 16 标记 moved；未开惯性时按逐帧增量平移 | `:78-97` |
| 惯性推进 | 按滚速率 0..6 +3 缩放，步长 `clamp(|delta|/4,1,32)` | `:99-110` |
| 松开-单击 | 清信标 / 退出跟随·集结·信标瞄准 / 取消超武 / 退出摆放 / 取消规划 / 清工具；空选择取消交互；点中外部目标派主命令；墙体主命令 | `:112-157` |
| 松开-拖拽 | 仅状态回写“camera panned” | `:159-186` |
| 取消事件 | 失焦 / 取消模式 / 应用停用：结束手势并释放捕获；捕获变更中断手势 | `:188-207` |

平移增量钳制 ±96；到达地图边缘状态置 “map edge”（`src/ui/input/RuntimeMapRightPointerController.cpp:55-69`）。

### 5.4 键盘准入与路由

路由顺序（`src/ui/input/RuntimeKeyboardRouterController.cpp:35-53`）：

1. 菜单打开 / 已结算 → 不路由战局快捷键。
2. `rejectObserverSelection()`（观察员不响应选中类键）。
3. `handleReferenceKey`（标签 / 工具 / 工厂 / 规划等参考键）。
4. 小键盘 0..9 归一化为主键盘数字（`:43-45`）。
5. 依次：对象循环 → 编组 → 视图导航 → 快速选择 → 规划 / 工厂键 → 雷达事件居中。

强制攻击按住态（Ctrl，VK 0x11 / 0xA2 / 0xA3）由独立小控制器维护，失焦 / 应用停用清零；鼠标消息中以 MK_CONTROL 位刷新（`src/ui/input/RuntimeKeyboardRouterController.cpp:3-33`）。

### 5.5 快捷键默认绑定

绑定表与修饰键位（`src/ui/input/RuntimePlayerCommands.h:17-24,28-55`）：修饰掩码 Shift `0x100`、Ctrl `0x200`、Alt `0x400`；合法键码低 8 位非零且高位不超 `0x7ff`。

| 动作 ID | 默认键 | 出处 |
| --- | --- | --- |
| TypeSelect / CombatantSelect / VeterancyNav / HealthNav | `T` / `P` / `Y` / `U` | `:30` |
| PreviousObject / NextObject | `M` / `N` | `:31` |
| PlanningMode / Follow | `Z` / `F` | `:32` |
| CenterOnRadarEvent / CenterBase | 空格(32) / `H` | `:33` |
| StructureTab / DefenseTab / InfantryTab / UnitTab | `Q` / `W` / `E` / `R` | `:34` |
| ToggleRepair / ToggleSell | `K` / `L` | `:35` |
| ShowCoordinates | `O` | `:35` |
| Diplomacy / ToggleAlliance | Tab(9) / `A` | `:36` |
| PlaceBeacon / DeleteBeacon | `B` / Delete(46) | `:36,74` |
| AllToCheer | `C` | `:37,87` |
| ChatToAll / ChatToAllies | Enter(13) / 36 | `:37,40` |
| SetPrimaryFactory / SetRallyPoint | Ctrl+`U` / `J` | `:38` |
| TeamSelect / TeamAddSelect / TeamCreate / TeamCenter | 数字 / Shift+数字 / Ctrl+数字 / Alt+数字 | `:43-46` |
| View1..4 / SetView1..4 | F1..F4(112..115) / Ctrl+F1..F4 | `:49-50` |
| Taunt_1..8 | `T`+1..8（116..123） | `:53` |

绑定可被 `keyboardmd.ini` 覆盖，查询走 `runtimeMenuKeyboardBindingCode(id, fallback)`（`src/ui/input/RuntimePlayerHotkeysUi.inl:104-106`）。

### 5.6 聊天与信标文本

| 项 | 规则 | 出处 |
| --- | --- | --- |
| 打开聊天 | `Enter` 对所有人 / 36 对盟友；清空草稿；非盟友聊天且有选中信标时进入信标文本编辑 | `src/ui/input/RuntimeReferenceCommunicationController.cpp:40-51` |
| 可打印字符 | ≥32 且非 127；草稿上限 80 个 UTF-16 代码单元 | `src/ui/input/RuntimeChatTextController.cpp:44-53` |
| 退格 | 删末单元；尾代理项成对删除低代理 + 高代理 | `:61-66` |
| 取消 | Esc(27) 关闭并清草稿 | `:58-60` |
| 提交 | Enter(13) 非重复；去空白后 UTF-8 编码；信标编辑缺选中信标则取消；空文本关闭 | `:67-74` |
| 命令种类 | 信标编辑 → `BeaconText`；盟友 → `ChatAllies`；否则 `ChatAll`；接收掩码取 `outgoingChatMask` | `:76-81` |
| UTF-8 编码 | 代理对重组为码点；超 `runtimeMultiplayerGameplayMaximumIdentifierBytes` 丢弃 | `:15-42` |
| 接收掩码 | 未显式指定时取全部人类槽位；发送掩码自带槽位位 | `src/ui/input/RuntimePlayerCommunication.inl:34-51` |

### 5.7 命令提交时机与同步重入

- UI 派发到世界命令前先过两道校验：信封校验 `validateRuntimeMultiplayerGameplayCommand` 与通信命令校验 `validateRuntimePlayerCommunication`；再按是否启用意图路由分别走 `router.communicate` 或本地 `applyRuntimePlayerCommunication`；失败回写“系统：操作未执行：<原因>”聊天行（`src/ui/input/RuntimePlayerHotkeysUi.inl:11-24`）。
- 目标 / 生产类命令的提交时机：摆放确认在左键松开视口内（`src/ui/input/RuntimeMapLeftPointerController.cpp:441-455`）；超武目标在松开落点（`:206-271`）；工厂集结 / 主行为在单击拾取后（`:341-343,495-505`）。
- 偏好保存：菜单滑块松开后 `persistMenuPreferences`（`src/ui/input/RuntimeMapLeftPointerController.cpp:49-58`）；战局内快捷键 / 编组为本地呈现状态，不入权威存档（边界见 `src/ui/input/RuntimePlayerCommands.h:177-179`）。

### 5.8 取消与焦点

- 取消事件接口覆盖失焦、取消模式、应用停用（`src/ui/input/RuntimeInputCancellationController.h`）。
- 右键拖拽手势在失焦 / 取消模式 / 停用时复位并释放捕获（`src/ui/input/RuntimeMapRightPointerController.cpp:188-207`）。
- 外交 / 聊天页在捕获变更、取消模式、失焦、停用消息下退出（`src/ui/input/RuntimePointerIngressController.cpp:30-35,184-188`）。

---

## 6. HUD 其余元素

### 6.1 资源计数与电力

| 元素 | 实现 | 出处 |
| --- | --- | --- |
| 资源行皮肤 | `credits` frame 0，高 16 | `src/runtime/host/RuntimeShell.cpp:19437-19453` |
| 电力显示结构 | `produced / used / animating` | `src/runtime/session/detail/RuntimeProductionOperations.h:467-471` |
| 电力段条 | 轨道高内每 3 px 一段；段宽 12、高 2；`powerp` 帧 3 已用 / 2 黄 / 1 绿 / 0 空 | `src/runtime/host/RuntimeShell.cpp:18403-18477` |
| 电力动画 | 步长 50 ms，每步 10 单位 | `src/runtime/session/detail/RuntimeProductionOperations.h:329-331` |
| 低电指示 | 图标底部 8 px 内边框 + 内填充矩形 | `src/runtime/host/RuntimeShell.cpp:18479-18506` |

资源数字 / 电力数字文字走通用位图文字机制（见 05b 章），本章不展开排版。

### 6.2 超武倒计时列表

绘制入口 `drawRuntimeSuperWeaponTimerList`（`src/runtime/host/RuntimeShell.cpp:23472-23588`）：

| 项 | 值 / 规则 | 出处 |
| --- | --- | --- |
| 边距 / 行高 | margin 8，rowHeight 18 | `:23504-23505` |
| 面板宽度 | `min(320, 可用宽, 内容宽)`，内容宽初值 120 | `:23510-23515` |
| 位置 | 视口右下，向上生长；裁剪到视口 | `:23516-23544` |
| 背景 | 1×1 黑像素 `RGB(0,0,0)`，AlphaBlend 透明度 160，外扩 4/3 px | `:23545-23553` |
| 行内容 | 标签列 `right-60`，倒计时列 `right-54..right` | `:23559-23581` |
| 颜色 | 按归属取 `runtimeSuperWeaponTimerColor` | `:23582` |
| 时间来源 | 剩余 ticks 按 `fixedTickHz` 换算秒，不再叠加速度倍率 | `:23561-23568` |

### 6.3 提示 / 聊天文字与 EVA

| 元素 | 规则 | 出处 |
| --- | --- | --- |
| 交互状态文本 | `interactionStatus` / `economyStatus` 在各控制器回写（选择中、雷达跳转、工具取消、超武不可用原因等） | 例 `src/ui/input/RuntimeMapLeftPointerController.cpp:155,166-167,195,225-228` |
| 聊天行队列 | `deque`，上限 8 条；存活 `15 × fixedTickHz` tick | `src/ui/input/RuntimePlayerCommunication.inl:105-111` |
| 聊天行可见性 | 本人或盟友可见；盟友 / 全员分流 | `:130-141` |
| 信标行 | 放置 / 文本编辑入聊天行；每方上限 3 个，不挤占旧标记 | `:143-172` |
| 欢呼 / 叫嚣 | 冷却 `10 × tickHz`；欢呼动画 2 tick；叫嚣按国家前缀选 `tau<国家>0<序号>.wav`，受 `tauntsEnabled` 开关约束 | `:181-217` |
| 结算横幅 / EVA | `resultPresentation.bannerVisible / bannerDurationMs` 与一次性 EVA 属结算章边界，本章不展开 | `src/runtime/host/RuntimeShell.cpp:27458-27499` |

### 6.4 外交面板

外交面板由指针准入层直接交互（`src/ui/input/RuntimePointerIngressController.cpp:95-152`）：

| 控件 ID | 作用 | 出处 |
| --- | --- | --- |
| 1059 | 关闭外交页 | `:97,126` |
| 1743..1749 | 各人类玩家结盟勾选（按行 `1742 + row`） | `:100-104,130-135` |
| 聊天控件位 | 按 `runtimeDiplomacyPointerChatControls` 表勾选 / 取消接收者 | `:105-108,137-143` |
| 1840 | 叫嚣开关 | `:110,127` |

- 右键松开关闭外交页（`:148-150`）。
- 键盘导航：Esc / Tab / Enter / 外交键关闭；上下键换行；`A` / 空格切换结盟（`src/ui/input/RuntimeReferenceCommunicationController.cpp:107-127`）。
- 结盟允许条件：非战役、允许盟友变化、非 FFA / 合作模式（`src/ui/input/RuntimePlayerCommunication.inl:64-69`）；双向提议，AI 自动接受，真人需回提（`:118-125`）。

---

## 7. 几何常量与颜色值汇总

### 7.1 几何常量

| 常量 | 值 | 出处 |
| --- | --- | --- |
| 侧栏宽度 | 168 px | `src/runtime/session/detail/RuntimeProductionOperations.h:333` |
| 资源行高 | 16 | `src/runtime/host/RuntimeShell.cpp:1981` |
| 顶部条高 | 32 | `src/runtime/host/RuntimeShell.cpp:1982` |
| 雷达高 | 110 | `src/runtime/host/RuntimeShell.cpp:1983` |
| side1 高 | 69 | `src/runtime/host/RuntimeShell.cpp:1984` |
| side2 行高 | 50 | `src/runtime/host/RuntimeShell.cpp:1985` |
| side3 高 | 26 | `src/runtime/host/RuntimeShell.cpp:1986` |
| addon 高 | 63 | `src/runtime/host/RuntimeShell.cpp:1990` |
| 按钮宽 / 高 / 行高 | 60 / 48 / 50 | `src/runtime/host/RuntimeShell.cpp:19337-19339` |
| 按钮列 left | 22 / 85 | `src/runtime/host/RuntimeShell.cpp:19342` |
| 翻页按钮宽 / 高 | 46 / 25 | `src/runtime/host/RuntimeShell.cpp:19381-19382` |
| 翻页按钮 left | 上一页 84 / 下一页 38 | `src/runtime/host/RuntimeShell.cpp:19384` |
| 雷达内框缩进 | 左 13、右 15 | `src/runtime/host/RuntimeShell.cpp:3198-3201` |
| 选中单位框 宽 / 高 / 顶偏移 | 36 / 40 / 30 | `src/runtime/host/RuntimeShell.cpp:9705-9707` |
| 建筑血条框宽 / 单位血条框宽 | 18 / 36，框高 4 | `src/runtime/host/RuntimeShell.cpp:9726,9736` |
| 框选 / 手势移动阈值 | 位移平方和 > 16 | `src/ui/input/RuntimeMapLeftPointerController.cpp:401`；`src/ui/input/RuntimeMapRightPointerController.cpp:58,86` |
| 右键平移步长钳制 | ±96；步长 `clamp(|delta|/4,1,32)` | `src/ui/input/RuntimeMapRightPointerController.cpp:59-60,71-76` |
| 滚轮步长 | 120  notch | `src/ui/sidebar/RuntimeSidebarPageController.cpp:74` |
| 聊天草稿上限 | 80 代码单元 | `src/ui/input/RuntimeChatTextController.cpp:48` |
| 聊天行上限 / 存活 | 8 条 / `15 × tickHz` tick | `src/ui/input/RuntimePlayerCommunication.inl:108-109` |
| 超武倒计时行高 / 边距 / 最大宽 | 18 / 8 / 320 | `src/runtime/host/RuntimeShell.cpp:23504-23515` |
| 双击选择时间窗 | 500 ms | `src/ui/time/RuntimeUiClock.h:17-20` |
| 就绪闪烁周期 | 500 ms | `src/ui/time/RuntimeUiClock.h:32-38` |

### 7.2 颜色值

| 用途 | 颜色 | 出处 |
| --- | --- | --- |
| 雷达背景 | `RGB(2,8,10)` | `src/runtime/host/RuntimeShell.cpp:19742` |
| 雷达默认地形点 | `RGB(84,116,70)` | `src/runtime/host/RuntimeShell.cpp:19875` |
| 雷达水面 | `RGB(38,70,120)` | `src/runtime/host/RuntimeShell.cpp:19897` |
| 雷达悬崖 | `RGB(128,92,54)` | `src/runtime/host/RuntimeShell.cpp:19899` |
| 雷达岸 / 坡 | `RGB(112,126,78)` | `src/runtime/host/RuntimeShell.cpp:19901` |
| 选中笔（单位） | `RGB(113,181,255)`，2 px | `src/runtime/host/RuntimeShell.cpp:23843` |
| 选中笔（建筑） | `RGB(236,242,238)`，2 px | `src/runtime/host/RuntimeShell.cpp:23844` |
| 框选虚线 | `RGB(228,238,250)`，1 px 点线 | `src/runtime/host/RuntimeShell.cpp:23993` |
| 进度遮罩（进行中） | `RGB(28,188,232)` | `src/runtime/host/RuntimeShell.cpp:18843` |
| 进度遮罩（挂起） | `RGB(218,160,64)` | `src/runtime/host/RuntimeShell.cpp:18843` |
| 边框：待摆放 | `RGB(120,242,220)` | `src/runtime/host/RuntimeShell.cpp:20571` |
| 边框：挂起 | `RGB(214,182,96)` | `src/runtime/host/RuntimeShell.cpp:20572` |
| 边框：生产中 | `RGB(92,204,236)` | `src/runtime/host/RuntimeShell.cpp:20573` |
| 边框：悬停 | `RGB(152,204,236)` | `src/runtime/host/RuntimeShell.cpp:20574` |
| 边框：就绪 | `RGB(96,126,156)` | `src/runtime/host/RuntimeShell.cpp:20575` |
| 队列角标底 / 框 / 字 | `RGB(2,5,8)` / `RGB(180,228,252)` / `RGB(232,248,255)` | `src/runtime/host/RuntimeShell.cpp:20591,20594,20602` |
| 缺图标笔：可用 / 禁用 | `RGB(84,96,108)` / `RGB(54,60,66)` | `src/runtime/host/RuntimeShell.cpp:18730` |
| 血条绿（暗/中/亮） | `RGB(0,50,0)` / `RGB(0,128,0)` / `RGB(0,255,0)` | `src/runtime/host/RuntimeShell.cpp:22876-22878` |
| 血条黄（暗/中/亮） | `RGB(50,50,0)` / `RGB(128,128,0)` / `RGB(255,255,0)` | `src/runtime/host/RuntimeShell.cpp:22881-22883` |
| 血条红（暗/中/亮） | `RGB(134,17,34)` / `RGB(204,34,34)` / `RGB(238,68,51)` | `src/runtime/host/RuntimeShell.cpp:22885-22887` |
| 超武倒计时背景 | 黑 `RGB(0,0,0)`，Alpha 160 | `src/runtime/host/RuntimeShell.cpp:23549-23552` |

与设备无关颜色封装：`RuntimeRgbColorValue` 从打包 `0xAABBGGRR` 取低 24 位，通道按 R / G / B 字节拆分（`src/ui/values/RuntimeColorValues.h:7-25`）。

---

## 本章缺口

1. **自定义 `MOUSE.SHP` 语境光标未落地**：战局内光标全部映射到 Win32 库存光标（箭头 / 手型 / 行动 / 禁止 / 隐藏），未按西武原图逐语境（攻击 / 移动 / 部署 / 收回 / 无效 / 悬停按钮）加载光标 SHP 帧；`src/ui/cursor/RuntimePanCursor.inl`、`RuntimeHarvesterCursor.inl` 为空桩。
2. **双击选中同类**：未发现 `WM_LBUTTONDBLCLK` 在战局内绑定“双击同类全选”；同类选择仅由 `T` 快捷键与 500 ms 内双击键触发全图模式覆盖。
3. **按钮按下态 / 扫光相位**：未发现按钮 pressed 下陷帧与横向扫光流光相位；按钮态由可用性布尔、边框色、进度时钟遮罩与就绪闪烁表达。
4. **秘密实验室相位绘制**：`src/ui/sidebar/RuntimePaintSecretLabPhase.inl` 为空桩，未实现秘密实验室相关相位覆盖。
5. **外交页静态布局**：`src/ui/diplomacy/RuntimeDiplomacyPage.inl`、`src/ui/selection/RuntimeIdleVariantSelection.inl` 为空桩；外交行 / 勾选几何由指针控制器内的控件 ID 与 `checkboxRect` 适配提供，独立页面布局表未在本章域内成文件。
6. **侧栏选中物信息面板**：`side1` 面板在参考引擎中承载工具按钮与分页标签；选中单位 / 建筑的 cameo、名称、血量与勋章反馈绘制在世界投影物之上，侧栏内未复刻西武右侧“选中物缩略图 + 大血条 + 勋章”面板。
7. **资源 / 电力数字排版**：数字本身走通用位图文字机制（05b 章域），本章只记录容器矩形与段条几何，未列字体度量与字距。
8. **EVA 语音播报**：本章域内仅见状态文本、欢呼 / 叫嚣音效与结算横幅边界；战局内事件触发的 EVA 语音播报链路未在本章源码域内定位到完整实现。
