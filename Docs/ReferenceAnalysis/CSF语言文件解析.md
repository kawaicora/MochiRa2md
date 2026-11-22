# CSF 语言文件（ra2md.csf / ra2.csf）解析要点

本工程所有界面文案只从 CSF 取：`MenuStrings.Get(key, fallback)`。换语言 = 换 StreamingAssets 里的本地化 CSF，代码零改动。

## 文件结构（实测 version 3）

```
header  : " FSC" | version | labelCount | stringCount | unused | language
label   : " LBL" | pairCount | nameLength | name(ASCII) | pair × pairCount
pair    : " RTS" | length(chars) | text
          "WRTS"| length(chars) | text | extraLen | extra(ASCII)
```

- 标签一律带前导空格：`" LBL"`(20 4C 42 4C)、`" RTS"`(20 52 54 53)；扩展串标签是 `"WRTS"`(57 52 54 53)，无前导空格。
- **version ≥ 3 的文本是逐字节取反存储的**：期望 `主選單` = `3B 4E 78 90 AE 55`，文件里是 `C4 B1 87 6F 51 AA`。解码前每个字节 `^0xFF`，再按 UTF-16LE 读（`charSize = version>=3 ? 2 : 1`）。
- `WRTS` 的附加字段在**文本之后**：先 `extraLen(4)` 再 `extraLen` 字节 ASCII（实测为 8 字节，如 `ceva001c`）。早期把附加字段放在长度之后，会在第 1722 条处错位并报 `期望 LBL，实际 ceva`。

## 实测数据（本机镜像）

| 文件 | 来源 | 声明标签 | 解析结果 | 语言 |
|---|---|---|---|---|
| `ra2md.csf` | `langmd.mix:0xBD835079`（332973B） | 5211 | 5211 | 9（中文） |
| `ra2.csf` | `language.mix:0x7F6FBCDF`（283368B） | 4479 | 4476 | 9（中文） |

取到的关键条目：`GUI:SinglePlayer`=單人遊戲、`GUI:Internet`=網際網路、`GUI:Network`=網路、`GUI:MoviesAndCredits`=動畫及製作人員、`GUI:Options`=選項、`GUI:ExitGame`=退出遊戲、`GUI:MainMenu`=主選單、`TXT_SLOWEST/SLOWER/SLOW/MEDIUM/FAST/FASTER/FASTEST`=最慢/較慢/慢/中等/快/較快/最快、`GUI:GameSpeed`=遊戲速度、`GUI:Back`=上一頁。

## 与原版 CSF 的差异（不要当成缺陷去"补"）

本地化 CSF 里**没有** `GUI:Resolution`、`GUI:TargetFPS`、`GUI:Apply` 这三条——它们是参考实现自定的 key，原版 YR 的选项页走的是 `GUI:GameOptions` 那套滑杆。因此这三个按钮会落到 `csf.get(key, fallback)` 的英文兜底（Resolution / Frame Rate / Apply），`遊戲速度` 走 CSF 中文。想让它们也本地化，正确做法是补 CSF 条目，而不是把中文写进代码。

## 代码落点

- `Assets/Scripts/Assets/CsfFile.cs`：解析器（`TryParse` / `TryGet` / `Get` / `Keys`）。
- `Assets/Scripts/Menu/MenuStrings.cs`：装载 `ra2md.csf` → 退回 `ra2.csf`，对外只暴露 `Get(key, fallback)`。
- `MenuShell.ButtonSpec` 增加 `Key` 字段；`MenuButtonPanel` / `MenuSlot` 携带 key，点击派发按 **key** 走（换语言不影响分支）。
- `MainMenuController`：按钮表、页标题、选项行标签/值全部按 key 取；选项页数值行 = `x + " x " + y`、`fps + " FPS"`、`TXT_*` 速度标签（对照参考 1301-1336）。
