# 03 · INI 加载器与规则数据库

## 本章覆盖范围

- 参考引擎通用 INI 解析器的词法规则、类型转换、默认值与叠加顺序。
- 规则（rulesmd.ini）、美术（artmd.ini）、声音（soundmd.ini）、音乐（theme.ini）、地图内嵌 INI、规则覆盖层（rules-overrides.ini 样例）、运行时配置（runtime_config.ini）各自被哪个模块读取、解析成什么对象。
- 规则数据库（RulesDatabase）的规则类型清单、生产规则解析器、数值校验/默认值/叠加/警告顺序。
- 字段 → 规则对象属性映射表（按规则类型分组）。
- 规则细节哈希（detail rules hash）与确定性用途。
- 美术规则、声音规则如何挂接到规则对象上。

## 对应源码目录

- 只读根：`D:\___SYSTEM_PATH\source\repos\ConcordRTS\`
- 通用解析器：`src/core/io/IniFile.h`、`src/core/io/IniFile.cpp`、`src/core/io/RuntimeIniTextReadBuffer.h`
- 规则数据库：`src/data/rules/`（`RulesDatabase.h/.cpp`、`ProductionRules.h`、`ProductionRulesLoader.h/.cpp`、`RulesModExtensions.inl`、`ModExtensionTypes.h`、`TechTreeAnalyzer.h/.cpp`、`RuntimeDetailRulesHash.h`）
- 美术规则：`src/data/art/ArtDatabase.h/.cpp`
- 声音规则：`src/assets/audio/AudioCatalog.cpp`、`src/audio/world/RuntimeWorldAudioSystem.cpp`
- 运行时配置：`src/runtime/config/RuntimeConfig.h/.cpp`
- 样例：`samples/rulesmd.ini`、`samples/artmd.ini`、`samples/runtime_config.example.ini`、`samples/mod-extensions/rules-overrides.ini`

> 本章所有结论均来自上述源码静态阅读；未在源码中出现的规则类型，在映射表中明确标注「参考引擎未实现」。行号以当前只读源码快照为准。

---

## 1. 通用 INI 解析器

### 1.1 文件定位

| 组件 | 文件 | 职责 |
| --- | --- | --- |
| `IniFile` | `src/core/io/IniFile.h:17-60` | 内存中的 section/key=value 容器与查询接口 |
| 解析实现 | `src/core/io/IniFile.cpp` | 文件加载、字符串加载、覆盖层追加、取值、注释剥离、大小写归一、打包地图段识别 |
| 文本缓冲 | `src/core/io/RuntimeIniTextReadBuffer.h:10-18` | 按行读取文本，供 `loadFromString` 复用 |

`IniFile` 内部结构（`IniFile.h:22-34`）：

```cpp
using Section = std::vector<std::pair<std::string, std::string>>;
std::unordered_map<std::string, Section> sections_;
```

注意：**Section 是有序 `vector`，不是 `map`**。同一 section 内的键按出现顺序保留，重复键不会在解析期去重；取值时从尾部向前扫描，后出现的键覆盖先出现的键（见 1.8）。

### 1.2 加载入口与字节/词法步骤

三个加载入口（`IniFile.cpp`）：

| 入口 | 行号 | 行为 |
| --- | --- | --- |
| `loadFromFile(path, literalNumericSections)` | `IniFile.cpp:122-138` | 读文件全文，转调 `loadFromString` |
| `loadFromPath(path, literalNumericSections)` | `IniFile.cpp:140-158` | 路径存在性检查后转调 `loadFromFile` |
| `loadFromString(text, literalNumericSections)` | `IniFile.cpp:176-229` | 纯词法解析，不触盘 |
| `appendFrom(other)` | `IniFile.cpp:231-250` | 把另一份 INI 的 section/键值追加到本容器尾部，用于规则覆盖层叠加 |

`loadFromString` 的逐行步骤（`IniFile.cpp:176-229`）：

1. 用 `RuntimeIniTextReadBuffer` 按行迭代（`IniFile.cpp:180-182`）。
2. 行整体 `trim`（`IniFile.cpp:184`）；空行跳过（`IniFile.cpp:185-187`）。
3. 识别 section：行首 `[`、行尾 `]`，内部内容 `trim` 后作为 section 名（`IniFile.cpp:189-207`）。
4. 非 section 行必须先有当前 section，否则跳过（`IniFile.cpp:218-220`）。
5. 按第一个 `=` 切分 key 与 value；两侧分别 `trim`；没有 `=` 的行跳过（`IniFile.cpp:221-227`）。
6. 写入当前 section 的 `vector` 尾部（`IniFile.cpp:228`）。

### 1.3 注释规则（以源码为准）

注释剥离在 `removeComment`（`IniFile.cpp:99-120`）：

- `;`：始终视为注释起始，从该字符起截断（`IniFile.cpp:102-104`）。
- `//`：普通文本段中视为注释起始（`IniFile.cpp:105-116`）。
- **打包地图段例外**：当当前 section 属于 `IsoMapPack5` / `OverlayPack` / `OverlayDataPack` / `PreviewPack` 时，`//` 不被剥离，作为二进制/十六进制记录的一部分保留（`IniFile.cpp:212`，`isPackedMapSection` 见 `IniFile.cpp:31-36`）。

```cpp
// IniFile.cpp:212
removeComment(line, !isPackedMapSection(currentSection));
```

> 源码中**没有**把 `#` 当作注释起始的处理；`#` 会原样进入 value。源码中也**没有**对 `$` 前缀做任何特殊解释（在 `src/core/io/` 内检索 `$`、`#` 前缀均无命中）。`$`/`#` 前缀语义在本参考引擎中未实现。

### 1.4 section 与 key=value

- section 名：`[xxx]` 括号内内容先 `trim`（`IniFile.cpp:193-196`）。
- section 合并：大小写不同但归一后相同的 section 复用已存在的 section 容器（`IniFile.cpp:199-205`）。
- key/value：仅按**第一个** `=` 切分；value 中允许再出现 `=`（`IniFile.cpp:222-226`）。
- 引号：解析器不识别成对引号，引号是 value 的普通字符，由消费方按需剥离（例如音频侧 `stripAudioDecorators`）。

### 1.5 大小写处理

- 比较函数 `equalsAsciiIgnoreCase`（`IniFile.cpp:15-29`）：逐字符 ASCII 大小写不敏感比较。
- section 查找 `getSection`（`IniFile.cpp:252-263`）：对 section 名做 ASCII 归一后在 `sections_` 中查找。
- key 查找 `getValue`（`IniFile.cpp:276-294`）：在 section 内逐个按键名做大小写不敏感比较。
- **存储保留原始拼写**：写入容器时不做大小写改写；归一仅用于查询匹配。

### 1.6 前后空白

`trim`（`IniFile.cpp:87-97`）裁剪字符集为 ` \t\r\n`：

- section 名、key、value 两侧空白都裁剪。
- value 内部空白保留；列表元素级裁剪由消费方在拆分后执行（见 1.7）。

### 1.7 逗号列表拆分

通用解析器**不做**列表拆分。列表拆分由各消费模块自行实现：

- 规则数据库：`splitList`（`RulesDatabase.cpp:287-310`），分隔符 `,` 或 `;`，元素 `trim`，丢弃空元素；`mergeUniqueIniLists`（`RulesDatabase.cpp:312-329`）按归一化 ID 去重合并。
- 声音目录：`splitWhitespace`（`AudioCatalog.cpp:149` 调用），按空白切分样本名。
- 整数列表：`parseIntegerList`（`RulesDatabase.cpp:523-537`），逗号/分号分隔，`stoi` 解析，失败记 0。

### 1.8 类型转换

类型转换不在 `IniFile` 内，而在规则数据库的读值辅助函数中（`RulesDatabase.cpp:3660-3787`）。所有辅助函数签名模式：`readXxx(ini, section, key, defaultValue)`；解析失败时压入警告并回退默认值。

| 类型 | 辅助函数 | 行号 | 解析规则 / 失败处理 |
| --- | --- | --- | --- |
| 整数 | `readInt` | `RulesDatabase.cpp:3660-3678` | `std::stoi`；`std::invalid_argument`/`std::out_of_range` 时压警告，返回默认值 |
| 浮点 | `readDouble` | `RulesDatabase.cpp:3680-3698` | `parseFiniteDouble`（`RulesDatabase.cpp:361-373`）；非有限值（NaN/Inf）回退默认值 |
| 生产浮点 | `readProductionDouble` | `RulesDatabase.cpp:3700-3709` | 有限浮点，用于生产规则倍率 |
| 百分比 | `readPercent` | `RulesDatabase.cpp:3711-3730` | `parsePercentToken`（`RulesDatabase.cpp:851-871`）：尾部 `%` 剥离后按百分号量纲解读；缺省由字段当前值提供 |
| 比率 | `readRatio` | `RulesDatabase.cpp:3732-3762` | 尾部 `%` 时除以 100；其余按原值；夹取/下限由调用方处理 |
| 布尔 | `readBool` | `RulesDatabase.cpp:3764-3787` | 真：`yes/true/1`；假：`no/false/0`；其余压警告并回退默认值 |
| 字符串 | `IniFile::getValue` | `IniFile.cpp:276-294` | section/key 缺失时返回默认字符串 |
| 颜色 | `parseParticleColor` | `RulesDatabase.cpp:388-410` | 颜色 token 解析（规则对象内保留原始颜色字符串与解析结果两种形态） |
| 坐标/向量 | `parseParticleVector3` | `RulesDatabase.cpp:375-386` | 三分量向量解析 |
| 列表 | `splitList` / `parseIntegerList` / `parseParticleColorList` | `RulesDatabase.cpp:287-310`、`523-537`、`412-425` | 见 1.7 |

