# 05b · 文字与视频解码（CSF / FNT / Bink）

## 本章覆盖范围

- CSF 文字表：` FSC` 头、` LBL` 标签块、` RTS` / `WRTS` 值块的字节布局、UTF-16 编码与按标签取值的查询接口，以及菜单 / 战役 / 遭遇战 UI 的消费方式。
- Westwood 位图字体（`.fnt`，`fonT`）：头部度量、65536 项字符映射表、字形记录布局、像素测量与排版、缺字回退策略，以及与 `src/ui/text/` 三个位图文字接口的对接。
- Bink 视频：播放服务职责、64 位运行时与 32 位解码助手子进程之间的共享内存 / 事件帧协议、自有帧像素、播放时钟、音轨与音量、停止清理流程，以及参考引擎不附带 `Binkw32.dll` 与影片数据的边界。
- 三类格式的错误处理约定与测试夹具 / 合同测试佐证位置。

## 对应源码目录

生产源码（`src/` 下 `.h/.cpp/.inl`）：

| 文件 | 职责 |
| --- | --- |
| `src/assets/text/CsfCodec.h` | CSF 条目结构 `Entry`、回车策略枚举 `CarriageReturnPolicy`、解码与投影接口声明 |
| `src/assets/text/CsfCodec.cpp` | CSF 头 / 标签 / 值块解析、UTF-16 反异或、回车符投影 |
| `src/assets/text/WestwoodFontCodec.h` | 字体度量 `WestwoodFontMetrics`、回退策略枚举 `WestwoodGlyphFallback`、头部解码与字形取记录接口声明 |
| `src/assets/text/WestwoodFontCodec.cpp` | `fonT` 头校验、字符映射表查找、字形记录有效性判定 |
| `src/assets/video/BinkVideoStream.h` | 跨进程共享头 `SharedHeader`、魔数 / 版本、播放模式 `PlaybackMode`、完成判定与映射大小计算 |
| `src/assets/video/BinkPlaybackService.h` | 帧 `BinkVideoFrame`、请求 `BinkPlaybackRequest`、播放服务 `BinkPlaybackService` 接口声明 |
| `src/assets/video/BinkPlaybackService.cpp` | 服务端（64 位运行时一侧）：路径解析、共享内存 / 事件创建、子进程启动、轮询校验、停止清理；非 Windows 空实现 |
| `src/assets/video/BinkPlaybackPaths.h` | 路径输入 `BinkPlaybackPathInputs`、解析结果 `BinkPlaybackPaths` 声明 |
| `src/assets/video/BinkPlaybackPaths.cpp` | 影片 / 解码器 DLL / 助手可执行文件的候选路径优先级 |
| `tools/BinkFrameExtractor.cpp` | 32 位解码助手源码：加载 `Binkw32.dll`、Bink API 绑定、共享内存发布、音轨后端选择、最终帧保持与清理 |

消费方（非 `assets/` 目录，属于本章链路）：

| 文件 | 职责 |
| --- | --- |
| `src/ui/main_menu/RuntimeMainMenu.cpp` | 主菜单 CSF 目录 `CsfCatalog`、位图字体包装 `WestwoodBitFont`、`BinkMenuAnimation` 服务包装、影片库扫描与播放页 |
| `src/runtime/menu/detail/RuntimeSkirmishMapResources.h` | 遭遇战地图资源 CSF 目录 `CsfCatalog`（含 `getAny` 多键回退） |
| `src/ui/skirmish/RuntimeSkirmishMenu.cpp` | 遭遇战菜单 CSF / `game.fnt` 加载与文字消费 |
| `src/ui/campaign/RuntimeCampaignMenu.cpp` | 战役菜单 CSF 目录 `CampaignCsfCatalog`（多目录追加合并、`LineFeed` 回车策略） |
| `src/ui/text/RuntimeMainBitmapText.h` / `.cpp` | 主菜单位图文字视图 `MainBitmapFontView`、测量与三趟着色绘制 |
| `src/ui/text/RuntimeSkirmishBitmapText.h` / `.cpp` | 遭遇战位图文字视图 `SkirmishBitmapFontView`、测量、像素绘制与 `MenuDrawList` 命令记录 |
| `src/ui/text/RuntimeResultBitmapText.h` / `.cpp` | 结算位图文字视图 `ResultBitmapFontView`、`sink` 矩形填充接口、省略号截断 |
| `src/ui/results/RuntimeResultBitFont.inl` | 结算页字体持有体 `RuntimeResultBitFont`、`game.fnt` 候选路径与缓存 |
| `src/runtime/campaign/RuntimeCampaignMovieResolver.h` / `.cpp` | 战役影片逻辑名解析（松散文件 / MIX 条目、`archive.mix!entry.bik` 引用、落盘物化与缓存） |

> 说明：`src/assets/text/` 只负责字节解码与度量解析，不含文件 I/O、资源选择、编码策略与配色策略（`src/assets/text/WestwoodFontCodec.h:13-15`）；目录加载、候选路径与 UI 着色均在消费方完成。

---

## 1. CSF 文字

### 1.1 模块定位

CSF（Westwood 字符串表）由 `concord::csf` 命名空间下的纯字节解码器处理，输入为 `std::span<const unsigned char>`，不触碰文件系统（`src/assets/text/CsfCodec.h:21`）。解码产物为条目向量，每条只保留**第一个值**；多值条目与尾部附加字节由解析器吞掉但不输出（`src/assets/text/CsfCodec.h:9-10,19-20`）。

条目结构（`src/assets/text/CsfCodec.h:11-15`）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `label` | `std::string` | 原始标签字节（如 `GUI:MainMenu`），解码器不做大小写归一、不去空白 |
| `firstValue` | `std::u16string` | 首个值的 UTF-16 代码单元序列；保留 NUL、代理项与 `\n` |

### 1.2 文件头字节布局

解码器要求文件至少 24 字节且前 4 字节为 ASCII ` FSC`（注意前导空格），否则返回空向量（`src/assets/text/CsfCodec.cpp:33`）。

