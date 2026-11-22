# ConcordRTS 参考映射（Unity 复刻分层落位）

参考项目：`D:\___SYSTEM_PATH\source\repos\ConcordRTS`（C++ / CMake，可玩版本，作者自述还原 95%）。
许可：**GPLv3**（`LICENSE` 第 1–3 行已确认）→ 只可作**结构与合同参考**，不得把源码搬进本项目，否则整个 Unity 工程会被传染成 GPL。

## 1. 参考项目的模块（实测目录，693 个 .cpp/.h）

| 模块 | 文件数 | 职责（按目录名） |
|---|---|---|
| `core` | 17 | INI、原子写文件、任务执行器、确定性哈希、安全字节、可执行目录 |
| `runtime` | 100 | host / session / timing / menu / campaign / presentation / replay / persistence / window / config / performance / contracts |
| `simulation` | 149 | abilities / ai / buildings / combat / economy / effects / movement / navigation / production / scenarios / superweapons / triggers / vision / mod_extensions |
| `render` | 78 | core / terrain / units / buildings / voxel / lighting / shadows / effects / geometry / visibility / preparation / resources |
| `assets` | 42 | archive（MIX）/ codecs（SHP、VXL、AUD、BMP）/ audio / terrain / text / video / menu / preview / inspection |
| `ui` | 127 | main_menu / loading / skirmish / campaign / sidebar / radar / hud / selection / results / diplomacy / settings / cursor / text / values / surface / drawing / input / time / resources / network / audio |
| `audio` | 41 | 混音、AUD 解码、声道管理 |
| `network` | 79 | 联机、回放同步、确定性与校验 |
| `data` | 28 | 规则/INI 数据模型（Art/AI/Rules/…） |
| `analysis` | 24 | 二进制分析工具（不属运行时） |
| `world` | 8 | 地图/地形/单元世界模型 |

## 2. 落到 Unity 的分层（本工程实际路径）

| ConcordRTS | 本工程 | 已落地文件 |
|---|---|---|
| `core/io/IniFile` | `Assets/Scripts/Core` | `CcIni.cs`（含 `sub_5276D0`/`sub_529560` 语义、十六进制与 `h` 后缀） |
| `runtime/timing` | `Assets/Scripts/Core` | `GameTime.cs`（timeGetTime 等价）、`FrameThrottle.cs`（55E160，11ms 门限） |
| `runtime/host` + 原版 WinMain | `Assets/Scripts/Boot` | `WinMainReplica.cs`（按行号 238→1297 的顺序） |
| 版权/安全 | `Assets/Scripts/Boot` | `CopyProtection.cs`（49F5C0 / 49F620，含两处偏差说明） |
| `runtime/session` + 原版 48CCC0 | `Assets/Scripts/Core` | `GameSession.cs`（外层每局 / 内层每帧） |
| 原版 55D360 | `Assets/Scripts/Core` | `MainLoop.cs`（帧预算、滞后三档、每 8 帧、动作序） |
| `render/core` + 原版 DSurface | `Assets/Scripts/Gfx` | `Surface16.cs`（RGB565、Blit、调色板展开、三矩形） |
| `runtime/presentation` | `Assets/Scripts/Gfx` | `FramePresenter.cs`（Point 过滤，整面 Texture2D 上屏） |
| `assets/archive`（MIX） | 计划 `Assets/Scripts/Assets` | 未落地 |
| `assets/codecs`（SHP/VXL/AUD/BMP） | 计划 `Assets/Scripts/Assets/Codecs` | 未落地 |
| `simulation/*` | 计划 `Assets/Scripts/Sim` | 未落地 |
| `ui/*` | 计划 `Assets/Scripts/Ui` | 未落地（侧栏 168 已由 `DisplayMode.ApplyViewRects` 固定） |

## 3. 必须沿用的五条合同（这是"能玩"与"还原"的分界）

1. **确定性**：逻辑只用整数/定点 + 固定步长；浮点仅出现在渲染插值。原版 `_controlfp(0x300,0x300)` 的意图就在此（`GameStatics.ApplyFpuContract`）。
2. **帧预算**：`tick = 0x3c / fps`、`budget = 1000 / fps`，滞后 >30ms 时按 1/4、1/2、3/4 三档各 +10ms（`MainLoop` 已实现）。
3. **11ms 补帧门限**：剩余 <11ms 不渲染，只 `Sleep(0)`（`FrameThrottle.RenderThresholdMs`）。
4. **画面只有一条路**：所有像素进 `Surface16`（RGB565）；Unity 的 Sprite/UGUI/Tilemap 不得承载游戏内容，否则 Z 序与裁剪无法对齐 `sub_4F4480`/`sub_6D3D10`/`sub_6A6C30`。
5. **每 8 帧判据用帧号低 3 位**（`(CurrentFrame & 7) == 7`），不是独立计数器。

## 4. 与参考项目的差异（Unity 侧不可避免）

| 项 | 原版 / ConcordRTS | Unity 复刻 | 影响 |
|---|---|---|---|
| 主循环 | 阻塞式 `while` + Sleep 自驱 | 一次 `Update` = 一次 55D360 | 一帧内多次 Render 被合并为一次 Present |
| 消息泵 | 自己 `PeekMessage`（0xBEEF 闸门） | 引擎独占 | `NotifyLauncher` 默认跳过消息段，已在代码里标注 |
| 视频 | Bink 解码 | 未实现 | 片头/菜单循环视频缺 |
| 网络 | IPX/WChat | 未实现 | 联机缺，不影响单机 |

## 5. 建议的下一步顺序（按可验证性排序）

1. `Assets/archive`：MIX 头解析 + 目录表（先只读 `.MIX`，不碰 LMD），验证能列出 `ra2.mix` 内条目。
2. `Assets/codecs`：SHP 帧解码 + 调色板（先 `mouse.shp`/`sidebar.shp` 这类小文件），画到 `Surface16`。
3. `ui/surface`：按钮/文本，用原版 `BitFont`/`FNT`。
4. `simulation`：先 `Rules`/`Art` 解析，再 `movement`/`combat` 最小闭环。
5. `runtime/menu`：主菜单 → 遭遇战 → 战斗，端到端跑通后再补 campaign。
