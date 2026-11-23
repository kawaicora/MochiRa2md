# 参考源码 → Unity 复刻 Parity 审计台账

## 校验方向（强制）
- **以参考项目 rs 源代码为唯一准绳**：先读参考 rs 它定义了什么类型/字段/默认值/行为/口径，再到 Unity 工程里核对我方是否实现。
- 禁止反向：不能用 Unity 现有代码去推断参考应有什么；Unity 缺的就是缺口。
- 参考项目名不得出现在工程代码/注释/文档中，一律称“参考/原版口径”。
- 全量清单：`parity_inventory.csv`（参考 rs，1770 个非测试文件）、`parity_unity_inventory.csv`（我方 cs，121 个）。

## 状态口径
- **已复刻**：参考该文件的核心类型/字段/默认值/解析口径在我方有对应实现，默认值一致。
- **部分**：主结构在，但参考的扩展分支/Ares·Phobos 子配置/运行时行为未落地。
- **未落地**：我方无对应类型或解析。
- **待核对**：尚未逐文件读过参考源码，状态未判定。

## 审计顺序（按可玩链路）
1. `rules/` 规则库（对象类型/武器/弹头/子弹/前提/装甲/矿石/移动/超武/地形/覆盖物/粒子/全局规则）
2. `assets/` `asset_tools/` 资源解码（MIX/CSF/SHP/PAL/TMP/VXL/HVA/AUD/BIK/FNT）
3. `map/` 地图解析/地形/RMG/覆盖物/桥/管
4. `sim/` 世界实体/生产/移动/寻路/战斗/采矿/超武/触发/AI
5. `render/` 绘制管线/SHP/VXL/特效/雷达
6. `ui/` 侧栏/HUD/菜单/输入/光标
7. `audio/` 音频通道
8. `util/` 数学/方向/固定点/文本
9. `net/` 联机（远期，不挡单机）
10. `app/` 外壳/阶段/会话装配

---

## 1. rules/ 规则库

### 1.1 已核对（本轮及前序轮次）

