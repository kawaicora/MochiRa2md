# 05a · 图像与体素解码（PAL / SHP / TMP / VXL / HVA）

## 本章覆盖范围

- PAL 调色板：768 字节 256 色条目的字节布局、6 位 / 8 位通道判定、三种色彩展开策略、各消费方的长度与颜色策略选择。
- SHP 图像：文件头 / 帧记录结构、逐行编码格式（格式 0/1/2/3/4 在参考引擎中的真实支持情况）、像素展开、运行时与预览两套兼容策略、作者停顿帧（authored pause）、循环 / 往返（PingPong）帧选择、辅助动画帧范围与节奏、整数边界与截断处理。
- TMP 地形块：普通 TMP 头部 / 偏移表 / 子块图像记录、菱形底图与 extra / Z-data 段；raw format 3 兼容格式（SHP 式逐行 RLE）；定位缓存、元数据查询与地形绘制接口。
- VXL 体素 + HVA 体素动画：文件头部、段表、体素段尾表（footer）、列 / 跨度编码、HVA 3×4 矩阵帧变换；加载预热、源缓存与准入边界；Reference / Point 模式边界。**投影、面剔除、AO 着色、光栅化与阴影生成交给绘制章节**，本章只讲解码与数据结构，并在对应位置标注接口。
- 各解码器的错误处理、极端整数边界、测试 / 契约夹具位置与佐证引用。

## 对应源码目录

生产源码（`src/` 下 `.h/.cpp/.inl`）：

| 文件 | 职责 |
| --- | --- |
| `src/assets/codecs/PalDecoder.h` | PAL 原始调色板结构、长度策略 / 颜色策略枚举、解码与映射函数声明 |
| `src/assets/codecs/PalDecoder.cpp` | 768 字节读取、6 位通道判定、三策略展开 |
| `src/assets/codecs/ShpCodec.h` | SHP 解码策略枚举 `ShpDecodePolicy`、单帧结构 `ShpIndexedFrame`、解码入口声明 |
| `src/assets/codecs/ShpCodec.cpp` | SHP 头 / 帧记录解析、格式 0/1/2/3/4 行解码、运行时 / 预览兼容判定 |
| `src/assets/codecs/RuntimeTmpTypes.h` | TMP 子块 `RuntimeTmpSubTileImage`、瓦片文件 `RuntimeTmpTileFile` 结构 |
| `src/assets/codecs/RuntimeTmpCodec.h` | TMP 解码入口声明 |
| `src/assets/codecs/RuntimeTmpCodec.cpp` | 普通 TMP 头 / 偏移表 / 图像记录解码；raw format 3 兼容解码；溢出与边界校验 |
| `src/assets/terrain/RuntimeTerrainTmpQuery.h` | TMP 定位缓存、字节源抽象、备忘录端口、元数据 / 坡度 / 地形类型查询接口 |
| `src/assets/terrain/RuntimeTerrainTmpQuery.cpp` | 按索引 / 按名缓存、MIX 资源定位、landType 映射表、查询落地 |
| `src/assets/inspection/AssetInspector.cpp` | 资产检查路径下的 VXL / HVA 元数据解析与配对报告（`parseVxlMetadata` / `parseHvaMetadata`） |
| `src/render/core/RuntimeMapRenderer.h` | `RuntimeVoxelMetadata`、`RuntimeHvaTransform`、`RuntimeVoxelSourceCacheEntry`、光栅化体素 / 面结构 |
| `src/render/core/RuntimeMapRenderer.cpp` | 运行时 VXL 元数据 / 体素体段解码、HVA 元数据与逐帧矩阵解析、VXL 源缓存加载、渲染剖面 / 光栅模式枚举 |
| `src/render/voxel/RuntimeVoxelAnimation.inl` | VXL 动画帧选择、按帧 HVA 变换缓存 |
| `src/render/voxel/RuntimeGeometryVoxelAnimationMemo.inl` | HVA 帧数缓存备忘录 |
| `src/render/voxel/RuntimeVoxelBodyTransform.inl` | 段倾斜变换（HVA 矩阵与朝向旋转合成） |
| `src/render/voxel/RuntimeVoxelLoadingPrewarm.h` / `.inl` | 当前世界体素预热限制、准入条件、预算状态 |
| `src/render/voxel/RuntimeFutureVoxelBootstrap.h` / `.inl` | 未来类型体素引导预热、源选择与准入 |
| `src/render/buildings/RuntimeAuxiliaryAnimation.h` | 辅助动画帧范围解析、正向 / 往返循环、节奏换算 |
| `src/render/units/RuntimeUnitIdleAnimation.inl` | 单位待机 / 移动动画步进换算 |
| `src/runtime/timing/RuntimeRuleTime.h` | 规则帧率→tick 换算工具 |
| `src/data/art/ArtDatabase.h` / `.cpp` | ArtType 动画帧范围 / PingPong / Rate 等 INI 字段读取 |

测试 / 契约夹具（`tests/`，仅作格式约定佐证，不作为生产行为依据）：

| 文件 | 覆盖 |
| --- | --- |
| `tests/assets/ShpCodecContract.cpp`、`tests/assets/ShpCodecCorpus.h` | SHP 双策略（Preview / Runtime）字节语料与期望帧 / 错误比对 |
| `tests/infrastructure/PalDecoderContract.cpp` | 三种颜色策略的 64 级查表对照与长度策略 |
| `tests/infrastructure/RuntimeTmpCodecContract.cpp`、`tests/fixtures/RuntimeTmpCodecOld.inl` | 普通 TMP 与 raw format 3 字段级比对 |
| `tests/infrastructure/RuntimeTerrainTmpQueryContract.cpp`、`tests/fixtures/RuntimeTmpCodecOld.inl`、`tests/fixtures/RuntimeTerrainTmpQueryOld-r2.inl` | TMP 定位 / 元数据查询契约 |
| `tests/render/grand_cannon_raster/GrandCannonRasterContract.cpp` | 加农炮体素面 / 体素栅格任务夹具 |
| `src/render/voxel/diagnostics/RuntimeGpuVxlModeContract.inl` | Reference/Point 模式下 VXL/HVA 实机证据（运行时落盘 `.vxl.bin` / `.hva.bin`） |

> 说明：参考引擎的体素解码器与渲染管线耦合在 `RuntimeMapRenderer.cpp` 内，不存在独立的 “VXL 编解码模块目录”；`src/render/voxel/` 下的 `.inl` 主要承担动画、变换、预热与投影备忘录职责。

---

## 1. PAL 调色板

### 1.1 文件定位

解码器位于 `src/assets/codecs/PalDecoder.h` / `PalDecoder.cpp`，命名空间 `concord::assets`。解码只产出原始 RGB 条目与显示用展开结果；**调色板不推断透明索引、remap 表、像素格式或资源选择策略**（`PalDecoder.h:31-32` 头注释明确声明）。

### 1.2 字节 / 结构体表

**文件字节布局**（`PalDecoder.cpp:20-43`）：

| 偏移 | 长度 | 类型 | 含义 |
| --- | --- | --- | --- |
| `0` 起，每 3 字节一条 | 768 字节 | 256 × (R,G,B) | 颜色条目；分量顺序 R、G、B |
| 条目 `index` | 字节 `3*index` / `3*index+1` / `3*index+2` | `uint8` | 该条目红 / 绿 / 蓝分量 |

**结构体表**（`PalDecoder.h:9-21`）：