附加语义：

- 空值标记 `isNoneValue`（`RulesDatabase.cpp:754-758`）：归一后为 `none` / `<none>` 视为显式空值。
-  verses 百分比表：`parseVersesPercentages`（`RulesDatabase.cpp:873-...`），按条目解析 `Verses` 百分比。
- 老兵能力：`parseVeteranAbility`（`RulesDatabase.cpp:448-507`）；粒子行为：`parseParticleBehavior`（`RulesDatabase.cpp:332-359`）。

### 1.9 默认值与缺失处理

- 键缺失：`getValue` 直接返回调用方传入的默认字符串（`IniFile.cpp:286-291`）。
- 类型转换失败：不抛异常，压入 `warnings_`，返回默认值（`RulesDatabase.cpp:3666-3676` 等）。
- 数值夹取：多数数值在解析后由调用方 `std::clamp` / `std::max` 收口（例如 `BurstDelay` 夹在 `[1, 36000]`，`RulesDatabase.cpp:4833`）。

### 1.10 叠加 / 覆盖顺序

覆盖层追加在 `appendFrom`（`IniFile.cpp:231-250`）：

1. 遍历覆盖层每个 section，不存在则在本容器新建。
2. 把覆盖层的键值对逐条追加到目标 section 的 `vector` 尾部（`IniFile.cpp:244-248`）。
3. 取值时 `getValue` 从 `vector` 尾部向前扫描，**后追加的同名键生效**（`IniFile.cpp:286-291`）。

运行时实际叠加顺序（`RuntimeSessionWorld.cpp:30584-30600`）：

1. 基础规则 INI（`rulesSource` 解析出的 `rulesmd.ini`/`rules.ini`）先载入 `rulesIni`。
2. 游戏模式规则覆盖（`config.gameModeRulesOverride`，例如 `MPCoopMD.ini`、`MPTeamMD.ini` 等，见 `RuntimeShell.cpp:15303-15749`）通过 `appendFrom` 追加（`RuntimeSessionWorld.cpp:30596`）。
3. 地图内嵌规则 INI 再 `appendFrom` 追加（`RuntimeSessionWorld.cpp:30598`）。

即：**基础规则 ← 模式规则覆盖 ← 地图内嵌 INI**，后层覆盖前层。

### 1.11 重复键与警告/错误处理

- 重复键：解析期不报错、不去重；保留多次出现，读取时后者覆盖前者。
- 规则库重复 section：`RulesDatabase` 在装载类型时按归一化 ID 去重（例如 `ArtDatabase.cpp:963-966` 对重复 art section 记警告并忽略；规则侧类似的 `loadedXxxIds` 集合）。
- 解析错误：全部降级为警告，收集到 `warnings_`，不中断装载；会话报告侧以 `rules: ` 前缀挂到 `report.warnings`（`RuntimeSessionWorld.cpp:30605-30607`）。

### 1.12 等价小例子（自编，便于对照）

以下输入（自编等价示例，非源码片段）按上述规则解析：

```ini
; 分号注释
// 斜杠注释
[General]
Speed=1.0   ; 行尾注释
UnusedKey   ; 没有 =，整行跳过

[General]   ; 大小写/重复 section，并入已有 section
DefaultOn=yes
DefaultOff=no
Percent=50%
```

解析结果要点：

- `[General]` 两段合并；`DefaultOn=yes`、`DefaultOff=no` 追加到该 section 尾部。
- `Speed` value 为 `1.0`（行尾注释与两侧空白剥离）。
- `Percent=50%`：`readPercent`/`readRatio` 识别尾部 `%`。
- `UnusedKey` 行被丢弃。

---

## 2. 各 INI 文件如何被消费

### 2.1 消费链路总览

| INI 文件 / 来源 | 读取模块 | 解析成的对象 | 关键段名（源码/样例实测） | 出处 |
| --- | --- | --- | --- | --- |
| `rulesmd.ini`（基础规则） | `RulesDatabase::loadFromIni`，会话装配在 `RuntimeSessionWorld` | `UnitType` / `BuildingType` / `WeaponType` / `ProjectileType` / `WarheadType` / `OverlayType` / `SmudgeType` / `TiberiumType` / `TerrainType` / `LandType` / `ParticleSystemType` / `ParticleType` / `SuperWeaponRule` / `VoxelAnimType` / `CountryDefinition` / `SideDefinition` | `General`、`AudioVisual`、`CombatDamage`、`CrateRules`、`Powerups`、`ColorAdd`、`InfantryTypes`、`VehicleTypes`、`AircraftTypes`、`BuildingTypes`、`Warheads`、`OverlayTypes`、`SmudgeTypes`、`Tiberiums`、`TerrainTypes`、`ParticleSystems`、`Particles`、`SuperWeaponTypes`、`VoxelAnims`、`Countries`、`Sides` | `RulesDatabase.cpp:1830`、`1922`、`1941`、`1981`、`2034`、`2930`、`5780-5897`；`RuntimeSessionWorld.cpp:30585` |
| `artmd.ini`（美术规则） | `ArtDatabase::loadFromIni`，链接在 `ArtDatabase::buildLinks` | `ArtType`，经 `ArtObjectLink` 挂到规则对象 | 每个对象一个同名段（如 sample 中 `GTESLA`、`AFRNT`）；段内键 `Image`、`Cameo`、`Sequence`、`Foundation`、`Palette` 等 | `ArtDatabase.cpp:951-973`、`881-947`；`samples/artmd.ini` |
| `soundmd.ini`（声音事件） | `AudioCatalog`（资源审计）、`RuntimeWorldAudioSystem::loadFromIni`（运行时装载） | 声音事件 → 样本名列表；事件名由规则侧语音键引用 | `SoundList`（事件名清单）；每个事件一个同名段，样本键 `Sounds`/`Sound`/`Samples`/`Sample`/`File`/`Files`/`Russian`/`Allied`/`Yuri` | `AudioCatalog.cpp:1141-1199`、`137-145`；`RuntimeWorldAudioSystem.cpp:512` |
| `theme.ini` / `thememd.ini`（背景音乐） | `AudioCatalog` 主题目录；`AudioSampleResolver` 混合链 | 音乐曲目条目（事件名 → 样本） | `Themes`（曲目清单）；每个曲目一个同名段，键 `Sound`、`Normal` | `AudioCatalog.cpp:450-479`；`AudioSampleResolver.cpp:1089-1141` |
| 地图内嵌 INI | `MapDatabase::loadFromIni`；同时作为规则覆盖层 `appendFrom` | `MapDatabase` 地图实体；规则覆盖段并入 `rulesIni` | `Lighting`、`Waypoints`、`Smudge`、`Terrain`、`Structures`、`Units`、`Infantry`、`Aircraft`、`PreviewPack`、`IsoMapPack5`、`OverlayPack`、`OverlayDataPack` | `MapDatabase.cpp:236`、`2591`、`2619`、`2770`、`2879`、`2929`、`2983`、`3038`、`3336`；`RuntimeSessionWorld.cpp:30598` |
| `rules-overrides.ini`（规则覆盖样例） | 生产代码无独立文件名；由 `--mod-root` 挂载层提供；样例准备脚本把它拼到基础规则文本之后 | 同 `rulesmd.ini` 解析结果 | `AudioVisual`、`InfantryTypes`、`VehicleTypes`、`BuildingTypes`、`Powerups`、`NewUnit`、`NewNodInfantry`、`ExtraPowerPlant` | `samples/mod-extensions/rules-overrides.ini`；`samples/mod-extensions/prepare.ps1:9-10`；挂载识别 `GameResourceMount.cpp:140-149` |
| `runtime_config.ini`（运行时配置） | `RuntimeConfig::loadRuntimeConfig` | `RuntimeConfig` 标量配置（窗口、音频、AI、热键、战役、模式规则覆盖路径等） | `Runtime`、`Skirmish`、`Options`、`Audio`、`Hotkey`、`Campaign`；样例仅含 `Runtime`、`Skirmish` | `RuntimeConfig.cpp:129-450`；`samples/runtime_config.example.ini` |

### 2.2 样例段名实测

`samples/rulesmd.ini` 段名：`General`、`InfantryTypes`、`VehicleTypes`、`BuildingTypes`、`WeaponTypes`、`ProjectileTypes`、`Warheads`、`TerrainTypes`、`Tiberiums`、`AircraftTypes`、`Animations`、`AudioVisual`。