| 参考文件 | 职责 | Unity 落点 | 状态 | 缺口 |
|---|---|---|---|---|
| object_type/reader.rs | Techno/Building/Unit/Infantry/Aircraft 类型字段读取 | RulesDatabase.TechnoTypeClass/BuildingTypeClass/Unit/Infantry/Aircraft | 部分 | Ares/Phobos 扩展字段、能力标志子集未落地 |
| object_type.rs | 类型表/查询/默认值 | RulesDatabase 类型表 | 部分 | 查询方法（可建造/射程/归属判定）运行时未接 |
| weapon_type.rs | 武器类型 Damage/Range/ROF/Speed/Burst/Projectile/Warhead/标志 | RulesDatabase.WeaponTypeClass | 部分 | Ares 武器扩展（ApplyDamage/Abductor/IvanBomb/瞄准游标）未落地 |
| warhead_type.rs | 弹头 Verses/CellSpread/阵营作用/伤害类型/残骸/形变 | RulesDatabase.WarheadTypeClass | 部分 | Ares 弹头扩展、粒子/动画表、碎片类型解析未落地 |
| projectile_type.rs | 子弹 AA/AG/飞行行为/Cluster/弹性/Trailer | RulesDatabase.BulletTypeClass | 部分 | Shrapnel/Airburst 武器引用解析、Phobos 空爆未落地 |
| prerequisite.rs | 前提六组 POWER/FACTORY/BARRACKS/RADAR/TECH/PROC + 建筑名 | RulesDatabase.PrerequisiteGroups | 部分 | Ares 自定义前提组、前提覆盖判定运行时未接 |
| armor.rs | 11 类装甲名索引，未知→none(0) | RulesDatabase.ArmorTypes | 部分 | 装甲-弹头交互倍率查询未落地 |
| tiberium_type.rs | 矿石 Value/Power/Growth/Spread/百分比 | RulesDatabase.TiberiumTypeClass | 部分 | Ares 矿石扩展、矿石图像状态未落地 |
| locomotor_type.rs | LocomotorKind/SpeedType/MovementZone 枚举与解析 | RulesDatabase.LocomotorTypes + 类型字段 | 部分 | 通行矩阵行、桥梁可用性等运行时查询未接 |
| superweapon_type.rs | 超武 Type/RechargeTime/Action/SidebarImage/标志 | RulesDatabase.SuperWeaponTypeClass | 部分 | 30 种 Kind 运行时行为、子配置（SonarPulse/SpyPlane/DropPod…）未落地 |
| art_data.rs | 建筑 21 动画槽位/受损态/供电门控/部件锚点 | RulesDatabase.ArtRecord + MapObjectLayer 槽位 | 部分 | PowerUp 槽、部件锚点像素口径待实跑核对 |
| animation_sequence.rs | 序列 Kind/条带/FacingSlots/步兵默认条带 | BodySequence/ArtRecord | 部分 | 步兵 42 动作表全量、死亡条带未接 |
| shp_vehicle_sequence.rs | 车辆 Walk/Stand/Attack 条带/炮塔环/朝向块 | BodySequence | 部分 | 死亡条带、炮塔环起始帧运行时未接 |
| infantry_sequence.rs | 步兵动作记录 | BodySequence | 部分 | 动作→帧映射全量未接 |
| ini_parser.rs | INI 段/键/注释/重复键解析 | MapIniDocument | 部分 | 原生哈希键查找未落地 |
| ini_value.rs | ReadInt/ReadDouble/ReadRange/ReadSpeed/ReadBool/百分比 | MapIniDocument 取值辅助 | 部分 | ReadSpeed 科技速度口径、Leptons 系列、百分位读取待补 |
| ini_directives.rs | [include]/$Inherits 扩展指令 | MapIniDocument.ExpandDirectives | 部分 | 继承回填优先级待核 |
| foundation.rs | 地基尺寸 1x1…→leptons | ArtRecord.Foundation | 部分 | 自定义地基/像素→leptons 换算未落地 |
| flh.rs | 偏移 FLH leptons | ArtRecord 偏移字段 | 部分 | 炮塔/枪管偏移解析不全 |
| ruleset/country_side.rs | 国家/阵营注册与归属、国家倍率/老兵名单/Side 归属 | HouseClass.HouseTypeClass/SkirmishMatch | 部分 | 原生倍率（Firepower/Groundspeed/Airspeed/Armor/ROF/Cost/BuildTime/IncomeMult/Armor·Cost·Speed·BuildTime 五类 Mult）与 ParentCountry、老兵 token 名单本轮已解析；缺口：Color 配色方案注册表未落地（ColorSchemeIndex 恒 -1）；老兵 token→类型索引闭包解析未接；Side 成员向量回挂未维护；Ares 阵营扩展（Crew/Engineer/Technician/Disguise/SurvivorDivisor/BaseDefenses/ParaDrop 等）未落地 |
| country_loading.rs | 国家加载屏表现（File.LoadScreen/PAL/Name/SpecialName/Brief/Color、10 国存量默认表、Side 初始化配色） | 未落地 | 未落地 | 加载屏 SHP/PAL/标签/观察员背景旗、Side 0 AlliedLoad / 1 SovietLoad / 2 YuriLoad→Purple 配色初始化均未接；属菜单/名册表现链路 |

### 1.2 待逐文件核对（rules/ 剩余）

> 顺序：ruleset 全局 → 地形/覆盖物/粒子 → 战斗/生命 → 超武/AI → Ares·Phobos 扩展。

