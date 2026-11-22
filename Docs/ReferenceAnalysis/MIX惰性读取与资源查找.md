# MIX 惰性读取与资源查找链路

## 1. 为什么不能整档加载

`langmd.mix` 84.6MB、其内 `audiomd.mix` 74.5MB、`ra2.mix` 281.9MB。整档 `ReadAllBytes` + 逐条目猜格式会直接把进程打爆（旧实现把 74MB 的 `audiomd.mix` 灌进内存，还把 `thememd.mix` 误判成 18770 条目）。

现在只读「头部 + 目录」，条目字节按需 seek。

| 动作 | 实测 |
| --- | --- |
| 挂载 `langmd.mix`（84.6MB / 10 条目） | 27ms，堆 +32KB |
| 打开内层 `audiomd.mix`（74.5MB / 740 条目） | 1ms，堆 +105KB |
| 读一个条目（13450B WAV） | 0.79ms，只分配该条目大小 |
| 全部 16 个顶层档挂载 + 11 个菜单资源解析 | 122ms，堆 5.5MB（含 64 个档的目录） |

## 2. 三层模型

```
IMixByteSource           随机访问源
├─ FileMixSource         磁盘单 FileStream + lock + seek
├─ MemoryMixSource       内存（编辑器/测试）
└─ SliceMixSource        父档上的区间（嵌套档，零复制）

MixArchive               只持有 source + 目录
├─ Load(path)            读头 → 读目录 → 结束
├─ TryReadEntry(entry)   seek 到 DataStartOffset+Offset，只读这一条
└─ OpenChild(entry)      建 SliceMixSource + 解析它的目录
```

目录加密（flags `0x00020000`）：80 字节密钥块 → Blowfish 解首块拿 `FileCount` → 按 `6 + 12N` 反推密文长度 → 只读这段密文，`DataStartOffset = 密文结束位置`。

## 3. 像文件系统一样用

```csharp
var vfs = VirtualFileSystem.Instance;
vfs.ScanDirectory(Application.streamingAssetsPath);      // 只挂载，不读数据

foreach (var item in vfs.List("ra2.mix"))                 // 拿到列表（含是否还能再开）
    Debug.Log(item.Name + " " + item.Size + " archive=" + item.IsArchive);

var local = vfs.OpenArchive("ra2.mix/local.mix");         // mix 里的 mix
vfs.OpenFile("langmd.mix/audiomd.mix/804BD9D0", out byte[] wav);  // 最深一层直接取字节
```

## 4. 嵌套档怎么判定

只靠名字表会漏：`ra2.mix` 里装菜单 SHP 的那个 45.5MB 内层档**没有名字**，旧逻辑因此整片跳过，主菜单右侧按钮列、底条、右列顶全丢（表现为界面一片白）。

现在：

1. 名字以 `.mix` 结尾，或 CRC 命中已知内层档名 → 直接认；
2. 否则读该条目的 **10 字节头**做嗅探：普通头校 `10 + 12N ≤ Size`；加密头只看体积下限，真正合法性交给 `OpenChild + 95% 目录项越界校验`；
3. 通过才挂载，失败只记 `NestedWarnings`。

代价是每个候选一次 10 字节 read，不碰正文。

## 5. 查找顺序（与游戏一致）

1. `StreamingAssets` 松散文件（含子目录）直接命中 → 优先；
2. 再进 MIX：按名字算 CRC32 → 已挂载档（顶层 + 已打开的内层）里查；
3. 仍未命中 → 触发一次「顶层档内层档目录级扫描」后重查。

裸文件名无扩展名时按 `.shp/.pal/.ini/.csf/.wav/.aud/...` 逐个兜底。

## 6. 名字从哪来

MIX 目录只存 CRC，没有名字。名字表 `MixNameTable` 三个来源：

- 内置已知归档名；
- `StreamingAssets` 里所有松散文件名；
- **`artmd.ini` / `art.ini` / `rulesmd.ini` / `rules.ini`**：段名（`[SDBTNANM]` → `sdbtnanm`、`sdbtnanm.shp`）与 `Image=`/`Cameo=` 等文件名键的值全部登记。

所以「换语言换 CSF」「换资源换 MIX」都不需要改代码：文案走 CSF key，资源走 名字→CRC。

## 7. 主菜单右侧按钮列的正确画法

原版截图比对结论：

- 每槽底板 = `sdbtnanm`（156×42，`sdbtnanm.pal`）：空槽 frame 0（干净金属盘），占用槽 frame 2（金属框 + 黑内框 + 红渐变）；
- 底部「退出遊戲」= 第 9 块底板，同一素材，y = 199 + 8×42 = 535，压在 `sdbtm`（168×65）上；
- `mnbttn`（126×25，金边红面）**不参与**本菜单绘制——原版槽内看不到金黄色边框；
- 垫层 `sdbtnbkgd`（168×42）只露左右各 12px 边带。

画序：`mnscrnl` → `lwscrnl@(0,568)` → `sdtp@(632,0)` → `sdwrnanm@(670,48)` → 每槽 `sdbtnbkgd@(632,199+42i)` → `sdbtnanm@(644,199+42i)` → `sdbtm@(632,535)` → 底部底板 `@(644,535)`。