| 偏移 | 长度 | 字段 | 参考引擎处理方式 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | 魔数 ` FSC` | 必须逐字节匹配，否则整文件拒绝 | `CsfCodec.cpp:33` |
| 4 | 4 | 头字段（测试夹具中写 `3`） | **未读取、未定义语义**；被改写为任意值不影响解析 | `tests/assets/CsfCodecContract.cpp:40,86-90` |
| 8 | 4 | `labelCount`（小端 `uint32`） | 标签块数量；解码主循环上界 | `CsfCodec.cpp:34` |
| 12 | 4 | 头字段 | **未读取、未定义语义** | `CsfCodecContract.cpp:86-90` |
| 16 | 4 | 头字段 | **未读取、未定义语义** | `CsfCodecContract.cpp:86-90` |
| 20 | 4 | 头字段 | **未读取、未定义语义** | `CsfCodecContract.cpp:86-90` |

> 缺口提示：参考引擎没有解析 CSF 头中的语言标识 / 版本字段；凡需要语言判定的逻辑在本引擎中不存在，不能依据原版印象补写。`labelCount` 被声明为极大值时，循环以实际剩余字节为界（`CsfCodec.cpp:36-38`；佐证 `CsfCodecContract.cpp:99-100`）。

### 1.3 标签块（` LBL`）字节布局

从偏移 24 开始按标签顺序排列（`src/assets/text/CsfCodec.cpp:35-38`）：

| 偏移（块内） | 长度 | 字段 | 解析规则 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | 标签魔数 ` LBL` | 不匹配则中止后续解析（已完成的条目保留） | `CsfCodec.cpp:39` |
| 4 | 4 | `stringCount`（小端 `uint32`） | 该标签下值块数量；以剩余字节为界 | `CsfCodec.cpp:41` |
| 8 | 4 | `labelLength`（小端 `uint32`） | 标签字节长度 | `CsfCodec.cpp:43` |
| 12 | `labelLength` | 标签原始字节 | 原样保留为 `std::string`，含内嵌 NUL / 高位字节 | `CsfCodec.cpp:46-48` |
| 12+`labelLength` | — | 跟随 `stringCount` 个值块 | 见 1.4 | — |

### 1.4 值块（` RTS` / `WRTS`）字节布局

| 偏移（块内） | 长度 | 字段 | 解析规则 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | 值魔数：` RTS`（普通）或 `WRTS`（带附加字节） | 两者皆非则中止本标签解析 | `CsfCodec.cpp:53-58` |
| 4 | 4 | `characterCount`（小端 `uint32`） | UTF-16 代码单元个数；字节数 = `characterCount * 2` | `CsfCodec.cpp:60-63` |
| 8 | `characterCount * 2` | 编码文本 | 每两字节小端读取 `uint16`，与 `0xffff` 异或后成为 `char16_t` | `CsfCodec.cpp:70-75` |
| 8+文本 | 4（仅 `WRTS`） | `extraLength`（小端 `uint32`） | 附加字节长度；读取后跳过整段 | `CsfCodec.cpp:82-88` |
| — | `extraLength`（仅 `WRTS`） | 附加字节 | 内容语义**未解析、未定义**；必须在附加字节消耗完成后，该值才会被提交 | `CsfCodec.cpp:77-91` |

提交规则（`src/assets/text/CsfCodec.cpp:91-95`）：

- 只保留该标签**第 1 个值**到 `firstValue`；后续值即使解码成功也不输出。
- 标签为空字符串、或首个值为空字符串的条目不进入输出向量。
- 首值所在的 `WRTS` 若附加字节被截断 / 长度非法，整个首值不提交（佐证 `tests/assets/CsfCodecContract.cpp:161-165`）。

### 1.5 编码与回车投影

- 文本为 **UTF-16 代码单元**，不是 Unicode 标量值；代理项对原样保留，解码器不做配对（`src/assets/text/CsfCodec.h:10`）。
- 解码后由 `projectCarriageReturns` 按策略处理 `\r`（`src/assets/text/CsfCodec.cpp:100-106`）：

| 策略枚举 | `\r` 替换为 | 使用方 | 出处 |
| --- | --- | --- | --- |
| `CarriageReturnPolicy::Space` | 空格 `U+0020` | 主菜单、遭遇战地图资源 | `RuntimeMainMenu.cpp:255-256`；`RuntimeSkirmishMapResources.h:24-25` |
| `CarriageReturnPolicy::LineFeed` | 换行 `U+000A` | 战役菜单 | `RuntimeCampaignMenu.cpp:144-145` |

`\n`、NUL、代理项在两种策略下均保持原样（`src/assets/text/CsfCodec.h:23`；佐证 `CsfCodecContract.cpp:190-200`）。

### 1.6 查询接口与消费关系

解码器本身只产出向量；各消费方自行构建目录并提供按标签取值接口。

**主菜单目录** `CsfCatalog`（`src/ui/main_menu/RuntimeMainMenu.cpp:243-267`）：

| 接口 | 行为 | 出处 |
| --- | --- | --- |
| `load(path)` | 整文件读入 → `decodeEntries` → `Space` 策略投影 → 以 `lowerAscii(label)` 为键存入 `unordered_map<std::string,std::wstring>`；目录为空返回 `false` | `RuntimeMainMenu.cpp:246-260` |
| `get(key, fallback)` | 键经 `lowerAscii` 归一后查找；未命中或值为空串返回 `fallback` | `RuntimeMainMenu.cpp:262-266` |

**遭遇战地图资源目录**（`src/runtime/menu/detail/RuntimeSkirmishMapResources.h:12-51`）：`get` 额外对键做 `trimAscii`；`getAny(keys, fallback)` 按候选键顺序取首个非空值（`RuntimeSkirmishMapResources.h:39-50`）。

**战役目录** `CampaignCsfCatalog`（`src/ui/campaign/RuntimeCampaignMenu.cpp:131-172`）：

- `append(path)`：追加合并到现有 `values`，后加载目录覆盖同键；`source` 字段以分号拼接来源路径，用于诊断。
- 候选分 `ra2CsfCandidates` / `yrCsfCandidates` / `otherCsfCandidates` 三组，按 RA2 → 其他 → YR 顺序追加（`RuntimeCampaignMenu.cpp:1222-1305`）。
- `get(key, fallback)`：`lowerAscii` + `trimAscii` 归一；空值回退。

**已观察到的标签前缀与样例键**（原样照抄，出处见各消费方）：