| 名称 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- |
| `PalRgbChannels` | `{ r, g, b : uint8 }` | 单条 RGB 分量 | `PalDecoder.h:9-14` |
| `PalRawPalette.colors` | `array<PalRgbChannels, 256>` | 解码后的原始条目 | `PalDecoder.h:18` |
| `PalRawPalette.sixBit` | `bool` | 全部通道 ≤ 63 时为 `true`；任一分量 > 63 整体按 8 位处理 | `PalDecoder.h:19`；判定见 `PalDecoder.cpp:39` |

**策略枚举**：

| 枚举 | 取值 | 含义 | 出处 |
| --- | --- | --- | --- |
| `PalLengthPolicy::Exact768` | — | 文件必须恰好 768 字节 | `PalDecoder.h:23`；校验 `PalDecoder.cpp:23-25` |
| `PalLengthPolicy::Prefix768` | — | 至少 768 字节，忽略 768 之后的字节 | `PalDecoder.h:23`；校验 `PalDecoder.cpp:26-28` |
| `PalColorPolicy::RoundedSixBit` | — | 6 位值四舍五入到 0..255：`(v*255+31)/63` | `PalDecoder.cpp:9-10` |
| `PalColorPolicy::DacShiftTwo` | — | 6 位值左移 2 位：`v << 2` | `PalDecoder.cpp:11-12` |
| `PalColorPolicy::ReplicatedSixBit` | — | 高 2 位回填低 2 位：`(v<<2) \| (v>>4)` | `PalDecoder.cpp:13-14` |

### 1.3 解码步骤

1. 按长度策略校验字节数：`Exact768` 要求 `bytes.size() == 768`；`Prefix768` 要求 `bytes.size() >= 768`；不满足返回 `false`，输出保持不变（`PalDecoder.cpp:22-31`）。
2. 逐条读取 256 个条目，每条目 3 字节 R/G/B（`PalDecoder.cpp:33-38`）。
3. 扫描期间若任一通道 > 63，置 `sixBit = false`（`PalDecoder.cpp:39`）。
4. `mapPalColors` 展开显示色：`sixBit` 为 `false` 时原样返回 8 位条目；否则按所选策略逐分量展开（`PalDecoder.cpp:45-54`）。

### 1.4 消费者用法与策略选择

| 消费方 | 长度策略 | 颜色策略 | 出处 |
| --- | --- | --- | --- |
| 运行时地图调色板 `decodeRuntimePaletteBytes` | `Exact768` | `DacShiftTwo` | `src/render/core/RuntimeMapRenderer.cpp:294-305` |
| 地图预览导出器 | `Exact768` | `RoundedSixBit` | `src/assets/preview/MapPreviewExporter.cpp:318-329` |
| 战役菜单调色板 `decodeCampaignPalette` | `Prefix768` | `ReplicatedSixBit` | `src/ui/campaign/RuntimeCampaignMenu.cpp:626-633` |
| 资产检查器 `decodeRa2Palette` | `Exact768` | `RoundedSixBit` | `src/assets/inspection/AssetInspector.cpp:1342-1361` |

### 1.5 错误与边界

- 解码失败时**输出参数保持原样不被修改**（`PalDecoder.h:26`、`PalDecoder.cpp:21-42`）。
- 不存在透明色索引约定、不存在 owner remap 表读取；这些由上层消费方按各自调色板语境处理。
- 测试夹具：`tests/infrastructure/PalDecoderContract.cpp` 内置 64 级 `rounded / shifted / replicated` 三张金表，逐策略比对哈希（`PalDecoderContract.cpp:11-48`）。

---

## 2. SHP 图像

### 2.1 文件定位

解码器：`src/assets/codecs/ShpCodec.h` / `ShpCodec.cpp`。运行时适配层：`src/render/core/RuntimeProductionResources.cpp`；预览适配层：`src/assets/preview/MapPreviewExporter.cpp`。

### 2.2 文件头与帧记录字节布局

**文件头（8 字节，`fileHeaderSize = 8`，`ShpCodec.cpp:11`）**：

| 偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 2 | `uint16` | 参考引擎未使用（保留字） | `ShpCodec.cpp:15-18`（无对应读取） |
| 2 | 2 | `int16`（小端） | 全局画布宽 `globalWidth` | `ShpCodec.cpp:57` |
| 4 | 2 | `int16`（小端） | 全局画布高 `globalHeight` | `ShpCodec.cpp:58` |
| 6 | 2 | `uint16` | 帧总数 `frameCount` | `ShpCodec.cpp:176` |

**帧记录（24 字节，`frameRecordSize = 24`，`ShpCodec.cpp:12`；记录读取 `ShpCodec.cpp:40-47`）**：

| 记录内偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 2 | `int16` | 帧偏移 X `x` | `ShpCodec.cpp:44` |
| 2 | 2 | `int16` | 帧偏移 Y `y` | `ShpCodec.cpp:44` |
| 4 | 2 | `int16` | 帧宽 `width` | `ShpCodec.cpp:44` |
| 6 | 2 | `int16` | 帧高 `height` | `ShpCodec.cpp:44` |
| 8 | 1 | `uint8` | 压缩 / 格式标签 `compression` | `ShpCodec.cpp:45` |
| 9 | 1 | `uint8` | 预览专用格式高字节 `previewFormatHighByte` | `ShpCodec.cpp:45` |
| 10..19 | 10 | — | 非像素数据；参考引擎不解释 | `ShpCodec.cpp:42-43` 注释 |
| 20 | 4 | `uint32` | 帧数据载荷偏移 `payload` | `ShpCodec.cpp:46` |

**运行时帧结构 `ShpIndexedFrame`**（`ShpCodec.h:11-26`）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `offsetX` / `offsetY` | `int` | 帧相对全局画布的偏移 |
| `globalWidth` / `globalHeight` | `int` | 文件头全局画布尺寸 |
| `width` / `height` | `int` | 本帧尺寸 |
| `frameIndex` | `int` | 选中的帧序号 |
| `format` | `int` | 解析后的格式编号 |
| `dataOffset` | `uint32` | 载荷偏移 |
| `authoredPause` | `bool` | 作者停顿帧标记（见 2.6） |
| `pixels` | `vector<unsigned char>` | 展开后的索引像素（0..255） |
| `sourcePath` | `string` | 来源标识 |

### 2.3 格式编号与行编码

解码器支持的格式（`ShpCodec.cpp:218-246`）：

| 格式 | 行 / 像素编码 | 支持情况 | 出处 |
| --- | --- | --- | --- |
| 0 | 原始索引像素，连续存放 | 运行时 / 预览均支持 | `ShpCodec.cpp:219-230` |
| 1 | 原始索引像素，连续存放 | 运行时 / 预览均支持 | `ShpCodec.cpp:219-230` |
| 2 | 字面量行：每行一个长度字 + 该行像素，短行留透明、长行截断 | 仅 **Runtime** 策略支持；Preview 策略拒绝 | `ShpCodec.cpp:231-236`；短 / 长行规则 `ShpCodec.cpp:151-153` |
| 3 | 透明 RLE 行：每行长度字 + 令牌序列；非 0 为不透明像素，0 后跟 1 字节跳过计数 | 运行时 / 预览均支持 | `ShpCodec.cpp:119-137`、`ShpCodec.cpp:232-241` |
| 4 | 仅作为停顿帧格式被识别；非停顿场景下落入 “Unsupported format” | 非停顿不解码像素 | 停顿判定 `ShpCodec.cpp:76-79`；拒绝分支 `ShpCodec.cpp:242-245` |
| 其他 | — | 返回 “Unsupported SHP … format N” | `ShpCodec.cpp:242-245` |