> 注意：样例里出现了 `[WeaponTypes]`、`[ProjectileTypes]`、`[Animations]` 清单段，但规则数据库**不枚举**这三个清单段（见 3.1 与本章缺口）；武器/投射物由对象引用反向发现，动画类型在规则库中未实现为独立类型。

`samples/artmd.ini` 段名：`GTESLA`、`AFRNT`、`AMINE`。

`samples/runtime_config.example.ini` 段名：`Runtime`、`Skirmish`。

`samples/mod-extensions/rules-overrides.ini` 段名：`AudioVisual`、`InfantryTypes`、`VehicleTypes`、`BuildingTypes`、`Powerups`、`NewUnit`、`NewNodInfantry`、`ExtraPowerPlant`。

### 2.3 runtime_config.ini 键映射（`RuntimeConfig.cpp`）

| INI 段 | INI 键 | 配置字段 | 类型 / 默认 | 出处 |
| --- | --- | --- | --- | --- |
| `Runtime` | `ModRoot` | `modRootPath` | 字符串 | `RuntimeConfig.cpp:147-148` |
| `Runtime` | `GameModeRulesOverride` | `gameModeRulesOverride` | 字符串 | `RuntimeConfig.cpp:186-189` |
| `Skirmish` | `StartingCredits` | 开局资金等 | 整型/浮点，缺省由代码提供 | `RuntimeConfig.cpp:129-450` |
| 其余段 | `Options` / `Audio` / `Hotkey` / `Campaign` | 对应运行时选项 | 标量 | `RuntimeConfig.cpp:129-450` |

> `runtime_config.ini` 与游戏规则数据解耦：它只驱动运行时会话、窗口、音频与路径解析，不进入 `RulesDatabase`。

---

## 3. 规则数据库

### 3.1 规则类型清单（源码实际存在）

| 规则类型 | 容器 / 结构体 | 装载清单段 | 装载函数 | 出处 |
| --- | --- | --- | --- | --- |
| 步兵 | `UnitType`（`infantryTypes_`） | `InfantryTypes` | `loadUnitType` | `RulesDatabase.cpp:3813-4306` |
| 车辆 | `UnitType`（`vehicleTypes_`） | `VehicleTypes` | `loadUnitType` | 同上 |
| 飞行器 | `UnitType`（`aircraftTypes_`） | `AircraftTypes` | `loadUnitType` | 同上 |
| 建筑 | `BuildingType` | `BuildingTypes` | `loadBuildingType` | `RulesDatabase.cpp:4306-4820` |
| 武器 | `WeaponType` | 无清单段；由对象 `Primary`/`Secondary` 等引用发现 | `loadWeaponType` | `RulesDatabase.cpp:4821-4928`；发现逻辑 `6011-6040`、`6041-6120` |
| 投射物 | `ProjectileType` | 无清单段；由武器 `Projectile` 引用发现 | `loadProjectileType` | `RulesDatabase.cpp:4930-4985` |
| 弹头 | `WarheadType` | `Warheads`；再由武器 `Warhead` 引用补充 | `loadWarheadType` | `RulesDatabase.cpp:5351-5433`；发现逻辑 `6121-6180` |
| 覆盖物 | `OverlayType` | `OverlayTypes` | `loadOverlayType` | `RulesDatabase.cpp:4987-5023` |
| 碎屑/伤疤 | `SmudgeType` | `SmudgeTypes` | `loadSmudgeType` | `RulesDatabase.cpp:5025-5038` |
| 资源（泰矿） | `TiberiumType` | `Tiberiums` | `loadTiberiumType` | `RulesDatabase.cpp:5040-5062` |
| 地形 | `TerrainType` | `TerrainTypes` | `loadTerrainType` | `RulesDatabase.cpp:5064-5085` |
| 地形通行类型 | `LandType` | 固定默认清单（`Clear`/`Rough`/`Road`/`Water`/`Rock`/`Wall`/`Tiberium`/`Weeds`/`Beach`/`Ice`/`Railroad`/`Tunnel`） | `loadLandType` / `loadLandTypes` | `RulesDatabase.cpp:5087-5102`、`5780-5812` |
| 粒子系统 | `ParticleSystemType` | `ParticleSystems` | `loadParticleSystemType` | `RulesDatabase.cpp:5104-5230` 区间 |
| 粒子 | `ParticleType` | `Particles` | `loadParticleType` | `RulesDatabase.cpp:5231-5350` 区间 |
| 超武 | `SuperWeaponRule` | `SuperWeaponTypes` | `loadSuperWeaponRule` | `RulesDatabase.cpp:1385-1826` |
| 体素动画 | `VoxelAnimType` | `VoxelAnims` | 体素动画装载 | `RulesDatabase.cpp:2929-2950` |
| 国家 | `CountryDefinition` | `Countries` | `loadCountries` | `RulesDatabase.cpp:5814-5874` |
| 阵营 | `SideDefinition` | `Sides` | `loadSides` | `RulesDatabase.cpp:5876-5897` |
| 模组扩展：护盾类型 | `ShieldType`（`ModExtensionTypes.h:11-19`） | 扩展段（`ShieldTypes` 类） | 扩展装载 | `RulesModExtensions.inl`；`ModExtensionTypes.h` |
| 模组扩展：附着效果 | `AttachEffectDefinition`（`ModExtensionTypes.h:21-36`） | 弹头/对象上的 `AttachEffect` 段 | `loadAttachEffect` | `RulesDatabase.cpp:5355`；`ModExtensionTypes.h` |

参考引擎未实现 / 未独立成类型：

- 动画（Anim）类型：样例 `[Animations]` 清单存在，但规则库无独立 `AnimType` 容器；动画多以字符串引用挂在武器/弹头/超武上（`Anim`、`AnimList`、`LaunchAnim` 等）。
- 轨道/弹道分离清单段 `[WeaponTypes]`、`[ProjectileTypes]`：未被枚举。
- 水兵/植物等特殊类别：并入 `UnitType` 或未区分。

### 3.2 生产规则（`ProductionRules`）

文件：`src/data/rules/ProductionRules.h`、`ProductionRulesLoader.h/.cpp`。

值类型与解析器（`ProductionRulesLoader.cpp:75-110`）：

| INI 段 | INI 键 | 生产规则字段 | 解析器 | 校验 / 默认 | 出处 |
| --- | --- | --- | --- | --- | --- |
| `General` | `BuildSpeed` | 生产速度倍率 | `readProductionDouble` | 必须 `> 0` 且不超过上限；非法回退默认并记诊断 | `ProductionRulesLoader.cpp:75-85` |
| `General` | `BuildupTime` | 建造动画时长 | `readInt`/`readProductionDouble` | 同上 | `ProductionRulesLoader.cpp:86-90` |
| `General` | `MultipleFactory` | 多工厂加成 | `readProductionDouble` | 同上 | `ProductionRulesLoader.cpp:91-95` |
| `General` | `WallBuildSpeedCoefficient` | 围墙建造系数 | `readProductionDouble` | 同上 | `ProductionRulesLoader.cpp:96` |
| `General` | `LowPowerPenaltyModifier` | 低电惩罚修正 | `readProductionDouble` | 下限/上限夹取 | `ProductionRulesLoader.cpp:97-100` |
| `General` | `MinLowPowerProductionSpeed` | 低电最低速度 | `readProductionDouble` | 与最大值做 `min ≤ max` 校验 | `ProductionRulesLoader.cpp:101-106` |
| `General` | `MaxLowPowerProductionSpeed` | 低电最高速度 | `readProductionDouble` | 同上 | `ProductionRulesLoader.cpp:107-110` |

### 3.3 数值校验 / 默认值 / 叠加 / 警告顺序

1. **装载顺序**（`RulesDatabase::loadFromIni`，`RulesDatabase.cpp:1830` 起）：全局段（`General`/`AudioVisual`/`ColorAdd`/`CrateRules`/`Powerups`）→ 清单段类型 → 引用发现型武器/投射物/弹头 → 阵营/国家/前置别名 → 重建查找索引。
2. **默认值**：每个读值辅助函数带显式默认；结构体字段在头文件内联初始化（`RulesDatabase.h` 各结构体）。
3. **数值校验**：解析后 `std::clamp` / `std::max` / `std::min` 收口；非法值回退默认并压警告。
4. **叠加**：覆盖层经 `appendFrom` 尾部追加，取值后者优先（见 1.10）。
5. **警告顺序**：解析期警告按装载先后累积进 `warnings_`；`getWarnings()`（`RulesDatabase.cpp:3656-3658`）原样返回；会话装配层逐条加 `rules: ` 前缀并入报告（`RuntimeSessionWorld.cpp:30605-30607`）。

### 3.4 前置别名与科技树分析

- 前置别名：`loadPrerequisiteAliases`（`RulesDatabase.cpp:5899-5919`），从 `[General]` 读取 `PrerequisitePower`、`PrerequisiteFactory`、`PrerequisiteBarracks`、`PrerequisiteRadar`、`PrerequisiteTech`、`PrerequisiteProc`、`PrerequisiteProcAlternate`。
- 科技树分析：`TechTreeAnalyzer::analyzeTechTree`（`TechTreeAnalyzer.h:22-24`）**消费内存中的 `RulesDatabase`**，不直接解析 INI；输出 `TechTreeNode` 与科技树诊断（`TechTreeAnalyzer.cpp:61` 起）。`RuntimePrerequisiteProcAlternate.inl` 当前为空占位文件。