| 前缀 | 样例键 | 出处 |
| --- | --- | --- |
| `GUI:` | `GUI:MainMenu`、`GUI:SinglePlayerMenu`、`GUI:MoviesAndCredits`、`GUI:Options`、`GUI:Resolution`、`GUI:TargetFPS`、`GUI:GameSpeed`、`GUI:Version`、`GUI:Internet`、`GUI:Network` | `RuntimeMainMenu.cpp:1192-1200,1304-1314,1479,2603-2608` |
| `CRD:` | `CRD:CREDITS` | `RuntimeMainMenu.cpp:872,1198` |
| `Name:` | `Name:Temperate`、`Name:Snow`、`Name:Urban` | `RuntimeSkirmishMenu.cpp:1896-1900` |

**占位符展开**：制作人员名单行允许内嵌 `{GUI:XXX}` 形式占位符；`expandCreditsLine` 抽取花括号内键名查 CSF，未命中时回退为冒号后部分、下划线替换为空格的可读串（`src/ui/main_menu/RuntimeMainMenu.cpp:815-847`）。

**素材候选路径**（消费方自行 `firstExisting`，解码器不参与）：

| 场景 | 候选顺序 | 出处 |
| --- | --- | --- |
| 主菜单 | `GameMD/ra2md.csf` → `exportRoot/langmd/ra2md.csf` → `exportRoot/lang/ra2.csf` | `RuntimeMainMenu.cpp:895-899` |
| 遭遇战 | `GameMD/ra2md.csf` → `langmd/ra2md.csf` → `lang/ra2.csf` → `localUiRoot/ra2md.csf` | `RuntimeSkirmishMenu.cpp:1116-1119` |
| 战役 | RA2：`language/ra2.csf`、`Game/ra2.csf`、`ra2.csf`；YR：`langmd/ra2md.csf`、`GameMD/ra2md.csf`、`ra2md.csf`；另接数据库来源 `textCatalogPath` | `RuntimeCampaignMenu.cpp:1222-1293` |

> 其他引用：音频目录解析侧也会探测 `langmd/ra2md.csf`、`language/ra2.csf`、`GameMD/ra2md.csf`、`Game/ra2.csf`（`src/assets/audio/AudioCatalog.cpp:238-241`；`src/assets/audio/AudioSampleResolver.cpp:1538-1541`），用于语音条目身份核对，不属于本章文字消费链路。

### 1.7 CSF 错误处理约定

- 头不足 24 字节 / 魔数错误：返回空向量（`CsfCodec.cpp:33`；佐证 `CsfCodecContract.cpp:78-93`）。
- 逐字节截断：此前已完整提交的条目与完整首值保留；损坏尾部丢弃（`CsfCodec.h:19-20`；佐证 `CsfCodecContract.cpp:104-120,125-159`）。
- 标签长度 / 文本长度 / 附加长度被篡改为极大值：按剩余字节边界中止，不越界读取（`CsfCodec.cpp:45,64-67,78-88`；佐证 `CsfCodecContract.cpp:142-169`）。
- 重复键：解码器保留原始大小写与先后顺序；目录层 `lowerAscii` 归一后后者覆盖前者（佐证 `CsfCodecContract.cpp:173-186`）。
- 文件尾部多余字节忽略（佐证 `CsfCodecContract.cpp:95-96`）。

---

## 2. FNT / Westwood 位图字体

### 2.1 模块定位

`concord::assets` 命名空间下的纯字节字体解码器，只输出头部度量与字形记录指针；不负责文件 I/O、布局、编码或配色（`src/assets/text/WestwoodFontCodec.h:13-15`）。字形记录指针为**借用指针**，在字节持有方重载 / 释放前有效（`WestwoodFontCodec.h:23-27`）。

度量结构（`src/assets/text/WestwoodFontCodec.h:6-12`）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `stride` | `uint32` | 字形位图每行占用字节数 |
| `bitmapRows` | `uint32` | 字形位图行数 |
| `lineHeight` | `uint32` | 行高（像素），必须 `>= bitmapRows` |
| `glyphCount` | `uint32` | 字形记录条数 |
| `recordSize` | `uint32` | 单条记录字节数 = `1 + stride * bitmapRows` |

### 2.2 文件头字节布局

魔数为 ASCII `fonT`（`src/assets/text/WestwoodFontCodec.cpp:36-37`）。

| 偏移 | 长度 | 字段 | 校验规则 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | 魔数 `fonT` | 必须逐字节匹配 | `WestwoodFontCodec.cpp:36-37` |
| 4 | 4 | 保留头字 | 语义未定义；任意值不拒绝 | `WestwoodFontCodecContract.cpp:43` |
| 8 | 4 | `stride`（小端） | 必须 `> 0` 且 `<= 16` | `WestwoodFontCodec.cpp:38-40` |
| 12 | 4 | `bitmapRows`（小端） | 必须 `> 0` 且 `<= 64` | `WestwoodFontCodec.cpp:39-41` |
| 16 | 4 | `lineHeight`（小端） | `bitmapRows <= lineHeight <= 64` | `WestwoodFontCodec.cpp:41` |
| 20 | 4 | `glyphCount`（小端） | 必须 `> 0`；不按映射宽度截断 | `WestwoodFontCodec.cpp:42`；佐证 `WestwoodFontCodecContract.cpp:44-46` |
| 24 | 4 | `recordSize`（小端） | 必须等于 `1 + stride * bitmapRows`，且记录数不超过数据区可容纳条数 | `WestwoodFontCodec.cpp:43-44` |
| 28（`0x1c`） | 65536 × 2 | 字符映射表 | 每字符一个小端 `uint16` 记录序号（1 基） | `WestwoodFontCodec.cpp:6,21-23` |
| `28 + 131072`（`dataOffset`） | — | 字形记录区 | 按 `glyphCount` / `recordSize` 排列 | `WestwoodFontCodec.cpp:7,29` |

头部解码行为（`src/assets/text/WestwoodFontCodec.cpp:34-45`）：

- 文件短于 `dataOffset`、魔数错误：返回 `false`，且输出度量保持调用方原值（重载保护；佐证 `WestwoodFontCodecContract.cpp:36-38`）。
- 魔数可辨认但语义校验失败：原始字段仍写入度量（结果页重载约定；佐证 `WestwoodFontCodecContract.cpp:39-40`）。
- 尾部追加字节接受；记录区少一条记录则拒绝（佐证 `WestwoodFontCodecContract.cpp:41-42`）。