**行头约定**（`takeRow`，`ShpCodec.cpp:99-117`）：存储的行长度字包含长度字自身（空行为 2）。
- 剩余字节不足 2 → “row header is truncated”；
- 存储长度 `< 2` → “row length is invalid”；
- 存储长度 > 剩余字节 → “row data is truncated”。

格式 3 令牌序列展开（`expandTransparentRow`，`ShpCodec.cpp:119-137`）：

1. 取 1 字节令牌；
2. 令牌 ≠ 0：作为不透明像素写入当前列，列号 +1；
3. 令牌 = 0：再取 1 字节作为跳过（透明）计数，列号前移；
4. 跳过计数尾随字节缺失 → “transparent run is truncated.”。

### 2.4 解码步骤（`decodeShpIndexedFrame`，`ShpCodec.cpp:163-249`）

1. **头表长度校验**：文件不足 `8 + 24` 字节 → “SHP file is too small for a frame table”（`ShpCodec.cpp:172-175`）。
2. **帧计数校验**：读帧总数（偏移 6）；为 0 → “SHP file has no frames”（`ShpCodec.cpp:176-180`）。
3. **帧序号边界**：`frameIndex < 0` 或 `>= frameCount` → 越界错误（`ShpCodec.cpp:181-185`）。
4. **只校验选中记录**：参考引擎容忍未选中记录缺失、容忍指向帧表之前的数据偏移；选中记录超出表范围 → “frame table is truncated”（`ShpCodec.cpp:186-193`）。
5. **读元数据**：`frameMetadata` 按策略解析全局 / 帧尺寸与格式（`ShpCodec.cpp:54-69`、`194-196`）。
6. **停顿帧短路**：命中 `isRuntimePause` 时置 `authoredPause`，偏移清零，直接返回成功（`ShpCodec.cpp:197-203`，见 2.6）。
7. **画布 / 尺寸校验**：Runtime 策略下全局宽高必须 > 0；帧宽高必须 > 0 且 ≤ 4096，否则 “dimensions are unsupported”（`ShpCodec.cpp:206-212`）。
8. **偏移校验**：`dataOffset >= 文件大小` → “data offset is outside the file”（`ShpCodec.cpp:213-216`）。
9. **按格式解像素**：
   - 0/1：`width*height` 个连续索引；载荷不足 → “raw … is shorter than expected”（`ShpCodec.cpp:219-230`）；
   - 2/3：逐行调用 `decodeRows`（`ShpCodec.cpp:139-159`）；
   - 其余格式：拒绝。
10. 成功则把解码结果移动出参；失败保持调用方既有错误状态（`ShpCodec.h:28` 注释；`ShpCodec.cpp:247-248`）。

### 2.5 预览与运行时两套兼容策略

枚举 `ShpDecodePolicy { Preview, Runtime }`（`ShpCodec.h:9`）。

| 差异点 | Preview | Runtime | 出处 |
| --- | --- | --- | --- |
| 有符号 16 位尺寸 | 负值按无符号解释（`v + 0x10000`） | 保留符号扩展 | `policyDimension`，`ShpCodec.cpp:49-52` |
| 格式编号 | `format = compression + highByte*256` | `format = compression` | `ShpCodec.cpp:63-66` |
| 全局画布 ≤ 0 | 不做拒绝（仅帧尺寸 / 偏移校验） | 全局宽高 ≤ 0 视为非法画布 | `ShpCodec.cpp:206-207` |
| 格式 2 | 拒绝：“Unsupported SHP … format 2” | 解码 | `ShpCodec.cpp:233-236` |
| 错误描述前缀 | “first frame” | “frame” | `ShpCodec.cpp:205` |

- 预览调用点：`MapPreviewExporter.cpp:1136`（`ShpDecodePolicy::Preview`）。
- 运行时调用点：`RuntimeProductionResources.cpp:123`（`ShpDecodePolicy::Runtime`）。

### 2.6 停顿帧（authored pause）与缺省帧策略

**停顿帧判定** `isRuntimePause`（`ShpCodec.cpp:71-80`），必须同时满足：

1. 策略为 Runtime；
2. 全局宽、全局高均 > 0；
3. 帧宽 `width == 0`、帧高 `height == 0`、`dataOffset == 0`；
4. 格式属于 `{0, 1, 3, 4}`。

命中后：`offsetX = offsetY = 0`、`authoredPause = true`，不展开像素（`ShpCodec.cpp:197-203`）。运行时适配层把停顿帧映射为空 `RuntimeShpFrameImage`，仅保留全局画布尺寸与来源路径（`RuntimeProductionResources.cpp:126-132`）。

**帧序号归一与缺省**（运行时辅助 SHP 路径，`RuntimeProductionResources.cpp:1298-1308`）：

1. 字节缓存无帧表或 `frameCount <= 0` → 返回空（`RuntimeProductionResources.cpp:1298-1300`）；
2. `mainFramesOnly` 时按 `runtimeVisibleMainFrameCount` 折半得到主帧数（`RuntimeProductionResources.cpp:92-98`、`1302-1305`）；
3. 可用帧数 ≤ 0 时归一为 0，否则 `positiveModulo(requested, available)` 取模（`RuntimeProductionResources.cpp:1306-1308`）；
4. 阴影帧：拆半偶数帧时，阴影帧号 = 可见帧号 + 可见帧数（`runtimeShpShadowFrameIndex`，`RuntimeProductionResources.cpp:100-112`）；
5. 解码失败的条目保留路径与原因，调用方回传 “frame N: <错误> source=<路径>”，不静默替换成其他帧（`RuntimeProductionResources.cpp:1534-1552`）。

> 参考引擎未实现 “帧缺失时回退到第 0 帧像素” 的自动兜底；缺省行为是停顿帧 / 空图像或显式失败上报。

### 2.7 循环、往返（PingPong）与辅助动画帧选择

辅助动画帧选择入口：`runtimeAuxiliaryAnimationFrame(art, frameCount, mainFramesOnlyFallback, animationTick, fixedTickHz)`（`RuntimeAuxiliaryAnimation.h:51-64`）。

**帧范围解析** `resolveRange`（`RuntimeAuxiliaryAnimation.h:16-33`）：

| 规则来源 | 字段 | 含义 | 出处 |
| --- | --- | --- | --- |
| `LoopStart` | `animationLoopStart` / `hasAnimationLoopStart` | 循环起始帧；优先于 `Start` | `RuntimeAuxiliaryAnimation.h:17-18`；INI 读取 `ArtDatabase.cpp:1137,1142` |
| `Start` | `animationStart` / `hasAnimationStart` | 起始帧；无 `LoopStart` 时使用 | `RuntimeAuxiliaryAnimation.h:17-18`；`ArtDatabase.cpp:1136,1141` |
| `LoopEnd` | `animationLoopEnd` / `hasAnimationLoopEnd` | 排他性循环末帧 | `RuntimeAuxiliaryAnimation.h:23-25`；`ArtDatabase.cpp:1138,1143` |
| `End` | `animationEnd` / `hasAnimationEnd` | 相对 `Start`（或解析出的 begin）的帧计数，`max(1, End)` 叠加 | `RuntimeAuxiliaryAnimation.h:26-28`；`ArtDatabase.cpp:1140,1144` |
| 缺省 | — | 无上述键时：`mainFramesOnly` 且帧数为偶 → 半帧；否则全帧 | `RuntimeAuxiliaryAnimation.h:29-30` |

**整数边界**（`RuntimeAuxiliaryAnimation.h:19,32`）：