---

## 4. 字段 → 规则对象属性映射表

> 说明：下表键名按源码原样照抄；类型标注规则对象 C++ 类型；默认值为源码读值辅助函数中的缺省值；行号为 `RulesDatabase.cpp`（艺术表为 `ArtDatabase.cpp`）对应装载行。空间有限，每类列出源码实际读取的代表性键，未列键可在对应装载函数内按同模式检索。

### 4.1 步兵 / 车辆 / 飞行器（`UnitType`，`loadUnitType`）

| INI 键 | 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- |
| `UIName` | `uiName` | 字符串 | `""` | `RulesDatabase.cpp:3829` |
| `Name` | `name` | 字符串 | `id` | `RulesDatabase.cpp:3830` |
| `Image` | `image` | 字符串 | `""` | `RulesDatabase.cpp:3831` |
| `Strength` | `strength` | 整型 | `0` | 单位装载函数区间 `3813-4306` |
| `Armor` | `armor` | 字符串 | `""` | 同上 |
| `Sight` | `sight` | 整型 | `0` | 同上 |
| `Speed` | 速度相关字段 | 整型/浮点 | `0` | 同上 |
| `ROT` | `rot` | 整型 | `0` | 同上 |
| `Cost` | `cost` | 整型 | `0` | 同上 |
| `TechLevel` | `techLevel` | 整型 | `0` | `RulesDatabase.cpp:3852` |
| `Prerequisite` | `prerequisite` | 字符串列表 | 空 | 同上 |
| `Primary` | `primaryWeapon` | 字符串（武器 ID） | `""` | 同上 |
| `Secondary` | `secondaryWeapon` | 字符串（武器 ID） | `""` | 同上 |
| `ElitePrimary` | `elitePrimaryWeapon` | 字符串 | `""` | 同上 |
| `EliteSecondary` | `eliteSecondaryWeapon` | 字符串 | `""` | 同上 |
| `Owner` | `owner` | 字符串列表 | 空 | 同上 |
| `RequiredHouses` | `requiredHouses` | 字符串列表 | 空 | 同上 |
| `ForbiddenHouses` | `forbiddenHouses` | 字符串列表 | 空 | 同上 |
| `VeteranAbilities` / `EliteAbilities` | 老兵/精英能力表 | 能力列表 | 空 | `parseVeteranAbility` `448-507` |
| `VoiceSelect` / `VoiceMove` / `VoiceAttack` / `VoiceFeedback` / `VoiceSpecialAttack` / `VoiceSecondaryWeaponAttack` / `VoiceEnter` / `VoiceCapture` / `VoiceHarvest` / `VoiceDeploy` / `VoiceCrashing` / `VoiceSelectDeactivated` / `VoiceSelectEnslaved` | 语音事件引用 | 字符串（事件名，空白分隔） | `""` | 音频审计键表 `AudioCatalog.cpp:900-914` |
| `Locomotor` | 机动体引用 | 字符串（GUID 原样保留） | `""` | 单位装载函数区间 |
| `Cloakable` / `CloakingSpeed` | 隐身相关 | 布尔/整型 | `false` / `0` | 同上 |
| `Organic` | `organic` | 布尔 | `false` | 同上 |
| `Crusher` / `Crushable` | 碾压相关 | 布尔 | `false` | 同上 |
| `SelfHealing` | `selfHealing` | 布尔 | `false` | 同上 |
| `Size` | 载运体型 | 整型 | `0` | 同上 |
| `Deployer` / `IsSimpleDeployer` / `DeployTime` | 部署相关 | 布尔/整型 | `false` / `0` | 同上 |

### 4.2 建筑（`BuildingType`，`loadBuildingType`，`RulesDatabase.cpp:4306-4820`）

