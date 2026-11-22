# 04 · MIX 归档解析与虚拟文件系统（VFS）

## 本章覆盖范围

- 参考引擎对 RA2/YR 风格 MIX 归档的头部解析、明文目录 / 加密目录、校验和块、目录条目结构与条目读取流程。
- 文件名 CRC32 标识的生成规则、扩展名候选尝试顺序。
- 运行时资源会话中的 MIX 扫描、缓存策略、松散文件（loose files）兜底、多资源根优先级与覆盖顺序。
- 加密 / 压缩在参考引擎中的真实实现边界：哪些由引擎完成，哪些未实现或依赖外部素材。

## 对应源码目录

生产源码（`src/` 下 `.h/.cpp/.inc`）：

| 文件 | 职责 |
| --- | --- |
| `src/assets/archive/MixArchive.h` | MIX 条目结构 `MixEntry`、`MixArchive` 对外接口、文件名 ID 工具声明 |
| `src/assets/archive/MixArchive.cpp` | MIX 头部 / 目录 / 条目解析、CRC32、条目读取与落盘导出 |
| `src/assets/archive/WestwoodCrypto.h` | 加密目录解密接入点声明（命名空间 `westwood`） |
| `src/assets/archive/WestwoodCrypto.cpp` | RSA（公钥指数 65537）解出 Blowfish 密钥、Blowfish 分组解密 |
| `src/assets/archive/WestwoodBlowfishConstants.inc` | Blowfish P 数组与 S 盒初始常量 |

VFS 组装与消费方（非 `archive/` 目录，但属于本章链路）：

| 文件 | 职责 |
| --- | --- |
| `src/render/core/detail/RuntimeProductionResources.h` | 地形 / 渲染侧字节源适配器 `RuntimeTerrainTmpResourceByteSource`、VFS 工具函数声明 |
| `src/render/core/RuntimeProductionResources.cpp` | MIX 扫描、MIX 缓存、松散文件索引、统一按名取字节、优先级排序 |
| `src/render/core/RuntimeMapRenderer.h` | `RuntimeMixArchiveCacheEntry`、`RuntimeMapRenderCache` 缓存字段、`RuntimeMapRenderContext` 资源根字段 |
| `src/assets/terrain/RuntimeTerrainTmpQuery.h` | 地形瓦片字节源抽象 `RuntimeTerrainTmpByteSource`、`RuntimeTerrainTmpContext` |
| `src/assets/audio/AudioSampleResolver.cpp` | 音频侧 MIX / 松散文件 / `audio.idx`+`audio.bag` 三级解析与独立 MIX 缓存 |
| `src/assets/preview/MapPreviewExporter.cpp` | 预览导出器的 MIX 索引构建、按名 / 按 CRC 探测、嵌套 MIX 跳过策略 |
| `src/assets/preview/MapPreviewResourceContext.h` / `.cpp` | 预览资源会话 `MapPreviewResourceSession` 生命周期与松散字节缓存 |
| `src/assets/inspection/AssetInspector.cpp` | 资产检查器的 MIX 索引构建与条目解码缓存 |
| `src/assets/inspection/GameResourceMount.cpp` | 挂载层扫描报告（仅统计 / 诊断，不参与运行时字节解析） |
| `src/analysis/cli/main.cpp` | 命令行工具的 MIX 加载、条目导出 |

> 说明：参考引擎中不存在一个独立命名为 “VFS” 的全局类。运行时按名取字节的能力由 `RuntimeProductionResources.cpp` 中的解析链路（松散索引 + MIX 缓存 + 优先级排序）与各消费方自带的字节源适配器共同构成；`archive/` 目录只负责单个 MIX 文件的解析。

---

## 1. MIX 文件头部

### 1.1 标志位与版本判定

头部首 4 字节为 `flags`（小端 `uint32`）。参考引擎定义的已知标志位（`src/assets/archive/MixArchive.cpp:13-16`）：

| 常量 | 值 | 含义 | 出处 |
| --- | --- | --- | --- |
| `MixFlagChecksum` | `0x00010000` | 数据区末尾存在 20 字节校验和块 | `MixArchive.cpp:13` |
| `MixFlagEncryptedIndex` | `0x00020000` | 目录（索引）经 Blowfish 加密 | `MixArchive.cpp:14` |
| `ChecksumSize` | `20`（字节） | 校验和块固定长度 | `MixArchive.cpp:15` |
| `EntrySize` | `12`（字节） | 单条目录条目固定长度 | `MixArchive.cpp:16` |

加载入口为 `MixArchive::loadFromPath(const std::filesystem::path&)`（`MixArchive.cpp:186`），`loadFromFile(const std::string&)` 仅做路径转发（`MixArchive.cpp:182-184`）。

**版本 / 头型判定步骤**（`MixArchive.cpp:214-250`）：

1. 打开文件并读取文件大小 `fileSize_`（`MixArchive.cpp:200-212`）。
2. 读取首 4 字节 `flags_`（`MixArchive.cpp:214-217`）。
3. 计算 `knownFlags = MixFlagChecksum | MixFlagEncryptedIndex`（`MixArchive.cpp:219`）；若 `flags_ & ~knownFlags != 0`，判定为**旧式无标志头（legacy unflagged header）**（`MixArchive.cpp:220`）。
4. 旧式头分支（`MixArchive.cpp:222-239`）：
   - `fileCount_ = flags_ & 0xFFFF`（低 16 位，`MixArchive.cpp:223-224`）；
   - `dataSizeLow = (flags_ >> 16) & 0xFFFF`（`MixArchive.cpp:225-226`）；
   - 再读 2 字节 `dataSizeHigh`（`MixArchive.cpp:228`）；
   - `dataSize_ = dataSizeLow | (dataSizeHigh << 16)`（`MixArchive.cpp:235-237`）；
   - 清零 `flags_`，`hasChecksum_ = false`、`hasEncryptedIndex_ = false`（`MixArchive.cpp:233-239`）。
5. 标志头分支（`MixArchive.cpp:240-243`）：`hasChecksum_`、`hasEncryptedIndex_` 分别由对应标志位得出。
6. 若标志头仍含未识别高位，直接报错：“Unsupported MIX header flags. This reader currently supports RA2/YR-style flagged MIX files only.”（`MixArchive.cpp:245-250`）。