| 参考文件 | 初步判断 | 状态 |
|---|---|---|
| ruleset.rs | 规则集装配/句柄解析/类型闭包 | 待核对 |
| ruleset/loading.rs | 规则多层加载/顺序 pass | 待核对 |
| ruleset/lookup.rs | 类型名→句柄/索引查询 | 待核对 |
| ruleset/sections.rs | 全局段归属与段名绑定 | MapIniDocument 通用段读取 | 部分 | 段已能裸取；未按 General/AudioVisual/CombatDamage/AI/IQ 分类装配 |
| ruleset/general.rs | [General]/[AudioVisual]/[CombatDamage]/[AI]/[IQ] 全局常量：条件红黄阈值、老兵倍率/比例/上限、自修、采矿速率、超武时长、披风、间谍、工程师占领、护盾/铁幕/控脑、闪电风暴、侦察/间隙半径、飞行/悬停高度、AI 团队/防御/经济系数、IQ 等级行为、音效名表 | RulesDatabase 仅裸取 `[General] BaseUnit`（开局 MCV），无类型化全局配置类 | 未落地 | 全部全局键未解析；我方现用硬编码口径：ConditionYellow 50%/ConditionRed 25%、BuildTime 造价估算、GameSpeed 0=Unlimited 等，需对照本文件默认值逐键收口。音效名（Crate*/Building*/GUI*/Impact*/Chrono*…）随 audio 链路落地 |
| ruleset/assets.rs | 资源目录绑定 | 待核对 |
| ruleset/extensions.rs | 扩展 INI 合并 | 待核对 |
| ruleset/config_hash.rs | 规则配置哈希（联机锁步用） | 待核对 |
| ruleset/sound_names.rs | 音效名注册 | 待核对 |
| native_processing.rs / processed_type.rs / pass_processor.rs / pass_families.rs / pass_techno.rs / pass_references.rs / pass_houses.rs / registry_state.rs / type_projections.rs / processed_layers.rs / general_projections.rs / ares_rules_ext.rs / ares_house_state.rs | 有序规则 pass：Damage/Range/Speed/Warhead/Projectile/动画/类型引用回填 | 待核对 |
| native_ini_hash.rs | 原生 INI 哈希键直读 | 待核对 |
| native_processing 目录其余 | 见上 | 待核对 |
| object_type/queries.rs | 类型查询门面：AllowAirstrike（可空，建筑缺省 CanC4、单位缺省 true）、武器 FLH/备用开火 FLH、PitchSpeed 0.25、RollAngle 30°/PitchAngle 20°（-1 保留、度→弧度）、NoSpawnAlt、WaterImage（载具类型引用）、SensorArray、附加超武列表、披风门控/档位、次级转向速率、选择组、水面对应图像 | TechnoTypeClass 已补 PitchSpeed/RollAngle/PitchAngle/NoSpawnAlt/WaterImage/SensorArray 字段与读取 | 部分 | 缺口：查询方法本体未实现（airstrike 门控判定、武器 FLH 偏移表、附加超武列表、披风允许/档位、次级转向速率、选择组、水面图像绑定）；武器偏移表见 weapon_offsets.rs；Ares/Phobos 扩展门面（spotlight/driver/bounty/crew/cloak-power/chronoshift/designator/inhibitor 等）未落地 |
| ares_techno_type.rs | Ares Techno 扩展 | 待核对 |
| ares_building_type.rs | Ares 建筑扩展 | 待核对 |
| ares_weapon.rs | Ares 武器扩展 | 待核对 |
| ares_warhead.rs | Ares 弹头扩展 | 待核对 |
| ares_bullet_type.rs | Ares 子弹扩展 | 待核对 |
| ares_armor.rs | Ares 装甲/交互 | 待核对 |
| ares_prerequisites.rs | Ares 自定义前提组 | 待核对 |
| ares_prism_forwarding.rs | 棱镜转发 | 待核对 |
| ares_cloak_power.rs | 隐形供能 | 待核对 |
| ares_firestorm.rs | 火风暴 | 待核对 |
| ares_infiltration.rs | 渗透 | 待核对 |
| ares_veterancy.rs | 老兵等级 | 待核对 |
| ares_crew.rs / ares_driver.rs | 乘员/驾驶员 | 待核对 |
| ares_emp.rs | EMP | 待核对 |
| ares_bounty.rs | 赏金 | 待核对 |
| ares_spotlight.rs | 探照灯 | 待核对 |
| ares_mouse_cursor.rs | Ares 光标 | 待核对 |
| ares_production.rs | Ares 生产 | 待核对 |
| ares_side_extension.rs | 阵营扩展 | 待核对 |
| ares_country_presentation.rs | 国家表现 | 待核对 |
| ares_country_paradrop.rs | 国家伞兵 | 待核对 |
| ares_switch.rs | Ares 方言开关 | 待核对 |
| ares_abilities.rs | 能力标志 | 待核对 |
| ares_tiberium.rs | Ares 矿石扩展 | 待核对 |
| phobos_*.rs | Phobos 扩展（crit/shields/radiation/airburst/placement_preview/...） | 待核对 |
| terrain_object_type.rs | 地形对象（树/岩/路障）类型：Armor 缺省 wood、Strength 缺省 -1→General TreeStrength、SpawnsTiberium/IsAnimated/AnimationRate/AnimationProbability/Immune/LegalTarget 缺省 false/IsFlammable false/Bombable true/Insignificant true/RadarInvisible true/WaterBound false/IsVeinhole（置位则清 IsLogic 并强制 LegalTarget）、Temperate/SnowOccupationBits 7、Foundation 占用位 | RulesDatabase.TerrainTypeClass（本轮新增，标志位已解析） | 部分 | 缺口：Strength=-1 的 General TreeStrength 回填未接；Foundation 占用位表与 occupy-list 偏移未落地；RadarColor/AlphaImage 未解析；标志位未被渲染/战斗/通行消费 |
| overlay_types.rs | 覆盖物类型：序号→名字/Image，标志 Tiberium/Land/Strength 1/ChainReaction/Wall/DrawFlat true/Armor(wood)/LegalTarget true/IsVeins/IsVeinholeMonster/Gate/Crushable/Crate/CrateTrigger/Overrides/IsRubble/IsARock/NoUseTileLandType true/RadarColor；桥由硬编码序号判定（高桥 24/25/237/238；低木桥 74-101、122-125；低城桥 205-236） | RulesDatabase.OverlayTypeClass（本轮新增，标志位已解析，保留跳号索引）；OverlayMap 现仅消费序号→名字与 Image | 部分 | 缺口：Land 地形语义（LandType→通行/速度代价/挡地）表未落地；桥硬编码序号判定与高低桥区分未接；标志位尚未被渲染/通行/碾压逻辑消费；RadarColor RGB 未解析 |
| smudge_type.rs | 污迹类型：Crater/Burn false、Width/Height 1、Image(25 字符)、AlphaImage；格偏移 = data % Width / Width | RulesDatabase.SmudgeTypeClass（本轮新增） | 部分 | 缺口：AlphaImage 未解析；污迹格偏移换算与渲染层未接 |
| particle_type.rs | 粒子类型：Image(缺省=名)、StartFrame 0、MaxDC 0、MaxEC 1、Damage 0、NumLoopFrames 1、Translucency 0、WindEffect 0、Radius 0、DeleteOnStateLimit false、StartStateAI 0、StateAIAdvance 4、Translucent25/50State 0xFF、Normalized false、ColorSpeed 0、X/YVelocity 1、MinZVelocity 0、ZVelocityRange 1、BehavesLike、EndStateAI/FinalDamageState | 未落地（仅武器上保留 AttachedParticleSystem 字符串） | 未落地 | 粒子类型注册表与粒子生命周期/状态机未实现；随特效/战斗链路（#127/#130）落地 |
| particle_system_type.rs | 粒子系统类型：BehavesLike(Unknown)、Image(缺省=名)、Spawns false、SpawnFrames 1、Slowdown 0、ParticleCap 50、SpawnRadius 0、SpawnCutoff/SpawnTranslucencyCutoff 0、Lifetime -1、SpawnDirection 3 向量、ParticlesPerCoord/SpiralDeltaPerCoord/SpiralRadius、位置/移动/速度扰动系数 0、SpawnSparkPercentage 0、SparkSpawnFrames 0、LightSize 0、OneFrameLight false、Laser false、LaserColor | 未落地 | 未落地 | 粒子系统注册表与生成/生命周期未实现；随特效链路落地 |
| crate_rules.rs | [CrateRules]：FreeMCV false、WoodCrateImg/CrateImg/WaterCrateImg（OverlayType 可空）、HealCrateSound（音效可空）、CrateMinimum 1、CrateMaximum 255（上限 512）、CrateRadius 640 lepton（3 格，ReadRange）、CrateRegen 10.0、UnitCrateType（UnitType 可空）、SoloCrateMoney 2000、SilverCrate=HealBase(2)/WoodCrate=Money/WaterCrate=Money（[Powerups] 槽位） | 未落地（我方无任何宝箱解析/拾取） | 未落地 | 宝箱生成/随机放置/效果表与拾取链路整体未实现；随 D4 采矿/道具链路（#129）落地 |
| combat_damage.rs | [CombatDamage] 全局：MaxDamage 1000、CollapseChance 100、TerrainDeformation、DeformLowestLevel、RailgunDamageRadius 128、OpenToppedWarpDistance 5、FallingDamageMultiplier 1.0、CurrentStrengthDamage true、TiberiumExplosive、IvanDamage 100/IvanTimedDelay 450/IvanIconFlickerRate 8、CanDetonateTimeBomb/DeathBomb、BallisticScatter 256 lepton、Crush 384 lepton（1.8 格，ReadRange 截断） | 未落地（战斗系统整体未实现） | 未落地 | 全局 CombatDamage 段无类型化配置；弹头 Verses 11 项已在 WarheadTypeClass，装甲-弹头交互倍率查询、碾压半径、弹道散射、伊万炸弹参数均未接 |
| jumpjet_params.rs | 跳喷九参：JumpjetTurnRate 4、JumpjetSpeed 14（绝对空速，非 Speed 倍率）、JumpjetClimb 5.0、JumpjetCrash 5.0（坠降=climb+crash）、JumpjetHeight 500（进入 locomotor 时抬到下限 208 lepton）、JumpjetAccel 2.0（减速=accel×1.5）、JumpjetWobbles 0.15、JumpjetDeviation 40、JumpjetNoWobbles false | TechnoTypeClass 九字段本轮已解析（默认值照构造器种子） | 部分 | 缺口：高度下限 208 lepton 钳制未在移动层落地；跳喷爬升/坠降/悬停摆动/偏差的飞行模拟未实现（D1 移动链路） |
| foundation.rs 已列 | — | — |
| building_light.rs | 建筑灯光可见性 | 待核对 |
| building_rubble.rs | 建筑残骸 | 待核对 |
| building_upgrade.rs | 建筑升级槽：PowersUpBuilding(24 字符，空保留，`none` 为字面文本)、PowersUpToLevel(默认 -1，不钳制)、Upgrades(默认 0，不钳制)；3 槽 PowerUp{1..3}Anim/DamagedAnim/LocXX/YY/ZZ/YSort（读 ART 段，仅 Upgrades>0 的槽位可达） | BuildingTypeClass 已补主机名/等级/槽位数与 3 条 BuildingPowerUpArt 记录（Anim/受损 Anim/三坐标/YSort），按 ImageId 取 art 段 | 部分 | 缺口：主机名→BuildingType 索引闭包未解析；升级安装/退役事务、槽位容量门控、跨 INI 层保留旧值的运行时规则未实现 |
| building_extra_power.rs | 额外供能 | 待核对 |
| building_select_bracket.rs | 选中框 | 待核对 |
| gattling_type.rs | 加特林分段：IsGattling、WeaponStages、RateUp/RateDown；IsGattling 且 WeaponStages>1 时读 Stage1..N/EliteStage1..N（15 格连续块，无钳制，Stage7 叠写 EliteStage1、EliteStage7 叠写 RateUp） | TechnoTypeClass 已补 IsGattling 与 15 格 GattlingBlock，按原生顺序读 WeaponStages/RateUp/RateDown，门控循环读 Stage/EliteStage 并保留叠写槽位 | 部分 | 缺口：分段阈值→开火速率/阶段状态机（GattlingState 累加、阶段切换、精英表）运行时未实现；块外越界键（EliteStage9+/Stage15+）按参考残留口径丢弃 |
| gunner_turrets.rs | 炮手/IFV 炮塔：Gunner、IsChargeTurret；18 武器槽炮塔表（缺省 -1）；原版仅 Vehicle `FV` 读 17 对 *TurretIndex/*TurretWeapon（索引字面缺省、武器缺省 -1；武器=-1 写 charge 位，否则炮塔号写入武器槽）；Ares 路线 Gunner 车辆另读 WeaponTurretIndex{N}/WeaponUIName{N} 与扩展表 | TechnoTypeClass 已补 Gunner/IsChargeTurret、18 槽 TurretSlots（-1 初值）、ExtraTurrets/TurretUiNames；FV 17 对映射按原生缺省口径落地 | 部分 | 缺口：Ares 路线编号炮塔别名/UI 名标签未读；炮塔号→模型精灵/朝向切换运行时未实现；武器槽越界自定义索引按参考安全边界忽略 |
| missile_spawn.rs | 三族火箭块（V3Rocket/DMisl/CMisl）：PauseFrames/TiltFrames/PitchInitial/PitchFinal/TurnRate/RaiseRate/Acceleration/Altitude/Damage/EliteDamage/BodyLength/LazyCurve/Type；[CombatDamage] 普通+精英弹头（精英缺省回退本族普通）；CMisl 加速度缺省回退 DMisl 当场值；构造缺省 V3(0/60/0/1.0/0.05/1/0.4/768/1000/1000/256/true)、DMisl(10/60/0/1.0/0.05/1/0.4/768/1000/1000/128/false)、CMisl(10/30/1.0/1.0/1.0/1/0.6/768/500/500/128/false) | MissileSpawnParams + MissileSpawnRules 三族块已落地，按 [General] 前缀读标量与 Type，[CombatDamage] 读双弹头，含 CMisl 加速度回退与精英弹头回退 | 部分 | 缺口：TypeName/Warhead 未做类型表索引闭包；火箭族判定 family_of/rocket_family、神风等待帧、火箭 locomotor 俯仰/爬升/弹道与 Detonate 精英选择运行时未实现；Ares CustomMissile 块（起飞/尾迹动画、间距、自定义武器）未读 |
| recoil.rs | 后座：TurretRecoil 开关；炮塔组 TurretTravel(2)/CompressFrames(1)/HoldFrames(1)/RecoverFrames(1)；炮管组先复制炮塔值再被 Barrel* 覆盖；三个帧计数 clamp ≥1 | TechnoTypeClass 已补炮塔/炮管各四参，炮管种子复制炮塔后读 Barrel* 覆盖，帧计数下限 1 | 部分 | 缺口：后座压缩/保持/恢复帧的枪管/炮塔位移动画运行时未实现 |
| weapon_offsets.rs | 武器槽开火偏移/炮管几何：TurretCount>0 按 WeaponCount 编号槽读 Weapon{n}FLH/BarrelLength/BarrelThickness + EliteWeapon{n}*（精英缺省回退基础）；TurretCount=0 读旧版 Primary/SecondaryFireFLH 与 P/SBarrelLength/Thickness 及精英对；AlternateFLH0..4（缺省回退基础槽0 FLH）；18 槽基础/精英几何 | TechnoTypeClass 已补 TurretCount/WeaponCount、18 槽 WeaponBase/WeaponElite（FLH[3]+炮管长/粗）、5 端口 AlternateFlh；编号/旧版两条读取路径与精英缺省回退均落地 | 部分 | 缺口：FLH 开火点/炮管朝向与长度粗细未被弹道生成/渲染消费；Ares 第 18 槽之后扩展几何未读；TurretLocked 开火门控未实现 |
| attach_effect.rs | 附着效果 | 待核对 |
| mind_control_rules.rs | 精神控制规则 | 待核对 |
| invulnerability_rules.rs | 无敌规则 | 待核对 |
| powerups.rs | 强化道具 | 待核对 |
| digital_display.rs | 数字显示 | 待核对 |
| insignia_type.rs | 军衔徽章 | 待核对 |
| color_scheme.rs / color_add.rs / house_colors.rs / skirmish_colors.rs | 配色方案 | 待核对 |
| audio_sources.rs / sound_ini.rs | 音效源/音效 INI | 待核对 |
| mission_data.rs | 任务数据 | 待核对 |
| personality.rs | AI 性格 | 待核对 |
| team_ai_ini.rs | 小队 AI INI | 待核对 |
| threat_coefficients.rs | 威胁系数 | 待核对 |
| end_game.rs | 终局判定 | 待核对 |
| superweapon_targeting.rs | 超武目标选择：SW.ManualFire(默认 true)/AutoFire(false)/FireIntoShroud(true)/UseAITargeting(false)、SW.AffectsHouse/AffectsTarget/RequiresTarget/RequiresHouse/AIRequiresTarget/AIRequiresHouse 掩码、SW.AITargeting 模式/Constraints 约束/Preference 偏好、SW.RangeMinimum/Maximum(-1 无界)；TargetMask/HouseMask 位判定与 evaluate_target 预检 | SuperWeaponTypeClass 已补上述字段与原始 token 解析 | 部分 | 缺口：HouseMask/TargetMask 位表与解析未落地；手动/自动开火门控、AI 目标模式/偏好/约束、目标预检拒绝链、射程区间判定、时空门建筑位/遗传转化全 Techno 特判均未实现 |
| superweapon_influence.rs | 超武设计器/抑制器：SW.Designators/AnyDesignator/SW.Inhibitors/AnyInhibitor；Techno 侧 HunterSeeker.Ignore、DesignatorRange/InhibitorRange（nullable）、Chronoshift.Allow(true)/IsVehicle(false)/Crushable(true) | SuperWeaponTypeClass 已补设计器/抑制器名单与开关；TechnoTypeClass 已补猎人忽略、双射程 nullable、时空门三态 | 部分 | 缺口：名单→TechnoType 索引闭包未解析；设计/抑制器覆盖判定、射程邻接判定、时空门放置/腾位/碾压规则运行时未实现 |
| airstrike.rs / airstrike_targets.rs | 空袭 | 待核对 |
| aircraft_trailer.rs | 飞机尾迹 | 待核对 |
| bridge_warheads.rs | 桥弹头 | 待核对 |
| terrain_rules.rs | 12 类 LandType（Clear/Road/Water/Rock/Wall/Tiberium/Beach/Rough/Ice/Railroad/Tunnel/Weeds）；每段 Buildable、Foot/Track/Wheel/Hover/Float/Amphibious/FloatBeach 速度代价（缺键 1.0、Winged 硬 1.0、≥1.0 截 1.0、%/100）；GroundBlocked 硬编码 Water/Rock/Wall；TMP 地形字节→LandType 表 [0,8,8,8,8,10,9,3,3,2,6,1,1,0,7,3] | RulesDatabase.TerrainRules/LandType/LandTypeSemantics（本轮新增并装配进 TypeTables.Build） | 部分 | 缺口：地图 subtile 字节尚未经 LandFromTmpTerrain 落到格语义；速度代价未被移动/寻路消费；缺段零行（全阻）口径已保留但未接入通行判定 |
| voxel_anim_type.rs / voxel_frame_catalog.rs / voxel_model_plan.rs / voxel_asset_catalog.rs | 体素动画/帧目录/模型计划 | 待核对 |
| effect_asset_catalog.rs / terrain_asset_catalog.rs / buildup_asset_catalog.rs / alpha_image_assets.rs | 资源目录 | 待核对 |
| object_image.rs | 图像/Image 解析 | 待核对 |
| theme_ini.rs | 主题 INI | 待核对 |
| movies.rs | 影片注册 | 待核对 |
| campaigns.rs / campaign_loading.rs | 战役 | 待核对 |
| secret_lab.rs | 秘密实验室 | 待核对 |
| hunter_seeker.rs | 猎手无人机 | 待核对 |
| radar_event_config.rs | 雷达事件 | 待核对 |
| retail_sources.rs / retail_ini_fixture.rs / rule_policy.rs / compatibility.rs / error.rs / mod.rs | 工具/装配/错误 | 待核对 |