| INI 键 | 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- |
| `UIName` | `uiName` | 字符串 | `id` | `4310` |
| `Name` | `name` | 字符串 | `id` | `4311` |
| `Image` | `image` | 字符串 | `""` | `4312` |
| `AmbientSound` | `ambientSound` | 字符串 | `""` | `4313` |
| `TurretRotateSound` | `turretRotateSound` | 字符串 | `""` | `4314` |
| `AlternateArcticArt` | `alternateArcticArt` | 布尔 | `false` | `4315` |
| `Owner` | `owner` | 字符串列表 | 空 | `4316` |
| `RequiredHouses` | `requiredHouses` | 字符串列表 | 空 | `4317` |
| `ForbiddenHouses` | `forbiddenHouses` | 字符串列表 | 空 | `4318` |
| `TechLevel` | `techLevel` | 整型 | 缺省 | `4319` |
| `BuildLimit` | `buildLimit` | 整型 | `0` | `4320-4321` |
| `Trainable` | `veterancy` | 布尔 | `false` | `4330` |
| `Prerequisite` | `prerequisite` | 字符串列表 | 空 | `4344` |
| `DontScore` | `dontScore` | 布尔 | `false` | `4346` |
| `Insignificant` | `insignificant` | 布尔 | `false` | `4347` |
| `Strength` | `strength` | 整型 | `0` | `4348` |
| `ThreatPosed` | `threatPosed` | 整型 | `0` | `4349` |
| `NoShadow` | `noShadow` | 布尔 | `false` | `4350` |
| `Immune` | `immune` | 布尔 | `false` | `4351` |
| `TypeImmune` | `typeImmune` | 布尔 | `false` | `4352` |
| `ImmuneToPoison` | `immuneToPoison` | 布尔 | `false` | `4365` |
| `Armor` | `armor` | 字符串 | `""` | `4378` |
| `Explodes` | `explodes` | 布尔 | `false` | `4379` |
| `DeathWeapon` | `deathWeapon` | 字符串（武器 ID） | `""` | `4380` |
| `Primary` | `primaryWeapon` | 字符串 | `""` | `4385` |
| `Secondary` | `secondaryWeapon` | 字符串 | `""` | `4386` |
| `ElitePrimary` | `elitePrimaryWeapon` | 字符串 | `""` | `4387` |
| `EliteSecondary` | `eliteSecondaryWeapon` | 字符串 | `""` | `4388` |
| `TurretCount` | `turretCount` | 整型 | `0` | `4389` |
| `IsChargeTurret` | `chargeTurret` | 布尔 | `false` | `4412` |
| `IsGattling` | `gattling` | 布尔 | `false` | `4413` |
| `ROT` | `rot` | 整型 | `0` | `4440` |
| `Power` | `power` | 整型 | `0` | `4441` |
| `Powered` | `powered` | 布尔 | `false` | `4442` |
| `Drainable` | `drainable` | 布尔 | `false` | `4443` |
| `PoweredSpecial` | `poweredSpecial` | 布尔 | `false` | `4444` |
| `Overpowerable` | `overpowerable` | 布尔 | `false` | `4445` |
| `NavalTargeting` | `navalTargeting` | 整型/布尔 | 缺省 | `4446` |
| `LandTargeting` | `landTargeting` | 整型/布尔 | 缺省 | `4447` |
| `AirRangeBonus` | `airRangeBonus` | 浮点 | `0` | `4450` |
| `Sight` | `sight` | 整型 | `0` | `4451` |
| `Organic` | `organic` | 布尔 | `false` | `4452` |
| `Cloakable` | `cloakable` | 布尔 | `false` | `4453` |
| `Sensors` | `sensors` | 布尔 | `false` | `4459` |
| `SensorArray` | `sensorArray` | 布尔 | `false` | `4460` |
| `BuildCat` | `buildCat` | 字符串 | `""` | `4465` |
| `Adjacent` | `adjacent` | 布尔 | `false` | `4466` |
| `BaseNormal` | `baseNormal` | 布尔 | `false` | `4467` |
| `IsBaseDefense` | `isBaseDefense` | 布尔 | `false` | `4471` |
| `PowersUnit` | `powersUnit` | 字符串 | `""` | `4474` |
| `Factory` | `factory` | 字符串 | `""` | `4475` |
| `FreeUnit` | `freeUnit` | 字符串 | `""` | `4476` |
| `DeploysInto` | `deploysInto` | 字符串 | `""` | `4477` |
| `UndeploysInto` | `undeploysInto` | 字符串 | `""` | `4478` |
| `Enslaves` | `enslaves` | 布尔 | `false` | `4479` |
| `Deployer` | `deployer` | 布尔 | `false` | `4480` |
| `DeployFire` | `deployFire` | 字符串 | `""` | `4481` |
| `DeployToFire` | `deployToFire` | 布尔 | `false` | `4482` |
| `UnloadingClass` | `unloadingClass` | 字符串 | `""` | `4485` |
| `DeployTime` | `deployTime` | 整型 | `0` | `4486` |
| `DeployFacing` | `deployFacing` | 整型 | `0` | `4487` |
| `NoMovingFire` | `noMovingFire` | 布尔 | `false` | `4488` |
| `Refinery` | `refinery` | 布尔 | `false` | `4489` |
| `DockUnload` | `dockUnload` | 布尔 | `false` | `4490` |
| `Wall` | `wall` | 布尔 | `false` | `4506` |
| `Gate` | `gate` | 布尔 | `false` | `4507` |
| `Crusher` | `crusher` | 布尔 | `false` | `4512` |
| `Crushable` | `crushable` | 布尔 | `false` | `4513` |
| `OmniCrusher` | `omniCrusher` | 布尔 | `false` | `4514` |
| `Repairable` | `repairable` | 布尔 | `false` | `4517` |
| `SelfHealing` | `selfHealing` | 布尔 | `false` | `4518` |
| `Unsellable` | `unsellable` | 布尔 | `false` | `4523` |
| `Capturable` | `capturable` | 布尔 | `false` | `4524` |
| `Spyable` | `spyable` | 布尔 | `false` | `4525` |
| `CanC4` | `canC4` | 布尔 | `false` | `4526` |
| `Bombable` | `bombable` | 布尔 | `false` | `4527` |
| `BombSight` | `bombSight` | 布尔 | `false` | `4528` |
| `WaterBound` | `waterBound` | 布尔 | `false` | `4539` |
| `Naval` | `naval` | 布尔 | `false` | `4540` |
| `NeedsEngineer` | `needsEngineer` | 布尔 | `false` | `4541` |
| `CaptureEvaEvent` | `captureEvaEvent` | 字符串 | `""` | `4542` |
| `CanBeOccupied` | `canBeOccupied` | 布尔 | `false` | `4546` |
| `CanOccupyFire` | `canOccupyFire` | 布尔 | `false` | `4547` |
| `DistributedFire` | `distributedFire` | 布尔 | `false` | `4548` |
| `MaxNumberOccupants` | `maxNumberOccupants` | 整型 | `0` | `4549` |
| `Pip` | `pip` | 字符串 | `""` | `4550` |
| `PipScale` | `pipScale` | 字符串 | `""` | `4551` |
| `PipsDrawForAll` | `pipsDrawForAll` | 布尔 | `false` | `4552` |
| `Explosion` | `explosion` | 字符串 | `""` | `4553` |
| `DestroyAnim` | `destroyAnim` | 字符串 | `""` | `4554` |
| `LeaveRubble` | `leaveRubble` | 布尔 | `false` | `4555` |
| `InvisibleInGame` | `invisibleInGame` | 布尔 | `false` | `4556` |
| `LightVisibility` | `lightVisibility` | 整型/浮点 | `0` | `4558` |
| `LightIntensity` | `lightIntensity` | 浮点 | `0` | `4568` |
| `LightRedTint` / `LightGreenTint` / `LightBlueTint` | 灯光三色 | 颜色/浮点 | `0` | `4569-4571` |
| `MinDebris` / `MaxDebris` | 残骸数量 | 整型 | `0` | `4572` / `4574` |
| `DebrisAnims` | `debrisAnims` | 字符串列表 | 空 | `4575` |
| `DebrisTypes` | `debrisTypes` | 字符串列表 | 空 | `4576` |
| `DebrisMaximums` | `debrisMaximums` | 整型列表 | 空 | `4577` |
| `ConstructionYard` | `constructionYard` | 布尔 | `false` | `4578` |
| `WeaponsFactory` | `weaponsFactory` | 布尔 | `false` | `4579` |
| `FactoryPlant` | `factoryPlant` | 布尔 | `false` | `4580` |
| `UnitsCostBonus` | `unitsCostBonus` | 浮点 | `0` | `4587` |
| `UnitRepair` / `UnitReload` / `UnitSell` | 维修/装填/出售系数 | 浮点 | `0` | `4606-4608` |
| `Hospital` / `Armory` / `Ammo` / `Cloning` | 功能标志 | 布尔/整型 | `false`/`0` | `4616-4618` |
| `GapGenerator` | `gapGenerator` | 布尔 | `false` | `4628` |
| `SecretLab` | `secretLab` | 布尔 | `false` | `4639` |
| `Grinding` | `grinding` | 布尔 | `false` | `4667` |
| `Bunker` | `bunker` | 布尔 | `false` | `4668` |
| `InfantryAbsorb` | `infantryAbsorb` | 整型 | `0` | `4669` |
| `Passengers` | `passengers` | 整型 | `0` | `4670` |
| `SizeLimit` | `sizeLimit` | 整型 | `0` | `4671` |
| `ExtraPower` | `extraPower` | 整型 | `0` | `4672` |
| `OrePurifier` | `orePurifier` | 布尔 | `false` | `4673` |
| `SpySat` | `spySat` | 布尔 | `false` | `4674` |
| `Radar` | `radar` | 布尔 | `false` | `4675` |
| `Turret` | `turret` | 布尔 | `false` | `4676` |
| `TurretAnim` | `turretAnim` | 字符串 | `""` | `4677` |
| `TurretAnimX` / `TurretAnimY` / `TurretAnimZAdjust` | 炮塔动画偏移 | 整型 | `0` | `4680-4682` |
| `TurretRecoil` | `turretRecoil` | 整型 | `0` | `4683` |
| `ChargedAnimTime` | `chargedAnimTime` | 整型 | `0` | `4699` |
| `SuperWeapon` | `superWeapon` | 字符串（超武 ID） | `""` | `4700` |
| `SuperWeapon2` | `superWeapon2` | 字符串 | `""` | `4701` |
| `SuperWeapons` | `extraSuperWeapons` | 字符串列表 | 空 | `4702` |
| `CanPassiveAquire` / `CanPassiveAcquire` | 主动索敌 | 布尔 | `true` 类缺省 | `4703-4706` |
| `CanRetaliate` | `canRetaliate` | 布尔 | `true` 类缺省 | `4708` |
| `Storage` | `storage` | 整型 | `0` | `4709` |
| `SlavesNumber` / `SlaveRegenRate` / `SlaveReloadRate` | 奴隶相关 | 整型 | `0` | `4710-4712` |
| `HarvestRate` | `harvestRate` | 整型/浮点 | `0` | `4713` |
| `Soylent` | `soylent` | 布尔 | `false` | `4714` |
| `Cost` | `cost` | 整型 | `0` | `4715` |
| `Crewed` | `crewed` | 布尔 | `false` | `4716` |
| `CrateBeneath` / `CrateBeneathIsMoney` | 掉落箱子 | 布尔 | `false` | `4717-4718` |
| `RevealToAll` | `revealToAll` | 布尔 | `false` | `4719` |
| `CreateUnitSound` / `DamageSound` / `DieSound` / `CrushSound` / `WorkingSound` / `NotWorkingSound` | 声音事件引用 | 字符串 | `""` | `4720-4725` |

### 4.3 武器（`WeaponType`，`loadWeaponType`，`RulesDatabase.cpp:4821-4928`）