### 1.2 明文标志头布局

未加密目录、无校验和以外的标志组合时，头部为小端序：

| 偏移 | 长度 | 字段 | 类型 / 说明 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | `flags` | `uint32`，位组合见 1.1；旧式头此 4 字节被复用为 `fileCount` 低 16 位 + `dataSize` 低 16 位 | `MixArchive.cpp:214, 223-226` |
| 4 | 2 | `fileCount` | `uint16`，目录条目数（仅标志头） | `MixArchive.cpp:364-367` |
| 6 | 4 | `dataSize` | `uint32`，数据区总字节数（仅标志头） | `MixArchive.cpp:369-372` |
| 10 | `fileCount × 12` | 目录条目数组 | 明文条目，见 1.5 | `MixArchive.cpp:375-379, 423-430` |
| 10 + 目录长 | `dataSize` | 数据区 | 条目原始字节，按条目偏移 / 大小定位 | `MixArchive.cpp:381-389, 394-399` |
| 数据区末 | 20 | 校验和块 | 仅当 `hasChecksum_` 置位时存在；参考引擎不解析其内容 | `MixArchive.cpp:392, 401-406` |

旧式头布局（`MixArchive.cpp:362`，`headerSize = 2 + 4`）：

| 偏移 | 长度 | 字段 | 出处 |
| --- | --- | --- | --- |
| 0 | 2 | `fileCount`（低 16 位） | `MixArchive.cpp:223-224` |
| 2 | 4 | `dataSize`（低 16 位在首字高 16 位，高 16 位随后 2 字节） | `MixArchive.cpp:225-237` |
| 6 | `fileCount × 12` | 目录条目数组 | `MixArchive.cpp:376` |

### 1.3 加密目录布局

当 `hasEncryptedIndex_` 置位（`MixArchive.cpp:255`），头部顺序为：

| 偏移 | 长度 | 字段 | 说明 | 出处 |
| --- | --- | --- | --- | --- |
| 0 | 4 | `flags` | 含 `MixFlagEncryptedIndex` | `MixArchive.cpp:214` |
| 4 | 80 | 密钥块 `keyBlock` | 两段各 40 字节 RSA 密文，用于解出 56 字节 Blowfish 密钥 | `MixArchive.cpp:257, 260-264`；`WestwoodCrypto.cpp:196-204` |
| 84 | 8（首块） | 首加密分组 | 先单独解密以取出条目数 | `MixArchive.cpp:258, 285-289` |
| 84 | 目录密文总长（8 的倍数） | 加密目录分组 | 明文 = 2 字节条目数 + 4 字节数据大小 + 每条目 12 字节；按 8 字节分组填充 | `MixArchive.cpp:314-323, 338-342` |
| 目录密文末 | `dataSize` | 数据区 | `dataStartOffset_ = encryptedIndexEnd` | `MixArchive.cpp:360` |

加密目录解析步骤（`MixArchive.cpp:255-360`）：

1. 读 80 字节 `keyBlock`（`MixArchive.cpp:260-264`）；`encryptedIndexOffset = 4 + 80`（`MixArchive.cpp:266-270`）。
2. 先定位并读取首 8 字节加密分组（`MixArchive.cpp:272-289`），调用 `westwood::decryptMixIndexBlocks` 解密（`MixArchive.cpp:292-299`）。
3. 解密结果不足 6 字节报错（`MixArchive.cpp:301-304`）；`fileCount_` 取自解密缓冲偏移 0 的 `uint16`（`MixArchive.cpp:306`）。
4. 计算 `indexPayloadBytes = fileCount_ × 12`（`MixArchive.cpp:308-312`）；`plaintextHeaderBytes = 6 + indexPayloadBytes`（2 字节计数 + 4 字节数据大小，`MixArchive.cpp:314-320`）。
5. 密文长度按 8 字节分组向上取整：`encryptedIndexBytes = ceil(plaintextHeaderBytes / 8) × 8`（`MixArchive.cpp:322-323`）；越界检查（`MixArchive.cpp:325-330`）。
6. 重读整段加密目录（`MixArchive.cpp:332-342`），整体解密到 `decryptedIndex`（`MixArchive.cpp:344-351`）；长度不足报错（`MixArchive.cpp:353-356`）。
7. `dataSize_` 取自解密缓冲偏移 4 的 `uint32`（`MixArchive.cpp:358`）；`entryOffset = 6`（`MixArchive.cpp:359`）；`dataStartOffset_ = encryptedIndexEnd`（`MixArchive.cpp:360`）。

解密后的目录明文头部布局：

| 偏移 | 长度 | 字段 | 出处 |
| --- | --- | --- | --- |
| 0 | 2 | `fileCount`（`uint16`） | `MixArchive.cpp:306` |
| 2 | 4 | `dataSize`（`uint32`） | `MixArchive.cpp:358` |
| 6 | `fileCount × 12` | 目录条目数组（明文） | `MixArchive.cpp:359, 413-422` |

### 1.4 校验和（Checksum）

- 标志位 `MixFlagChecksum = 0x00010000`（`MixArchive.cpp:13`），块长固定 20 字节（`MixArchive.cpp:15`）。
- 参考引擎仅在加载时做边界核算：`dataAreaEnd = dataStartOffset_ + dataSize_`；若置位校验和，再要求 `dataAreaEnd + 20 <= fileSize_`（`MixArchive.cpp:392-406`）。
- **未实现**：校验和块内容的读取、算法识别与完整性校验在 `archive/` 目录中不存在；20 字节块被整体跳过，不参与条目定位。

### 1.5 目录条目结构

条目固定 12 字节，小端序（`EntrySize = 12`，`MixArchive.cpp:16`）。内存结构 `MixEntry`（`src/assets/archive/MixArchive.h:8-13`）：

| 字段 | 类型 | 含义 | 出处 |
| --- | --- | --- | --- |
| `id` | `std::uint32_t` | 文件名 CRC32 标识（见第 2 节） | `MixArchive.h:9` |
| `offset` | `std::uint32_t` | 条目数据相对数据区起点的偏移 | `MixArchive.h:10` |
| `size` | `std::uint32_t` | 条目数据字节数 | `MixArchive.h:11` |
| `absoluteOffset` | `std::uint64_t` | 运行时算出的文件内绝对偏移 = `dataStartOffset_ + offset` | `MixArchive.h:12`；`MixArchive.cpp:432-435` |