### 2.3 字形记录布局

记录区起始 `dataOffset`，第 `ref - 1` 条记录地址 = `dataOffset + index * recordSize`（`src/assets/text/WestwoodFontCodec.cpp:26-29`）：

| 偏移（记录内） | 长度 | 字段 | 含义 |
| --- | --- | --- | --- |
| 0 | 1 | `width` | 字形可见宽度（像素）；必须 `<= stride * 8` |
| 1 | `stride * bitmapRows` | 位图掩码 | 1-bit  packed 位图，每行 `stride` 字节，位序 MSB-first：像素 `(x, y)` 位于 `row = record + 1 + y*stride`，位 `row[x >> 3] & (0x80 >> (x & 7))` |

记录有效性判定（`src/assets/text/WestwoodFontCodec.cpp:16-32`）：

| 失败原因 | 枚举值 | 条件 |
| --- | --- | --- |
| 字体整体不可用 | `Unavailable::Font` | 文件短于 `dataOffset`；或 `recordSize <= 1`、`lineHeight == 0`、`stride == 0`、`stride > 16`、`bitmapRows == 0`、`bitmapRows > 64`；或记录下标越出数据区 |
| 映射无效 | `Unavailable::Reference` | 映射序号 `ref == 0`，或 `ref > glyphCount` |
| 宽度非法 | `Unavailable::Width` | `record[0] > stride * 8` |

### 2.4 字符映射查找与缺字回退

查找接口（`src/assets/text/WestwoodFontCodec.cpp:46-55`）：

| 回退策略枚举 | 行为 | 使用方 |
| --- | --- | --- |
| `MissingReferenceOnly` | 仅映射无效（`Reference`）时回退到 `?` 字形；宽度非法（`Width`）返回 `nullptr` | 遭遇战、结算文字 |
| `AnyUnavailableRecord` | 映射无效或宽度非法均回退到 `?` 字形 | 主菜单文字 |

补充规则：

- 字符本身就是 `?` 时不再二次回退；`?` 记录本身损坏时返回 `nullptr`，不递归（佐证 `WestwoodFontCodecContract.cpp:33-34`）。
- `width == 0` 的记录仍视为有效（佐证 `WestwoodFontCodecContract.cpp:35`）。
- 查找不修改持有方字节（佐证 `WestwoodFontCodecContract.cpp:26`）。

### 2.5 排版、测量与像素绘制接口

三个消费方视图均持有字节 `span` 与度量，`valid()` 条件一致：字节非空、`recordSize > 1`、`lineHeight > 0`。

**测量规则**（三处一致，出处见下）：

- 逐字符取记录；命中则该字前进 `record[0] + 1` 像素，缺字前进 `5` 像素；整串宽度为累计值减 1（空串为 0）。
- 主菜单视图对 UTF-16 代理项对折叠为一个 `?` 代码单元；遭遇战 / 结算视图逐个代码单元访问（含代理项）。

| 视图 | 头文件 | 测量接口 | 绘制 / 记录接口 | 出处 |
| --- | --- | --- | --- | --- |
| `MainBitmapFontView` | `src/ui/text/RuntimeMainBitmapText.h:10-16` | `measureMainBitmapText`（`wstring_view` / `u16string_view`） | `drawMainBitmapText` 三趟着色：阴影偏移 `(1,1)` → 底色趟 → 高光趟（仅上邻行空位像素） | `RuntimeMainBitmapText.cpp:22-31,33-83,85-92` |
| `SkirmishBitmapFontView` | `src/ui/text/RuntimeSkirmishBitmapText.h:13-19` | `measureSkirmishBitmapText` | `drawSkirmishBitmapText` 直写 BGRA 像素；`recordSkirmishBitmapText` 生成 `MenuDrawKind::Glyph` 命令（掩码按 `record+1`、`recordSize-1` 入库）；两趟：阴影 `(20,12,0)` → 本色 | `RuntimeSkirmishBitmapText.cpp:15-22,24-75,77-93` |
| `ResultBitmapFontView` | `src/ui/text/RuntimeResultBitmapText.h:10-13` | `measureResultBitmapText` | `drawResultBitmapText` 经 `ResultBitmapTextSink` 回调（`beginText` / `beginPass` / `fillRun` / `endPass` / `endText`）按水平像素段填充；两趟阴影偏移；超宽文本逐字符收缩并补 `...` 省略号 | `RuntimeResultBitmapText.cpp:12-19,21-56,58-63` |

**对齐与裁剪**：起点 X 按 `centered` 在半开放矩形内居中；起点 Y 按 `verticallyCentered` 依 `lineHeight` 垂直居中；像素写入越出矩形 / 表面边界一律丢弃（`RuntimeMainBitmapText.cpp:37-52`；`RuntimeSkirmishBitmapText.cpp:33-39,63-64`；`RuntimeResultBitmapText.cpp:30-33`）。

**字体持有体与素材候选**：

| 持有体 | 候选路径 | 出处 |
| --- | --- | --- |
| 主菜单 `WestwoodBitFont` | `GameMD/game.fnt` → `Game/game.fnt` | `RuntimeMainMenu.cpp:269-308,903-907` |
| 遭遇战 `gameFont` | `GameMD/game.fnt` → `Game/game.fnt` | `RuntimeSkirmishMenu.cpp:1303-1307` |
| 结算 `RuntimeResultBitFont` | `modRoot/game.fnt` → `GameMD/game.fnt` → `Game/game.fnt`；按 `modRoot|resourceRoot` 缓存 | `RuntimeResultBitFont.inl:42-55` |

**与系统字体的关系**：位图字体链路之外，`src/ui/text/win32/` 下另有一套 GDI 系统字体适配器（如 `RuntimeMainSystemFontWin32`、`RuntimeCampaignSystemFontWin32` 等），角色枚举为 `MainSystemFontRole::{Button, Title, Small}`（`src/ui/text/RuntimeMainSystemFont.h:7`）。系统字体适配器独立于 `.fnt` 解码器，不属于本章字节格式范围；位图字体不可用时的上层选择策略由各菜单状态机持有。