| INI 键 | 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- |
| `Damage` | `damage` | 整型 | `0` | `4825` |
| `ROF` | `rof` | 整型 | `0` | `4826` |
| `Range` | `range` | 浮点 | `0.0` | `4827` |
| `MinimumRange` | `minimumRange` | 浮点 | `0.0` | `4828` |
| `Projectile` | `projectile` | 字符串（投射物 ID） | `""` | `4829` |
| `Speed` | `speed` | 整型 | `0` | `4830` |
| `Warhead` | `warhead` | 字符串（弹头 ID） | `""` | `4831` |
| `Burst` | `burst` | 整型 | `0` | `4832` |
| `BurstDelay` | `burstDelay` | 整型（夹取 `[1,36000]`） | `3` | `4833` |
| `BurstDelays` | `burstDelays` | 整型列表（上限 256） | 空 | `4835-4837` |
| `TurboBoost` | `turboBoost` | 布尔 | `false` | `4838` |
| `Report` | `report` | 字符串（声音事件） | `""` | `4839` |
| `Anim` | `anim` | 字符串 | `""` | `4840` |
| `OccupantAnim` | `occupantAnim` | 字符串 | `""` | `4841` |
| `OpenToppedAnim` | `openToppedAnim` | 字符串 | `""` | `4842` |
| `Charges` | `charges` | 布尔 | `false` | `4843` |
| `Bright` | `bright` | 布尔 | `false` | `4844` |
| `DecloakToFire` | `decloakToFire` | 布尔 | `true` | `4845` |
| `RevealOnFire` | `revealOnFire` | 布尔 | `true` | `4846` |
| `CellRangefinding` | `cellRangefinding` | 布尔 | `false` | `4847` |
| `MigAttackCursor` | `migAttackCursor` | 布尔 | `false` | `4848` |
| `SabotageCursor` | `sabotageCursor` | 布尔 | `false` | `4849` |
| `NeverUse` | `neverUse` | 布尔 | `false` | `4850` |
| `Suicide` | `suicide` | 布尔 | `false` | `4851` |
| `FireInTransport` | `fireInTransport` | 布尔 | `true` | `4852` |
| `IsLaser` | `laser` | 布尔 | `false` | `4853` |
| `IsHouseColor` | `houseColorLaser` | 布尔 | `false` | `4854` |
| `DiskLaser` | `diskLaser` | 布尔 | `false` | `4855` |
| `DrainWeapon` | `drainWeapon` | 布尔 | `false` | `4856` |
| `LaserInnerColor` / `LaserOuterColor` / `LaserOuterSpread` | 激光颜色 | 颜色字符串 | `""` | `4857-4859` |
| `LaserDuration` | `laserDuration` | 整型（下限 0） | `0` | `4860-4861` |
| `LaserThickness` | `laserThickness` | 整型（夹取 `[1,32]`） | `1` | `4862-4866` |
| `IsSonic` | `sonic` | 布尔 | `false` | `4867` |
| `AmbientDamage` | `ambientDamage` | 整型 | `0` | `4868` |
| `IsElectricBolt` | `electricBolt` | 布尔 | `false` | `4869` |
| `IsAlternateColor` | `alternateElectricColor` | 布尔 | `false` | `4870-4871` |
| `IsRadBeam` | `radBeam` | 布尔 | `false` | `4872` |
| `IsMagBeam` | `magBeam` | 布尔 | `false` | `4873` |
| `IsRailgun` | `isRailgun` | 布尔 | `false` | `4874` |
| `UseSparkParticles` | `useSparkParticles` | 布尔 | `false` | `4875-4880` |
| `UseFireParticles` | `useFireParticles` | 布尔 | `false` | `4881-4886` |
| `AttachedParticleSystem` | `attachedParticleSystem` | 字符串（粒子系统 ID） | `""` | `4887-4889` |
| `Spawner` | `spawner` | 布尔 | `false` | `4890` |
| `LimboLaunch` | `limboLaunch` | 布尔 | `false` | `4891` |
| `InfiniteMindControl` | `infiniteMindControl` | 布尔 | `false` | `4892-4897` |
| `FireOnce` | `fireOnce` | 布尔 | `false` | `4898` |
| `PermanentMindControl` | `permanentMindControl` | 布尔（兼容 `PermaControl`） | `false` | `4899-4904` |
| `DisguiseFakeBlinkTime` | `disguiseFakeBlinkTime` | 整型（下限 0） | `0` | `4905-4908` |
| `DisguiseFireOnly` | `disguiseFireOnly` | 布尔 | `false` | `4909-4914` |
| `AreaFire` | `areaFire` | 布尔 | `false` | `4915` |
| `IsRadEruption` | `isRadEruption` | 布尔 | `false` | `4916` |
| `RadLevel` | `radLevel` | 整型（下限 0） | `0` | `4917` |
| `OmniFire` | 生命周期 `omniFire` | 布尔 | `false` | `4920` |
| `DistributedFire` | 生命周期 `distributedFire` | 布尔 | `false` | `4922` |

### 4.4 投射物（`ProjectileType`，`loadProjectileType`，`RulesDatabase.cpp:4930-4985`）

| INI 键 | 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- |
| `Image` | `image` | 字符串 | `""` | `4934` |
| `Palette` | `palette` | 字符串 | `""` | `4935` |
| `Color` | `color` | 颜色字符串 | `""` | `4936` |
| `Arcing` | `arcing` | 布尔 | `false` | `4939` |
| `Floater` | `floater` | 布尔 | `false` | `4940` |
| `ROT` | `rot` | 整型 | `0` | `4941` |
| `AA` | `aa` | 布尔 | `false` | `4942` |
| `AG` | `ag` | 布尔（显式存在性另存 `agExplicit`） | `true` | `4943-4944` |
| `AN` | `an` | 布尔 | `false` | `4945` |
| `ASW` | `asw` | 布尔 | `false` | `4946` |
| `Inviso` | `inviso` | 布尔 | `false` | `4947` |
| `Animates` | `animates` | 布尔 | `false` | `4948` |
| `Inaccurate` | `inaccurate` | 布尔 | `false` | `4949` |
| `FlakScatter` | `flakScatter` | 布尔 | `false` | `4950` |
| `SubjectToCliffs` | `subjectToCliffs` | 布尔 | `false` | `4951` |
| `SubjectToElevation` | `subjectToElevation` | 布尔 | `false` | `4952-4953` |
| `SubjectToWalls` | `subjectToWalls` | 布尔 | `false` | `4954` |
| `High` | `high` | 布尔 | `false` | `4955` |
| `VeryHigh` | `veryHigh` | 布尔 | `false` | `4956` |
| `Shadow` | `shadow` | 布尔 | `true` | `4957-4962` |
| `Vertical` | `vertical` | 布尔 | `false` | `4963` |
| `Airburst` | `airburst` | 布尔 | `false` | `4964` |
| `Proximity` | `proximity` | 布尔 | `false` | `4965` |
| `Ranged` | `ranged` | 布尔 | `false` | `4966` |
| `Dropping` | `dropping` | 布尔 | `false` | `4967` |
| `Level` | `level` | 布尔 | `false` | `4968` |
| `CourseLockDuration` | `courseLockDuration` | 整型（下限 0） | `0` | `4969` |
| `FirersPalette` | `firersPalette` | 布尔 | `false` | `4970` |
| `AnimPalette` | `animPalette` | 布尔 | `false` | `4971` |
| `Frames` | `frames` | 整型 | `0` | `4972` |
| `Translucency` | `translucency` | 整型 | `0` | `4973` |
| `Cluster` | `cluster` | 整型 | `0` | `4974` |
| `Acceleration` | `acceleration` | 整型 | `3` | `4975` |
| `Trailer` | `trailer` | 字符串（动画 ID） | `""` | `4976` |
| `TrailerSeperation` | `trailerSeperation` | 整型 | `0` | `4977`（源码键名拼写为 `Seperation`，原样保留） |
| `AirburstWeapon` | `airburstWeapon` | 字符串（武器 ID） | `""` | `4978` |
| `ShrapnelWeapon` | `shrapnelWeapon` | 字符串（武器 ID） | `""` | `4979` |
| `ShrapnelCount` | `shrapnelCount` | 整型 | `0` | `4980-4981` |

### 4.5 弹头（`WarheadType`，`loadWarheadType`，`RulesDatabase.cpp:5351-5433`）

| INI 键 | 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- |
| `Shield.Penetrate` | `shieldPenetrate` | 布尔 | `false` | `5356` |
| `CellSpread` | `cellSpread` | 浮点 | `0.0` | `5357` |
| `PercentAtMax` | `percentAtMax` | 浮点 | `0.0` | `5358` |
| `Verses` | `verses` | 百分比字符串（原样保留） | `""` | `5359` |
| `AnimList` | `animList` | 字符串 | `""` | `5360` |
| `Explosion` | `explosion` | 字符串 | `""` | `5361` |
| `Particle` | `particle` | 字符串（粒子 ID） | `""` | `5362` |
| `ParticleSystem` | `particleSystem` | 字符串（粒子系统 ID） | `""` | `5363` |
| `InfDeath` | `infDeath` | 整型 | `0` | `5364` |
| `ProneDamage` | `proneDamage` / `proneDamageMultiplier` | 字符串 / 比率（下限 0） | `""` / `1.0` | `5365-5369` |
| `CombatLightSize` | `combatLightSize` | 比率 | `0.0` | `5370-5375` |
| `CLDisableRed` / `CLDisableGreen` / `CLDisableBlue` | 灯光禁用三色 | 布尔 | `false` | `5376-5378` |
| `Wall` | `wall` | 布尔 | `false` | `5379` |
| `PenetratesBunker` | `penetratesBunker` | 布尔 | `false` | `5380` |
| `Wood` | `wood` | 布尔 | `false` | `5381` |
| `Psychedelic` | `psychedelic` | 布尔 | `false` | `5382` |
| `Poison` | `poison` | 布尔 | `false` | `5383` |
| `AffectsAllies` | `affectsAllies` | 布尔（默认随 `psychedelic`/自杀武器/`poison`） | 动态 | `5392-5394` |
| `AffectsEnemies` | `affectsEnemies` | 布尔 | `true` | `5395` |
| `Bright` | `bright` | 布尔 | `false` | `5396` |
| `Conventional` | `conventional` | 布尔 | `false` | `5397` |
| `Sparky` | `sparky` | 布尔 | `true` | `5398` |
| `Fire` | `fire` | 布尔 | `false` | `5399` |
| `Tiberium` | `tiberium` | 布尔 | `false` | `5400` |
| `MindControl` | `mindControl` | 布尔 | `false` | `5401` |
| `Temporal` | `temporal` | 布尔 | `false` | `5402` |
| `Radiation` | `radiation` | 布尔 | `false` | `5403` |
| `ElectricAssault` | `electricAssault` | 布尔 | `false` | `5404-5409` |
| `PsychicDamage` | `psychicDamage` | 布尔 | `false` | `5410` |
| `MakesDisguise` | `makesDisguise` | 布尔 | `false` | `5411` |
| `Rocker` | `rocker` | 布尔 | `false` | `5412` |
| `Parasite` | `parasite` | 布尔 | `false` | `5413` |
| `Culling` | `culling` | 布尔 | `false` | `5414` |
| `Paralyzes` | `paralyzes` | 整型（下限 0） | `0` | `5415` |
| `IvanBomb` | `ivanBomb` | 布尔 | `false` | `5416` |
| `BombDisarm` | `bombDisarm` | 布尔 | `false` | `5417` |
| `Airstrike` | `airstrike` | 布尔 | `false` | `5418` |
| `IsLocomotor` | `isLocomotor` | 布尔 | `false` | `5419` |
| `Locomotor` | `locomotor` | 字符串（GUID 原样） | `""` | `5420` |
| `CellInset` | `cellInset` | 整型（下限 0） | `0` | `5421` |
| `ShakeXlo` / `ShakeXhi` / `ShakeYlo` / `ShakeYhi` | 生命周期震屏 | 整型 | `0` | `5424-5427` |