磁盘条目字节布局：

| 偏移 | 长度 | 字段 | 出处 |
| --- | --- | --- | --- |
| 0 | 4 | `id`（`uint32`，小端） | `MixArchive.cpp:419, 424` |
| 4 | 4 | `offset`（`uint32`，小端） | `MixArchive.cpp:420, 425` |
| 8 | 4 | `size`（`uint32`，小端） | `MixArchive.cpp:421, 426` |

条目解析与边界校验步骤（`MixArchive.cpp:410-451`）：

1. 加密目录：从 `decryptedIndex` 按 `entryOffset` 读 `id / offset / size`，随后 `entryOffset += 12`（`MixArchive.cpp:413-422`）。
2. 明文目录：直接从文件流顺序读三个 `uint32`（`MixArchive.cpp:423-430`）。
3. `absoluteOffset = dataStartOffset_ + offset`，加法溢出检查（`MixArchive.cpp:432-435`）。
4. 双重越界校验：绝对偏移 + 大小不得超过文件大小（`MixArchive.cpp:437-441`）；相对偏移 + 大小不得超过声明的 `dataSize_`（`MixArchive.cpp:443-448`）。
5. 条目压入 `entries_`（`MixArchive.cpp:450`）；全部读完后置 `indexReadable_ = true`（`MixArchive.cpp:453`）。

### 1.6 Blowfish 加密目录的密钥 / 算法接入

接入点：`westwood::decryptMixIndexBlocks(keyBlock, encryptedBlocks, decryptedBlocks, errorMessage)`（`src/assets/archive/WestwoodCrypto.h:9-14`；实现 `WestwoodCrypto.cpp:358-380`）。

**密钥解包（RSA，公钥指数 65537）**，`decryptMixBlowfishKey`（`WestwoodCrypto.cpp:191-224`）：

| 步骤 | 说明 | 出处 |
| --- | --- | --- |
| 入参校验 | `keyBlock` 必须恰好 80 字节（2 × 40），否则报错 | `WestwoodCrypto.cpp:196-204` |
| 模数 | 40 字节大端公钥模数 `kWestwoodPublicModulusBe` | `WestwoodCrypto.cpp:12-18, 206-209` |
| 大整数表示 | 16 个 `uint32` 小端 limb 的 `BigInt` | `WestwoodCrypto.cpp:20-24, 26-51` |
| 分段 | 两段各 40 字节，按小端解析为密文 | `WestwoodCrypto.cpp:213-217` |
| 模幂 | `powMod65537(encrypted, modulus)`，指数固定 `65537` | `WestwoodCrypto.cpp:218, 160-177` |
| 输出 | 每段追加 39 字节小端明文，共 78 字节，截取前 **56 字节**作为 Blowfish 密钥 | `WestwoodCrypto.cpp:219-223` |

**Blowfish 分组解密**，`BlowfishCipher`（`WestwoodCrypto.cpp:250-352`）：

| 环节 | 说明 | 出处 |
| --- | --- | --- |
| 初始常量 | P 数组 18 个 `uint32`、S 盒 1024 个 `uint32`，来自 `WestwoodBlowfishConstants.inc` | `WestwoodCrypto.cpp:253-254`；`WestwoodBlowfishConstants.inc:1, 7` |
| 密钥编排 | 密钥循环字节填充 P 数组，再用全零分组加密覆写 P 与 S | `WestwoodCrypto.cpp:255-276` |
| 分组解密 | 每 8 字节一组；`readLe32` + `byteSwap` → `decryptPair` → `byteSwap` + `writeLe32` 写回 | `WestwoodCrypto.cpp:279-291` |
| 解密轮 | 16 轮 Feistel，交替左右半；逆序使用 P 数组 | `WestwoodCrypto.cpp:331-351` |
| 长度校验 | 密文长度必须为 8 的倍数 | `WestwoodCrypto.cpp:367-370` |

> 注意：参考引擎仅解密**目录（索引）**；条目数据区本身不经过该解密流程。

---

## 2. CRC32：文件名到条目标识

### 2.1 文件名规范化

`normalizeMixFileName`（`MixArchive.cpp:128-150`）：