### 2.6 FNT 错误处理约定

- 头部不可辨认：度量保留原值，持有体清空字节，加载失败（`WestwoodBitFont::load` 见 `RuntimeMainMenu.cpp:287-290`；`RuntimeResultBitFont.inl:13-18`）。
- 运行时取字形失败返回 `nullptr`；排版层按缺字前进 5 像素，不抛异常。
- 视图为同步借用：字节持有体重载后旧视图失效（`RuntimeMainBitmapText.h:8`；`RuntimeSkirmishBitmapText.h:11`）。

---

## 3. Bink 视频

### 3.1 模块定位与外部边界

`src/assets/video/` 提供播放服务 `BinkPlaybackService`：它是**一个解码子进程及其 IPC 的唯一所有者**，接口只在持有线程同步使用，不提供并发 `start/poll/stop` 契约（`src/assets/video/BinkPlaybackService.h:29-33`）。

**外部依赖边界（必须写明）**：

- 参考引擎仓库**不附带** `Binkw32.dll`，也不附带任何 `.bik` 影片数据；视频桥接源码不含解码器与影片，播放时由使用者自行提供本地游戏所需文件（`README.md:366`；仓库清单中无 `.bik / .fnt / .csf / .dll` 二进制素材，佐证 `README.md:5,304`）。
- 运行时核心为 64 位，解码助手 `BinkFrameExtractor.exe` 为独立 32 位可执行文件（`README.md:312`）。
- 非 Windows 平台：服务 `supported()` 返回 `false`，`start` 置失败信息 `"Bink playback is unsupported on this platform"`，`poll/stop` 为空操作（`src/assets/video/BinkPlaybackService.cpp:296-311,317-323`）。

### 3.2 请求、帧与播放模式

请求结构 `BinkPlaybackRequest`（`src/assets/video/BinkPlaybackService.h:20-27`）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `resourceRoot` | `fs::path` | 解码器 DLL 候选根 |
| `exportRoot` | `fs::path` | 影片导出目录候选根 |
| `movie` | `fs::path` | 指定影片；为空则解析默认主菜单影片 |
| `mode` | `PlaybackMode` | `Loop`（循环）或 `Once`（单次） |
| `soundEnabled` | `bool` | 是否尝试音轨 |
| `soundVolume` | `int` | 音量 0..32768，启动时钳制 |

帧结构 `BinkVideoFrame`（`src/assets/video/BinkPlaybackService.h:13-18`）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `width` / `height` | `int` | 帧像素尺寸 |
| `bgra` | `std::vector<unsigned char>` | 自有的自上而下 BGRA 像素；`poll` 成功才整体替换，失败保留调用方原帧 |
| `source` | `std::string` | 来源标识，形如 `<movie>!frame=<frameNumber>` |

播放模式与完成判定（`src/assets/video/BinkVideoStream.h:11-24`）：

| 模式 | 完成条件 |
| --- | --- | `Loop` | 不完成（助手侧帧号回绕续播） |
| `Once` | `frameCount > 0` 且 `frameNumber >= frameCount` |

### 3.3 候选路径解析

`resolveBinkPlaybackPaths` 只做路径发现，不启动助手、不触碰播放会话（`src/assets/video/BinkPlaybackPaths.h:5-6`）；每条候选取第一个存在的常规文件，不做规范化、排序或解码失败后的影片回退（`BinkPlaybackPaths.h:21-23`）。

| 结果字段 | 候选顺序 | 出处 |
| --- | --- | --- |
| `movie` | 未指定影片：`exportRoot/langmd/ra2ts_l.bik` → `exportRoot/lang/ra2ts_l.bik`；显式指定影片：仅该路径，缺失即空，无默认回退 | `BinkPlaybackPaths.cpp:22-27` |
| `decoderDll` | `resourceRoot/GameMD/Binkw32.dll` → `resourceRoot/Game/Binkw32.dll` | `BinkPlaybackPaths.cpp:28-31` |
| `helper` | 助手目录 / `BinkFrameExtractor.exe` → 其父目录同名 → 父目录 `bink_decoder32/Debug/BinkFrameExtractor.exe` → 当前目录 `build/BinkFrameExtractor.exe` → 当前目录 `BinkFrameExtractor.exe` | `BinkPlaybackPaths.cpp:32-39` |

### 3.4 影片头几何探测

启动前由服务端读取影片头前 36 字节做几何校验（`src/assets/video/BinkPlaybackService.cpp:51-67`）：

| 偏移 | 长度 | 字段 | 规则 |
| --- | --- | --- | --- |
| 0 | 3 | 魔数 `BIK` | 不匹配返回失败 |
| 20 | 4 | `width`（小端） | `> 0` 且 `<= 4096` |
| 24 | 4 | `height`（小端） | `> 0` 且 `<= 4096`；`width*height <= uint32 最大值 / 4` |

> 参考引擎只解析宽高用于分配共享面；影片内部分辨率、帧率等字段不在服务端解析，而由助手从 Bink 句柄头部读取后回写共享头（见 3.7）。

### 3.5 跨进程帧协议（共享内存 + 事件）

**共享头 `SharedHeader`**（`src/assets/video/BinkVideoStream.h:33-50`）：指针-free、自然对齐，总大小 64 字节（`BinkVideoStream.h:52`），供 64 位运行时与 32 位助手跨进程发布完成帧。

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `magic` | `uint32` | `kMagic = 0x314b4e42`（`"BNK1"`） |
| `version` | `uint32` | `kVersion = 1` |
| `capacityWidth` / `capacityHeight` | `uint32` | 共享面容量尺寸（服务端按影片几何填入） |
| `frameCapacityBytes` | `uint32` | 单缓冲字节容量（`width*height*4`） |
| `status` | `volatile int32` | `<0` 助手错误码（绝对值）；`0` 未就绪；`1` 帧就绪 |
| `activeBuffer` | `volatile int32` | 有效缓冲下标 `0` / `1` |
| `sequence` | `volatile int32` | 帧序号，自增；消费方用于去重 |
| `width` / `height` | `uint32` | 当前帧几何 |
| `frameBytes` | `uint32` | 当前帧字节数 |
| `frames` | `uint32` | 影片总帧数（助手回写） |
| `frameRate` / `frameRateDiv` | `uint32` | 帧率分子 / 分母（助手回写） |
| `frameNumber` | `uint32` | 当前帧号（助手回写） |
| `reserved` | `uint32` | 保留 |