### 4.6 超武（`SuperWeaponRule`，`loadSuperWeaponRule`，`RulesDatabase.cpp:1385-1826`）

| INI 键 | 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- |
| `UIName` | `uiName` | 字符串 | `id` | `1410` |
| `Name` | `name` | 字符串 | `id` | `1411` |
| `Type` | `type` | 字符串（超武类型枚举） | `""` | `1412` |
| `Action` | `action` | 字符串 | `""` | `1413` |
| `SidebarImage` | `sidebarImage` | 字符串 | `""` | `1414` |
| `PreDependent` | `preDependent` | 字符串 | `""` | `1415` |
| `RechargeVoice` / `ChargingVoice` / `ImpatientVoice` / `SuspendVoice` | 语音事件 | 字符串 | `""` | `1416-1419` |
| `SpecialSound` / `StartSound` | 声音事件 | 字符串 | `""` | `1420-1421` |
| `RechargeTime` | 充能时间 | 整型 | 缺省 | `1424` |
| `Range` | `range` | 浮点/整型 | `0` | `1426` |
| `IsPowered` | `isPowered` | 布尔 | `false` | `1435` |
| `PreClick` / `PostClick` | 动作链 | 字符串 | `""` | `1436-1437` |
| `ShowTimer` | `showTimer` | 布尔 | `false` | `1438` |
| `TargetMode` | `targetMode` | 枚举/字符串 | `""` | `1451` |
| `Warhead` | `warhead` | 字符串（弹头 ID） | `""` | `1458` |
| `PayloadWeapon` | `payloadWeapon` | 字符串（武器 ID） | `""` | `1459` |
| `PayloadProjectile` | 负载投射物 | 字符串（投射物 ID） | `""` | `1461` |
| `PayloadWarhead` | 负载弹头 | 字符串（弹头 ID） | `""` | `1464` |
| `LaunchAnim` / `ImpactAnim` / `ActiveAnim` / `InvokeAnim` / `FirstAnim` / `SecondAnim` / `BoltAnim` / `CloudAnim` | 动画引用 | 字符串 | `""` | `1466-1472` |
| `PlaneType` | `planeType` | 字符串（单位 ID） | `""` | `1481` |
| `Damage` / `Radius` | 伤害 / 半径 | 整型 | `0` | `1492-1493` |
| `Projectile` | `projectile` / `payloadProjectile` | 字符串 | `""` | `1588-1590` |
| `AnimList` | `impactAnim` | 字符串 | `""` | `1592` |
| `DetonationAltitude` | `nuclearAltitudeLeptons` | 整型 | 缺省 | `1596` |
| `Acceleration` | `nuclearUpAcceleration` / `nuclearDownAcceleration` | 浮点/整型 | 缺省 | `1597-1598` |
| `ROT` | `nuclearDownSpeed` | 整型 | `0` | `1601` |
| `FlightLevel` | `supportFlightLevelLeptons` | 整型（下限 1） | `1500` | `1482` |
| `ParadropRadius` | `paradropRadiusLeptons` | 整型（下限 0） | `1024` | `1483` |
| `ParachuteMaxFallRate` | `parachuteFallRate` | 整型（下限 1） | `-3` 绝对值 | `1484` |
| `CrateMaximum` / `CrateMinimum` / `CrateRadius` / `CrateRegen` / `SoloCrateMoney` | 箱子规则（`[CrateRules]`） | 整型/浮点 | `255`/`1`/`3.0`/`3.0`/`5000` | `1981-2005` |

> 超武各 `Type` 分支（闪电风暴、铁幕、力场、心灵控制、突变、照相机、伞兵等）在 `1492-1805` 区间内按类型分别读取 `[General]`/`[CombatDamage]` 回退键；完整键表需逐分支对照源码。

### 4.7 覆盖物 / 碎屑 / 资源 / 地形 / 通行类型

| 类型 | INI 键 | 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- | --- |
| 覆盖物 `OverlayType` | `Name` | `name` | 字符串 | `id` | `5006` 区间，`4996` |
| 覆盖物 | `Image` | `image` | 字符串 | `id` | `4997` |
| 覆盖物 | `Theater` / `NewTheater` | `theater` / `newTheater` | 布尔 | `false` | `4998-5000` |
| 覆盖物 | `Palette` / `AltPalette` | `palette` / `altPalette` | 字符串 | `""` | `5001-5002` |
| 覆盖物 | `ToOverlay` | `toOverlay` | 字符串 | `""` | `5003` |
| 覆盖物 | `Foundation` | `foundation` | 字符串 | `""` | `5004` |
| 覆盖物 | `Tiberium` | `tiberium` | 布尔 | `false` | `5005` |
| 覆盖物 | `Crate` / `CrateTrigger` | `crate` / `crateTrigger` | 布尔 | `false` | `5009-5010` |
| 覆盖物 | `LegalTarget` | `legalTarget` | 布尔 | `false` | `5011` |
| 覆盖物 | `RadarInvisible` / `RadarColor` | `radarInvisible` / `radarColor` | 布尔 / 颜色 | `false` / `""` | `5012-5013` |
| 覆盖物 | `NoUseTileLandType` | `noUseTileLandType` | 布尔 | `false` | `5014-5015` |
| 覆盖物 | `DrawFlat` | `drawFlat` | 布尔 | `false` | `5016` |
| 覆盖物 | `Wall` | `wall` | 布尔 | `false` | `5017` |
| 覆盖物 | `IsARock` | `isARock` | 布尔 | `false` | `5018` |
| 覆盖物 | `ZAdjust` / `ShadowIndex` | `zAdjust` / `shadowIndex` | 整型 | `0` / `-1` | `5019-5020` |
| 碎屑 `SmudgeType` | `Crater` / `Burn` | `crater` / `burn` | 布尔 | `false` | `5033-5034` |
| 碎屑 | `Width` / `Height` | `width` / `height` | 整型（下限 1） | `1` | `5035-5036` |
| 资源 `TiberiumType` | `Name` / `Image` / `Value` / `Growth` / `GrowthPercentage` / `Spread` / `SpreadPercentage` / `Power` / `Color` | 同名字段 | 字符串 / 整型 / 浮点 / 颜色 | 见 `5049-5059` | `5040-5062` |
| 地形 `TerrainType` | `Name` | `name` | 字符串 | `id` | `5073` |
| 地形 | `AmbientSound` | `ambientSound` | 字符串 | `""` | `5074` |
| 地形 | `SpawnsTiberium` | `spawnsTiberium` | 布尔 | `false` | `5075` |
| 地形 | `IsAnimated` / `AnimationRate` / `AnimationProbability` | 动画相关 | 布尔 / 整型 / 浮点 | `false` / `0` / `0.0` | `5076-5079` |
| 地形 | `Strength` / `Immune` / `RadarColor` | `strength` / `immune` / `radarColor` | 整型 / 布尔 / 颜色 | `0` / `false` / `""` | `5080-5082` |
| 通行 `LandType` | `Foot` / `Track` / `Wheel` / `Float` / `FloatBeach` / `Hover` / `Amphibious` | 各载具通行百分比 | 百分比 | 字段内联缺省 | `5093-5099` |
| 通行 | `Buildable` | `buildable` | 布尔 | 字段内联缺省 | `5100` |

### 4.8 粒子系统 / 粒子（`ParticleSystemType` / `ParticleType`）

- 装载区间：`RulesDatabase.cpp:5104-5350`。
- 关键字段族：粒子系统行为列表（`parseParticleBehavior`，`332-359`）、颜色列表（`parseParticleColorList`，`412-425`）、向量（`parseParticleVector3`，`375-386`）。
- 详细逐键映射在本章缺口中标注为待补；源码结构见 `RulesDatabase.h` 中对应结构体声明。

### 4.9 参考引擎未实现的类型（对照 YR 规则分类）

| YR 常见类型 | 参考引擎状态 |
| --- | --- |
| 独立动画（Anim）规则类型 | 未实现为规则容器；仅以字符串 ID 被武器/弹头/超武/建筑引用 |
| 轨道清单 `[WeaponTypes]` / 投射物清单 `[ProjectileTypes]` | 未枚举；引用发现制 |
| 地形 INI（theater ini）规则解析 | 由独立 `TheaterDatabase` 处理，不在规则数据库内 |
| 舰船/两栖单独类型划分 | 参考引擎未在规则层单独建类，并入 `UnitType` |

---

## 5. 规则哈希与确定性

### 5.1 两类哈希