1. 去除首尾空白（空格 / `\t` / `\r` / `\n`，`MixArchive.cpp:18-28, 129`）。
2. 若首尾成对为 `"…"` 或 `'…'`，剥去引号后再次去空白（`MixArchive.cpp:131-136`）。
3. 反复剥去尾部 `/`、`\`（`MixArchive.cpp:138-142`）。
4. 取最后一个 `/` 或 `\` 之后的纯文件名（剥离目录分量，`MixArchive.cpp:144-147`）。

`MixArchive.h:69-70` 注释明确：逻辑 MIX 名保留原始字节规则——修剪 / 去引号 / 取基名、仅 ASCII 大写与 CRC 填充，不做文件系统或 Unicode 折叠。

### 2.2 大写、填充与算法

`mixFileIdFromName(const std::string&)`（`MixArchive.cpp:680-706`）：

| 步骤 | 规则 | 出处 |
| --- | --- | --- |
| 规范化 | 先过 `normalizeMixFileName` | `MixArchive.cpp:681` |
| 大写 | 仅 ASCII `a-z` → `A-Z`，非 ASCII 字符保持原样 | `MixArchive.cpp:120-126, 685-687` |
| 4 字节分组填充 | 若长度不是 4 的倍数：追加剩余字节数（`originalRemainder`）作为填充首字节，再用该余数组的首字节补齐到 4 字节 | `MixArchive.cpp:689-698` |
| CRC 初值 | `0xFFFFFFFF` | `MixArchive.cpp:700` |
| 逐字节更新 | `updateCrc32`，多项式 `0xEDB88320`（CRC-32/ISO-HDLC 反向多项式） | `MixArchive.cpp:166-178, 701-703` |
| 输出 | 取反 `~crc` | `MixArchive.cpp:705` |

补充事实：

- 参考引擎**未实现**文件名长度截断（如按 12 字符截断后再算 CRC）；`mixFileIdFromName` 直接对规范化后的全名字节计算。
- 无扩展名判断工具 `hasFileExtension`（`MixArchive.cpp:152-156`）：规范化后存在 `.` 且其后至少 1 个字符。
- ID 格式化工具 `formatMixId` 输出大写 8 位十六进制（`MixArchive.cpp:708-717`）。

### 2.3 扩展名候选尝试

`findEntryByName`（`MixArchive.cpp:519-564`）：

1. 规范化文件名；空名直接返回 `nullptr`（`MixArchive.cpp:520-523`）。
2. 先用原名算 CRC 并 `findEntryById`（`MixArchive.cpp:525`）。
3. 若原名已带扩展名：查到即返回，查不到也不再尝试候选扩展名（`MixArchive.cpp:526-528`）。
4. 原名无扩展名时，按下表顺序逐个拼接扩展名再算 CRC 查询（`MixArchive.cpp:530-561`）：

| 顺序 | 扩展名 | 顺序 | 扩展名 |
| --- | --- | --- | --- |
| 1 | `.shp` | 13 | `.des` |
| 2 | `.vxl` | 14 | `.lun` |
| 3 | `.hva` | 15 | `.pcx` |
| 4 | `.tmp` | 16 | `.fnt` |
| 5 | `.pal` | 17 | `.csf` |
| 6 | `.ini` | 18 | `.wav` |
| 7 | `.map` | 19 | `.aud` |
| 8 | `.mpr` |  |  |
| 9 | `.mmx` |  |  |
| 10 | `.yrm` |  |  |
| 11 | `.yro` |  |  |
| 12 | `.yrx` |  |  |

另含 `.tem`、`.sno`、`.urb`、`.ubn`（`MixArchive.cpp:543-547`）。

按 ID 查找 `findEntryById`（`MixArchive.cpp:501-517`）：线性扫描；首个匹配先记录，遇到 `size > 0` 的匹配立即返回；若全部同名条目大小为 0，返回首个匹配。

---

## 3. 条目读取、缓存与嵌套 MIX

### 3.1 单条目读取

`MixArchive::readEntryData(entry, data, errorMessage)`（`MixArchive.cpp:566-621`）：

1. 前置条件：`indexReadable_` 必须为真（`MixArchive.cpp:574-577`）；条目大小不得超过当前平台 `streamsize` 上限（`MixArchive.cpp:579-582`）。
2. 再次核算边界：绝对偏移 + 大小落在数据区内且不超文件大小（`MixArchive.cpp:584-593`）。
3. **每次读取重新打开 MIX 文件流**（`MixArchive.cpp:595-599`），按 `absoluteOffset` 定位（`MixArchive.cpp:601-605`），读 `size` 字节（`MixArchive.cpp:607-618`）。
4. **条目字节为原始读取，`archive/` 层不做任何解压 / 解密**；压缩或格式解码由消费方 codec 完成。

落盘导出接口：`extractEntryToFile`（先读字节再建目录写文件，`MixArchive.cpp:623-664`）；`extractFileByName`（按名查条目后导出，`MixArchive.cpp:666-678`）。

### 3.2 运行时 MIX 缓存（渲染 / 地形链路）

缓存条目结构 `RuntimeMixArchiveCacheEntry`（`src/render/core/RuntimeMapRenderer.h:406-411`）：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `loadAttempted` | `bool` | 是否已尝试加载（失败也标记，避免重复打开） |
| `loaded` | `bool` | 索引是否可读 |
| `archive` | `MixArchive` | 归档实例 |
| `errorMessage` | `std::string` | 加载失败原因 |

缓存与索引状态字段位于 `RuntimeMapRenderCache`（`RuntimeMapRenderer.h:652-659`）：

| 字段 | 含义 | 出处 |
| --- | --- | --- |
| `mixArchiveRootPath` | 已扫描的资产根路径 | `RuntimeMapRenderer.h:652` |
| `mixArchivePathsScanned` | 目录扫描是否完成 | `RuntimeMapRenderer.h:653` |
| `mixArchivePaths` | 已排序的候选 MIX 路径列表 | `RuntimeMapRenderer.h:654` |
| `mixArchivesByPath` | 按路径缓存的 `RuntimeMixArchiveCacheEntry` | `RuntimeMapRenderer.h:655-656` |
| `looseResourceIndexRootPath` / `looseResourceIndexBuilt` / `looseResourcePathsByName` | 松散文件索引及其命中映射 | `RuntimeMapRenderer.h:657-659` |

加载函数 `loadRuntimeMixArchiveFromCache`（`RuntimeProductionResources.cpp:818-834`）：路径为键 `try_emplace`；首次尝试时 `loadFromFile` 且要求 `isIndexReadable()`；失败写入 `errorMessage`，不缓存成功态。

### 3.3 嵌套 MIX：参考引擎当前状态

- `MixArchive` 只提供从**文件系统路径**加载的接口（`loadFromFile` / `loadFromPath`，`MixArchive.h:19-23`）；**没有**从内存字节流（条目内的嵌套 `.mix`）二次构造 `MixArchive` 的路径。
- 预览导出器在诊断解码时，扩展名 `.mix` 的条目直接标注 `decode=skip nested-mix size=…`，不展开（`src/assets/preview/MapPreviewExporter.cpp:3030-3034`）。
- 音频侧注释描述了原版 `ra2.mix -> local.mix -> theme.ini`、`ra2md.mix -> localmd.mix -> thememd.ini` 的嵌套链（`src/assets/audio/AudioSampleResolver.cpp:1200-1205, 1253, 1278`）；但当主题 INI 仍处于嵌套 MIX 中时，运行时使用**硬编码的 13 首 RA2 / 7 首 YR + 4 个前端阶段**曲目清单作为直装兜底（`AudioSampleResolver.cpp:1206-1284`），并不递归打开嵌套 MIX。
- 结论：**参考引擎未实现嵌套 MIX 的递归层级解析与嵌套查找顺序**；运行时准备好的资产根通常已把嵌套内容导出为松散文件（`resources/mix_exports/`）。

---

## 4. 松散文件兜底与多资源根优先级

### 4.1 松散文件索引（渲染 / 地形链路）

松散文件候选根 `runtimeLooseObjectSearchRoots`（`RuntimeProductionResources.cpp:574-586`）：

| 顺序 | 根路径（相对 `assetRoot`） |
| --- | --- |
| 1 | `assetRoot` |
| 2 | `ra2md/localmd` |
| 3 | `expandmd02` |
| 4 | `expandmd01` |
| 5 | `cameomd` |
| 6 | `langmd` |
| 7 | `language` |
| 8 | `extracted_overlays` |

纳入松散索引的扩展名 `isRuntimeObjectLooseExtension`（`RuntimeProductionResources.cpp:530-541`）：`.shp`、`.vxl`、`.hva`、`.tem`、`.urb`、`.ubn`、`.des`、`.sno`、`.lun`。

索引构建 `ensureRuntimeLooseResourceIndex`（`RuntimeProductionResources.cpp:588-637`）：根路径变化时清空重建；逐根递归收集候选文件；若整根为空（自定义资产根未按 RA2/YR 目录布局导出），退回对根目录自身递归扫描（`RuntimeProductionResources.cpp:614-617`）；按松散优先级排序后，以小写文件名为键 `try_emplace` 入表（同名先到先得，`RuntimeProductionResources.cpp:619-636`）。

松散优先级 `runtimeLooseResourceSearchPriority`（数值越小越优先，`RuntimeProductionResources.cpp:513-528`）：

| 路径特征 | 优先级 |
| --- | --- |
| 含 `/extracted_buildings/` | 0 |
| 含 `/ra2md/localmd/` | 10 |
| 含 `/ra2md/` | 20 |
| 含 `/ra2/` | 30 |
| 其他 | 100 |

按名查找 `findRuntimeLooseResourceByName`（`RuntimeProductionResources.cpp:639-662`）：根路径等于默认资产根时走索引表；否则对指定根做大小写不敏感的递归文件名匹配 `findFileByNameCaseInsensitive`（`RuntimeProductionResources.cpp:220-264`）。

### 4.2 MIX 候选扫描与排序

候选判定 `isRuntimeObjectResourceMixCandidate`（`RuntimeProductionResources.cpp:458-511`）：文件名以 `iso` 开头的 `.mix`，或命中内置名单（`cachemd.mix`、`conqmd.mix`、`localmd.mix`、`loadmd.mix`、`ra2md.mix`、`expandmd01/02.mix`、`cameomd.mix`、`genermd.mix`、`des/desert.mix`、`lun/lunar.mix`、`snowmd.mix`、`ubn/urbann.mix` 及对应 RA2 `cache.mix`、`conquer.mix`、`local.mix`、`load.mix`、`neutral.mix`、`ra2.mix`、`generic.mix`、`tem/temperat.mix`、`urb/urban.mix`、`sno/snow.mix`、`sidec01/02.mix`、`sidenc01/02.mix`、`cameo.mix` 等）。

扫描 `ensureRuntimeMixArchivePaths`（`RuntimeProductionResources.cpp:664-816`）：

1. 优先按内置清单在 `ra2md/`、`ra2/`、`langmd`、`language`、`expandmd02/01.mix`、`ra2md.mix`、`expand01.mix`、`ra2.mix` 等固定位置探测存在性（`RuntimeProductionResources.cpp:703-768`）。
2. 内置清单为空时，才对资产根递归扫描所有 `.mix` 并过候选判定（`RuntimeProductionResources.cpp:786-802`）。
3. 按 MIX 基础优先级排序（`RuntimeProductionResources.cpp:770-782`）。

基础 MIX 优先级 `runtimeMixSearchPriority`（数值越小越优先，`RuntimeProductionResources.cpp:324-362`）：

| MIX / 路径特征 | 优先级 |
| --- | --- |
| `expandmd02.mix` | -30 |
| `expandmd01.mix` | -20 |
| `ra2md.mix` | -10 |
| 含 `/ra2md/localmd.mix` | 0 |
| 含 `/ra2md/conqmd.mix` | 10 |
| 其他 `/ra2md/` 路径 | 20 |
| 含 `/ra2/local.mix` | 30 |
| 含 `/ra2/conquer.mix` | 40 |
| `expand01.mix` | 45 |
| `ra2.mix` | 46 |
| 其他 `/ra2/` 路径 | 50 |
| 其他 | 100 |

按资源名再调权 `runtimeMixSearchPriorityForFile`（`RuntimeProductionResources.cpp:392-456`）：

- 加载屏资源（`ls800*.shp`、`mplsu/mplsuk/mplsg/mplsf/mplsk/mplsr/mplsc/mplsl/mplsi/mpyls/mplsobs.pal`）：`loadmd.mix` 为 -70，`load.mix` 为 -65（`RuntimeProductionResources.cpp:400-421`）。
- 侧边栏资源（`credits.shp`、`top.shp`、`side1/2/2b/3.shp`、`addon.shp`、`tabs.shp`、`tab00-03.shp`、`diplobtn.shp`、`optbtn.shp`、`repair.shp`、`sell.shp`、`r-dn/r-up.shp`、`gclock2.shp`、`power/powerp.shp`、`radar.shp`、`pips.shp`、`sidebar.pal`，名单见 `RuntimeProductionResources.cpp:364-390`）：`sidec02md.mix` 为 -60，`sidec01/02.mix` 为 -55，`conqmd/conquer.mix` 为 -20（`RuntimeProductionResources.cpp:423-435`）。
- `.shp/.sha/.vxl/.hva` 资源：`conqmd.mix` 为 -50，`conquer.mix` 为 -45，`cachemd/cache.mix` 为 -40，`iso*` 开头 MIX 为 -30（`RuntimeProductionResources.cpp:437-453`）。

### 4.3 统一解析顺序（渲染 / 地形链路）

入口 `loadRuntimeResourceBytesByName(looseRootPaths, assetRootPath, fileName, cache, bytes, sourceName, errorMessage)`（`RuntimeProductionResources.cpp:899-962`）：

1. **显式非默认松散根优先**：遍历调用方传入的 `looseRootPaths`，落在默认资产根之外的根先尝试 `findRuntimeLooseResourceByName`，命中即返回（`RuntimeProductionResources.cpp:927-933`）。
2. **MIX 归档优先于默认导出松散副本**：调用 `loadRuntimeResourceBytesFromMixArchives` 在所有候选 MIX 中查找（`RuntimeProductionResources.cpp:936-945`）。
3. 若资产根下存在 MIX 归档（`runtimeAssetRootHasMixArchives`，`RuntimeProductionResources.cpp:892-897`），MIX 未命中即返回失败，**不再回退默认松散文件**（`RuntimeProductionResources.cpp:948-950`）。
4. 无 MIX 归档的根视为独立松散 / 自定义安装：再遍历默认松散根兜底（`RuntimeProductionResources.cpp:952-959`）。

MIX 内查找 `loadRuntimeResourceBytesFromMixArchives`（`RuntimeProductionResources.cpp:836-890`）：

1. 确保 MIX 路径已扫描（`RuntimeProductionResources.cpp:843`）。
2. 按当前资源名对路径列表做 `stable_sort`，权重取 `runtimeMixSearchPriorityForFile`（`RuntimeProductionResources.cpp:846-859`）。
3. 逐归档取缓存实例；`findEntryByName` 命中后 `readEntryData` 读字节；`sourceName = mixPath + "!" + fileName`（`RuntimeProductionResources.cpp:861-884`）。
4. 全部未命中报错：“resource was not found in loose files or MIX archives: …”（`RuntimeProductionResources.cpp:887-889`）。

### 4.4 `mix_exports` 松散根定位

`runtimeMixExportsRootFromPath`（`RuntimeProductionResources.cpp:271-293`）：从给定路径（文件则取父目录）逐级向上查找目录名 `mix_exports`。

上下文级定位 `runtimeMixExportsRootForContext`（`RuntimeProductionResources.cpp:295-306`），顺序：`tileRootPath` → `structureRootPath` → `assetRootPath`。带缓存的版本 `cachedRuntimeMixExportsRootForContext`（`RuntimeProductionResources.cpp:308-322`），上下文根路径变化时重算。

运行时配置侧的标准导出目录（只读参考）：

| 用途 | 路径（相对 `localDataRootPath`） | 出处 |
| --- | --- | --- |
| 默认资产根 | `resources/mix_exports` | `RuntimeSessionNavigation.cpp:75-85` |
| 建筑松散根（按地图名） | `resources/mix_exports/extracted_buildings/<mapStem>` | `RuntimeSessionNavigation.cpp:87-107` |
| 地形瓦片松散根（按地图名） | `resources/mix_exports/extracted_tiles/<mapStem>` | `RuntimeSessionNavigation.cpp:30-37` |
| 调色板 | `resources/mix_exports/palettes`、`extracted_palettes` | `RuntimeSessionNavigation.cpp:66-72, 120-123` |
| CLI 默认资产根 | `<rules 父目录>/resources/mix_exports` | `analysis/cli/main.cpp:1749-1759` |

### 4.5 音频链路的解析顺序（独立实现）

音频解析器自带一套 MIX 扫描与缓存，不复用渲染链路缓存（`src/assets/audio/AudioSampleResolver.cpp`）：

1. **松散 WAV/AUD 优先**：`resolveLooseAudioFile`（`AudioSampleResolver.cpp:861-`）；松散根 `looseAudioRoots` 按 `ra2` / `yr` / 未区分三套给出（`AudioSampleResolver.cpp:824-859`）；候选文件名 `looseAudioFileCandidates` 生成原名、`.wav`、`.aud` 及去装饰后的同名变体（`AudioSampleResolver.cpp:793-822`）。
2. **MIX 内松散条目**：`resolveMixLooseAudioFile`（`AudioSampleResolver.cpp:1967-1975` 调用）；MIX 候选路径 `audioMixPathCandidates` 覆盖 `langmd/audiomd.mix`、`ra2md/localmd/audiomd.mix`、`expandmd01/audiomd.mix`、`language/audio.mix`、根目录、`ra2md/audiomd.mix`、`ra2/audio.mix` 等（`AudioSampleResolver.cpp:384-397`）；按 `sourceId`（`ra2` / `yr`）过滤（`AudioSampleResolver.cpp:399-413`）。
3. **`audio.idx` + `audio.bag` 库**：前两级未命中后遍历 `loadAudioBagLibraries` 结果按名查条目（`AudioSampleResolver.cpp:1985-1989` 及后续）。
4. MIX 缓存 `loadCachedAudioMixArchive`：进程内 `static unordered_map<string, shared_ptr<MixArchive>>`，失败也缓存空值（`AudioSampleResolver.cpp:443-460`）。
5. MIX 内读取 `readMixFileData`：`findEntryByName` → `readEntryData`（`AudioSampleResolver.cpp:429-441`）。

---

## 5. VFS 对外接口与消费关系

### 5.1 `MixArchive` 直接接口

声明见 `src/assets/archive/MixArchive.h:15-52`：

| 接口 | 签名 / 含义 | 出处 |
| --- | --- | --- |
| `loadFromFile` | 按原生窄编码路径加载 MIX | `MixArchive.h:19`；`MixArchive.cpp:182` |
| `loadFromPath` | 按 `std::filesystem::path` 加载；`getPath()` 仅为 UTF-8 诊断标签，不用于重开归档 | `MixArchive.h:21-23`；`MixArchive.cpp:186` |
| `getPath / getFlags / getFileCount / getDataSize / getFileSize / getDataStartOffset` | 元数据只读访问器 | `MixArchive.h:25-30`；`MixArchive.cpp:457-479` |
| `hasChecksum / hasEncryptedIndex / isIndexReadable` | 头标志与索引可读性 | `MixArchive.h:31-33`；`MixArchive.cpp:481-491` |
| `getErrorMessage` | 最近一次错误描述 | `MixArchive.h:34`；`MixArchive.cpp:493-495` |
| `getEntries` | 全条目列表 | `MixArchive.h:35`；`MixArchive.cpp:497-499` |
| `findEntryById` | 按 CRC ID 查条目 | `MixArchive.h:36`；`MixArchive.cpp:501-517` |
| `findEntryByName` | 按文件名（含扩展名候选）查条目 | `MixArchive.h:37`；`MixArchive.cpp:519-564` |
| `readEntryData` | 读条目原始字节 | `MixArchive.h:38-42`；`MixArchive.cpp:566-621` |
| `extractEntryToFile` / `extractFileByName` | 条目落盘导出 | `MixArchive.h:43-52`；`MixArchive.cpp:623-678` |

工具函数：`mixFileIdFromName`（`MixArchive.h:71`；`MixArchive.cpp:680-706`）、`formatMixId`（`MixArchive.h:72`；`MixArchive.cpp:708-717`）。

### 5.2 地形 / 渲染字节源抽象

`RuntimeTerrainTmpByteSource`（`src/assets/terrain/RuntimeTerrainTmpQuery.h:38-56`）：

| 方法 | 含义 | 出处 |
| --- | --- | --- |
| `readLooseBytes(path, bytes, errorMessage)` | 直接读松散文件字节 | `RuntimeTerrainTmpQuery.h:41-45` |
| `readResourceBytesByName(rootPaths, assetRootPath, fileName, bytes, sourceName, errorMessage)` | 按名走 VFS（松散 + MIX） | `RuntimeTerrainTmpQuery.h:46-53` |
| `cachedMixExportsRoot()` | 已缓存的 `mix_exports` 根 | `RuntimeTerrainTmpQuery.h:54` |
| `assetRootHasMixArchives(assetRootPath)` | 资产根是否存在 MIX 归档 | `RuntimeTerrainTmpQuery.h:55` |

适配器 `RuntimeTerrainTmpResourceByteSource`（`src/render/core/detail/RuntimeProductionResources.h:190-223`）：四个方法分别转发到 `loadBytes`、`loadRuntimeResourceBytesByName`、`cachedRuntimeMixExportsRootForContext`、`runtimeAssetRootHasMixArchives`。

上下文 `RuntimeTerrainTmpContext`（`RuntimeTerrainTmpQuery.h:31-36`）持有 `theater`、`tileRootPath`、`assetRootPath`、`structureRootPath`；渲染侧完整上下文 `RuntimeMapRenderContext` 另含 `palettePath`、`structurePalettePath` 等（`RuntimeMapRenderer.h:782-786`）。

### 5.3 资源会话生命周期

| 会话 / 缓存载体 | 生命周期 | 出处 |
| --- | --- | --- |
| `RuntimeMapRenderCache` | 渲染帧 / 场景级缓存；持有 MIX 归档缓存、松散索引、字节与帧缓存；上下文根路径变化时对应索引自动失效重建 | `RuntimeMapRenderer.h:545-659`；`RuntimeProductionResources.cpp:588-599, 667-672` |
| `MapPreviewResourceSession` | 显式消费方独占一个会话；无进程全局 / 线程局部缓存；调用与刷新由该消费方串行化；引用在刷新后失效。`refresh()` 清空全部缓存域并递增 `mountGeneration` | `src/assets/preview/MapPreviewResourceContext.h:34-42`；`MapPreviewResourceContext.cpp:15-42` |
| 预览会话松散字节缓存 | 仅成功读取的字节入缓存；打开 / 大小 / 读取失败不缓存；相对路径相对会话 `packageRoot` 解析 | `MapPreviewResourceContext.h:59-63`；`MapPreviewResourceContext.cpp:43-64` |
| 音频 MIX 缓存 | 解析器内进程级 `static` 共享缓存，跨会话存活 | `AudioSampleResolver.cpp:443-460` |
| 资产检查器缓存 | `CapabilityReportState` 内 `decodeCache` 按 `loose:<path>` / `mix:<archive>!<id>#<size>#<ext>` 为键缓存解码结果 | `AssetInspector.cpp:2402-2473` |

