# 17. 单位 / 建筑个体绘制（SHP 与 VXL）

> 静态阅读 `D:\___SYSTEM_PATH\source\repos\ConcordRTS\`，不构建、不写入源码。
> 解码层（PAL / SHP / TMP / VXL / HVA 字节布局、解码器内部状态机、源缓存条目字段）已在 `05a-图像与体素解码(PAL-SHP-TMP-VXL-HVA).md` 第 3、4 章定稿，本章只引用、不重复字节级布局。
> 源码清单与类层次见 `01-源码清单与类层次.md` 第 2.5 节；建筑子系统规则语义见 `15-建筑.md` 第 6 章；本章聚焦**个体层的绘制编排**：资源解码/缓存 → 朝向选择 → 投影 → 炮身/炮塔/炮管动画 → 阴影 → 主体/武器/辅助动画合成 → 选框/血条挂载。

## 目录

- [1. 本章定位与衔接点](#1-本章定位与衔接点)
- [2. 个体绘制总流程](#2-个体绘制总流程)
- [3. SHP 单位绘制](#3-shp-单位绘制)
- [4. VXL 体素单位绘制](#4-vxl-体素单位绘制)
- [5. 建筑绘制](#5-建筑绘制)
- [6. 几何与命中](#6-几何与命中)
- [7. 阴影、选框、归属色 / Remap](#7-阴影选框归属色--remap)
- [8. 资源就绪、缺帧兜底、缓存复用与预热准入](#8-资源就绪缺帧兜底缓存复用与预热准入)
- [9. 未实现 / 受限模式标注](#9-未实现--受限模式标注)
- [10. 本章缺口](#10-本章缺口)
- [附录 A. 覆盖渲染文件清单](#附录-a-覆盖渲染文件清单)

---

## 1. 本章定位与衔接点

### 1.1 覆盖范围

| 层 | 目录 | 职责 |
| --- | --- | --- |
| 单位 SHP 编排 | `src/render/units/` | 步兵待机/游泳/死亡序列、载具车身动力学、极地美术选择、地面载具维修记录 |
| 体素编排 | `src/render/voxel/` | VXL/HVA 动画 memo、段变换、面/壳/角点投影、炮口锚点、巨炮预热光栅任务 |
| 建筑编排 | `src/render/buildings/` | 辅助动画帧选择、生物反应堆、倒塌、维修厂、工厂出厂口、碾磨、超武、建筑深度行内核 |
| 几何 / 命中 | `src/render/geometry/` | 标准坐标、边界盒、命中跨度、武器发射位置（FLH）查询、炮口锚点、选框/血条几何 |

主编排实现集中在 `src/render/core/RuntimeMapRenderer.cpp`（匿名命名空间内的静态/内联辅助函数），缓存条目与渲染上下文结构在 `src/render/core/RuntimeMapRenderer.h`。

### 1.2 与既有章节的衔接

| 上游 / 下游 | 章节 | 衔接内容 |
| --- | --- | --- |
| 资源解码 | `05a-图像与体素解码(PAL-SHP-TMP-VXL-HVA).md` 第 3 章（SHP）、第 4 章（VXL/HVA）、4.5（源缓存与准入） | 本章只消费解码后的 `RuntimeShpFrameImage` / `RuntimeVoxelSourceCacheEntry` / `RuntimeVoxelCacheEntry`，不复述字节布局 |
| 规则 / 美术元数据 | `01-源码清单与类层次.md` 2.5；`15-建筑.md` 第 6 章 | `ArtType` 序列帧、朝向数、帧偏移、辅助动画表、建筑规则字段的语义出处 |
| 渲染管线总览（规划中第 16 章） | 待补 | 个体绘制记录如何进入深度排序、阴影通道、合成通道；本章第 2 章给出记录结构与调用点 |
| 任务调度 | `src/core/tasks/BoundedTaskExecutor.h` | 巨炮预热任务的有界执行器（见 4.6） |

### 1.3 关键缓存条目（结构字段表）

| 类型 | 出处 | 关键字段 |
| --- | --- | --- |
| `RuntimeObjectShpSpriteCacheEntry` | `src/render/core/RuntimeMapRenderer.h:129-144` | `loadAttempted`、`loaded`、`image`（主体帧）、`shadowImage`（阴影帧）、`resolvedImageId`、`sourcePath`、`frameIndex`、`frameCount`、`shadowFrameIndex`、`hasShadowFrame`、`usedSequenceFrame`、`sourceFingerprint`、`sequenceName`、`errorMessage` |
| `RuntimeAuxiliaryShpBytesCacheEntry` | `RuntimeMapRenderer.h:146-155` | 辅助 SHP 字节缓存：`bytes`、`sourceFingerprint`、`resolvedImageId`、`frameCount` |
| `RuntimeVoxelMetadata` | `RuntimeMapRenderer.h:157-168` | `imageId`、`vxlPath`、`hvaPath`、`vxlSectionCount`、`hvaSectionCount`、`hvaFrameCount`、文件大小、段名样本 |
| `RuntimeHvaTransform` | `RuntimeMapRenderer.h:170-177` | `valid`、12 元素 3×4 矩阵 `m` |
| `RuntimeVoxelSourceCacheEntry` | `RuntimeMapRenderer.h:179-188` | `vxlBytes`、`hvaBytes`、`firstFrameTransforms`、`transformsByFrame`、`errorMessage` |
| `RuntimeVoxelCacheEntry` | `RuntimeMapRenderer.h:190-209` | `vehicleBodyPoseUse`、`modelBounds`、`loaded`、`hasSprite`、`image`、`shadowPixels`、`shadowSpanCache`、`shadowSectionRequested/Applied`、`facingBucket`、`animationFrameIndex`、`rasterFitScale`、`projectedRangeX/Y`、`errorMessage` |
| `RuntimeRasterVoxel` | `RuntimeMapRenderer.cpp:176` | 光栅化后的体素单元（列/面投影产物，字段见 4.3） |
| `RuntimeRasterFace` | `RuntimeMapRenderer.cpp:193` | 光栅化后的可见面（字段见 4.3） |

---

## 2. 个体绘制总流程

### 2.1 步骤化流程

世界对象在一帧内的个体绘制分两条通道：**阴影通道**先画（或与主体通道分离记录），**主体通道**按地面体素/精灵排序键合成。下列步骤按执行顺序列出，调用点均在 `RuntimeMapRenderer.cpp`。

| # | 步骤 | 调用点（文件:行号） | 说明 |
| --- | --- | --- | --- |
| 1 | 规则美术对象解析 | `resolveObjectSpriteImageIdCandidates`（`RuntimeMapRenderer.cpp:1303`）；`findArtTypeForObject` | 由 `WorldObjectInstance` 取 `UnitType`/`BuildingType` → `ArtType`，解析图像 ID 候选（含极地后缀、替换美术） |
| 2 | 资源解码 / 缓存查询 | SHP：`loadRuntimeObjectShpSpriteFromCache`（`RuntimeMapRenderer.cpp:12949`）；辅助 SHP：`loadRuntimeAuxiliaryShpSpriteFromCache`（`src/render/core/detail/RuntimeProductionResources.h:162`）；VXL 源：`loadRuntimeVoxelSourceFromCache`（`RuntimeMapRenderer.cpp:13781`）；VXL 条目：`loadRuntimeVoxelImageFromCache`（`RuntimeMapRenderer.cpp:14531`，批量入口 `:13875`）、`loadRuntimeVoxelFromCache`（`:14554`） | 命中缓存直接复用；未命中走冷解码（解码细节见 05a 第 3、4 章） |
| 3 | 朝向选择 | 通用桶函数 `worldObjectArtFacingBucket`（`src/world/model/GameWorld.cpp:508-516`）；对象朝向 `facingBucketForObject`（`RuntimeMapRenderer.cpp:1515`）；朝向值 `facingBucketForFacingValue`（`:1522`）；SHP 方向图 `facingBucketForDirectionalShp`（`:1529`）；VXL 桶数常量 `runtimeVoxelFacingBucketCount = 32`（`:1513`） | 运行时朝向 0–255 映射到美术朝向桶；公式见 3.1 |
| 4 | 帧 / 序列选择 | 序列帧 `selectRuntimeObjectSequenceFrame`（`RuntimeMapRenderer.cpp:1912`）；主帧 `selectRuntimeObjectShpFrame`（`:2078`）；辅助动画帧 `selectRuntimeAuxiliaryAnimationFrame`（`:1634`）；待机偏移 `runtimeUnitIdleFrameOffset`（`units/RuntimeUnitIdleAnimation.inl:29`） | 步兵/载具按任务态、移动、开火、游泳、部署等序列选 strip；建筑按主帧/损坏/占领/废墟选帧 |
| 5 | 世界坐标 → 屏幕投影 | 放置结果 `RuntimeObjectWorldRenderPlacement`（`geometry/RuntimeObjectWorldRenderPlacement.h:7-13`）与解析 `runtimeObjectWorldRenderPlacement`（`:21-25`）；原生/屏幕点换算 `runtimeRenderPlacementNativePoint` / `runtimeRenderPlacementPixelPoint` / `runtimeRenderPlacementPixelRect` / `runtimeRenderPlacementNativeRect`（`geometry/win32/RuntimeRenderPlacementWin32.h:39,42,50,53`）；体素绘制中心 `runtimeVoxelModelDrawCenter`（`RuntimeMapRenderer.cpp:10152`）；投影轴 `runtimeVxlProjectionAxes`（`:1451` 声明 / `:3999` 定义） | 精灵按原生像素尺寸贴放；体素按模型单位 + fitScale 投影到屏幕 |
| 6 | 炮身 / 炮塔 / 炮管动画 | 单位体素炮塔 `tryDrawRuntimeUnitVoxelTurret`（`RuntimeMapRenderer.cpp:19884`）；单位体素炮管 `tryDrawRuntimeUnitVoxelBarrel`（`:19964`）；建筑体素炮塔 `tryDrawRuntimeStructureTurretAnim`（`:23174`）；炮塔屏幕偏移 `runtimeStructureTurretAnimScreenOffset`（`:23064`）；炮塔挂载点 `runtimeVoxelTurretOffset`（`:1453`） | 炮塔/炮管使用独立朝向（`object.turretFacing`）与独立拟合尺度；见 4.4 |
| 7 | 阴影判定与绘制 | 规则抑制 `runtimeObjectRulesSuppressShadow`（`RuntimeMapRenderer.cpp:1047`）；拆半阴影判定 `runtimeObjectSourceHasSplitShadow`（`:1036`）；可渲染阴影 `runtimeObjectShouldRenderShadow`（`:1063`）；体素阴影段 `runtimeObjectVoxelShadowSection`（`:1076`）；主体阴影挂接 `drawRuntimeAttachedVoxelShadows`（`:23947`）；世界对象阴影 `drawRuntimeWorldObjectShadow`（`:24093`）；阴影帧绘制 `drawRuntimeShpShadowFrame`（`:15486`） | 阴影在独立通道/记录中处理；见第 7 章 |
| 8 | 主体合成 | 移动单位主体 `drawRuntimeMobileGeometryBody`（`geometry/RuntimeMobileSpriteGeometry.inl:79-96`）；建筑主体 `drawRuntimeStructureGeometryBody`（`geometry/RuntimeStructureSpriteGeometry.inl:3-45`）；SHP 精灵 `tryDrawObjectShpSprite`（`RuntimeMapRenderer.cpp:23481`）；体素精灵 `tryDrawVoxelSprite`（`:23762`）；SHP 帧落地 `drawRuntimeShpFrame`（`:14593`）；体素精灵落地 `drawRuntimeVoxelImageSprite`（`:15782`） | 先伪装/间谍体，再按 VXL/SHP 分支，最后挂降落伞等附属 |
| 9 | 武器 / 辅助动画合成 | 建筑主动动画 `drawRuntimeStructureActiveAnimations`（`RuntimeMapRenderer.cpp:22428`）；辅助 SHP 落地 `drawRuntimeAuxiliaryShpSprite`（`:21038`）；辅助居中偏移 `runtimeAuxiliaryShpAdjustedCenter`（`:20762`）；工厂出厂前景 `drawRuntimeFactoryAirExitForeground`（`buildings/RuntimeFactoryAirExitForeground.inl:4`） | 建筑分前景/背景/平贴层；单位附属（烟雾、尾迹）在 effects 目录，本章不展开 |
| 10 | 选框 / 血条挂载 | 建筑选框几何 `runtimeStructureSelectionFrameGeometry`（`src/runtime/host/RuntimeShell.cpp:23199`）；选框结构 `RuntimeStructureSelectionFrameGeometry`（`RuntimeShell.cpp:22709-22717`）；血条布局 `runtimeStructureHealthBarLayout`（`RuntimeShell.cpp:22737`）；血条结构 `RuntimeStructureHealthBarLayout`（`:22729-22735`） | 选框/血条在宿主层（Shell）基于几何查询结果挂载，不属于位图合成；见 7.2 |

### 2.2 绘制记录结构

| 结构 | 出处 | 用途 |
| --- | --- | --- |
| `RuntimeFootprintDimensions` | `RuntimeMapRenderer.cpp:10289` | 占地宽高，用于排序与选框几何 |
| `RuntimeGroundBodyKind` | `RuntimeMapRenderer.cpp:10507` | 地面主体类别枚举（体素 / 精灵 / 建筑等） |
| `RuntimeGroundBodySortKey` | `RuntimeMapRenderer.cpp:10533` | 地面主体排序键 |
| `RuntimeWorldObjectDrawRecord` | `RuntimeMapRenderer.cpp:10546` | 待绘制世界对象记录（对象、中心、占地、层级标记等） |
| `RuntimeGroundBodyDrawRecord` | `RuntimeMapRenderer.cpp:10568` | 地面主体排序记录 |
| `RuntimeWorldObjectShadowDrawRecord` | `RuntimeMapRenderer.cpp:11137` | 阴影通道记录 |
| `RuntimeVoxelSpriteDrawInfo` | `RuntimeMapRenderer.cpp:15768` | 体素精灵绘制参数 |
| `RuntimeVoxelDrawResult` | `RuntimeMapRenderer.cpp:23718` | 体素绘制结果（是否出图、像素统计等） |

### 2.3 主体分支判定

移动单位主体在 `RuntimeMobileSpriteGeometry.inl:79-96` 内分派：

1. 伪装快照 `runtimeCamouflageSnapshot` → `drawRuntimeCamouflageBody`；
2. 间谍伪装快照 `runtimeSpyDisguiseSnapshot` → `drawRuntimeSpyDisguiseBody`；
3. 其余：`object.kind == Unit && objectUsesVoxelArt(...)` → `tryDrawVoxelSprite`（`RuntimeMapRenderer.cpp:23762`），否则 `tryDrawObjectShpSprite`（`:23481`）；
4. 最后 `drawRuntimeMobileParachute` 挂降落伞附属。

---

## 3. SHP 单位绘制

### 3.1 朝向帧选择（8 / 16 / 32 朝向）

**朝向桶公式**（`GameWorld.cpp:508-516`）：

```cpp
// facingCount <= 1 或 facing < 0 → 桶 0
authoredFacing = (((runtimeFacing - 32) % 256) + 256) % 256;
bucket = ((authoredFacing * facingCount) + 128) / 256 % facingCount;
```

| 项 | 取值 / 规则 | 出处 |
| --- | --- | --- |
| 美术朝向数 | `ArtType::facings`；缺省 8；夹取范围 `[1, 32]` | `RuntimeMapRenderer.cpp:1938-1940`、`:2032-2036`、`:2141-2146` |
| 隐式精灵单位布局 | 固定 8 朝向，桶函数 `facingBucketForImplicitSpriteUnit` | `RuntimeMapRenderer.cpp:2139-2150` |
| 方向 SHP 桶 | `facingBucketForDirectionalShp`（`RuntimeMapRenderer.cpp:1529`） | 用于有方向 strip 的 SHP |
| 体素桶数 | `runtimeVoxelFacingBucketCount = 32` | `RuntimeMapRenderer.cpp:1513` |
| 巨炮炮管前后遮挡判定 | 32 桶内 `[5, 20]` 炮管在前 | `runtimeGrandCannonBarrelDrawsInFront`（`RuntimeMapRenderer.cpp:114-119`） |

> **注**：朝向相关字段成对存在（`src/world/model/GameWorld.h:111-114`）：整型当前朝向 `facing` / `turretFacing`，以及平滑旋转插值器 `bodyFacingClock` / `turretFacingClock`（`RuntimeFacingClock`，定义于 `src/runtime/timing/RuntimeRuleTime.h:68-80`，字段 `initial/target/rate/elapsedNumerator/lastTick/lastFacing`）。帧选择与炮口查询主要消费当前朝向 `object.facing` / `object.turretFacing`（`RuntimeMapRenderer.cpp:19906` 一带炮塔/炮管绘制分支；炮口查询 `geometry/RuntimeWeaponMuzzleGeometry.inl:158` 同时取 `firingFacing` 与 `object.facing` 计算枪/船体偏航）；两个时钟字段记录旋转过渡状态并参与序列化（`src/world/model/GameWorld.cpp:916-919`）。

### 3.2 序列帧选择（待机 / 移动 / 开火 / 受损 / 死亡）

**候选序列** `runtimeSequenceCandidatesForObjectState`（`RuntimeMapRenderer.cpp:1688`）按任务态生成候选序列名，包括：

- 空降 `Paradrop`（`airborne && mission == "paradropdrop"`，`:1694-1696`）；
- 部署中 / 已部署 / 部署开火 / 收起（`deploying`、`deployedIdle`、`deployFire`、`deployedFire`、`undeploy`，`:1697-1712`）；
- 欢呼 `Cheer` 仅改待机动画显示，不改命令/世界态（`:1927-1937`）；
- 游泳序列由 `runtimeInfantryIsSwimmingForRender`（`units/RuntimeInfantrySwimAnimation.inl:1-6`）判定后并入候选（`RuntimeMapRenderer.cpp:1950`）。

**序列命中** `selectRuntimeObjectSequenceFrame`（`RuntimeMapRenderer.cpp:1912-2074`）：

1. `ArtType::sequenceFrames` 为空或帧数 ≤ 1 → 不命中（`:1920-1924`）；
2. 缓存键由 `artId|kind|mission|airborne|render|moving|swimming|prone|deployed|facing|frames` 组成（`:1951-1962`），命中 `sequenceFrameMetadataByKey` 直接复用（`:1963-2002`）；
3. 候选序列名表 → `findRuntimeSequenceFrame` 命中 `SequenceFrame`（`:2004-2016`）；
4. `framesPerFacing` 夹取 `[1, frameCount]`；`facingStride = max(framesPerFacing, facingMultiplier)`；`facingCount` 由 `facingMultiplier > 0` 决定是否启用朝向（`:2021-2036`）；
5. 相位 `runtimeSequenceFramePhase(...)`；帧号 = `startFrame + facingBucket * facingStride + clamp(phase, 0, framesPerFacing-1)`，越界按 `frameCount` 取模（`:2041-2057`）；
6. 元数据回写缓存（`:2062-2072`）。

**主帧选择** `selectRuntimeObjectShpFrame`（`RuntimeMapRenderer.cpp:2078-2125` 及后续分支）：

| 对象类别 | 帧规则 | 出处 |
| --- | --- | --- |
| 建筑·废墟 | 可取废墟时取最后一个可见主帧（拆半阴影帧已被调用方剔除） | `RuntimeMapRenderer.cpp:2091-2095` |
| 建筑·大门 | `runtimeGateFrameForObject` 命中即返回 | `:2096-2104` |
| 建筑·可进驻 | 状态红警退避取帧 1；有人进驻且 ≥3 帧取帧 2；否则帧 0 | `:2107-2116` |
| 建筑·受损 | `damageLevels > 1` 取 `damageLevels-1`，否则帧 1；夹取到主帧范围 | `:2117-2123` |
| 步兵 / 载具 | 先走序列帧；未命中再按朝向 + 移动/开火分支 | `:2127-2153` |
| 开火 strip | 隐式精灵单位的海军有机单位（`organic && naval && navalTargeting==3`）使用独立 `spriteFiringStartTick` 倒计时；strip 倒放（注释引零售 `0x74141A / 0x73C64B`） | `:2156-2167` 一带 |

**步兵序列节拍** `runtimeInfantrySequenceDelayTicks`（`RuntimeMapRenderer.cpp:1583`）把规则帧延迟换算为 tick；步相 `runtimeSequenceFramePhase` 在移动距离 / 动画 tick 间取相位（移动动画相位公共实现 `worldObjectMovementAnimationPhase`，`GameWorld.cpp:518-530`）。

### 3.3 待机 / 移动 / 游泳 / 死亡动画

| 动画 | 实现 | 出处 |
| --- | --- | --- |
| 待机 tick | `runtimeUnitAnimationTick`：存活为 0；时空门 WarpIn/Cooldown 减快照进度；冻结/瘫痪态归 0；其余取 `animationTick` | `units/RuntimeUnitIdleAnimation.inl:3-18` |
| 待机步进 | `runtimeUnitAnimationStep`：`(tick * 15) / (fixedTickHz * rulesFrameDelay)` | `units/RuntimeUnitIdleAnimation.inl:20-27` |
| 待机帧偏移 | 优先 `UnitType::idleRate`；否则用 `ArtType::standingFrames` + 序列延迟 | `units/RuntimeUnitIdleAnimation.inl:29-41` |
| 游泳判定 | `runtimeInfantryIsSwimmingForRender` → `runtimeInfantrySwimmingWithMetadata(..., resolveRuntimeTerrainMetadata)` | `units/RuntimeInfantrySwimAnimation.inl:1-6` |
| 死亡序列 | `runtimeInfantryDeathSequence`：极地美术替换（见 3.6）；变体 `notHuman ? clamp(infDeath,1,5) : (infDeath==2?2:1)`；候选名 `Die{v}` / 水中 `WetDie1/2`；朝向按逆时针反转 `reversed = (256 - facing%256)%256` 后取桶；拒绝 Ready 占位的 Die3/4/5 | `units/RuntimeInfantryDeathAnimation.h:17-69` |

### 3.4 阴影帧拆半

- 拆半阴影由 `ArtType::shadow`（规则美术）判定，见 `runtimeObjectSourceHasSplitShadow`（`RuntimeMapRenderer.cpp:1036`）；
- 主体条目同时保存 `image` 与 `shadowImage`，`hasShadowFrame` / `shadowFrameIndex` 标记阴影帧可用性（`RuntimeMapRenderer.h:129-144`）；
- 可见主帧数 `runtimeVisibleMainFrameCount(frameCount, splitShadow)`：拆半时主帧为原始帧数一半（调用点 `RuntimeMapRenderer.cpp:13027`、`:21197`、`:22322`、`:30646-30647` 等；契约断言 `744 → 372 / 744`）；
- 阴影帧落地 `drawRuntimeShpShadowFrame`（`RuntimeMapRenderer.cpp:15486`），居中锚点 `runtimeAuxiliaryShpAdjustedCenter(..., shadow=true)`。

### 3.5 辅助动画（单位侧）

单位附属 SHP 走通用辅助缓存 `loadRuntimeAuxiliaryShpSpriteFromCache`（`RuntimeProductionResources.h:162`）与落地 `drawRuntimeAuxiliaryShpSprite`（`RuntimeMapRenderer.cpp:21038`）。辅助动画帧选择规则集中在建筑侧 `RuntimeAuxiliaryAnimation.h`（见 5.1），单位挂接处复用同一帧计算器。

### 3.6 极地（Snow）美术选择

| 函数 | 规则 | 出处 |
| --- | --- | --- |
| `runtimeArcticTheater` | 剧场名归一化为小写后等于 `snow` / `.sno` | `units/RuntimeArcticArt.h:6-11` |
| `runtimeArcticArtId` | `alternateArcticArt && snowy` 时 ID 追加 `A` 后缀（对齐零售 `ObjectType::LoadArt 0x5F9070`）；每次从不可变规则图派生，不在缓存上下文累积后缀 | `units/RuntimeArcticArt.h:13-18` |

### 3.7 载具车身动力学

**姿态结构** `RuntimeVehicleBodyPose`（`units/RuntimeVehicleBodyDynamics.h:10-27`）：`pitch`、`roll`、`height`、`hullLocalRoll`、`hullYaw`、`terrainPitch/Roll/Height`、`terrainSupported`；`active()` 按阈值判定是否有姿态偏移。

**模型包围盒** `RuntimeVoxelModelBounds`（`RuntimeVehicleBodyDynamics.h:75-79`）：`valid`、`minimum[3]`、`maximum[3]`。

**车身旋转** `RuntimeVoxelBodyRotation`（`RuntimeVehicleBodyDynamics.h:83-111`）：两度量化的 `pitch`/`roll` 分桶、`floorZ`、`waterClipZ`、四元数 `useQuaternion + quaternion[4]`；`rotate(x,y,z)` 支持四元数与两轴欧拉两种路径。

| 动力学来源 | 实现 | 出处 |
| --- | --- | --- |
| 寄生虫咬击姿态 | `runtimeVehicleParasiteMotion`：时长 0.72s，质量由 `sqrt(3.5/weight)` 夹取 `[0.5,1.35]`，弹跳 `hop = max(0, 1.05·t − 5·t²)` | `RuntimeVehicleBodyDynamics.h:41-53` |
| 爆炸摇撼 | `runtimeVehicleBlastMotion`：距离衰减 `1 − distance/radius`，强度夹取 `[0,1.4]`；抬升 `height = max(0, 1.8·√strength·t − 5·t²)·(1−t)²` | `RuntimeVehicleBodyDynamics.h:55-73` |
| 姿态 → 旋转 | `runtimeVehicleBodyRotation`：船体（`hullLocalRoll`）绕水原点用四元数；地面车平地冲动角限 9 度、静止坡面限 45 度 | `RuntimeVehicleBodyDynamics.h:113-132` |
| 地面支撑抬升 | `runtimeVehicleGroundSupportLift`：取包围盒四角旋转后最低 Z 与坡度，返回 −lowest | `RuntimeVehicleBodyDynamics.h:134-149` |
| 摇撼系数 | `runtimeVehicleBlastRockingCoefficient`：`Rocker` 弹头取直接摇撼系数；否则 `0.65·clamp(cellSpread,0,1)` | `units/RuntimeVehicleBodyRender.inl:9-14` |
| 爆炸意图 | `runtimeVehicleBodyHasBlastIntent`：`warheadImpact/bombImpact/deathImpact/unitDestruction/superWeaponEffect` 或空/`impact` 且生命周期 `Impact/DeathImpact` | `RuntimeVehicleBodyRender.inl:16-24` |
| 姿态来源聚合 | `RuntimeVehicleBodyPoseSources`：按目标索引寄生虫 / 乌贼 / 爆炸冲击，空间索引候选 | `RuntimeVehicleBodyRender.inl:26-34` 起 |
| 车身偏航 | `pose.hullYaw = (worldObjectArtFacingBucket(facing, 32) − 8) · 2π/32` | `RuntimeVehicleBodyRender.inl:141` 一带 |
| 地面维修记录 | `RuntimeGroundVehicleRepairRecords.inl`：维修厂附着的地面载具记录（在修、进度、前景排序配合） | `units/RuntimeGroundVehicleRepairRecords.inl` |

> 车身姿态只做呈现偏移；角度/高度从仿真时间与物理地面位置派生，不读取绘制计数或墙钟（`RuntimeVehicleBodyDynamics.h:8-9` 注释）。

---

## 4. VXL 体素单位绘制

### 4.1 消费的解码结构

| 结构 | 出处 | 角色 / 字段 |
| --- | --- | --- |
| `RuntimeVoxelSourceCacheEntry` | `RuntimeMapRenderer.h:179-188` | 源字节 + HVA 首帧/按帧变换，解码产物见 05a 第 4 章 |
| `RuntimeVoxelCacheEntry` | `RuntimeMapRenderer.h:190-209` | 按朝向桶 / 动画帧 / 拟合尺度缓存的光栅条目 |
| `RuntimeDecodedVoxel` | `RuntimeMapRenderer.cpp:5314-5320` | 段解码后的单体素：`x/y/z`、`color`、`normal`；消费点 `:5575`（收集）、`:5706`、`:5927`、`:6100`、`:6246`（投影/面生成） |
| `RuntimeRasterVoxel` | `RuntimeMapRenderer.cpp:176-191` | 列投影后的体素单元：屏幕 `x/y`、阴影 `shadowX/shadowY`、`depth`、`color`、`light`、`ownerRemap`、`material`、`normalIndex`、`vplSection`、`surfaceClass`、`normalBrightness`、`sectionIndex` |
| `RuntimeRasterFace` | `RuntimeMapRenderer.cpp:193-209` | 面投影后的可见面：四顶点 `x[4]/y[4]/shadowX[4]/shadowY[4]/cornerDepth[4]`、`depth`、`color/light/ownerRemap/material/normalIndex/vplSection/surfaceClass/normalBrightness/sectionIndex` |
| `RuntimeVoxelModelBounds` | `RuntimeVehicleBodyDynamics.h:75-79` | 模型局部坐标包围盒 |
| `RuntimeVehicleBodyPose` | `RuntimeVehicleBodyDynamics.h:10-27` | 车身动力学姿态 |
| `RuntimeVoxelBodyRotation` | `RuntimeVehicleBodyDynamics.h:83-111` | 车身旋转/裁剪 |
| `RuntimeVxlRasterSizing` | `RuntimeMapRenderer.cpp:211-217` | 光栅拟合尺度：`maxScale 0.61`、`maxWidth 66.0`、`maxHeight 48.0`、`forcedFitScale`、`heightProjectionMultiplier`、`pixelFootprint` |

### 4.2 Reference / Point 模式边界

- 体素几何 memo 提供参考模式开关：`runtimeGeometryVoxelAnimationFrameCount`、`findRuntimeGeometryVoxelFrame`、`rememberRuntimeGeometryVoxelFrame`（`geometry/RuntimeGeometryQueryDeclarations.inl:26-39`）；参考模式用于契约对比，不进入正式缓存键。
- 相机几何 memo 同理：`RuntimeCameraGeometryMemoReferenceScope` 与环境变量 `CONCORD_CAMERA_GEOMETRY_MEMO_REFERENCE` 强制参考路径（`geometry/RuntimeCameraGeometryMemo.h:29-51`）。
- 预热准入也有 V1 / 参考双路径开关（见 8.3）。

### 4.3 列 / 面投影、壳候选、角点投影

| 阶段 | 实现 | 出处 |
| --- | --- | --- |
| 段变换合成 | HVA 段矩阵取自源缓存 `firstFrameTransforms` / `transformsByFrame`；动画帧 memo 记录段名、帧号、变换快照 | `voxel/RuntimeVoxelAnimation.inl`、`voxel/RuntimeVoxelBodyTransform.inl`、`voxel/RuntimeGeometryVoxelAnimationMemo.inl` |
| 面准备 | `RuntimeVoxelFacePreparation.inl`：按投影朝向剔除背面、准备法向亮度 / 材质 / remap 索引；面/体素数量上限见 4.6 巨炮任务常量 | `voxel/RuntimeVoxelFacePreparation.inl` |
| 壳候选 | 解码体素在投影前按可见性 / 表面类收集为壳候选面；逐面裁剪规则散落在 `RuntimeMapRenderer.cpp` 光栅段（`:5706`、`:5927`、`:6100`、`:6246` 一带）；面准备契约见 `voxel/diagnostics/RuntimeVoxelFacePreparationContract.inl`。注意 `RuntimeFutureVoxelBootstrapShellCandidates.inl` 是**未来单位类型候选名单**收集，与体素壳面候选无关 | `voxel/RuntimeFutureVoxelBootstrapShellCandidates.inl:4-27` |
| 角点投影 memo | 角点投影结果按朝向 / 包围盒 memo 化，避免每帧重算 | `voxel/RuntimeVoxelCornerProjection.inl`、`voxel/RuntimeVoxelCornerProjectionMemo.h` |
| 体素碎片 | `RuntimeVoxelDebris.h`：死亡 / 破坏碎片的体素呈现记录 | `voxel/RuntimeVoxelDebris.h` |
| 落地绘制 | `drawRuntimeVoxelImageSprite`（`RuntimeMapRenderer.cpp:15782`）消费 `RuntimeVoxelSpriteDrawInfo`（`:15768`）；主体入口 `tryDrawVoxelSprite`（`:23762`） | 主流程见 2.1 |

> 体素逐列 / 逐面的像素光栅化与深度排序属于渲染内核细节，本章不展开；面顶点、深度、亮度平面的输出布局见巨炮任务平面枚举（4.7）。

### 4.4 炮身 / 炮塔 / 炮管锚点与动画 memo

| 部件 | 缓存 / 锚点 | 出处 |
| --- | --- | --- |
| 单位炮塔 | `tryDrawRuntimeUnitVoxelTurret`：独立朝向桶、独立拟合尺度；候选图序列 `runtimeUnitTurretImageCandidates` | `RuntimeMapRenderer.cpp:19884` |
| 单位炮管 | `tryDrawRuntimeUnitVoxelBarrel`：受 `runtimeDrawSeparateUnitVoxelBarrels` 开关控制；候选 `runtimeUnitBarrelImageCandidates`；炮管像素偏移环境量 `CONCORD_VXL_BARREL_PIXEL_OFFSET_X/Y` | `RuntimeMapRenderer.cpp:19964`；`RuntimeMapRenderer.cpp:23751`；`geometry/RuntimeWeaponMuzzleGeometry.inl:140-149` |
| 炮塔挂载点 | `runtimeVoxelTurretOffset`：按车身拟合尺度计算安装偏移；车身旋转激活时先反投影到局部、旋转后再投影回屏幕 | `RuntimeMapRenderer.cpp:1453`；`geometry/RuntimeWeaponMuzzleGeometry.inl:125-138` |
| 动画 memo | `RuntimeGeometryVoxelAnimationMemo.inl`：图像 ID / 归属 / 朝向 / 帧 / 俯仰 / 旋转键下的条目命中与回写 | `voxel/RuntimeGeometryVoxelAnimationMemo.inl` |
| 车身变换 memo | `RuntimeVoxelBodyTransform.inl`：姿态 → 旋转 / 支撑抬升的派生结果 memo | `voxel/RuntimeVoxelBodyTransform.inl` |
| 原生炮口锚点 | `runtimeNativeVoxelMuzzleAnchor`：按图像指纹在模型局部坐标取锚点；失败回退 `resolveRuntimeVoxelMuzzleEndpoints` | `voxel/RuntimeNativeVoxelMuzzleAnchors.inl`；`geometry/RuntimeWeaponMuzzleGeometry.inl:42-46` |
| 炮口端点缓存 | `cache.voxelMuzzleEndpointsByKey`：键 = `assetRootPath|lower(imageId)`；多管模型按 FLH 局部点就近选管（含天启双管分离端面） | `geometry/RuntimeWeaponMuzzleGeometry.inl:37-56` |
| 建筑体素炮塔锚点 | `resolveRuntimeVoxelWeaponMuzzlePoint` 建筑分支：`turretAnim` 加载 → 炮管候选 → 炮塔回退；叠加 `runtimeStructureTurretAnimScreenOffset` 与后座 `runtimeStructureRecoilScreenOffset` | `geometry/RuntimeWeaponMuzzleGeometry.inl:65-92` |

**炮口投影模型**（`geometry/RuntimeWeaponMuzzleGeometry.inl:6-178`）：

- FLH 单位换算 `modelUnitsPerLepton = 0.18`（`:12`）；
- 枪偏航 `gunYaw` 与船体偏航 `hullYaw` 分别按 `(bucket − 8) · 2π/32` 计算（`:154-158`）；
- 局部点先按枪偏航旋转，再叠车身旋转；非原点枢轴模型在无炮塔时对包围盒中心取半宽（`:117-121`）；
- 屏幕投影 `x = (x − y)·screenXScale·fitScale`，`y = ((x + y)·groundYScale − z·heightYScale)·fitScale`（`:174-176`）。

### 4.5 建筑体素炮塔（Grand Cannon 除外）

- 建筑体素炮塔判定：`building->turretAnimIsVoxel && !turretAnim.empty()`；巨炮识别 `gtgcan` / `gtgcantur`（`geometry/RuntimeWeaponMuzzleGeometry.inl:67-71`）；
- 巨炮模型尺度 `runtimeGrandCannonModelScale()`；普通共享部件尺度受 `runtimeVxlSharedPartScaleEnabled()` 控制（`:77-78`）；
- 绘制入口 `tryDrawRuntimeStructureTurretAnim`（`RuntimeMapRenderer.cpp:23174`），朝向桶 `facingBucketForFacingValue`（`:23256`）。

### 4.6 巨炮预热光栅任务

**光栅任务边界** `concord::grand_cannon`（`voxel/GrandCannonRasterTask.h`）：

| 项 | 常量 / 结构 | 出处 |
| --- | --- | --- |
| 面上限 | `MaxFaces = 131072` | `GrandCannonRasterTask.h:12` |
| 体素上限 | `MaxVoxels = 65536` | `GrandCannonRasterTask.h:13` |
| 维度上限 | `MaxDimension = 384` | `GrandCannonRasterTask.h:14` |
| 面结构 `Face` | 四顶点 `x[4]/y[4]/depth[4]`、`normalBrightness`、`material`、`color/normal/vpl/ownerRemap` | `:15-20` |
| 阴影项 `Shadow` | `x/y/section/color` | `:21-25` |
| 任务输入 `Input` | 宽高、padding、阴影段、面数/体素数、深度 ε、面偏置、深度区间 | `:26-30` |
| 任务输出 `Output` | 面数、三角形数、不透明宽高、顶/侧/下像素数 | `:31-36` |
| 输出平面 | `Pixels, Raw, Remap, Light, Normal, Brightness, Vpl, Depth, ShadowPixels` 共 9 字节平面 + 材质平面 | `:38` |
| 入口 | `raster(JobContext)`、`workerBudget(requested, hardwareThreads)` | `:40-41` |

**预热作业生命周期** `RuntimeGrandCannonPrewarmJob`（`voxel/RuntimeGrandCannonPrewarm.h`、`voxel/RuntimeGrandCannonPrewarm.cpp`）：

| 阶段 | 行为 | 出处 |
| --- | --- | --- |
| 构造 | 按请求 worker 数与硬件并发算 `workerBudget`；有界执行器队列参数 4/4；执行器字节预算 `ExecutorBytes = 64 MiB` | `RuntimeGrandCannonPrewarm.h:30`；`RuntimeGrandCannonPrewarm.cpp:39-42` |
| `prepare` | 仅属主线程、全新作业可调用；`RuntimeGrandCannonPreparedFacing::inspect` 取源信息；逐朝向 `prepare`，按 `CacheBytes = 128 MiB` 累计电荷；每 4 个或末尾 `dispatchPrepared`；失败注入钩子用于契约测试 | `RuntimeGrandCannonPrewarm.cpp:82-154`；`RuntimeGrandCannonPrewarm.h:31` |
| 朝向槽位 | `std::array<Facing, 31> facings`；完成判定 `installed == 62`（31 朝向 × 2 槽位） | `RuntimeGrandCannonPrewarm.cpp:30`、`:201` |
| `merge` | 属主线程、代际匹配才合并入目标缓存 | `RuntimeGrandCannonPrewarm.cpp:165-187` |
| `finish` | 先 `merge` 再收尾；`stop()` 无条件终止；`done()` 查完成 | `RuntimeGrandCannonPrewarm.h:24-27`；`RuntimeGrandCannonPrewarm.cpp:187-199` |
| 指标 | `Metrics`：workers、submitted、installed、synchronousFallbacks、peakJobs/Running/ExecutorBytes、largestJobBytes、cacheBytes、sourceBytes、prepareMs、ownerWaitMs、elapsedMs | `RuntimeGrandCannonPrewarm.h:13-18` |

**朝向准备桥** `RuntimeGrandCannonPreparedFacing`（`voxel/RuntimeGrandCannonPreparedFacing.h:12-29`）：`inspect`（源字节 / 回退电荷）、`prepare(context, cache, bucket, generation)`、`count`、`cacheCharge`、`task(index)`、`accept(index, output)`、`install(cache)`；不保留任何上下文 / 规则 / 美术 / 剧场 / 缓存指针跨任务边界。

> 巨炮预热是**帧属主线程**构造、有界执行器并发光栅、属主线程合并安装的三段式；代际 `generation` 不匹配的输出一律丢弃（`RuntimeGrandCannonPrewarm.cpp:59`、`:167`）。

### 4.7 体素阴影

- 阴影段请求 / 应用记录在 `RuntimeVoxelCacheEntry::shadowSectionRequested/Applied`（`RuntimeMapRenderer.h:201-202`）；
- 挂接阴影 `drawRuntimeAttachedVoxelShadows`（`RuntimeMapRenderer.cpp:23947`）；
- 体素阴影段选择 `runtimeObjectVoxelShadowSection`（`RuntimeMapRenderer.cpp:1076`）。

---

## 5. 建筑绘制

### 5.1 辅助动画帧选择（通用）

`RuntimeAuxiliaryAnimation.h` 提供建筑 / 单位共用的 strip 帧计算器：

| 结构 / 函数 | 规则 | 出处 |
| --- | --- | --- |
| `FrameRange` | `begin`、`end`（`end` 为独占帧号） | `RuntimeAuxiliaryAnimation.h:11-14` |
| `resolveRange` | 起始：`animationLoopStart` → `animationStart` → 0；结束：`LoopEnd` 绝对值，否则 `Start + AnimationEnd`；无定义且主帧模式下偶数帧数折半；夹取 `[begin+1, available]` | `:16-33` |
| `FrameCycle` | `range`、`forwardCount`、`returnCount`；`at(advance)`：乒乓回程排除两端点，折返帧只显示一次 | `:35-47` |
| `runtimeAuxiliaryAnimationFrame` | 节拍 `runtimeRuleFramesToTicks(runtimeRuleAnimationDelayFrames(art->rate), hz)`；`advance = max(1, tick / interval)`；ping-pong 回程长 `length − 2` | `:51-64` |
| `selectRuntimeAuxiliaryAnimationFrame` | 渲染侧包装（含供电暂停时钟、损坏帧选择等） | `RuntimeMapRenderer.cpp:1634` |

### 5.2 建筑主动动画层编排

`drawRuntimeStructureActiveAnimations`（`RuntimeMapRenderer.cpp:22428`）按 `ArtType::activeAnimations` / `specialAnimations` / `auxiliaryAnimations` 的 key 分派到下列专用计算器：

| 动画 | 帧 / 相位规则 | 调用点（`RuntimeMapRenderer.cpp`） | 专用实现 |
| --- | --- | --- | --- |
| 维修厂动画 | 快照 `kind == "repairDepot"`；`SpecialAnim`=伸展、`SpecialAnimTwo`=作业、其余=收起；`ageTicks = snapshot.progress` | `:22577` | `buildings/RuntimeRepairDepotAnimationRender.inl:1-29` |
| 碾磨动画 | 冲击演员 `renderIntent == "grinderCycle"` 且目标为本建筑、`remainingTicks > 0`；帧 = 演员帧号或按 `(total − remaining)/delay` 夹取 | `:22592` | `buildings/RuntimeGrindingAnimationRender.inl:1-18` |
| 超武建筑 | key 集合 `SuperAnim/Two/Three/Four`；`ageTicks` 来自快照；有 `AnimationEnd` 时为绝对包含帧（NAMISL_F/H 0–15，损坏半 16–31），否则 LoopStart + 步相模 LoopEnd 跨度 | `:22596` | `buildings/RuntimeSuperWeaponBuildingRender.inl:1-23`、层解析 `:25-45` |
| 工厂出厂口 | 帧由 `runtimeFactoryExitFrame` 在开启动画起止 tick 间插值；背景层先于出厂飞机，前景墙后画 | `:22606` | `buildings/RuntimeFactoryAirExitRender.inl:3-23`（视野裁剪）、`buildings/RuntimeFactoryAirExitForeground.inl:4-36`（前景墙） |
| 生物反应堆 | 仅 `infantryAbsorb` 建筑；`activeanim/activeanimtwo` 两层；供电层使用暂停时钟 `powerAnimationPausedAtTick/Ticks`；原始层用 `bioReactorAnimationStartTick` | `:22647` | `buildings/RuntimeBioReactorAnimation.h:11-47` |
| 倒塌 / 废墟 | 帧 `runtimeBuildingCollapseFrame(collapse, tick, hz)`；拆半阴影先在阴影通道画；主体用建筑自定义 PAL | — | `buildings/RuntimeBuildingCollapseRender.inl:16-40` |
| 地面维修厂前景排序 | 对 `unitRepair && !naval && !waterBound && art->flat` 的维修厂收占地块，地面单位按空间索引前后排序；>16 个启用空间索引 | — | `buildings/RuntimeRepairDepotRender.inl:1-40` 起 |

### 5.3 建筑主体、地基、建造动画

| 元素 | 调用点 | 说明 |
| --- | --- | --- |
| 地基（Bib） | `tryDrawRuntimeStructureBib`（`RuntimeMapRenderer.cpp:21688`） | 平贴层，深度层 `Bib` |
| 建造动画 | `tryDrawRuntimeStructureBuildup`（`RuntimeMapRenderer.cpp:21732`） | 建造剩余 tick 驱动的拼装帧 |
| 损坏起火 | `drawRuntimeStructureDamageFires`（调用点见 `RuntimeMapRenderer.cpp:22259` 一带） | 受损状态附属火焰 |
| 自定义 PAL | `runtimeBuildingCustomPaletteForRender`：`Palette` 无前缀时按 `lib → libtem.pal` 前缀规则拼当前兵种后缀 | `RuntimeBuildingCollapseRender.inl:1-14` |

### 5.4 建筑深度与遮挡

**深度层枚举** `RuntimeBuildingDepthLayer`（`buildings/RuntimeBuildingDepth.h:9`）：`Body, Animation, Flat, Bib, NavalVoxel, NavalSprite, InfantrySprite, WallSprite`。

**深度帧** `RuntimeBuildingDepthFrame`（`RuntimeBuildingDepth.h:27-36`）：画布宽高、帧宽高、`offsetX/Y`、占地宽高、`moveX/Y`、`normalZAdjust`、`nativeFrameTopY`、`heightPixel`、`zAdjust`、`yDrawOffset`、`navalBottom`、层标记。

**逐像素深度**：

| 函数 | 规则要点 | 出处 |
| --- | --- | --- |
| `runtimeBuildingAnimationSlotYSort` | 零售反投影系数 `4.2667F / 8.5334F`（x87 浮点字面量），结果 + `ySortAdjust` | `RuntimeBuildingDepth.h:11-25` |
| `runtimeBuildingDepthAtPixel` | 海军/步兵/墙精灵层：底行梯度深度，步兵偏置 12、海军精灵 2、墙精灵 1；Body 且占地 <8 采样 BUILDNGZ 模板（`raw + 256 − 65` 变换、有符号模板高度）；Flat/Bib 按行顶深度；其余按行底三分深度 | `:38-92` |
| `runtimeBuildingRenderDepthAtPixel` | 模板外未覆盖像素回退 Deg90 规则补遮挡，不擦除既有轮廓 | `:94-108` |
| `RuntimeBuildingDepthPixel` | `depth`、`depthOwner`、`visibleOwner` | `:110-114` |
| `runtimeBuildingDepthAccept` | 仅加建筑间深度；保留建筑内部既有层序；自有可见像素不被等深覆盖 | `:116-128` |

**行内核与场景**：

| 组件 | 出处 | 职责 |
| --- | --- | --- |
| `RuntimeBuildingDepthRowKernel` | `buildings/RuntimeBuildingDepthRowKernel.h:10` | 单行深度扫描内核 |
| 深度渲染作用域 | `buildings/RuntimeBuildingDepthRender.inl` | 对象/部件/帧/缓冲/像素复制/混合像素等 RAII 作用域与状态（结构清单见 01 章 2.5） |
| 空场景参考 | `buildings/RuntimeBuildingDepthEmptyScene.h:11` | 无建筑深度场景的参考作用域 |

---

## 6. 几何与命中

### 6.1 标准坐标与放置

| 组件 | 出处 | 职责 |
| --- | --- | --- |
| `RuntimeObjectWorldRenderPlacement.h` | `geometry/RuntimeObjectWorldRenderPlacement.h:7-25` | 放置结果结构（中心 / 排序点 / 主体视口 / 空中前景 / 工厂前景宿主）与解析入口 |
| `RuntimeRenderPlacementWin32.h` | `geometry/win32/RuntimeRenderPlacementWin32.h:39-53` | 原生 `POINT/RECT` 与有符号 32 位屏幕点/矩形互转；几何查询各入口的 Win32 适配 |
| `RuntimeObjectBodyAnchor.h` | `geometry/RuntimeObjectBodyAnchor.h` | 主体锚点（含被击中空中目标的身体中心回退） |
| `RuntimeSpriteGeometryCapture` | `geometry/RuntimeSpriteGeometry.inl:4-10` | 几何查询重放主体绘制，在索引像素边界停止，不触达调色板 / 光照 / 目标输出；收集 `bounds/canvasBounds/layerCount/hitSpans` |
| 捕获辅助 | `RuntimeSpriteGeometry.inl:12-40` | 矩形并集、不透明裁剪、不透明跨度收集 |

### 6.2 边界盒查询

| 查询 | 出处 | 输入 / 输出 |
| --- | --- | --- |
| `resolveRuntimeObjectShpSpriteBounds` | `geometry/RuntimeObjectShpBoundsQuery.h` | SHP 主体边界矩形 |
| `resolveRuntimeMobileObjectSpriteBounds` | `geometry/RuntimeMobileSpriteGeometry.inl:112-128` | 移动单位边界：先查 memo，未命中用 `captureRuntimeMobileSpriteGeometry` 离屏重放，结果回写 memo |
| `RuntimeMobileSpriteBoundsProbe` | `geometry/RuntimeMobileSpriteBoundsProbe.h:10-25` | 诊断探针：精灵/渲染边界、渲染像素、越界像素、层数、资源是否解析、是否精确命中、是否裁剪、孤立主体像素 |
| `resolveRuntimeStructureSpriteBounds` | `geometry/RuntimeStructureSpriteQuery.h` | 建筑主体边界 |
| `resolveRuntimeSpawnedMissileSpriteBounds` | `geometry/RuntimeSpawnedMissileBoundsQuery.h:12-16` | 已发射导弹的非零像素包络（右/底排他） |
| 建筑可见命中跨度 | `geometry/RuntimeBuildingVisibleHitSpans.h` | 建筑各已出版本层的可见跨度集合 |

### 6.3 命中测试

| 组件 | 出处 | 规则 |
| --- | --- | --- |
| `RuntimeSpriteHitSpan` | `geometry/RuntimeSpriteHitSpan.h` | 单行 `[left, right)` 跨度，带原始排序顺序与借用生命周期 |
| `runtimeSpriteHitSpansContain` | `geometry/RuntimeSpriteHitSpanQueries.h:10-11` | 跨度集合包含点判定 |
| `RuntimeMapSurfacePick` | `geometry/RuntimeMapSurfacePick.h` | 地面格拾取（含高度 / 遮挡回退） |
| 查询作用域 | `geometry/RuntimeGeometryQueryScope.h:14-25` | 不可变几何租约：世界 / 上下文 / 相机 / 源快照在退出前保持不变；`viewportStructureGeometryOnly` 选项 |
| 几何 memo | `geometry/RuntimeGeometryQueryMemo.inl`、`geometry/RuntimePaintSpecialObjectIdMemo.inl` | 边界 / 跨度 memo 复用与绘制对象 ID 标记 |

### 6.4 武器发射位置（FLH）与炮口锚点

| 查询入口 | 出处 | 规则 |
| --- | --- | --- |
| 体素武器炮口 | `resolveRuntimeVoxelWeaponMuzzlePoint`（`geometry/RuntimeWeaponMuzzleGeometry.inl:6-178`） | FLH 利伯顿 → 模型单位（0.18/lepton）；与所绘体素同模型单位、同固定尺度、同姿态；世界 FLH 仍是仿真弹道原点，此处只施加呈现修正 |
| 建筑 SHP 炮口 | `resolveRuntimeStructureWeaponMuzzlePoint`（`geometry/RuntimeStructureWeaponMuzzleAttachment.inl:6-73`） | 体素 / 炮塔建筑直接返回 false；`PrimaryFirePixelOffset/SecondaryFirePixelOffset` 像素锚点；`DualOffset` 按朝向角把 FLH 旋进 60×30 原生投影（系数 30.0 / 15.0 / √6·15.0/256）；原生锚点表仅对未替换 SHP 生效 |
| 步兵炮口 | `resolveRuntimeInfantryWeaponMuzzlePoint`（`geometry/RuntimeInfantryMuzzleAttachment.inl:3-50`） | 原生指纹表（BORIS/GI/… 共 31 项）校验替换 SHP 不套原生锚点；锚点按当前主体帧宽高原像素偏移；海军单位走 `runtimeUnderwaterShpAlignmentOffsetY` |
| 海军（海豚）炮口 | `resolveRuntimeMarineWeaponMuzzlePoint`（`geometry/RuntimeMarineMuzzleAttachment.inl:6-25`） | 仅 `dlph` + 指纹校验；原生像素坐标无 FLH / 缩放倍率 |
| 原生锚点表 | `RuntimeInfantryMuzzleAnchors.inl`、`RuntimeMarineMuzzleAnchors.inl`、`RuntimeStructureMuzzleAnchors.inl` | 按图像 ID / 序列名 / 帧号 / 宽高查锚点 |
| 开放式运输 | `RuntimeOpenTransportMuzzle.inl` | 物理查询保留原有活动世界 / 美术查找归属（当前为空壳注释） |
| 弹体屏幕点 | `geometry/RuntimeProjectileScreenPoint.h` | 弹体 / 特效屏幕点换算 |
| 建筑炮塔屏幕偏移 | `geometry/RuntimeStructureTurretScreenOffset.h`；实现 `runtimeStructureTurretAnimScreenOffset`（`RuntimeMapRenderer.cpp:23064`） | 按占地宽高 + 相机投影的炮塔锚点偏移 |

> 炮口锚点契约审计：`geometry/RuntimeAllWeaponMuzzleRender.inl:3` 起的 `runRuntimeAllWeaponMuzzleRenderContract` 为离线契约渲染（8 方向网格、十字标记记录坐标、CSV/HTML 输出），不属于运行时绘制路径。

---

## 7. 阴影、选框、归属色 / Remap

### 7.1 阴影

| 判定 / 绘制 | 出处 | 规则 |
| --- | --- | --- |
| 规则抑制阴影 | `runtimeObjectRulesSuppressShadow`（`RuntimeMapRenderer.cpp:1047`） | 规则标记禁止阴影的对象跳过 |
| 拆半阴影判定 | `runtimeObjectSourceHasSplitShadow`（`:1036`） | 美术 `shadow` 标记 |
| 可渲染阴影 | `runtimeObjectShouldRenderShadow`（`:1063`） | 汇总规则 / 美术 / 状态 |
| 体素阴影段 | `runtimeObjectVoxelShadowSection`（`:1076`） | 按对象类别选阴影段配置 |
| SHP 阴影帧 | `drawRuntimeShpShadowFrame`（`:15486`） | 拆半阴影帧落地 |
| 挂附体素阴影 | `drawRuntimeAttachedVoxelShadows`（`:23947`） | 主体条目 `shadowPixels` / `shadowSpanCache` |
| 世界对象阴影 | `drawRuntimeWorldObjectShadow`（`:24093`） | 阴影通道记录 → 屏幕投影绘制 |
| 阴影记录 | `RuntimeWorldObjectShadowDrawRecord`（`:11137`） | 与主体记录分离 |

### 7.2 选框与血条

选框 / 血条不在位图合成层绘制，而由宿主层（Shell）基于几何查询结果挂载：

| 结构 / 函数 | 出处 | 规则 |
| --- | --- | --- |
| `RuntimeStructureSelectionFrameGeometry` | `RuntimeShell.cpp:22709-22717` | `left/top/right/bottom` 角点、`footprintBounds`、`verticalHeight`、`valid` |
| `runtimeStructureSelectionFrameGeometry` | `RuntimeShell.cpp:23199-23252` | 取占地中心集的最左 / 最上 / 最右 / 最下角，半宽高外扩；笼子描述固定地基，不随动画不透明包络变化；炮管 / 旗帜 / 后座超出部分仍由各自跨度参与命中；`verticalHeight` 夹取 `[18, 4096]` |
| `runtimeStructureFrameCrownPoint` | `RuntimeShell.cpp:22719-22727` | 角点向上抬升 `verticalHeight + clearance` |
| `RuntimeStructureHealthBarLayout` | `RuntimeShell.cpp:22729-22735` | `topEnd/leftEnd`、`segmentAnchors`、`bounds`、`valid` |
| `runtimeStructureHealthBarLayout` | `RuntimeShell.cpp:22737` 起 | 血条挂在选框冠边之上，保留冠边斜率；按实际光栅 X 圆整每段高度，避免逐段裁剪把整条血条倾斜约 1.5 像素 |

### 7.3 归属色 / Remap

| 判定 / 应用 | 出处 | 规则 |
| --- | --- | --- |
| 单位是否归属染色 | `runtimeObjectShouldOwnerRemap`（`RuntimeMapRenderer.cpp:11438`） | 按兵种 / 美术 / 状态判定 |
| 归属色查询 | `runtimeOwnerRemapColor(context, owner)` | 取玩家归属调色板 remap 色 |
| 建筑层 remap | 辅助动画调用处传入 `runtimeStructureLayerShouldOwnerRemap(host, context, image)`（如 `RuntimeFactoryAirExitForeground.inl:32`） | 按层美术决定是否染色 |
| 体素 remap 平面 | `RuntimeRasterFace.ownerRemap`、输出平面 `Remap`（`GrandCannonRasterTask.h:19,38`） | 体素面级归属索引 |
| 不可见时透明度 | `runtimeObjectSpriteOpacity`（`RuntimeMapRenderer.cpp:10266`） | 战争迷雾 / 隐身等透明度呈现 |

---

## 8. 资源就绪、缺帧兜底、缓存复用与预热准入

### 8.1 资源就绪与缺帧兜底

| 场景 | 兜底行为 | 出处 |
| --- | --- | --- |
| SHP 冷加载失败 | 条目保留 `loaded = false` 与 `errorMessage`；主体跳过绘制，不抛异常 | `RuntimeMapRenderer.h:130-143`；`loadRuntimeObjectShpSpriteFromCache`（`RuntimeMapRenderer.cpp:12949`） |
| 序列未命中 | `RuntimeObjectSequenceFrameResult.matched = false`；回退朝向 + 待机 / 移动主帧分支 | `RuntimeMapRenderer.cpp:2006-2016`、`:2127-2137` |
| 帧号越界 | 按 `frameCount` 取模，负数修正到非负 | `:2052-2057`、`:1990-1995` |
| 死亡序列占位帧 | Die3/4/5 的 Ready 占位（`startFrame=0, frameCount=1, facingMultiplier=1`）被拒绝，继续找真实死亡帧 | `RuntimeInfantryDeathAnimation.h:50-54` |
| 体素源缺失 / 解码失败 | `loaded = false`；`hasSprite = false`；炮口查询回退主体锚点 | `RuntimeMapRenderer.h:179-209`；`RuntimeWeaponMuzzleGeometry.inl:15-16,95-96` |
| 原生炮口锚点缺失 | 回退 `resolveRuntimeVoxelMuzzleEndpoints` 几何端点；再无端点则保持 FLH 局部点 | `RuntimeWeaponMuzzleGeometry.inl:42-46` |
| 建筑自定义 PAL 缺失 | 回退默认调色板路径 | `RuntimeBuildingCollapseRender.inl:1-14` |
| 巨型 / 越界画布 | 边界探针宽高 >4096 标记 `clipped` 并放弃 | `RuntimeMobileSpriteBoundsProbe.h:17-19`；`RuntimeMobileSpriteGeometry.inl:148-153` |

### 8.2 缓存复用

| 缓存 | 键 / 复用维度 | 出处 |
| --- | --- | --- |
| 主体 SHP 条目 | 图像 ID 候选解析结果 + 朝向 / 序列帧 / 阴影标记 | `RuntimeMapRenderer.cpp:12949-13043` |
| 序列帧元数据 | `artId|kind|mission|airborne|render|moving|swimming|prone|deployed|facing|frames` | `RuntimeMapRenderer.cpp:1951-1962` |
| 体素条目 | 图像 ID + 归属 + 朝向桶 + 尺度 + 阴影 + 动画帧 + 俯仰 + 旋转 | `RuntimeGeometryQueryDeclarations.inl:32-39`；`RuntimeVoxelCacheEntry`（`RuntimeMapRenderer.h:190-209`） |
| 炮口端点 | `assetRootPath|lower(imageId)`；本体键 `…|native-body|…` | `RuntimeWeaponMuzzleGeometry.inl:36-37,107` |
| 几何边界 memo | 世界 / 上下文 / 相机 / 源快照不可变前提下复用 | `RuntimeGeometryQueryScope.h:11-13`；`RuntimeCameraGeometryMemo.h:56-121` |
| 相机包络 memo | 比较每个有序 x/y/level、瓦片尺寸与原始雷达投影；条目数 `[4096, 262144]` | `RuntimeCameraGeometryMemo.h:65-67,71-115` |

### 8.3 预热准入

**当前世界普通体素预热** `prewarmRuntimeCurrentWorldNormalVoxels`（`voxel/RuntimeVoxelLoadingPrewarm.h:62-65`）：

| 限制 | 值 | 出处 |
| --- | --- | --- |
| 新条目上限 | 2048 | `RuntimeVoxelLoadingPrewarm.h:12` |
| 尝试上限 | 4096 | `:13` |
| 新增负载字节 | 64 MiB | `:14` |
| 族数上限 | 128 | `:15` |
| 源字节上限 | 8 MiB | `:16` |
| 段体素网格 / 总体网格 | 1M / 4M | `:17-18` |
| HVA 帧数 / 段数 | ≤16 / ≤128 | `:19-20` |
| 墙钟 | 15000 ms | `:21` |

**准入状态机** `RuntimeVoxelLoadingPrewarmState`（`voxel/RuntimeVoxelLoadingPrewarmAdmission.inl:49-98`）：

- `permitRequest()`：停止态 / 回调重入拒绝；尝试数、新条目数、负载字节超预算 → `budgetStopped`；墙钟超限 → `timeStopped`（`:59-72`）；
- 进度回调：仅加载窗口泵，默认空；C23 路径每 100 ms 至多一次；UI 耗时计入同一软预算，不重置截止时间（`:74-97`）；
- 发布 `runtimeVoxelLoadingPrewarmPublish`：未加载 / 无精灵记失败；载荷字节按条目字段容量保守估算；已有条目不覆盖；稳定键单独登记（`:146-165`）；
- 预热条目必须是冷解码：不允许已带不透明跨度 / 阴影跨度 / 色表派生分配（`:121-127`）。

**未来类型引导预热** `prewarmRuntimeFutureNormalVoxelTypes`（`voxel/RuntimeFutureVoxelBootstrap.h:64-66`）：

| 限制 | 值 | 出处 |
| --- | --- | --- |
| 族数 / 新条目 / 尝试 | 32 / 1024 / 2048 | `RuntimeFutureVoxelBootstrap.h:16-18` |
| 新增负载 / 族源字节 / 临时读字节 | 64 MiB / 8 MiB / 16 MiB | `:19-22` |
| 索引路径数 / 字节 | 256 / 1 MiB | `:23-24` |
| 墙钟 | 15000 ms | `:25` |

未来候选收集 `collectRuntimeFutureNormalVoxelTypeIds`（`RuntimeFutureVoxelBootstrapShellCandidates.inl:4-27`）：仅 UnitType、科技树允许、非秘密实验室注入、科技等级达标、国家可见；不要求当前工厂 / 资金 / 前置建筑 / 窃取科技 / 建造限制（如 SREF 不因未建 GATECH 被排除）；候选上限 128。

**参考 / V1 模式开关**：`CONCORD_VOXEL_LOADING_PREWARM_REFERENCE`、`CONCORD_VOXEL_LOADING_PREWARM_V1_REFERENCE`（`RuntimeVoxelLoadingPrewarmAdmission.inl:5-32`）；契约检查 `runRuntimeVoxelLoadingPrewarmAssetContractCheck`、`runRuntimeFutureVoxelBootstrapAssetContractCheck`（`RuntimeVoxelLoadingPrewarm.h:67`、`RuntimeFutureVoxelBootstrap.h:67`）。

---

## 9. 未实现 / 受限模式标注

| 项 | 状态 | 出处 / 说明 |
| --- | --- | --- |
| GPU 体素 / 精灵材质路径 | 本章静态阅读未展开；契约存在 `RuntimeGpuVxlModeContract`、`RuntimeGpuSpriteMaterialContract`、`RuntimeGpuFrozenWorldContract` | `voxel/diagnostics/`、`units/diagnostics/`、`buildings/diagnostics/` |
| 开放式运输炮口 | 仅保留物理查询归属注释，无锚点实现 | `geometry/RuntimeOpenTransportMuzzle.inl:1-5` |
| 诊断 / 契约目录 | `diagnostics/` 子目录全部为离线契约、烟雾对照与审计渲染，不属于运行时个体绘制路径；本章引用其存在性，不展开实现 | 各 `*/diagnostics/` 目录 |
| 巨炮预热失败注入 | 仅测试钩子 `failure == 3` 用于代际推进契约 | `RuntimeGrandCannonPrewarm.cpp:147`；`RuntimeGrandCannonPrewarmTestHooks.h` |
| 双形态 / 替换 SHP 原生锚点 | 同名替换 SHP 不套原生锚点（指纹校验拦截） | `RuntimeInfantryMuzzleAttachment.inl:2,38-39` |
| 建筑 SHP 炮塔 / 体素炮塔互斥 | 建筑 SHP 炮口查询对 `voxel || turret` 建筑直接返回 false | `RuntimeStructureWeaponMuzzleAttachment.inl:15` |
| 第 16 章管线衔接 | 渲染管线总览章节尚未落盘；本章记录的绘制记录结构（2.2）即为其衔接点 | 见 1.2 |

---

## 10. 本章缺口

1. **主循环逐行排序**：地面主体排序键 `RuntimeGroundBodySortKey` 的完整比较规则、阴影通道与主体通道在帧循环中的先后调用点（31550 一带阴影探针、40577/40901 主体阴影调用）尚未逐行固化为规则表；需在管线章（第 16 章）结合 `MapDrawList` / `MapDrawFrame` 补齐。
2. **体素光栅内核**：`RuntimeRasterVoxel` / `RuntimeRasterFace` 的完整字段、列扫描、壳候选裁剪、面投影到像素的逐像素规则散落在 `RuntimeMapRenderer.cpp` 15768–16200 一带与 `RuntimeVoxelFacePreparation.inl` / `RuntimeVoxelCornerProjection.inl`，本章只定阶段边界；像素级算法待 05b（体素光栅化）章节展开。
3. **单位附属效果绘制**：烟雾、尾迹、电弧、寄生缠绕、乌贼触手、导弹体等在 `src/render/effects/` 与 `units/diagnostics/` 对应生产路径，本章未逐一梳理。
4. **建筑动画专用计算器行号**：`runtimeRepairDepotAnimationFrame`、`runtimeGrindingAnimationFrame`、`runtimeSuperWeaponBuildingFrame`、`runtimeFactoryExitFrame`、`runtimeBuildingCollapseFrame`、`runtimeStructureRecoilScreenOffset` 的函数定义体位于 `RuntimeMapRenderer.cpp` 各辅助段，本章引用了调用点（5.2 表），定义体参数规则待补。
5. **步兵 / 海军锚点表**：`RuntimeInfantryMuzzleAnchors.inl`、`RuntimeMarineMuzzleAnchors.inl`、`RuntimeStructureMuzzleAnchors.inl` 的逐图像锚点坐标表未逐项转录；复刻时按指纹 + 帧号查表使用。
6. **伪装 / 间谍体**：`runtimeCamouflageSnapshot`、`runtimeSpyDisguiseSnapshot` 的资源替换与锚点继承规则仅记录了分派点（`RuntimeMobileSpriteGeometry.inl:84-90`），快照字段来源需查世界状态快照章节。
7. **深度行内核实现**：`RuntimeBuildingDepthRowKernel.h:10` 与 `RuntimeBuildingDepthRender.inl` 各 RAII 作用域的逐行扫描 / 混合像素规则未逐行展开，建筑间遮挡的完整像素结果需对照契约 `RuntimePaintBuildingDepthReuseContract` 验证。
8. **HVA 按帧变换合成**：`transformsByFrame` 在炮塔 / 炮管动画中的帧索引选择与段名匹配规则未在本章固化；解码侧见 05a 第 4.3，渲染侧 memo 见 4.4。

---

## 附录 A. 覆盖渲染文件清单

### A.1 `src/render/units/`（生产）

| 文件 | 角色 |
| --- | --- |
| `RuntimeArcticArt.h` | 剧场判定与极地美术 ID 后缀 |
| `RuntimeGroundVehicleRepairRecords.inl` | 地面载具维修记录 |
| `RuntimeInfantryDeathAnimation.h` | 步兵死亡序列选择（Die/WetDie、朝向反转、占位帧过滤） |
| `RuntimeInfantrySwimAnimation.inl` | 步兵游泳判定包装 |
| `RuntimeUnitIdleAnimation.inl` | 待机 tick / 步进 / 帧偏移 |
| `RuntimeVehicleBodyDynamics.h` | 车身姿态、模型包围盒、旋转、支撑抬升、寄生虫 / 爆炸运动 |
| `RuntimeVehicleBodyRender.inl` | 爆炸意图、摇撼系数、姿态来源聚合、车身偏航 |

### A.2 `src/render/voxel/`（生产）

| 文件 | 角色 |
| --- | --- |
| `GrandCannonRasterTask.h/.cpp` | 巨炮有界光栅任务：面 / 体素 / 维度上限、输入输出、平面布局、worker 预算 |
| `RuntimeFutureVoxelBootstrap.h/.inl` | 未来类型引导预热限制、统计、入口 |
| `RuntimeFutureVoxelBootstrapShellCandidates.inl` | 未来 UnitType 候选名单收集（非体素壳面候选） |
| `RuntimeGeometryVoxelAnimationMemo.inl` | 几何查询体素动画 memo |
| `RuntimeGrandCannonPreparedFacing.h` | 单朝向准备 / 安装桥 |
| `RuntimeGrandCannonPrewarm.h/.cpp` | 巨炮预热作业生命周期、预算、代际合并、指标 |
| `RuntimeGrandCannonPrewarmTestHooks.h` | 测试钩子 |
| `RuntimeNativeVoxelMuzzleAnchors.inl` | 原生体素炮口锚点 |
| `RuntimeVoxelAnimation.inl` | 体素动画帧 memo |
| `RuntimeVoxelBodyTransform.inl` | 车身变换 memo |
| `RuntimeVoxelCornerProjection.inl` | 角点投影 |
| `RuntimeVoxelCornerProjectionMemo.h` | 角点投影 memo |
| `RuntimeVoxelDebris.h` | 体素碎片记录 |
| `RuntimeVoxelFacePreparation.inl` | 面准备 / 背面剔除 / 材质索引 |
| `RuntimeVoxelLoadingPrewarm.h/.inl` | 当前世界体素预热限制、统计、入口 |
| `RuntimeVoxelLoadingPrewarmAdmission.inl` | 预热准入状态机、载荷估算、发布规则、参考开关 |
| `RuntimeVoxelMuzzleGeometry.inl` | 体素炮口几何 |

### A.3 `src/render/buildings/`（生产）

| 文件 | 角色 |
| --- | --- |
| `RuntimeAuxiliaryAnimation.h` | 通用辅助动画帧范围 / 循环 / 乒乓计算器 |
| `RuntimeBioReactorAnimation.h` | 生物反应堆原始 / 供电层时钟 |
| `RuntimeBuildingCollapseRender.inl` | 倒塌 / 废墟绘制与自定义 PAL |
| `RuntimeBuildingDepth.h` | 深度层枚举、深度帧、逐像素深度、遮挡接受规则 |
| `RuntimeBuildingDepthEmptyScene.h` | 空深度场景参考作用域 |
| `RuntimeBuildingDepthRender.inl` | 建筑深度渲染 RAII 作用域与状态 |
| `RuntimeBuildingDepthRowKernel.h` | 深度行扫描内核 |
| `RuntimeBuildingRepairRender.inl` | 建筑维修呈现 |
| `RuntimeFactoryAirExitForeground.inl` | 工厂出厂口前景墙绘制与遮挡像素捕获 |
| `RuntimeFactoryAirExitRender.inl` | 出厂飞机视野裁剪（背景层遮挡） |
| `RuntimeGrindingAnimationRender.inl` | 碾磨周期演员与帧选择 |
| `RuntimeRepairDepotAnimationRender.inl` | 维修厂伸展 / 作业 / 收起相位 |
| `RuntimeRepairDepotRender.inl` | 维修厂前景排序 |
| `RuntimeSuperWeaponBuildingRender.inl` | 超武建筑层与帧选择 |

### A.4 `src/render/geometry/`（生产）

| 文件 | 角色 |
| --- | --- |
| `RuntimeAirImpactBodyCenterRender.inl` | 空中命中身体中心几何查询 |
| `RuntimeAllWeaponMuzzleRender.inl` | 全武器炮口契约审计渲染（离线） |
| `RuntimeBuildingVisibleHitSpans.h` | 建筑可见命中跨度 |
| `RuntimeCameraGeometryMemo.h` | 相机包络几何 memo |
| `RuntimeGeometryQueryDeclarations.inl` | 几何元数据与体素帧 memo 声明 |
| `RuntimeGeometryQueryMemo.inl` | 几何查询 memo |
| `RuntimeGeometryQueryScope.h` | 几何查询不可变租约 |
| `RuntimeInfantryMuzzleAnchors.inl` / `RuntimeInfantryMuzzleAttachment.inl` / `RuntimeInfantryMuzzleQuery.h` | 步兵锚点表、炮口附着、查询入口 |
| `RuntimeMapSurfacePick.h` | 地面拾取 |
| `RuntimeMarineMuzzleAnchors.inl` / `RuntimeMarineMuzzleAttachment.inl` / `RuntimeMarineMuzzleQuery.h` | 海军（海豚）锚点与附着 |
| `RuntimeMobileSpriteBoundsProbe.h` / `RuntimeMobileSpriteBoundsQuery.h` | 移动单位边界探针与查询 |
| `RuntimeMobileSpriteGeometry.inl` | 移动单位主体分派与边界重放 |
| `RuntimeObjectBodyAnchor.h` | 主体锚点 |
| `RuntimeObjectShpBoundsQuery.h` | SHP 边界查询 |
| `RuntimeObjectWorldRenderPlacement.h` | 世界 / 屏幕放置换算 |
| `RuntimeOpenTransportMuzzle.inl` | 开放式运输炮口（占位） |
| `RuntimePaintSpecialObjectIdMemo.inl` | 绘制对象 ID memo |
| `RuntimeProjectileScreenPoint.h` | 弹体屏幕点 |
| `RuntimeSpawnedMissileBoundsQuery.h` | 导弹包络查询 |
| `RuntimeSpriteGeometry.inl` | 精灵几何捕获与跨度收集 |
| `RuntimeSpriteHitSpan.h` / `RuntimeSpriteHitSpanQueries.h` | 命中跨度结构与包含判定 |
| `RuntimeStructureMuzzleAnchors.inl` / `RuntimeStructureMuzzleQuery.h` | 建筑锚点表与查询 |
| `RuntimeStructureSpriteGeometry.h` / `RuntimeStructureSpriteGeometry.inl` | 建筑主体几何捕获 |
| `RuntimeStructureSpriteQuery.h` | 建筑边界查询 |
| `RuntimeStructureTurretScreenOffset.h` | 建筑炮塔屏幕偏移 |
| `RuntimeStructureWeaponMuzzleAttachment.inl` | 建筑 SHP 炮口附着（像素偏移 + DualOffset FLH） |
| `RuntimeVoxelMuzzleQuery.h` | 体素炮口查询 |
| `RuntimeWeaponMuzzleAttachment.inl` | 武器炮口附着说明与仿射修正约定 |
| `RuntimeWeaponMuzzleGeometry.inl` | 体素武器炮口投影 |
| `win32/RuntimeRenderPlacementWin32.h` | Win32 放置点适配 |

> 各目录 `diagnostics/` 下文件为契约 / 烟雾对照，未列入生产覆盖；完整清单见 `01-源码清单与类层次.md` 2.5。