- begin 钳制到 `[0, available-1]`；
- end 钳制到 `[begin+1, available]`；`int64` 请求值先计算再钳制，避免负范围。

**循环模式** `FrameCycle`（`RuntimeAuxiliaryAnimation.h:35-47`）：

- 正向循环：`returnCount = 0`，`advance % length` 映射到 `[begin, end)`；
- 往返（`PingPong = true` 且 `length > 1`）：`returnCount = length - 2`；回程排除两个端点，使转折帧只显示一次（`[a,b,c,d] → [c,b]` 后进入下一循环）（`RuntimeAuxiliaryAnimation.h:43-45,58`）。
- `PingPong` INI 键读取：`ArtDatabase.cpp:1172`；字段 `ArtDatabase.h:189`。

**节奏（帧间隔）**：

1. 规则帧率 `Rate` 换算为动画帧数：`runtimeRuleAnimationDelayFrames(rate) = rate > 0 ? max(1, 900/rate) : 1`（`RuntimeRuleTime.h:57-59`）；
2. 帧数换算 tick：`runtimeRuleFramesToTicks(frames, fixedTickHz)`；间隔至少 1 tick（`RuntimeAuxiliaryAnimation.h:59-60`）；
3. 步进：`advance = max(1, animationTick / tickInterval)`，再由循环映射取帧（`RuntimeAuxiliaryAnimation.h:62-63`）。

**单位待机 / 移动步进**（`RuntimeUnitIdleAnimation.inl`）：

- 死亡 / 时空冻结 / 瘫痪等状态步进回 0（`RuntimeUnitIdleAnimation.inl:6-18`）；
- `runtimeUnitAnimationStep = (tick * 15) / (fixedTickHz * max(1, rulesFrameDelay))`（`RuntimeUnitIdleAnimation.inl:20-27`）；
- 待机优先用 `idleRate`，否则按步兵序列延迟（`RuntimeUnitIdleAnimation.inl:29-41`）。

**ArtType 动画相关 INI 键清单**（`ArtDatabase.cpp:1134-1173`）：`Rate`、`Start`、`LoopStart`、`LoopEnd`、`End`、`LoopCount`、`PingPong`、`Reverse`。

> 缺口提示：`Reverse` 字段被解析（`ArtDatabase.cpp:1173`、`ArtDatabase.h:190`），但辅助动画循环与单位步进路径中未发现消费该标志的分支；`LoopCount` 仅用于投射物 / 特效循环计数（见战斗与投射物章节），不参与 SHP 帧循环。

### 2.7 错误与边界汇总

| 类别 | 边界 / 行为 | 出处 |
| --- | --- | --- |
| 文件过短 | 不足 `8+24` 字节拒绝 | `ShpCodec.cpp:172-175` |
| 空帧表 | `frameCount == 0` 拒绝 | `ShpCodec.cpp:177-180` |
| 帧越界 | 负帧号 / 超表帧号拒绝；记录截断拒绝 | `ShpCodec.cpp:181-193` |
| 尺寸上限 | 帧宽高 > 4096 拒绝（Runtime） | `ShpCodec.cpp:208-212` |
| 偏移越界 | `dataOffset` 超出文件拒绝 | `ShpCodec.cpp:213-216` |
| 行截断 | 行头 / 行载荷 / 跳过计数三类错误文案区分格式 2 与格式 3 | `ShpCodec.cpp:88-97` |
| 像素越界 | 格式 3 不透明列超宽被裁剪（写保护目标范围）；格式 2 长行整体消费并截断 | `ShpCodec.cpp:124-125,151-153` |
| 失败语义 | 失败不出参、保留调用方既有错误 | `ShpCodec.h:28` |

测试夹具：`tests/assets/ShpCodecCorpus.h` 覆盖原始格式 0/1、格式 3、最大 4096×4096 空行、有符号 16 位边界值（65535 / 32768 / 4097）、各格式停顿帧；契约在 `tests/assets/ShpCodecContract.cpp` 中对 Preview / Runtime 双策略逐例比对成功标志、帧字段与错误文案。

---

## 3. TMP 地形块

### 3.1 文件定位

解码器：`src/assets/codecs/RuntimeTmpCodec.h` / `.cpp`；类型：`RuntimeTmpTypes.h`；定位与查询：`src/assets/terrain/RuntimeTerrainTmpQuery.h` / `.cpp`。

### 3.2 普通 TMP 头部与偏移表

**文件头（16 字节，`headerBytes = 16`，`RuntimeTmpCodec.cpp:603,613-616`）**：