### 5.4 与各消费模块的关系

| 消费模块 | 接入方式 | 出处 |
| --- | --- | --- |
| `assets/codecs`（SHP / PAL / TMP / CSF / FNT） | codec 只接收已读出的字节向量解码，不直接接触 `MixArchive`；如 `decodeRuntimeShpFrameFromBytes` 调用 `decodeShpIndexedFrame(bytes, …)` | `RuntimeProductionResources.cpp:114-142`；codecs 目录无 `MixArchive` 引用 |
| `assets/terrain`（地形瓦片） | 经 `RuntimeTerrainTmpByteSource` 抽象按名取字节；索引命中记录 `runtimeTerrainMixResourcePrefix + fileName` 作为来源标签；根下无 MIX 时才回退松散引用文件 | `RuntimeTerrainTmpQuery.cpp:232-258` |
| `assets/menu`（菜单图像） | `MenuImageResources` 以 `MapPreviewResourceContext` 根路径构造，走既有松散文件策略；`assets/menu/` 目录内无直接 `MixArchive` 调用 | `src/assets/menu/MenuImageResources.h:14-45` |
| `assets/preview`（地图预览导出） | 自建 `IndexedMixArchive` 列表（路径 + `MixArchive` + `id → 条目下标` 反查表），在预览根下递归收集 `.mix` | `MapPreviewExporter.cpp:178-180, 605-679` |
| `assets/audio`（音频） | 独立 MIX 候选 / 缓存 / 三级解析（松散 → MIX 条目 → `idx/bag`） | `AudioSampleResolver.cpp:384-460, 1952-1989` |
| `assets/inspection`（资产检查） | 扫描松散文件与 `.mix`，构建 `mixEntriesById` 反查表；条目解码走统一缓存 | `AssetInspector.cpp:2095-2131, 2458-2494` |
| `analysis/cli`（命令行工具） | 加载可读 MIX 列表、按名 / 按 CRC 定位、`extractEntryToFile` 导出条目 | `analysis/cli/main.cpp:587-634, 819-831, 2272-2298, 2360-2394` |
| `assets/inspection/GameResourceMount` | 仅做挂载层目录扫描与扩展名 / 规则文件统计报告，不解析 MIX 条目、不参与运行时取字节 | `GameResourceMount.cpp:96-150, 272-306` |