映射大小 = `sizeof(SharedHeader) + frameCapacityBytes * 2`（双缓冲，`BinkVideoStream.h:57-60`）。

**进程间对象与命令行**（`src/assets/video/BinkPlaybackService.cpp:121-166`）：

1. 自增全局序号生成唯一后缀（PID + tick + 序号），命名：
   - 共享内存：`Local\ConcordRTS_Bink_Map_<suffix>`
   - 帧事件：`Local\ConcordRTS_Bink_Frame_<suffix>`
   - 停止事件：`Local\ConcordRTS_Bink_Stop_<suffix>`（手动重置）。
2. `CreateFileMappingW` + `MapViewOfFile`，在映射上 placement-new 构造 `SharedHeader`，填入容量几何。
3. `CreateEventW` 创建帧事件（自动重置）与停止事件（手动重置）。
4. 命令行参数（全部加引号，`CREATE_NO_WINDOW`，`BinkPlaybackService.cpp:157-183`）：

| 位置 | 内容 |
| --- | --- |
| `argv[1]` | 流模式开关：`--stream` / `--stream-once` / `--stream-muted` / `--stream-once-muted`（`BinkPlaybackService.cpp:37-49`） |
| `argv[2]` | `decoderDll` 路径 |
| `argv[3]` | `movie` 路径 |
| `argv[4]` | 共享内存名 |
| `argv[5]` | 帧事件名 |
| `argv[6]` | 停止事件名 |
| `argv[7]` | 音量 0..32768（静音时为 0） |

### 3.6 服务端启动 / 轮询 / 停止流程

**启动**（`src/assets/video/BinkPlaybackService.cpp:79-191`）：

1. 先 `stop()` 清理上一会话，清空失败信息与完成标志。
2. 解析三个路径；影片缺失、DLL 缺失、助手缺失分别置对应失败信息并返回 `false`（`BinkPlaybackService.cpp:100-106`）。
3. 几何探测；非法头 / 超出解码器面容量置失败（`BinkPlaybackService.cpp:108-118`）。
4. 创建共享内存、事件；失败置 `"CreateFileMapping failed"` / `"MapViewOfFile failed"` / `"CreateEvent failed"`（`BinkPlaybackService.cpp:133-153`）。
5. 音量钳制到 `0..32768`；`soundEnabled && soundVolume > 0` 才视为有声（`BinkPlaybackService.cpp:155-156`）。
6. `CreateProcessW` 启动助手；失败置 `"the 32-bit Bink decoder helper could not start"`（`BinkPlaybackService.cpp:172-186`）。

**轮询**（`BinkPlaybackService.cpp:193-242`）：

1. 等待帧事件 `waitMilliseconds`；超时未就绪时检查子进程是否已退出：退出则置 `finished_` 并 `stop()`，返回 `false`。
2. 读 `status`：`<0` 置失败信息 `"the Bink decoder helper reported error <code>"` 并停止。
3. 就绪校验：`status == 1`、`sequence > 0` 且不等于上次序号、`activeBuffer ∈ {0,1}`、宽高非零、`width*height*4 == frameBytes` 且不超容量；任一不满足返回 `false`，不动调用方帧。
4. 按 `activeBuffer` 取像素缓冲，`assign` 到 `output.bgra`（自有拷贝，共享视图不逃逸）；写入 `source` 标识；记录上次序号。

**停止与清理**（`BinkPlaybackService.cpp:244-270`）：

1. 置停止事件；等待子进程退出 1500 ms；超时才 `TerminateProcess`（仅第三方解码器卡死在退出阶段时），再等 250 ms。
2. 关闭进程句柄、解除映射、关闭映射 / 帧事件 / 停止事件句柄，复位容量与序号。
3. `stop` 复位序号但保留完成 / 失败状态直到下次 `start`（`BinkPlaybackService.h:32-33`）；析构函数调用 `stop()`（`BinkPlaybackService.cpp:77`）。

### 3.7 助手侧（32 位解码进程）行为

助手源码 `tools/BinkFrameExtractor.cpp`，`--stream*` 模式进入 `streamBinkFrames`（`tools/BinkFrameExtractor.cpp:193-464`）：

**启动绑定**：

1. 校验参数个数与音量解析（音量钳制 0..32768；非数字退出码 2，`tools/BinkFrameExtractor.cpp:199-223`）。
2. `OpenFileMappingA` / `OpenEventA` 打开共享内存与两个事件；失败关闭已打开句柄并返回 1（`tools/BinkFrameExtractor.cpp:225-234`）。
3. 校验共享头 `magic` / `version` / `frameCapacityBytes`；不匹配发布错误码 10（`tools/BinkFrameExtractor.cpp:245-255`）。
4. `LoadLibraryA` 加载 `Binkw32.dll`；失败错误码 11（`tools/BinkFrameExtractor.cpp:257-266`）。
5. 绑定导出函数（stdcall 修饰名，`tools/BinkFrameExtractor.cpp:267-284`）：

| Bink API 修饰名 | 用途 |
| --- | --- |
| `_BinkOpen@8` | 打开影片 |
| `_BinkClose@4` | 关闭影片 |
| `_BinkDoFrame@4` | 解码当前帧 |
| `_BinkNextFrame@4` | 推进到下一帧 |
| `_BinkWait@4` | 按原始 Bink 时钟等待帧就绪 |
| `_BinkService@4` | 服务音频缓冲（最终帧保持期使用） |
| `_BinkSetSoundOnOff@8` | 开关音轨 |
| `_BinkSetVolume@8` | 设置每缓冲音量 |
| `_BinkCopyToBuffer@28` | 拷帧到目标缓冲 |

必需导出缺失错误码 12（`tools/BinkFrameExtractor.cpp:285-295`）。

**音轨与音量**：