| 偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` | X 方向块数 `xBlocks` | `RuntimeTmpCodec.cpp:613` |
| 4 | 4 | `uint32` | Y 方向块数 `yBlocks` | `RuntimeTmpCodec.cpp:614` |
| 8 | 4 | `uint32` | 基础块宽 `blockWidth` | `RuntimeTmpCodec.cpp:615` |
| 12 | 4 | `uint32` | 基础块高 `blockHeight` | `RuntimeTmpCodec.cpp:616` |

- 偏移表：头后每槽 4 字节 `uint32` 偏移（`offsetBytes = 4`，`RuntimeTmpCodec.cpp:604,657`）；槽数 = `xBlocks * yBlocks`。
- 普通格式判定：`xBlocks/yBlocks` 非 0 且 ≤ 64，`blockWidth/blockHeight` 非 0 且 ≤ 512；不满足则转入 raw format 3 兼容路径（`RuntimeTmpCodec.cpp:618-629`）。
- 槽内偏移为 0 表示空槽（`RuntimeTmpCodec.cpp:658-660`）；非 0 偏移必须落在表尾之后、文件之内（`RuntimeTmpCodec.cpp:83-89,91-110`）。

**瓦片结构 `RuntimeTmpTileFile`**（`RuntimeTmpTypes.h:50-61`）：

| 字段 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- |
| `loadAttempted` / `loaded` | `bool` | 是否尝试 / 成功加载 | `RuntimeTmpTypes.h:51-52` |
| `blockWidth` / `blockHeight` | `int` | 基础块尺寸 | `RuntimeTmpTypes.h:53-54` |
| `images` | `vector<RuntimeTmpSubTileImage>` | 子块数组（含空槽） | `RuntimeTmpTypes.h:55` |
| `errorMessage` | `string` | 最近错误 | `RuntimeTmpTypes.h:56` |
| `gridWidth` / `gridHeight` | `int` | 作者地形网格；raw format 3 不编码网格，留 0 | `RuntimeTmpTypes.h:57-60` |

### 3.3 子块图像记录（52 字节头，`imageHeaderBytes = 52`，`RuntimeTmpCodec.cpp:121`）

| 偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | `int32` | 基础块原始偏移 X `rawBaseOffsetX` | `RuntimeTmpCodec.cpp:155` |
| 4 | 4 | `int32` | 基础块原始偏移 Y `rawBaseOffsetY` | `RuntimeTmpCodec.cpp:156` |
| 16 | 4 | `uint32` | 数据块大小 `dataBlockSize`（用于判定 extra Z-data） | `RuntimeTmpCodec.cpp:157` |
| 20 | 4 | `int32` | extra 块原始偏移 X `rawExtraOffsetX` | `RuntimeTmpCodec.cpp:158` |
| 24 | 4 | `int32` | extra 块原始偏移 Y `rawExtraOffsetY` | `RuntimeTmpCodec.cpp:159` |
| 28 | 4 | `uint32` | extra 宽 `extraWidth` | `RuntimeTmpCodec.cpp:160` |
| 32 | 4 | `uint32` | extra 高 `extraHeight` | `RuntimeTmpCodec.cpp:161` |
| 36 | 4 | `uint32` | 标志位 `flags` | `RuntimeTmpCodec.cpp:162` |
| 41 | 1 | `uint8` | 地形类型 `terrainType` | `RuntimeTmpCodec.cpp:163` |
| 42 | 1 | `uint8` | 坡度类型 `rampType` | `RuntimeTmpCodec.cpp:164` |
| 43..45 | 3 | `RGB` | 雷达左色 `radarLeft` | `RuntimeTmpCodec.cpp:165-169` |
| 46..48 | 3 | `RGB` | 雷达右色 `radarRight` | `RuntimeTmpCodec.cpp:170-174` |

标志位（`RuntimeTmpCodec.cpp:176-181`）：

| 位 | 名称 | 含义 |
| --- | --- | --- |
| `1` | `tmpImageFlagExtraData` | 存在 extra 像素段 |
| `2` | `tmpImageFlagZData` | 基础段附带 Z-data |

**子块结构 `RuntimeTmpSubTileImage`**（`RuntimeTmpTypes.h:13-48`）分四组：

1. 元数据：`present`、`hasTerrainType`、`terrainType`、`rampType`、`hasRadarColors`、`radarLeft/Right`；
2. 合成画布：`offsetX/Y`、`width/height`、`globalWidth/Height`、`pixels`、调色板哈希与打包像素缓存；
3. 基础菱形块：`baseOffsetX/Y`、`baseWidth/Height`、`basePixels`、`baseZData`、`hasBaseZData`；
4. extra 块：`hasExtra`、`extraOffsetX/Y`、`extraWidth/Height`、`extraPixels`、`extraZData`、`hasExtraZData`。

### 3.4 普通 TMP 解码步骤（`decodeTmpImagePixels`，`RuntimeTmpCodec.cpp:112-350`）

1. **尺寸校验**：`blockWidth/Height` 为 0 或记录区间非法 → “invalid image dimensions”（`RuntimeTmpCodec.cpp:123-126`）。
2. **菱形底图像素计数**：行宽从 4 起，前半段（`y < blockHeight/2 - 1`）每行 +4，其余每行 −4；累加得 `baseColorBytes`（`RuntimeTmpCodec.cpp:129-147`）。
3. **记录长度校验**：记录字节数 < `52 + baseColorBytes` → “record is too small for base pixels”（`RuntimeTmpCodec.cpp:149-153`）。
4. **读元数据**：偏移、尺寸、标志、地形 / 坡度、雷达色（`RuntimeTmpCodec.cpp:155-174`）。
5. **extra 准入校验**：标志位 + extra 宽高 > 0、≤ 512、宽高乘积不溢出，才判定 `hasExtra`（`RuntimeTmpCodec.cpp:179-191`）。
6. **段偏移计算**：基础像素 `basePixelDataOffset = 记录头 + 52`；Z-data 紧随基础像素；extra 像素紧随基础 Z-data（若有）；extra Z-data 紧随 extra 像素（`RuntimeTmpCodec.cpp:192-198`）。Z-data / extra 越界分别报错（`RuntimeTmpCodec.cpp:199-208`）。
7. **坐标系**：基础块偏移固定为 (0,0)；extra 偏移相对基础偏移取差（`RuntimeTmpCodec.cpp:210-213`）；合成画布取基础与 extra 的并集边界（`RuntimeTmpCodec.cpp:215-225`）；边界退化拒绝（`RuntimeTmpCodec.cpp:227-230`）。
8. **展开基础菱形**：逐行按行宽居中写入 `basePixels`；非 0 调色板索引同时写入合成画布 `pixels`，索引 0 留透明（`RuntimeTmpCodec.cpp:271-298`）。
9. **展开 extra**：逐像素写入；落出合成画布边界 → “extra pixels fall outside computed bounds”（`RuntimeTmpCodec.cpp:300-347`）。

### 3.5 raw format 3 兼容格式

当普通头部校验失败时进入 `loadRawTmpTileFile`（`RuntimeTmpCodec.cpp:422-593`）：该格式借用 SHP 式像素尺寸与帧槽，**不编码二维地形网格**（`gridWidth = gridHeight = 0`，`RuntimeTmpCodec.cpp:434-435`）。

**槽记录（24 字节，`slotRecordBytes = 24`，`RuntimeTmpCodec.cpp:429`）**：

| 偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` | 打包锚点（低 16 位 X、高 16 位 Y，有符号 16 位扩展） | `RuntimeTmpCodec.cpp:491,513-516` |
| 4 | 4 | `uint32` | 打包尺寸（低 16 位宽、高 16 位高） | `RuntimeTmpCodec.cpp:492-494` |
| 20 | 4 | `uint32` | 槽数据偏移 | `RuntimeTmpCodec.cpp:430,495` |

**解析步骤**：

1. 取头宽 / 高 / 槽数（偏移 2 / 4 / 6，`RuntimeTmpCodec.cpp:442-444`）；
2. 扫描各槽偏移字段，找第一个合法数据偏移作为表 / 数据分界（`RuntimeTmpCodec.cpp:446-460`）；无合法偏移 → “unsupported block dimensions”（`RuntimeTmpCodec.cpp:462-465`）；
3. 表大小必须被 24 整除；槽数 ≤ 4096；头槽数非 0 时必须与推算槽数一致（`RuntimeTmpCodec.cpp:467-481`）；
4. 逐槽校验偏移范围、宽高 ≤ 512（`RuntimeTmpCodec.cpp:497-509`）；
5. 像素解码走 `decodeRawTmpFormat3Pixels`（`RuntimeTmpCodec.cpp:352-420`）：每行一个 `uint16` 行长度字（含自身），令牌序列同 SHP 格式 3——非 0 为像素，0 后跟跳过计数；跳过计数超宽时钳到行宽（`RuntimeTmpCodec.cpp:408-413`），不透明像素超宽拒绝（`RuntimeTmpCodec.cpp:393-396`）；
6. 无任何可解码图像 → 失败；部分失败保留首个警告，整体仍可成功（`RuntimeTmpCodec.cpp:585-592`）。

### 3.6 定位缓存与元数据查询接口

**缓存结构** `RuntimeTerrainTmpCache`（`RuntimeTerrainTmpQuery.h:22-28`）：

| 字段 | 含义 |
| --- | --- |
| `tmpTilesByPath` | 路径 / 资源键 → `RuntimeTmpTileFile` |
| `lastTmpTileLoadError` | 最近一次加载诊断 |
| `terrainTilePathCacheRoot` | 缓存根指纹（剧场 / 资源根 / 瓦片根变化时清表） |
| `terrainTilePathByIndex` / `missingTerrainTilePathByIndex` | 瓦片索引 → 定位结果 / 已确认缺失集合 |

**字节源抽象** `RuntimeTerrainTmpByteSource`（`RuntimeTerrainTmpQuery.h:38-56`）：松散文件读取、按名资源读取、MIX 导出根、资源根是否含 MIX 归档。**备忘录端口** `RuntimeTerrainTmpMemoPort`（`RuntimeTerrainTmpQuery.h:60-73`）：按索引命中 / 记录上层 memo。

**查询接口**（`RuntimeTerrainTmpQuery.h:75-139`；落地 `.cpp`）：