### 5.5 挂载层分类（诊断视角）

`ResourceMountKind` 三档（`GameResourceMount.h:8-12`）：`OriginalGame`、`LocalData`、`Mod`；请求 `ResourceMountRequest` 含 `kind / name / rootPath / recursive`（`GameResourceMount.h:14-19`）。分类统计 `.mix / .ini / 地图 / .pal / .shp / .tmp(含 .tem/.urb) / .vxl / .hva`，并识别 `rules.ini/rulesmd.ini`、`art.ini/artmd.ini`、剧院 INI 与调色板存在性（`GameResourceMount.cpp:115-149`）。报告成败条件：至少一层存在，且具备规则 / 艺术 / 地图 / MIX 之一（`GameResourceMount.cpp:283-285`）。

---

## 6. 加密 / 压缩在参考引擎中的真实边界

### 6.1 参考引擎已实现

| 能力 | 实现位置 |
| --- | --- |
| 明文 MIX 头部与目录解析（标志头 / 旧式头） | `MixArchive.cpp:214-250, 361-390, 410-451` |
| 加密目录的 RSA 公钥解包（指数 65537，40 字节模数 × 2 段 → 56 字节密钥） | `WestwoodCrypto.cpp:191-224, 160-177` |
| Blowfish 分组解密目录（P/S 常量内置，16 轮 Feistel，8 字节分组） | `WestwoodCrypto.cpp:250-352, 358-380`；`WestwoodBlowfishConstants.inc` |
| 文件名 CRC32 标识生成与按名 / 按 ID 查找、扩展名候选 | `MixArchive.cpp:166-178, 501-564, 680-706` |
| 条目原始字节读取与落盘导出 | `MixArchive.cpp:566-678` |
| 运行时 MIX 扫描、优先级排序、归档缓存、松散索引与兜底 | `RuntimeProductionResources.cpp:513-890, 899-962` |