- 音轨数由影片头偏移 40 处的小端 `uint32` 读取（`tools/BinkFrameExtractor.cpp:129-141,297-299`）；`soundEnabled` 为假时按 0 处理。
- 后端选择：优先 DirectSound（私有次缓冲音量控制，配一个隐藏 `STATIC` 工具窗口隔离系统 WaveOut 设备音量）；DirectSound 设备打开失败回退 WaveOut。WaveOut 仅在音量满格 `32768` 时启用——旧版 Bink 会改系统级设备音量，故不对 WaveOut 调用 `BinkSetVolume`（`tools/BinkFrameExtractor.cpp:96-115,303-347`）。
- `BinkOpen` 成功后、解码开始前立即按零售顺序施加 `BinkSetVolume`（`tools/BinkFrameExtractor.cpp:315-319`）。
- 音频后端初始化失败：关闭声音继续纯视频（`tools/BinkFrameExtractor.cpp:360-371`）。
- 主菜单音量由百分比换算：`clamp(percent,0,100) * 32768 + 50) / 100`（`src/ui/main_menu/RuntimeMainMenu.cpp:355-357`；样例：40% → 13107，50% → 16384，100% → 32768，`RuntimeMainMenu.cpp:2486-2491`）。

**帧循环**（`tools/BinkFrameExtractor.cpp:404-454`）：

1. 循环顶部查停止事件；`BinkWait` 未就绪则再等停止事件 1 ms 后继续。
2. 读 `activeBuffer`，选另一侧缓冲 `writeIndex`。
3. `BinkDoFrame` → `BinkCopyToBuffer`（表面类型 `BINKSURFACE32 = 3`，拷贝全部 `0x80000000`）；随后把每像素 alpha 位置写 255（`tools/BinkFrameExtractor.cpp:416-426`）。
4. 回写 `frameNumber`，内存屏障后发布 `activeBuffer`、自增 `sequence`、置 `status = 1`、`SetEvent(frameEvent)`。
5. `Once` 模式达到末帧：进入最终帧保持流程——按帧间隔 + 125 ms 输出缓冲余量计算保持时长，期间持续 `BinkService` 喂音频，停止事件可立即打断（`tools/BinkFrameExtractor.cpp:143-175,433-449`）；随后 `BinkNextFrame` 续播（`Loop` 模式保留 Bink 1.0q 的帧号回绕，`tools/BinkFrameExtractor.cpp:450-453`）。

**容量与错误码**：影片宽高超容量、帧字节超缓冲发布错误码 14（`tools/BinkFrameExtractor.cpp:373-390`）；`BinkOpen` 失败错误码 13（`tools/BinkFrameExtractor.cpp:349-358`）。结束时关闭影片、销毁声音窗口、释放 DLL、解除映射并关闭三个句柄（`tools/BinkFrameExtractor.cpp:456-463`）。

### 3.8 播放时钟与上层消费

- **帧节奏时钟在助手侧**：由 Bink 原生 `BinkWait` 门控帧起点；服务端不做逐帧计时（`tools/BinkFrameExtractor.cpp:405-408`；`BinkPlaybackService.h:33` 注释「The Windows helper supplies the original Bink clock and final-frame hold」）。
- 主菜单侧仅用 `presentationClock->nowMs()` 做**首帧启动超时**判定：首帧未到时记录启动时刻，超时或助手失败 / 提前退出则回退静态预览图（`src/ui/main_menu/RuntimeMainMenu.cpp:600-627,633-659`）。
- `BinkMenuAnimation` 是服务的薄包装：`poll` 时与调用方 `AssetImage` 交换 `bgra` 缓冲，避免拷贝共享视图（`RuntimeMainMenu.cpp:359-397`）。
- 主菜单背景影片以 `Loop` 模式启动；影片播放页（战役 / 选片）以 `Once` 模式启动，Esc / 空格可跳过（`RuntimeMainMenu.cpp:2104-2107,2008`）。
- 影片库扫描：`GameMD/Movies`、`Movies` 目录下收集 `.bik`，按文件名去重并排序（`RuntimeMainMenu.cpp:767-812`）。
- 战役影片经 `RuntimeCampaignMovieResolver` 解析逻辑名：松散文件直接给路径；MIX 条目以 `archive.mix!entry.bik` 虚拟引用保存，播放请求时才物化到确定性缓存目录（`src/runtime/campaign/RuntimeCampaignMovieResolver.h:20-35,51-59`）。

### 3.9 视频错误处理约定

| 失败点 | 失败信息 / 行为 | 出处 |
| --- | --- | --- |
| 默认影片未找到 | `"ra2ts_l.bik was not found"` | `BinkPlaybackService.cpp:101-103` |
| 指定影片未找到 | `"requested Bink movie was not found"` | `BinkPlaybackService.cpp:103-104` |
| 解码器 DLL 缺失 | `"Binkw32.dll was not found"` | `BinkPlaybackService.cpp:105` |
| 助手缺失 | `"the 32-bit Bink decoder helper was not found"` | `BinkPlaybackService.cpp:106` |
| 影片头非法 | `"the Bink movie has an invalid header"` | `BinkPlaybackService.cpp:110-112` |
| 超出面容量 | `"the Bink movie exceeds the decoder surface limit"` | `BinkPlaybackService.cpp:115-118` |
| 共享内存 / 事件失败 | `"CreateFileMapping failed"` / `"MapViewOfFile failed"` / `"CreateEvent failed"` | `BinkPlaybackService.cpp:141,143,152` |
| 助手进程启动失败 | `"the 32-bit Bink decoder helper could not start"` | `BinkPlaybackService.cpp:185` |
| 助手上报错误 | `"the Bink decoder helper reported error <code>"`（`status < 0`） | `BinkPlaybackService.cpp:209-213` |
| 校验不通过 | 返回 `false`，调用方帧保持不变 | `BinkPlaybackService.cpp:221-229` |
| 平台不支持 | `"Bink playback is unsupported on this platform"` | `BinkPlaybackService.cpp:301` |

---

## 4. 测试夹具与合同测试佐证

### 4.1 生产格式合同测试（自编字节夹具，不依赖官方素材）