| 接口 | 职责 | 出处 |
| --- | --- | --- |
| `loadRuntimeTmpTileFromCache` | 按路径加载并缓存 | `RuntimeTerrainTmpQuery.cpp:67-84` |
| `loadRuntimeTmpTileByNameFromCache` | 按名（MIX / 松散）加载并缓存 | `RuntimeTerrainTmpQuery.cpp:86-135` |
| `loadRuntimeTerrainTileForIndex` / `…Unmemoized` | 剧场索引 → 定位 → 加载；memo 包裹 | `RuntimeTerrainTmpQuery.cpp:164-285` |
| `resolveRuntimeTerrainMetadata` | 返回具备地形类型的子块图像指针；索引 65535 / 负索引 / 越界返回空 | `RuntimeTerrainTmpQuery.cpp:287-313` |
| `resolveRuntimeTerrainSubTileRampType` | 取坡度类型，缺省 0 | `RuntimeTerrainTmpQuery.cpp:343-351` |
| `resolveRuntimeTerrainSubTileLandType` | 经映射表转 `RuntimeTerrainLandType` | `RuntimeTerrainTmpQuery.cpp:353-367` |
| `resolveRuntimeTerrainTmpTileGridShape` | 返回网格宽高；raw format 3 或网格不符返回 false | `RuntimeTerrainTmpQuery.cpp:315-341` |

**landType 映射表** `RuntimeLandTypeByTmpTerrainType[16]`（`RuntimeTerrainTmpQuery.h:16-18`）：`{0,8,8,8,8,10,9,3,3,2,6,1,1,0,7,3}`；`terrainType` 越界返回 `Unknown(-1)`（`RuntimeTerrainTmpQuery.cpp:57-65`）。

**地形绘制侧接入点**（本章只标注接口，像素合成与深度平面见绘制章节）：

- 渲染器适配封装：`RuntimeMapRenderer.cpp:26402-26410`、`28507-28531`；
- 坡度 / 地面遮挡判定调用：`src/render/terrain/RuntimeVehicleTerrainSlopeRender.inl:18,57`；`src/render/core/RuntimeMapRenderer.cpp:32588-32629,34532`。

### 3.7 错误与边界

| 类别 | 行为 | 出处 |
| --- | --- | --- |
| 文件过短 | 不足 `16+4` 字节拒绝 | `RuntimeTmpCodec.cpp:608-611` |
| 槽数 / 表大小溢出 | `checkedMul` 拒绝 | `RuntimeTmpCodec.cpp:632-647` |
| 偏移越界 | 图像偏移必须在表尾之后、文件之内 | `RuntimeTmpCodec.cpp:83-110` |
| 菱形行超宽 | “packed diamond row is wider than the tile image” | `RuntimeTmpCodec.cpp:131-134` |
| Z-data / extra 越界 | 分段独立报错 | `RuntimeTmpCodec.cpp:199-208` |
| extra 像素越界 | 拒绝并报错 | `RuntimeTmpCodec.cpp:333-339` |
| raw 槽数 | 0 < 槽数 ≤ 4096；宽高 ≤ 512 | `RuntimeTmpCodec.cpp:473-477,506-509` |
| raw 行截断 / 无效长度 / 不透明超宽 / 跳过截断 | 四类独立错误文案 | `RuntimeTmpCodec.cpp:369-406` |

测试夹具：`tests/infrastructure/RuntimeTmpCodecContract.cpp` + `tests/fixtures/RuntimeTmpCodecOld.inl` 字段级比对；`tests/infrastructure/RuntimeTerrainTmpQueryContract.cpp` + `tests/fixtures/RuntimeTerrainTmpQueryOld-r2.inl` 定位 / 查询契约。

---

## 4. VXL 体素 + HVA 体素动画

### 4.1 文件定位

- 检查路径元数据解析：`src/assets/inspection/AssetInspector.cpp`（`parseVxlMetadata:614-678`、`parseHvaMetadata:712-797`、配对报告 `inspectVxlHvaPair:5074-5159`）。
- 运行时元数据 / 解码：`src/render/core/RuntimeMapRenderer.cpp`。
- 动画与变换：`src/render/voxel/RuntimeVoxelAnimation.inl`、`RuntimeGeometryVoxelAnimationMemo.inl`、`RuntimeVoxelBodyTransform.inl`。
- 预热与缓存准入：`src/render/voxel/RuntimeVoxelLoadingPrewarm.h/.inl`、`RuntimeFutureVoxelBootstrap.h/.inl`。

### 4.2 VXL 头部与段表字节布局

**固定头（34 字节，`fixedHeaderBytes = 34`；内嵌调色板 768 字节；`RuntimeMapRenderer.cpp:5159-5160`）**：

| 偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 16 | ASCII | 签名，必须为 `"Voxel Animation"` | `RuntimeMapRenderer.cpp:5170-5174`；检查器 `AssetInspector.cpp:630,691-693` |
| 16 | 4 | `uint32` | 头部保留值 `headerValue0` | `AssetInspector.cpp:631` |
| 20 | 4 | `uint32` | 段数 `sectionCount`（上限 1024） | `RuntimeMapRenderer.cpp:5176,5184-5187` |
| 24 | 4 | `uint32` | 段数镜像 `sectionCountMirror`（仅检查器比对） | `AssetInspector.cpp:633,707-709` |
| 28 | 4 | `uint32` | 体素体段字节数 `bodySize` | `RuntimeMapRenderer.cpp:5177` |
| 32 | 1 | `uint8` | owner remap 起始色索引（运行时）；检查器记为 `paletteMarker` 低字节 | `RuntimeMapRenderer.cpp:5178`；`AssetInspector.cpp:635` |
| 33 | 1 | `uint8` | owner remap 结束色索引（运行时） | `RuntimeMapRenderer.cpp:5179` |
| 34..801 | 768 | 256 × RGB | 内嵌调色板 | `RuntimeMapRenderer.cpp:5160`；检查器统计最大分量 `AssetInspector.cpp:645-650` |
| 802 起 | 28 × 段数 | 段记录 | 段表；前 16 字节为段名 ASCII | `sectionRecordBytes = 28`，`RuntimeMapRenderer.cpp:5161,5207-5217`；段名读取 `AssetInspector.cpp:672-675` |

> 参考引擎运行时解码器**只读取段表中的段名样本**（最多前 4 个段名入 `metadata.vxlSectionSample`，`RuntimeMapRenderer.cpp:2326-2339`）；段记录其余字段在生产解码路径中未被使用。

### 4.3 VXL 体素体段：段尾表（footer）与列 / 跨度编码

**段尾表**位于体素体段之后，每段 92 字节（`sectionFooterBytes = 92`，`RuntimeMapRenderer.cpp:5162,5231-5244`）：

| 尾表偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | `uint32` | 段体素数据相对体素体段起点的偏移 `limbDataOffset` | `RuntimeMapRenderer.cpp:5469` |
| 12 | 4 | `float32` | HVA 乘子 `hvaMultiplier`；非有限或 0 → 回退 1.0 | `RuntimeMapRenderer.cpp:5357-5362,5470-5475` |
| 16 | 48 | 12 × `float32` | 段基础 3×4 矩阵（本体内姿态） | `RuntimeMapRenderer.cpp:5364-5378,5537-5551` |
| 64 / 68 / 72 | 4×3 | `float32` | 局部包围盒 min X / Y / Z | `RuntimeMapRenderer.cpp:5404-5412,5483-5491` |
| 76 / 80 / 84 | 4×3 | `float32` | 局部包围盒 max X / Y / Z | `RuntimeMapRenderer.cpp:5413-5421,5492-5499` |
| 88 | 1 | `uint8` | 段网格 X 尺寸 `sizeX` | `RuntimeMapRenderer.cpp:5350,5476` |
| 89 | 1 | `uint8` | 段网格 Y 尺寸 `sizeY` | `RuntimeMapRenderer.cpp:5351,5477` |
| 90 | 1 | `uint8` | 段网格 Z 尺寸 `sizeZ` | `RuntimeMapRenderer.cpp:5352,5478` |
| 91 | 1 | `uint8` | 法线 / 面类型 `normalType` | `RuntimeMapRenderer.cpp:5479` |