### 6.2 未实现 / 边界外

| 事项 | 参考引擎当前状态 | 出处 / 说明 |
| --- | --- | --- |
| 校验和块校验 | 20 字节块仅做边界预留，未读取、未识别算法、未做完整性校验 | `MixArchive.cpp:392-406`；`archive/` 内无校验和实现 |
| 条目级压缩解压 | `readEntryData` 原始读取，`archive/` 层无 deflate / zlib / LZ 等解压代码；条目是否压缩由消费方 codec 判断 | `MixArchive.cpp:566-621` |
| 嵌套 MIX 递归展开 | 无内存字节流构造 `MixArchive` 的接口；预览诊断跳过 `.mix` 条目；音频嵌套链以硬编码清单兜底 | `MixArchive.h:19-23`；`MapPreviewExporter.cpp:3030-3034`；`AudioSampleResolver.cpp:1200-1284` |
| 条目数据区加密 | 仅目录解密；数据区在 `archive/` 层按明文偏移直接读取 | `WestwoodCrypto.cpp:358-380` 仅作用于目录分组 |
| `.aud` / IMA ADPCM 解码 | 不属于归档层；由 `AudioDecoder` 独立完成（`decodeAudToWave`、`decodeImaAdpcmBagSampleToWave`） | `src/assets/audio/AudioDecoder.h:27-36` |
| Bink 视频 | 视频播放走独立 `assets/video` 模块；归档层不涉及其解复用 / 解码 | `src/assets/video/`（见本章链路表） |
| HVA / PCX 在预览诊断中的解码 | 预览诊断对 `.hva`、`.pcx` 标注 `decode=skip`，不展开 | `MapPreviewExporter.cpp:3016-3028` |
| 多编码 / Unicode 文件名 | 逻辑名仅 ASCII 大写、去目录、去引号；无 Unicode 折叠；路径在 Windows 走原生 ACP 窄编码 | `MixArchive.h:17-22, 69-70`；`MixArchive.cpp:120-150` |