| 测试文件 | 覆盖内容 | 出处 |
| --- | --- | --- |
| `tests/assets/CsfCodecContract.cpp` | 短头 / 坏魔数拒绝；头字段 4/12/16/20 未被使用；逐字节截断保留已完成条目；多值只取首值；`WRTS` 附加字节截断不提交首值；重复键 / 空键 / 空值 / 内嵌 NUL；两种回车策略；尾部垃圾字节忽略 | `CsfCodecContract.cpp:75-201` |
| `tests/assets/WestwoodFontCodecContract.cpp` | 合成字体头字段精确解析；全 65536 代码单元映射查找；两种回退策略；越界序号 / 零映射 / 非法宽度 / 坏 `?` 记录 / 零宽度；短输入保留度量；语义坏头写入原始字段；截断记录拒绝；尾部多余字节接受；保留头字不限制；`glyphCount` 不按映射宽度截断；`stride=16 / rows=64` 边界 | `WestwoodFontCodecContract.cpp:21-49` |
| `tests/assets/BinkPlaybackServiceContract.cpp` | 服务不可拷贝 / 不可移动；`SharedHeader` 大小 64；32 轮负向生命周期：缺失影片启动失败、失败信息匹配、`poll` 不改动自有帧、重复 `stop` 状态保留；非 Windows 不报播放成功 | `BinkPlaybackServiceContract.cpp:6-37` |
| `tests/assets/MoviePathContract.cpp` | 助手 / DLL / 影片候选顺序与目录拒绝规则；显式缺失影片无默认回退；显式影片优先；相对候选在空发现路径下保留；MIX 与松散影片搜索顺序、物化缓存（尺寸 / mtime 身份）、锁定归档跳过、空松散文件仍胜出 | `MoviePathContract.cpp:48-174` |

### 4.2 UI 文字与影片链路测试

| 测试目录 / 文件 | 覆盖内容 | 出处 |
| --- | --- | --- |
| `tests/ui/main_bitmap_text/MainBitmapTextContract.cpp`、`MainBitmapTextNativeContract.cpp`、`OriginalMainFontReference.h` | 主菜单位图文字测量 / 绘制合同；参考头在测试时从本机已安装原始字体加载字节，仓库不内置字体二进制 | `tests/ui/main_bitmap_text/` 目录清单 |
| `tests/ui/result_bitmap_text/ResultBitmapTextValueContract.cpp`、`ResultBitmapTextNativeContract.cpp`、`OriginalResultFontReference.h` | 结算位图文字测量、省略号截断与 sink 矩形填充合同 | `tests/ui/result_bitmap_text/` 目录清单 |
| `tests/infrastructure/RuntimeSkirmishBitmapTextContract.cpp` | 遭遇战位图文字像素 / 命令记录合同 | `tests/infrastructure/RuntimeSkirmishBitmapTextContract.cpp` |
| `tests/campaign/RuntimeCampaignMovieResolverSmoke.cpp` | RA2 `movies01/02` 与 YR `movmd03.mix` 影片解析隔离；未知版本号不跨归档；`art.ini [Movies]` 索引审计（未解析项白名单）；物化 BIK 签名 `BIK`、尺寸与缓存复用校验 | `RuntimeCampaignMovieResolverSmoke.cpp:64-242` |
| `tests/campaign/CampaignPresentationDecoderSmoke.cpp` 等 | 战役表现 / 进度 / 数据源 Smoke（诊断性质，不作为生产行为依据） | `tests/campaign/` 目录清单 |

### 4.3 夹具性质说明

- `tests/assets/movie_path/CMakeLists.txt` 只编译路径策略与归档读取源码，不含视频设备 / IPC（`tests/assets/movie_path/CMakeLists.txt:1-3`）。
- 仓库 `tests/fixtures/`、`samples/` 中的 INI / MAP 为自编测试输入，不是完整原版素材（`README.md:304`）；本章未在仓库内发现任何内置 `.csf` / `.fnt` / `.bik` / `Binkw32.dll` 二进制样本。

---

## 5. 本章缺口 / 参考引擎未覆盖项

1. **CSF 头语言与版本字段未解析**：偏移 4、12、16、20 的字段在解码器中无定义语义；引擎内不存在按语言筛选 / 多目录语言匹配的实现。
2. **CSF 多值条目只保留首值**：同一标签的第 2 个及以后值被吞掉；`WRTS` 附加字节内容未解析（用途与格式在源码中无对应消费）。
3. **CSF 不做编码转换**：解码产物为 UTF-16 代码单元，代理项不配对；目录层转 `std::wstring` 按代码单元直转，无规范化、无宽度折叠。
4. **FNT 无字符斜体 / 粗体 / 间距表 / 字形原点偏移**：记录只有宽度与 1-bit 掩码；字距为固定 `width + 1`，缺字固定前进 5 像素；基线、上下偏移字段在头中不存在。
5. **FNT 字符映射为单级 BMP 映射**：按 `uint16` 代码单元查表，没有代理项对映射、没有字体回退链；`?` 之外的缺字策略由各 UI 视图层决定。
6. **位图字体与 GDI 系统字体的上层切换策略**：两套字体适配器并存，本章只梳理 `.fnt` 字节链路；何时用位图字体、何时降级系统字体的状态机判定分布在各菜单源码中，未抽象为统一字体服务。
7. **Bink 解码本体不在仓库内**：实际帧解码、音轨解码全部依赖外部 `Binkw32.dll`；引擎内没有独立的 Bink 比特流 / 压缩帧解析实现。
8. **影片元数据解析有限**：服务端只读宽高；帧率、总帧数、帧号由助手从 Bink 句柄回写共享头，引擎源码不解析 BIK 文件内部 chunk 结构；音频轨道条数仅读取头部偏移 40 一个 `uint32`。
9. **无音轨混音 / 音量持久化**：音量仅在启动时按百分比换算并一次性传给助手；WaveOut 后端受旧版 Bink 系统音量限制；运行中切音量、静音切换在本链路中未见实现。
10. **非 Windows 平台视频链路为空实现**：`BinkPlaybackService` 非 Windows 编译单元只返回不支持状态，无跨平台解码后备。
11. **仓库不附带影片与字体 / 文字样本**：所有 `.csf`、`.fnt`、`.bik`、`Binkw32.dll` 均需使用者从本地已安装游戏提供；本章字节布局结论以生产源码与自编合同测试为准，未用官方二进制样本复核。
12. **战役影片缓存的大小身份策略**：物化缓存按尺寸 + mtime 判定身份，同尺寸替换不重新校验（`MoviePathContract.cpp:121-125` 佐证为有意行为）；复刻时需注意这不是内容哈希校验。