**体素编码**（`RuntimeMapRenderer.cpp:5508-5695`）：

1. 列表：段体素数据起点后，`sizeX*sizeY` 个 `int32` 列偏移；`-1` 表示空列；负值拒绝（`RuntimeMapRenderer.cpp:5615-5627`）。
2. 列偏移解析到行数据区；越出体素体段拒绝（`RuntimeMapRenderer.cpp:5629-5639`）。
3. 列内跨度序列：先读 `skip`（字节）、再读 `count`（字节）；`z += skip`；随后 `count` 个 `(color, normal)` 字节对；最后 1 字节 `duplicateCount`；`skip=count=duplicateCount=0` 结束该列（`RuntimeMapRenderer.cpp:5645-5693`）。
4. 解出的体素入 `RuntimeDecodedVoxel { x, y, z, color, normal }`（`RuntimeMapRenderer.cpp:5314-5320`），同时写入占用网格 `occupiedVoxels`；颜色 0 仍计入占用（后续映射到可见色，不打透明洞）（`RuntimeMapRenderer.cpp:5674-5686`）。
5. 跨度循环保护：迭代计数 > `sizeZ + 32` → “column span loop did not terminate”（`RuntimeMapRenderer.cpp:5646-5650`）。

**解码中间结构**（`RuntimeMapRenderer.h:176-209`）：

| 结构 | 关键字段 | 含义 |
| --- | --- | --- |
| `RuntimeRasterVoxel` | `x/y/shadowX/shadowY/depth`、`color`、`light`、`ownerRemap`、`material`、`normalIndex`、`vplSection`、`surfaceClass`、`sectionIndex` | 单个体素光栅结果 |
| `RuntimeRasterFace` | 4 角 `x/y/shadowX/shadowY/cornerDepth`、`depth`、`color`、`light`、`ownerRemap`、`material`、`normalIndex`、`vplSection`、`surfaceClass`、`sectionIndex` | 单张四面体面结果 |

> 上述结构到投影、面剔除、AO、阴影与最终像素的流程属于绘制章节；本章不展开。

### 4.4 HVA 头部与帧矩阵

**固定头（24 字节，`fixedHeaderBytes = 24`，`RuntimeMapRenderer.cpp:2367,2468`）**：

| 偏移 | 长度 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 16 | ASCII | 关联路径名 `path`（检查器读取） | `AssetInspector.cpp:728` |
| 16 | 4 | `uint32` | 帧数 `frameCount`（上限 100000） | `RuntimeMapRenderer.cpp:2376,2378-2382` |
| 20 | 4 | `uint32` | 段数 `sectionCount`（上限 1024） | `RuntimeMapRenderer.cpp:2377,2378-2382` |
| 24 起 | 16 × 段数 | ASCII | 段名表 | `RuntimeMapRenderer.cpp:2368,2384-2392` |
| 段名表后 | 12 × `float32` / 段 / 帧 | 矩阵 | 每段每帧一个 3×4 矩阵（12 浮点，48 字节） | `RuntimeMapRenderer.cpp:2369-2370,2400-2419` |

**运行时变换结构** `RuntimeHvaTransform`（`RuntimeMapRenderer.h:170-177`）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `valid` | `bool` | 矩阵是否全部有限有效 |
| `m[12]` | `array<double,12>` | 3×4 行主序矩阵；初始化为单位阵 |

**逐帧变换解析** `parseRuntimeHvaFrameTransformsFromBytes`（`RuntimeMapRenderer.cpp:2461-2542`）：

1. 头长度 / 帧数 / 段数 / 帧号边界校验：`frameCount == 0`、`> 100000`、`sectionCount == 0`、`> 1024`、`frameIndex` 越界均拒绝（`RuntimeMapRenderer.cpp:2477-2484`）；
2. 段名表偏移、矩阵总数、矩阵字节数逐层溢出校验（`RuntimeMapRenderer.cpp:2486-2419`）；
3. 帧偏移 = 段名表后 + `frameIndex * sectionCount * 48`；越出文件拒绝（`RuntimeMapRenderer.cpp:2502-2519`）；
4. 逐段读 12 个 `float32`；任一非有限 → 该变换 `valid = false` 并整体失败（`RuntimeMapRenderer.cpp:2523-2537`）。

**帧选择与缓存**：

- 动画帧选择 `runtimeVoxelAnimationFrame`（`RuntimeVoxelAnimation.inl:1-17`）：
  - 飞行模型未着陆时回第 0 帧；非飞行且非移动且无 `idleRate` 回第 0 帧；
  - 帧数 ≤ 1 回 0；
  - 延迟：非移动用 `idleRate`，否则用 `walkRate`；`step % frames`（`RuntimeVoxelAnimation.inl:10-16`）。
- 帧数来源：`runtimeGeometryVoxelAnimationFrameCount` 取自 HVA 元数据 `hvaFrameCount`，按图像备忘录缓存（`RuntimeGeometryVoxelAnimationMemo.inl:3-18`）。
- 按帧变换缓存：帧 0 存于 `firstFrameTransforms`；其余帧懒加载到 `transformsByFrame[frame]`（`RuntimeVoxelAnimation.inl:19-29`）。

**段变换合成** `runtimeTiltVoxelSection`（`RuntimeVoxelBodyTransform.inl:1-29`）：HVA 段矩阵与世界轴倾斜 / 朝向旋转合成，含枢轴平移回退；具体投影归属绘制章节。

### 4.5 加载预热、源缓存与准入

**源缓存条目** `RuntimeVoxelSourceCacheEntry`（`RuntimeMapRenderer.h:179-188`）：`loadAttempted`、`loaded`、`metadata`、`vxlBytes`、`hvaBytes`、`firstFrameTransforms`、`transformsByFrame`、`errorMessage`。元数据 `RuntimeVoxelMetadata`（`RuntimeMapRenderer.h:157-168`）：图像 ID、VXL/HVA 路径、段数、帧数、文件大小、段名样本。

**冷加载** `loadRuntimeVoxelSourceFromCache`（`RuntimeMapRenderer.cpp:13781-13869`）：

1. 由图像 ID 推 `.vxl` / `.hva` 文件名，按资源根取字节；任一缺失记录错误并保留未加载状态（`RuntimeMapRenderer.cpp:13811-13843`）；
2. 依次解析 VXL 元数据 → HVA 元数据 → 首帧矩阵变换；任一步失败 `loaded = false`（`RuntimeMapRenderer.cpp:13845-13867`）。

**预热限制** `RuntimeVoxelLoadingPrewarmLimits`（`RuntimeVoxelLoadingPrewarm.h:11-26`）：新条目 2048、尝试 4096、负载字节 64 MiB、族数 128、源字节 8 MiB、段网格 1M、总体网格 4M、HVA 帧数 ≤ 16、段数 ≤ 128、墙钟 15000ms。

**准入条件** `runtimeVoxelLoadingPrewarmSourceSupported`（`RuntimeVoxelLoadingPrewarm.inl:13-50`）：