---

## 本章缺口 / 参考引擎未覆盖项

1. **校验和算法未还原**：20 字节校验块的算法归属（如 SHA1 类）、覆盖范围与校验逻辑在参考引擎中未实现，仅有边界核算；复刻时需另行确认原版格式。
2. **嵌套 MIX 递归解析缺失**：不支持从条目字节流二次加载内层 MIX，也无层级深度限制与嵌套查找顺序；当前依赖预先导出的 `mix_exports` 松散文件。
3. **条目压缩格式未在归档层处理**：`archive/` 不判断条目是否压缩；若原版存在压缩条目，参考引擎的读取路径未覆盖对应解压逻辑（需结合具体素材与 codec 边界核实）。
4. **CRC 文件名长度规则未复刻**：参考引擎未做文件名长度截断（如 12 字符截断）；与原版 CRC 生成细节的差异需用真实素材比对验证。
5. **旧式头判定为启发式**：`legacyUnflaggedHeader` 依据“标志位之外是否存在高位”判定（`MixArchive.cpp:219-220`），未提供显式版本号字段；异常头型的兼容边界未在源码中展开。
6. **音频主题嵌套链为硬编码兜底**：直装根下 `theme.ini / thememd.ini` 位于嵌套 MIX 时，参考引擎使用写死的曲目清单而非解析嵌套 INI（`AudioSampleResolver.cpp:1206-1284`）。
7. **菜单资源不经 MIX 直读**：`assets/menu` 仅走松散文件根策略；菜单 BMP/PCX 在 MIX 内的直读路径在参考引擎中未接入。
8. **挂载层为诊断统计**：`GameResourceMount` 只扫描计数，不构成运行时解析层；Mod 层覆盖顺序目前仅按请求顺序累积报告，无运行时覆盖仲裁实现（`GameResourceMount.cpp:272-306`）。
9. **缓存策略差异**：渲染链路 MIX 归档按路径常驻 `RuntimeMapRenderCache`；音频链路为进程级静态缓存；预览会话缓存随 `refresh()` 整体失效。复刻 Unity 时需明确各会话的缓存粒度与失效时机，参考引擎未提供统一缓存抽象。
10. **素材外部依赖**：`mix_exports` 松散文件、导出的地图 / 瓦片 / 调色板等由外部准备流程产出；参考引擎源码中不包含把零售 MIX 导出为松散目录的内置流水线（CLI 仅支持按条目手动导出，`analysis/cli/main.cpp:831, 2298, 2394`）。
