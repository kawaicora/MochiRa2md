# Unity 原生架构：对象模型 / 音频 / 联机 / URP

## 0. 当前真实状态（不夸大）

- 已有 14 个 C# 文件，离线编译 0 error；内容是**启动链 + 帧循环骨架**（时间、INI、节流、会话状态机、后备面缓冲）。
- **没有**：场景里可挂的根对象、资源解码（MIX/SHP/VXL/AUD）、任何可见单位/建筑/地图、音频、联机。
- 之前的 `Surface16` 只保留为**解码中间缓冲**；最终画面必须进 Unity 的 Texture2D / Mesh，走 URP 正常渲染，不再用自研软表面当最终上屏路径。

## 1. 对象模型（一切都是 GameObject）

### 1.1 根对象

| 组件 | 职责 |
|---|---|
| `GameManager : MonoBehaviour` | 场景根（DontDestroyOnLoad），持有会话循环、资源工厂、音频服务、网络会话；`Update` 驱动一帧 |
| `FramePresenter` | 呈现后端（已有），只做最后一层上屏 |
| 资源工厂 `AssetFactory` | MIX 读取 → 解码 → 产出 GameObject / Sprite / Mesh |

### 1.2 SHP（2D 立面：步兵、载具、建筑、图标）

```
MIX 目录 → ShpCodec 解码（调色板索引 + 帧元数据：宽/高/偏移/帧数/朝向）
        → Palette 展开为 Texture2D（RGBA32，FilterMode.Point）
        → Sprite.Create（每帧/每方向一个 Sprite）
GameObject
 ├─ SpriteRenderer      （2D 渲染，URP 兼容）
 └─ ShpAnimator        （按帧号/朝向切 Sprite；待机/行走/开火多帧）
```

- Z 序：`sortingOrder` + 世界 Y 坐标；裁剪沿用原版矩形/边距规则。
- 菜单图标、侧栏按钮也走 SHP/Sprite 路线。

### 1.3 VXL（3D 体素模型：载具、飞机、建筑主体）

```
VXL 体素 → 顶点表 + 面表 → Mesh（vertices / triangles / uv 指向调色板纹理）
GameObject
 ├─ MeshFilter
 ├─ MeshRenderer       （URP Lit / Unlit 材质，正常出阴影、遮挡）
 └─ VxlAnimator        （32 方向帧切换；受损等级、动画帧）
```

- 体素硬边：点采样纹理；调色板索引存顶点色或 Palette 贴图。
- 朝向帧：每个方向一个 Mesh，或单 Mesh + 预烘焙朝向表。
- 这是"在 Unity 里跟正常模型一样显示"的落点：不是贴图贴片，是真实 Mesh。

### 1.4 地图

- 地形：Mesh 瓦片或 Terrain；单位/建筑 GameObject 挂在世界坐标上。
- 视野/战争迷雾：组件层处理，不阻塞渲染管线。

## 2. 音频系统

### 2.1 资源与解码

- MIX 内 `.aud` / `.wav` → 解码 PCM → `AudioClip.Create`。
- 压缩编码（Westwood ADPCM 类）：先留解码器接口，PCM 直通优先跑通。

### 2.2 架构（对照 ConcordRTS `audio/backend` + `audio/events`）

```
AudioMixer（Unity 自带）
 ├─ SFX 组     暴露参数 Vol_Sfx
 ├─ Music 组   暴露参数 Vol_Music
 ├─ Speech 组  暴露参数 Vol_Speech
 └─ UI 组      暴露参数 Vol_Ui

SfxChannelPool : AudioSource 对象池   脚步/开火/爆炸/一次性音效，不为每个音效建场景对象
MusicSource    : 循环 BGM
SpeechSource   : EVA / 语音，带打断规则
AudioEvents    : simulation 事件 → 播放对应音效（对照 audio/events 各 Runtime*Audio）
```

- 3D 音效：世界位置 AudioSource；菜单/UI 走 2D。
- 峰值/限幅：先靠 Unity 混音器，后续再补限幅器。

### 2.3 选项音量

- 设置面板滑条 → `mixer.SetFloat("Vol_Sfx", dB)` 等；dB 换算由线性 0..1 映射。
- 落盘 `RA2MD.INI [Audio]`：`Sfx / Music / Speech / Ui`；启动时 `CcIni` 读回并设到混音器。
- 静音/主音量作为总控组。

## 3. 联机

### 3.1 确定性前提（帧同步的硬条件）

- 固定 15Hz 逻辑步长；**只复制输入/命令，不复制状态**。
- RNG 对局内种子一致；逻辑只用整数/定点。
- 每对象校验和沿用原版 55D360 同步段：8 字节帧状态 → 对象数 4 字节 → 每对象 4 字节校验和；不一致即脱战（原版 `sub_48DC90`）。

### 3.2 传输层

```
INetTransport（接口）
 ├─ LocalTransport      单机占位
 ├─ UdpLanTransport     局域网：UDP 广播发现 + 直连
 └─ RelayTransport      在线中继（后置，先留接口）
```

### 3.3 会话流程

1. 大厅：地图 / 玩家 / 颜色 / 队伍 / 昵称 / 端口 / 直连 IP。
2. 地图传输：大厅阶段交换地图哈希，必要时传文件（对照 ConcordRTS `network/lobby/MapTransfer`）。
3. 开局：同步种子、地图、玩家配置；固定步长跑帧同步。
4. 运行期：命令广播 + 校验和比对；失步提示并停赛。
5. 在线匹配/排行后置（对照 `network/internet`），不阻塞局域网闭环。

## 4. URP 约束

- 工程：`com.unity.render-pipelines.universal 14.0.12`（URP 14）。
- SHP：SpriteRenderer（2D，URP 兼容）；VXL：MeshFilter/MeshRenderer + URP Lit/Unlit 材质。
- 调色板：做成 256 色纹理，材质采样；不用运行时逐像素拼 565 作为最终显示。

## 5. 落地顺序（每步可验证）

1. **GameManager 场景入口**：挂上就能跑，修掉"没有可挂对象"的问题；会话循环接到 `Update`。
2. MIX 目录读取 + SHP 解码 → 场景里一个测试 GameObject 显示精灵。
3. VXL 解码 → Mesh 在场景里按朝向显示。
4. AudioMixer + 通道池 + 选项音量读写 INI。
5. UDP 局域网帧同步（大厅 → 开局 → 校验和比对）。