- 必须已加载且 VXL / HVA 字节与首帧变换非空；
- HVA 帧数 1..16；VXL / HVA 段数 1..128；
- VXL 字节 ≥ 802（34 头 + 768 调色板）；
- 尾表 `802 + sections*28 + bodySize` 与 `sections*92` 落在文件内；
- 各段网格 `sizeX*sizeY*sizeZ` 不超段 / 总量上限。

**引导预热** `RuntimeFutureVoxelBootstrap`（`RuntimeFutureVoxelBootstrap.inl:277-339`）：候选图像 ID 非空、≤128 字符、不含 `|`；复用已有源或现场读字节；同样在变换解析前拒绝帧数 / 段数越界源（`RuntimeFutureVoxelBootstrap.inl:326-330`）。两类预热均只在加载期运行，不创建世界对象、不修改世界状态（`RuntimeFutureVoxelBootstrap.h:11-14`、`RuntimeVoxelLoadingPrewarm.h:57-65`）。

### 4.6 Reference / Point 模式边界

渲染剖面与光栅模式枚举（`RuntimeMapRenderer.cpp:3557-3570`）：

| 枚举 | 取值 | 环境变量 | 出处 |
| --- | --- | --- | --- |
| `RuntimeVxlRenderProfile` | `Reference` / `FixedScale` / `Westwood` / `Current` / `Experimental` / `Legacy` | `CONCORD_VXL_RENDER_PROFILE`，缺省 `Reference` | `RuntimeMapRenderer.cpp:3557-3564,3572-3600` |
| `RuntimeVxlReferenceRaster` | `Point` / `Cuboid` / `Coverage` | `CONCORD_VXL_REFERENCE_RASTER`，缺省 `Point` | `RuntimeMapRenderer.cpp:3566-3570,3634-3651` |

- 参考引擎当前默认组合为 **Reference 剖面 + Point 光栅**（契约断言见 `src/render/voxel/diagnostics/RuntimeGpuVxlModeContract.inl:4`）。
- `Reference` / `FixedScale` 属 reference-like；`Reference` / `FixedScale` / `Westwood` 属 simple（`RuntimeMapRenderer.cpp:3621-3632`）。
- 面准备分支存在 reference / 优化覆盖开关 `runtimeVoxelFacePreparationReferenceForDecode`（`src/render/voxel/RuntimeVoxelFacePreparation.inl:5-14`）；投影角点备忘录在非支持维度 / 非整数坐标下回退到原始表达式（`RuntimeVoxelCornerProjectionMemo.h:21-91`）。
- 模式选择、面剔除、投影几何与像素合成的完整链路属于绘制章节，本章不展开。

### 4.7 错误与边界

| 类别 | 行为 | 出处 |
| --- | --- | --- |
| VXL 头过短 | 不足 `34+768` 拒绝 | `RuntimeMapRenderer.cpp:5165-5168` |
| 签名不符 | 非 `"Voxel Animation"` 拒绝 | `RuntimeMapRenderer.cpp:5170-5174` |
| 段数非法 | 0 或 > 1024 拒绝 | `RuntimeMapRenderer.cpp:5184-5187` |
| 段表 / 体段 / 尾表溢出 | `checkedMul` / `checkedAdd` 逐级拒绝 | `RuntimeMapRenderer.cpp:5207-5244` |
| 列偏移负 / 越界 | 拒绝 | `RuntimeMapRenderer.cpp:5623-5639` |
| 跨度不终止 | 迭代计数保护拒绝 | `RuntimeMapRenderer.cpp:5646-5650` |
| 跨度截断 | 列内字节越出体段拒绝 | `RuntimeMapRenderer.cpp:5651-5668` |
| 网格溢出 | 占用网格 `sizeX*sizeY*sizeZ` 溢出拒绝 | `RuntimeMapRenderer.cpp:5577-5590` |
| HVA 头 / 表 / 矩阵越界 | 逐层溢出与边界校验 | `RuntimeMapRenderer.cpp:2371-2419,2472-2519` |
| 矩阵非有限 | 拒绝并报 “non-finite value” | `RuntimeMapRenderer.cpp:2531-2535` |
| 体素 / 面上限 | 常规路径按文件大小预估容量；加农炮捕获路径有 `MaxVoxels / MaxFaces` 硬上限 | `RuntimeMapRenderer.cpp:5256-5259`；`GrandCannonRasterTask.cpp:20-29` |

测试 / 佐证：

- 体素面 / 体素栅格任务夹具：`tests/render/grand_cannon_raster/GrandCannonRasterContract.cpp`；
- VXL/HVA 实机证据契约：`src/render/voxel/diagnostics/RuntimeGpuVxlModeContract.inl:44-50`（运行时把源字节落盘为 `.vxl.bin` / `.hva.bin`，并记录段数 / 帧数 / 文件名清单）；
- HVA 帧变换契约：`src/render/units/diagnostics/RuntimeUnitIdleAnimationContract.inl:19-29`；
- 体素面准备 / 角点投影契约：`src/render/voxel/diagnostics/RuntimeVoxelFacePreparationContract.inl`、`RuntimeVoxelCornerProjectionDecoderContract.inl`。

---

## 本章缺口 / 参考引擎未覆盖项

1. **PAL 无透明索引与 remap 表解码**：解码器不读取任何透明色索引、owner remap 范围或 alpha 约定；透明与 remap 由各消费方在调色板 / VXL 头字节语境下自行处理（`PalDecoder.h:31-32`）。
2. **SHP 格式 4 非停顿像素未解码**：格式 4 仅在停顿帧判定中被识别；非停顿的格式 4 落入 “Unsupported format”，参考引擎未实现其像素编码。
3. **SHP `Reverse` 标志未接入循环**：INI `Reverse` 被解析到 `ArtType`，但辅助动画循环与单位步进路径未发现消费分支。
4. **SHP 缺失帧无自动第 0 帧兜底**：解码失败保留错误与路径，由上层决定空图像或上报；不存在引擎内部静默回退帧。
5. **TMP raw format 3 无地形网格**：兼容格式只承载槽位 / 像素，`gridWidth/gridHeight` 留 0；网格形状查询对该格式返回 false（`RuntimeTmpTypes.h:57-60`、`RuntimeTerrainTmpQuery.cpp:315-341`）。
6. **VXL 段记录 28 字节中的偏移 / 尺寸字段未被运行时解码使用**：段体素定位依赖尾表 `limbDataOffset`；段名以外的段表字段在生产路径中未解析。
7. **VXL 内嵌调色板未按条目展开消费**：运行时体素颜色经外部调色板 / VPL lookup 解析；内嵌 768 字节仅被检查器统计最大分量，未参与运行时着色。
8. **VXL 体素壳 / 面的最终投影、遮挡剔除、AO 与阴影不属于本章范围**：参考引擎将这些耦合在渲染管线中，本章仅标注解码数据结构与接口。
9. **HVA 仅解析 3×4 矩阵元数据与逐帧变换**；未发现骨骼 / 关节层级、缩放键帧插值或非均匀时间轴的解码。
10. **未发现独立的 VXL/HVA 纯编解码模块**：运行时解码与渲染缓存强耦合在 `RuntimeMapRenderer.cpp`；独立可复用的编解码边界需要在 Unity 复刻时自行抽象。

## 关联章节

- [04 · MIX 归档解析与虚拟文件系统](04-MIX归档与虚拟文件系统.md)：本章字节源（松散文件 / MIX 按名读取）的上游来源。
- [05b · 文字与视频解码（CSF / FNT / Bink）](05b-文字与视频解码(CSF-FNT-Bink).md)：同属资源解码章节，覆盖文字表、位图字体与影片流。