| 哈希 | 成员 | 算法 | 出处 |
| --- | --- | --- | --- |
| 模组扩展规则哈希 | `modExtensionRulesHash_` | FNV-1a 64 位：种子 `14695981039346656037`，乘子 `1099511628211`；条目按归一化键排序，序列化形式 `canonicalId(section)|lower(key)=value\n`，逐字节异或乘算 | `RulesModExtensions.inl:87-103`；接口 `RulesDatabase.h:1627`、成员 `1735` |
| 规则细节哈希 | `detailRulesHash_` | 由 `runtimeDetailRulesHash(*this)` 序列化规则库后 FNV 混合；装载完成后计算一次 | `RuntimeDetailRulesHash.h:10-129`；计算点 `RulesDatabase.cpp:3221`；接口 `RulesDatabase.h:1669`、成员 `1780` |

### 5.2 校验与用途

- 音频缓存失效判断：环境音/炮塔声音频在准备前比对 `rules.getDetailRulesHash()`，哈希变化才重建音频准备态（`RuntimeObjectAmbientRuntime.inl:4-9`、`RuntimeTurretSoundRuntime.inl:4-6`）。
- 会话稳定哈希：并入壳层稳定哈希混合（`RuntimeShell.cpp:10424`）。
- 多人权威一致性：并入多人权威规则哈希混合（`RuntimeSessionWorld.cpp:11215`），用于确定性校验/状态一致性比对。
- 模组扩展哈希：供规则扩展（护盾、附着效果）做规则版本/变更判定（`RuntimeDetailRulesHash.h:14` 输出到流）。

### 5.3 确定性要点

- 哈希输入先按归一化键排序，消除段内顺序差异。
- section/key 比较走 ASCII 小写归一（`canonicalIdKey`），保证大小写不敏感的规则文本产生同一哈希。
- 浮点/枚举以规范化文本形式参与序列化，避免内存布局差异影响哈希稳定性。

---

## 6. 美术规则与声音规则挂载

### 6.1 artmd.ini → 规则对象（`src/data/art/`）

装载：`ArtDatabase::loadFromIni`（`ArtDatabase.cpp:951-973`），遍历 INI 所有 section 逐个构造 `ArtType`；重复 section 记警告并忽略（`963-966`）。

链接：`buildLinks`（`ArtDatabase.cpp:881-947`）对每类规则对象调用 `chooseArtId(rule.id, rule.image)`，先按规则 `Image` 字段、否则按规则 ID 在美术库中查找；找不到则生成缺失链接记录。

代表性美术键（`ArtDatabase.cpp`）：

| INI 键 | ArtType 字段 | 类型 | 默认值 | 出处行号 |
| --- | --- | --- | --- | --- |
| `Image` | 美术引用基准 | 字符串 | `""` | `1034` |
| `Cameo` / `AltCameo` | 图标 / 备选图标 | 字符串 | `""` | `1035-1036` |
| `Sequence` | 序列引用 | 字符串 | `""` | `1037` |
| `Foundation` | 地基 | 字符串 | `""` | `1038` |
| `Palette` / `CustomPalette` / `AltPalette` | 调色板 | 字符串 | `""` | `1039-1041` |
| `Layer` | 图层 | 字符串/枚举 | `""` | `1042` |
| `Theater` / `NewTheater` | 剧院美术 | 布尔 | `false` | `1043-1044` |
| `Remapable` | `remapable` | 布尔 | `false` | `1047` |
| `Normalized` | `normalized` | 布尔 | `false` | `1048` |
| `Voxel` | `voxel` | 布尔 | `false` | `1049` |
| `SpawnDelay` | `spawnDelay` | 整型（下限 0） | `0` | `1050` |
| `Rotates` | `rotates` | 布尔 | `false` | `1051` |
| `UseLineTrail` / `LineTrailColor` / `LineTrailColorDecrement` | 尾迹线 | 布尔 / 颜色 / 整型 | `false` / `""` / `16` | `1052-1055` |
| `Turret` | `turret` | 布尔 | `false` | `1058` |
| `DemandLoad` / `DemandLoadBuildup` / `FreeBuildup` | 按需加载 | 布尔 | `false` | `1059-1061` |
| `DamageLevels` | `damageLevels` | 整型 | `0` | `1063` |
| `Height` | `height` | 整型 | `0` | `1064` |
| `TurretOffset` | 炮塔偏移 | 坐标 | `0` | `1065` |
| `PrimaryFireFLH` / `SecondaryFireFLH` | 枪口位置 | FLH 坐标 | `0` | `1082` / `1087` |
| `TrailerAnim` / `ExpireAnim` | 尾迹/消亡动画 | 字符串 | `""` | `1116-1117` |
| `SpawnsParticle` | 粒子引用 | 字符串 | `""` | `1121` |
| `Report` / `StartSound` | 声音事件 | 字符串 | `""` | `1132-1133` |
| `Buildup` / `BibShape` | 建造动画 / 地形边 | 字符串/枚举 | `""` | `1174-1175` |
| `GateStages` | 大门阶段 | 整型/列表 | `0` | `1202` |
| `TiberiumSpawnType` | 泰矿生成类型 | 字符串 | `""` | `1238` |
| `MuzzleFlash` | 枪口闪光 | 字符串/布尔 | 缺省 | `1283` |

建筑链接附加炮塔字段：`turretAnim`、`turretAnimIsVoxel`、`turretAnimX/Y/ZAdjust`（`ArtDatabase.cpp:915-926`），这些值来自建筑规则对象而非 artmd 段。

### 6.2 soundmd.ini → 规则对象（`src/assets/audio/`）

- 清单段 `[SoundList]`：每个条目值是事件名；按事件名取同名段（`AudioCatalog.cpp:1141-1186`）。
- 事件段样本键：`Sounds`、`Sound`、`Samples`、`Sample`、`File`、`Files`、`Russian`、`Allied`、`Yuri`；值按空白拆成样本名（`AudioCatalog.cpp:137-149`）。
- 规则侧引用：单位段内语音键 `VoiceSelect`、`VoiceMove`、`VoiceAttack`、`VoiceFeedback`、`VoiceSpecialAttack`、`VoiceSecondaryWeaponAttack`、`VoiceEnter`、`VoiceCapture`、`VoiceHarvest`、`VoiceDeploy`、`VoiceCrashing`、`VoiceSelectDeactivated`、`VoiceSelectEnslaved`（`AudioCatalog.cpp:900-914`）；`[AudioVisual]` 的 `VoiceIFVRepair` 作为共享事件单独审计（`1018-1033`）。
- 运行时装载：`RuntimeWorldAudioSystem::loadFromIni`（`RuntimeWorldAudioSystem.cpp:512`）把事件解析为可播放样本引用。
- 音乐：`[Themes]` 列出曲目名，曲目段键 `Sound`（样本）、`Normal`（是否常规曲目，默认 `yes`）（`AudioCatalog.cpp:450-479`）。

挂载链：规则对象语音/声音字段（事件名字符串）→ `soundmd.ini` 同名事件段 → 样本名 → 资产音频索引解析到可解码样本。

---

## 本章缺口

1. **粒子系统/粒子逐键映射未完整展开**：`ParticleSystemType`、`ParticleType` 装载区间（`RulesDatabase.cpp:5104-5350`）内各行为键、颜色键、向量键的字段/默认值/行号表待后续按函数逐段补齐。
2. **超武分支键表未穷举**：`loadSuperWeaponRule` 按 `Type`/`Action` 分多个分支读取 `[General]`、`[CombatDamage]` 回退键；本章仅列出公共键与代表性分支键，逐分支完整对照待补。
3. **单位/建筑部分键映射为区间标注**：`loadUnitType` 函数体较长，表中部分键以函数区间代替单行号；逐键行号需在 `3813-4306`、`4306-4820` 内逐条复核。
4. **`#` 注释与 `$` 前缀语义未实现**：源码解析器仅识别 `;` 与 `//`；若 YR 原始规则中存在 `#` 注释或 `$` 前缀引用写法，当前参考引擎不解析，移植时需确认是否补齐。
5. **Locomotor GUID 未在规则层解析**：`Locomotor` 值按原始字符串保留，未见 GUID → 机动体注册表的解析表；机动体注册表与匹配逻辑在其他章节/其他模块。
6. **`rules-overrides.ini` 无生产级文件名约定**：样例覆盖层通过 `--mod-root` 挂载与文本拼接方式叠加；生产代码未识别固定的 `rules-overrides.ini` 文件名，覆盖层语义完全复用 `appendFrom`。
7. **`RuntimePrerequisiteProcAlternate.inl` 为空占位**：`PrerequisiteProcAlternate` 别名仅在 `[General]` 读入字符串，扩展处理体未实现。
8. **动画规则类型未独立**：`[Animations]` 清单段在样例中存在，但规则库无对应容器；动画引用全部为字符串挂接，动画序列/帧规则归属 `ArtDatabase` 序列侧，待美术章节对照。
9. **theater ini 未纳入本章**：剧院/地形美术 INI 由独立 `TheaterDatabase` 处理，其段名、键表与规则数据库的耦合关系在后续剧院章节展开。
10. **音频运行时事件绑定细节**：本章覆盖规则键 → 事件段 → 样本的解析与审计链路；会话内事件触发、音量/混合通道、EVA 队列的运行时逻辑见音频运行时章节。
