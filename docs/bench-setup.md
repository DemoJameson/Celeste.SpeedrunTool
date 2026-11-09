# SpeedrunTool 基准测试环境搭建（从零复现）

> 目标：可重复地测量 SpeedrunTool（SRT）savestate 的 save/load 耗时、GC、内存与帧卡顿，
> 用于量化"降低 savestate 内存 / 提升 save/load 速度 / 避免游戏卡顿"的优化收益。
> 最终目标场景是 **Strawberry Jam (SJ) Grandmaster Heartside**；测量机器不是 8GB 目标机，
> 因此本基准的用途是 **同一台机器上不同构建的横向对比**，绝对值仅供参考。
>
> 结果见 [bench-baseline.md](bench-baseline.md)，原始数据在 `D:\DSH\Celeste\bench\results\`。

---

## 1. 环境事实

| 项 | 值 |
|---|---|
| 游戏 | `D:\Games\SteamLibrary\steamapps\common\Celeste`，Celeste 1.4.0.0-fna |
| Everest | 1.6662.0 stable（`Celeste.Mod.mm`） |
| .NET | 8.0.33（游戏 runtime），构建用 dotnet SDK 9.0.320 |
| 图形 | FNA / D3D11，1280x720 窗口，vsync 开（60fps 基准） |
| 机器 | 16 logical CPU，64GB RAM（注意：**不是**用户目标 8GB 旧机） |
| SJ 场景 | `StrawberryJam2021/5-Grandmaster/ZZ-HeartSide`（areaId 141），起点房间 `a0-Start`，222 entities，bounds 464x1016 |
| 备用场景 | `StrawberryJam2021/5-Grandmaster/maya`（SRT 的 desync-safe map），房间 `Insert Coin`，193 entities |
| 构建产物 | `D:\DSH\Celeste\artifacts\{upstream-mod,opt-v1,opt-v2,debug-mod,...}`（每个目录包含 `SpeedrunTool.dll` + `everest.yaml` + `Dialog/` + `Graphics/`） |
| 基准工程 | `D:\DSH\Celeste\bench`（不属于 SRT 仓库，不进 PR） |

---

## 2. 目录与文件清单

```
D:\DSH\Celeste\bench\
├─ SrtBench\                # 基准测试 mod 源码（net8.0, Everest 模组）
│  ├─ SrtBench.csproj
│  ├─ everest.yaml          # Name: SrtBench, DLL: bin/net8.0/SrtBench.dll
│  └─ Source\               # BenchRunner / SrtBridge / BenchConfig / JsonBuilder / SrtBenchModule
├─ configs\                 # 每个 tag 一份测试配置 <tag>.json
├─ env-<tag>.cmd            # 可选：跑该 tag 前设置的环境变量（如 SRT_PROFILE=1）
├─ build.cmd                # 构建 SrtBench 并部署到 Mods\SrtBench
├─ run.cmd <tag> [timeout]  # 一键跑基准：独占检查→清结果→写 RUN→启动→轮询→超时杀→复制 log→打印摘要
├─ summarize.py <tag>       # 打印 results\<tag>.json 的摘要
├─ collect_results.py       # 汇总所有 baseline-*.json → results\SUMMARY.md / summary.json
├─ swap_srt.py              # 安全切换 Mods\SpeedrunTool 到 artifacts\<build>，自动备份 + 保 CJK 字体
├─ install_sj.py            # 安装 Strawberry Jam + 全部 required 依赖（结果写 installed-mods.json）
├─ install_missing.py       # 补装 GameBanana 搜索失败的依赖（当前 3 个：Gravity/memorial/Twig）
├─ run.cmd / swap_srt.py    # 见上
├─ results\                 # <tag>.json（完整数据）、<tag>.done、log-<tag>.txt、harness-<tag>.log、SUMMARY.md
├─ backup_mods\             # 被替换的 SRT 构建、备份的 everest.yaml（yaml\）、last_srt_swap.txt
├─ dl\                      # 下载缓存（SJ map pack 与依赖 zip）
└─ tools\ApiDump\           # 开发辅助：用 Mono.Cecil 反查 Celeste/Everest/mod API 的 IL
```

游戏侧新增/改动：

| 路径 | 说明 |
|---|---|
| `Mods\SrtBench\` | 部署的基准 mod（build.cmd 生成） |
| `Mods\StrawberryJam2021\` + 57 个依赖目录 | SJ 1.0.13 + Assets/AudioA/B/C + 53 个 helper（见 `bench\installed-mods.json`） |
| `Mods\FrostHelper\everest.yaml` | **benchmark 环境改动**：删除了 `OptionalDependencies:` 整段（原因见 §4），原文件备份于 `bench\backup_mods\yaml\` |
| `Mods\SpeedrunTool\` | 随时可被 `swap_srt.py` 切换成不同构建；`Dialog/Fonts/chinese*_0.png` 必须保留 |

---

## 3. 安装 Strawberry Jam + 依赖（不需要 Olympus GUI）

Olympus 的下载逻辑走的是「GameBanana API + 多个 banana 镜像」，可以直接用脚本复刻，
不需要启动 Olympus、不需要登录、也不需要模拟按键。`install_sj.py` 的流程：

1. **SJ 本体**：GameBanana mod **424541**（"Strawberry Jam Collab"，注意不是 150792）。
   它有 5 个文件，各自解压后是**独立的 Everest mod**：
   | 文件 | 文件 id | 解压后 mod |
   |---|---|---|
   | Map Pack v1.0.1.3（94MB） | 1823799 | `StrawberryJam2021` 1.0.13 |
   | Hi-res assets | 941090 | `StrawberryJam2021Assets` 1.0.1 |
   | Audio A/B/C（各约 318MB） | 1060720 / 937262 / 1060721 | `StrawberryJam2021AudioA/B/C` |
2. **读取 `everest.yaml` 的 `Dependencies`**（SJ 有 52 条，其中 51 条是可安装 mod，Everest 跳过），
   递归解析并安装每个依赖的最新版本。
3. **依赖名 → GameBanana mod id**：Everest 内部名（如 `DJMapHelper`）与 GameBanana 显示名
   （`DJ Map Helper`）不同，直接用 GameBanana 搜索会装错（实测 `AdventureHelper` 会命中
   "The Climb"）。可靠做法：
   - 读 Olympus 本地缓存 `C:\Users\Lozen\AppData\Local\Olympus\cached-mod-ids-to-names.json`
     （`{内部名: 显示名}`，6388 条），拿到显示名；
   - 用 `https://maddie480.ovh/celeste/gamebanana-search?q=<显示名>` 或
     `https://gamebanana.com/apiv11/Util/Search/Results?...&_idGameRow=6460`
     搜索，**按显示名精确匹配**（normalize 后相等）才接受；
   - `memorialHelper` 是 GameBanana **Tool**（tools/6850）而不是 Mod；GitHub-only 的 mod
     不能靠这个流程装（本基准所有依赖都在 GameBanana 上有）。
4. **下载文件**：按顺序尝试镜像（均实测可用，约 5–22MB/s）：
   - `https://celestemodupdater.0x0a.de/banana-mirror/<fileId>.zip`
   - `https://banana-mirror-mods.celestemods.com/<fileId>.zip`
   - `https://library.risingsunlight.dev/celeste/mods/<fileId>.zip`
   - `https://celeste.weg.fan/api/v2/download/gamebanana-files/<fileId>`
   - `https://gamebanana.com/dl/<fileId>`
5. **解压**：检测 zip 内 `everest.yaml` 位置（根目录或单层子目录），按 `Name:` 放进
   `Mods\<Name>\`，并校验解压后的 `version >= SJ 要求的版本`；已有更高版本则跳过。
6. **最终校验 + 记录**：逐条对照 SJ `everest.yaml` 的 51 条 required 依赖，全部满足才返回 0；
   安装清单写入 `bench\installed-mods.json`（name/version/gamebanana id/file id）。

`TwigHelper 1.6.0` 是个 **293 字节的 stub mod**（只有 everest.yaml，依赖 `Everest` + `JackalHelper`，
功能已并入 JackalHelper），`install_missing.py` 按正常文件太小的规则会拒装，已手工解压安装。

### 3.1 验证

```bash
# 期望：required satisfied: 51/51, missing=[]
python D:\DSH\Celeste\bench\check_deps.py
```

> 注意：`check_deps.py` 用 indentation-aware 的极简 YAML 解析器（`install_sj.py` 内），
> 因为 Everest 的 everest.yaml 是「顶层 mod 在 0 缩进、依赖在 4 缩进」的小众格式。

---

## 4. 关键环境修复（否则 SJ 不会被加载）

**症状**：SJ 装全了，但 `AreaData.Areas` 里没有任何 SJ 地图（`jam_area_count=0`），
日志里 `StrawberryJam2021` 只有一行 `Dependency StrawberryJam2021Assets ... not loaded! Delaying.`，
之后既不加载也不报错。

**根因**（Everest loader 的 delayed-retry 缺陷，1.6662 实测）：
- `FrostHelper 1.81.1` 因 **OptionalDependencies 里的 `OutbackHelper`** 尚未加载而被延迟
  （`Loader.LoadModDelayed` 把 mod 放进 `Loader.Delayed`）；
- 主加载循环结束后 Everest 执行 `enforceOptionalDependencies=false` +
  `CheckDependenciesOfDelayedMods()`，但某些情况下延迟 mod 不会被重新扫描并加载；
- `FrostHelper` 是 SJ 的 **required dep** → SJ 也被延迟且永不加载 → SJ 地图不注册。
- 附带影响：`CeilingUltraGym`、`TestMap`、`GhostModForTas` 等同样卡住。

**修复（benchmark 环境专用，不属于 mod 本体）**：把 FrostHelper 的 optional 依赖段删掉：

```bash
# 备份（已做，原始文件在 bench\backup_mods\yaml\FrostHelper.everest.yaml.bak）
# 修改 D:\Games\SteamLibrary\steamapps\common\Celeste\Mods\FrostHelper\everest.yaml
# 删除从 "OptionalDependencies:" 到文件末尾的内容，只保留 Name/Version/DLL/Dependencies。
```

修改后日志应出现：

```
Dependencies of mod StrawberryJam2021 1.0.13 are now satisfied: loading
Registered code module Celeste.Mod.StrawberryJam2021.StrawberryJam2021Module for module StrawberryJam2021 1.0.13
```

`AreaData.Areas` 会从 15 涨到 **143**（SJ 128 个），`ZZ-HeartSide` 的 areaId = **141**。

**回滚**：`cp bench\backup_mods\yaml\FrostHelper.everest.yaml.bak Mods\FrostHelper\everest.yaml`。

> 注意：这是测试环境改动，**报告/结果里需要标注**"benchmark 环境移除了 FrostHelper 的
> OptionalDependencies"。对 SRT 本身无影响。

**另一个进图坑**：collab map 用 `LevelEnter.Go(new Session(...))` 进入时，如果
`session.StartedFromBeginning == true`，会走 `EnterWithPostcardRoutine` 等玩家按键（自动化
会一直卡住）。harness 里在 `Go` 之前设置 `session.StartedFromBeginning = false`。

---

## 5. 构建 / 部署 SrtBench

前置：SRT 构建需要带 **ModInterop API**（`SpeedrunTool.SaveLoad` 导出
`SaveStateTas/LoadStateTas/SaveState/LoadState/ClearState/SwitchSlot/SetPreCloneEnabled/...`）。
upstream 老构建没有这些导出，`SrtBridge` 会反射 fallback 到内部
`SaveSlotsManager.SaveStateTas(string)` 等 public 方法（实测可用），但 **PreClone 开关不可用**
（`preclone_supported=false`）。

```bash
cd D:\DSH\Celeste\bench
build.cmd
# 等价于:
#   dotnet build SrtBench\SrtBench.csproj -c Release -p:CelestePrefix="D:\Games\SteamLibrary\steamapps\common\Celeste"
#   复制 everest.yaml + bin\net8.0\SrtBench.dll 到 Mods\SrtBench\
```

SrtBench 依赖（everest.yaml）：`Everest 1.6662.0`、`SpeedrunTool 3.27.0`。
它不含任何 SRT 编译期引用（全靠反射），因此**不用重新编译**就能切换 SRT 构建。

---

## 6. 跑测试

```cmd
cd D:\DSH\Celeste\bench
run.cmd <tag> [timeout_seconds=600]
```

`run.cmd` 做的事：

1. 校验 `configs\<tag>.json` 与 `Mods\SrtBench` 存在；
2. **单实例保护**：若已有 `Celeste.exe`，最多等 120s（每 2s 轮询）后才启动；
3. 复制 `configs\<tag>.json` → `bench.json`，删除旧 `results\<tag>.*`；
4. 若存在 `env-<tag>.cmd` 则 `call` 之（用来设 `set SRT_PROFILE=1` 等环境变量）；
5. 写 `bench\RUN`（harness 的触发文件），从游戏目录 `start Celeste.exe`；
6. 每 2s 轮询 `results\<tag>.done` / 进程存活；超时则 `taskkill /F /IM Celeste.exe`；
7. 复制游戏 `log.txt` → `results\log-<tag>.txt`，删 `RUN`；
8. 调 `summarize.py` 打印摘要。

### 6.1 配置字段（`configs\<tag>.json`）

```jsonc
{
  "tag": "baseline-preclone-on",     // 结果文件名 results\<tag>.json
  "iterations": 20,                  // TAS 模式 save/load 循环次数
  "warmup": 3,                       // 测量前的热身循环
  "slots": 3,                        // 顺序存档 1/2/3 个槽，测量常驻内存增量（0=不测）
  "preclone": true,                  // true/false，经 ModInterop SetPreCloneEnabled 生效
  "mode": "both",                    // tas | normal | both
  "normal_iterations": 3,            // normal 模式（含 freeze+wipe）次数
  "post_load_ms": 1000,              // 每次 load 后收集帧时间的窗口
  "freeze_off": true,                // 把 SRT FreezeAfterLoadStateType 设为 Off（否则冻结等输入）
  "gc_after_clear": false,           // 关掉 ClearState 的强制 GC，便于测原始内存
  "gc_after_load": false,
  "target_sid": "StrawberryJam2021/5-Grandmaster/ZZ-HeartSide",
  "target_level": "",                // 可选：指定房间；空=地图起始房间
  "target_area_id": -1,              // 可选：直接指定 AreaData.ID（-1=按 SID 查）
  "bench_dir": "D:\\DSH\\Celeste\\bench",
  "output_dir": "D:\\DSH\\Celeste\\bench\\results"
}
```

### 6.2 harness 流程（SrtBench）

1. `Engine.Update` hook（`On.Monocle.Engine.Update`）每帧驱动状态机；
2. 检测到 `bench\RUN` → 读 `bench.json`；
3. 等游戏进入 `Celeste.Overworld`（主菜单）→ 反射初始化 SRT API → 写 SRT 设置
   （`FreezeAfterLoadStateType=Off`、`GcAfter*State=false`、`NoMessageAfterSaveLoad=true`）
   并 `SetPreCloneEnabled(cfg.preclone)`；
4. `SaveData.Start(new SaveData(), 0)`（主菜单没有 SaveData.Instance）+ 解析 `target_sid` →
   `LevelEnter.Go(session, false)`（`StartedFromBeginning=false` 防 postcard）；
5. 等 `Engine.Scene is Level` + `Session.Area.ID == target` + player 存在 → settle 1.5s；
6. 热身、TAS 循环（每帧只做一次 save 或 load，便于把长帧归因）：
   - save：`SaveStateTas(slot)`，Stopwatch 计 call 时间；前后各取 GC/内存快照；
   - load：`LoadStateTas(slot)` 同上；然后收集 `post_load_ms` 窗口内每帧耗时；
7. slots：`ClearState()` 后依次 `SaveStateTas("benchs1/2/3")`，每次等 1.2s + 强制 GC 后取内存；
8. normal 模式（可选）：`SaveState()` / `LoadState()`（非 TAS，走 freeze + wipe），
   记录 call 时间、从调用到 SRT `State == None` 的 **visible 时间**、窗口内最大帧；
9. 写 `results\<tag>.json` + `results\<tag>.done`，`Celeste.Instance.Exit()` 退出游戏。

### 6.3 失败排查

| 现象 | 看哪里 |
|---|---|
| `status=error` | `results\<tag>.json` 的 `error` 字段 + `harness-<tag>.log` |
| 游戏 4x 秒后退出但没有结果 | `results\harness-<tag>.log` 最后一行（崩在读图/主菜单）、`LogHistory\log_*.txt`、游戏目录 `errorLog.txt` |
| `SpeedrunTool interop unavailable` | 当前 SRT 构建太老（无 interop 且反射也找不到），换构建 |
| 进图超时 | `harness-<tag>.log` 的 `waiting for level: scene=...`；若 scene 是 `Celeste.LevelEnter` 且一直在，检查 postcard/`StartedFromBeginning`；若 `LevelLoader` 崩溃见 §10 |
| 主菜单卡住等 saveData | 不应再出现：旧版 harness 会等 `SaveData.Instance != null`，主菜单永远为 null；已改为 `SaveData.Start` |

---

## 7. 切换 SpeedrunTool 构建

`artifacts\<name>\` 目录须包含 `SpeedrunTool.dll` + `everest.yaml`（+ 可选 `Dialog/`、`Graphics/`）。

```bash
python D:\DSH\Celeste\bench\swap_srt.py opt-v1          # 切到 artifacts\opt-v1
python D:\DSH\Celeste\bench\swap_srt.py upstream-mod    # 切到 upstream baseline
python D:\DSH\Celeste\bench\swap_srt.py --current       # 查看当前部署版本+字体数
python D:\DSH\Celeste\bench\swap_srt.py --restore       # 恢复上一次被替换的构建
```

行为：先 `taskkill` 检查（**有 Celeste 在跑会拒绝执行**），把当前 `Mods\SpeedrunTool` 移动到
`bench\backup_mods\SpeedrunTool-replaced-<timestamp>-<version>`（不删除），再复制目标构建；
如果目标构建缺少 `Dialog\Fonts\chinese*_0.png`，自动从被替换构建补回（CJK 字体必须保留），
并记录 `backup_mods\last_srt_swap.txt`。

> ⚠️ 不要手工 `rm -rf Mods\SpeedrunTool`：会丢 CJK 字体 PNG。
> 换构建后建议看一眼 `--current` 的输出和 `Mods\SpeedrunTool\SpeedrunTool.dll` 的大小/md5。

---

## 8. 指标定义（`results\<tag>.json`）

**计时（主线程 Stopwatch，单位 ms）**
- `iterations[i].save_ms` / `load_ms`：`SaveStateTas`/`LoadStateTas` 同步调用耗时（纯 save/load 成本，
  不含 freeze/wipe）；
- `iterations[i].save_frame_ms` / `load_frame_ms`：包含该调用的**引擎帧耗时**（下一帧开头测量，
  即玩家/渲染真正感知的帧长）；
- `iterations[i].window_*`：load 调用后 `post_load_ms` 窗口内的帧时间（含 load 帧本身）；
- `iterations[i].window_after_load_frame_*`：**去掉 load 帧之后**的帧统计（p50/p99/max、
  `over16ms/over33ms` 帧数）——判断"load 之后有没有持续/周期性掉帧"的关键；
- `normal[i].save_call_ms/load_call_ms`：非 TAS 调用耗时；
- `normal[i].save_visible_ms/load_visible_ms`：从调用到 SRT 解除 freeze（`State==None`）的
  **体感耗时**（含 wipe 动画）；
- `normal[i].*_max_frame_ms`：freeze/wipe 期间最大帧。

**GC / 分配**
- `save_gc0/1/2`、`load_gc0/1/2`：该次调用触发的 GC 次数增量（`GC.CollectionCount` 差）；
- `save_alloc_bytes` / `load_alloc_bytes`：`GC.GetTotalAllocatedBytes(false)` 差（进程级）；
- `save_thread_alloc_bytes` / `load_thread_alloc_bytes`：主线程 `GC.GetAllocatedBytesForCurrentThread` 差；
- `summary.gc_delta_total`：所有迭代累计。

**内存**
- `baseline_memory.*`：进图稳定后（含强制 GC）的 `GC.GetTotalMemory(false)` / WorkingSet /
  PrivateMemorySize64；
- `iterations[i].save_managed_after` / `load_managed_after` / `load_private_after`：每次动作后快照；
- `summary.memory_after_load.*`：load 后 managed/private/WS 的均值/min/max；
- `slots[i].delta_managed_bytes/delta_private_bytes/delta_ws_bytes`：存档 1/2/3 槽、等待 1.2s +
  强制 GC 后相对 baseline 的增量（**managed 增量最可信**，private 受 LOH/碎片噪声影响大）。

**帧时间**
- `summary.all_frames`：整个测量期所有帧 p50/p99/max（60fps 基准 ≈ 16.67ms）；
- `summary.post_load_window_after_load_frame.*`：上面"去掉 load 帧"的窗口聚合。

**环境**
- `env.srt_version`（注意：SRT 的 AssemblyVersion 是 1.0.0.0，不反映真实版本，看 `Mods\SpeedrunTool\everest.yaml`）、
  `env.srt_api`（反射到的导出类）、`env.preclone_supported/applied`、
  `env.env_SRT_PROFILE`、`env.sid/start_room/start_room_entities/start_room_bounds`、
  `env.mods_loaded`（EverestModule 数量）。

---

## 9. 打新 tag / 测新构建的标准流程

```cmd
:: 0) 确认构建产物 & 没有游戏在跑
tasklist | findstr /I Celeste.exe
dir D:\DSH\Celeste\artifacts\<build>

:: 1) 切构建（自动备份当前 + 补字体）
python D:\DSH\Celeste\bench\swap_srt.py <build>

:: 2) 冒烟：确认能进图、能 save/load（~50s）
::    用 configs\smoke-vanilla.json（原版 1A，2 iter），崩不崩一眼看出
run.cmd smoke-vanilla 200

:: 3) 正式矩阵（每个 tag 建议 >=2 次重复，取中位数）
run.cmd baseline-preclone-on 420      :: preclone=true
run.cmd baseline-preclone-off 420     :: preclone=false
::   （configs 里 preclone 字段控制；slots=3 会一次测出 1/2/3 槽）

:: 4) 汇总
python D:\DSH\Celeste\bench\collect_results.py     :: → results\SUMMARY.md
```

命令行小抄：

```cmd
copy configs\baseline-preclone-on.json bench.json  :: 手工改 config 时
```

**新 tag 命名建议**：`<build>-preclone-<on|off>[-<scene>][-repN]`，例如
`opt-v2-preclone-on`、`opt-v2-preclone-off`、`upstream-preclone-on`、
`baseline-preclone-on-maya`。

---

## 10. 已知问题 / 注意事项

1. **单实例**：同时只能有一个 Celeste。SRT/游戏会抢 `log.txt`、Steam、音频，且两个实例都会写
   `results`，测量完全不可信。`run.cmd` 与 `swap_srt.py` 都有检查，别绕过。
2. **不要回 Overworld**：CommunalHelper 的 Elytra hook 在"没有 Player 的 Overworld + 有 SaveData"
   状态下会 NRE 并打崩游戏（`errorLog.txt` 可见 `Elytra.Input_UpdateGrab`）。harness 进图后
   不再回 Overworld，测完直接 `Celeste.Instance.Exit()`。
3. **`Mods\Cache`**：Everest 的 mod assembly 缓存。首次装很多 mod 后第一/第二次启动会重建缓存
   （启动 40–60s）；缓存正常后约 12–20s。缓存损坏时表现是某些 mod 不加载/启动卡住，删掉即可重建。
4. **SRT 构建回归记录（2026-11）**：构建 `opt-v2`（以及 bisect 的 `26a0af0`/`9d4f3cb`）在当前环境
   **一进图就无痕崩溃**（LevelLoader 读图时进程直接消失，无 managed 异常、`errorLog.txt` 不更新，
   像 StackOverflow/native FastFail）。bisect 结论见 [bench-baseline.md](bench-baseline.md) §9：
   罪魁是 `DeepClonerSafeTypes.KnownTypes.Clear()`。若拿到的构建带这个调用，smoke 会失败——换构建。
5. **SRT 旧构建没有 ModInterop**：`SrtBridge` 反射 fallback 到内部 `SaveSlotsManager`，能测
   save/load/clear/slots，但不能切 PreClone（`preclone_supported=false`）。
6. **SJ probe 判据**：`run.cmd probe-sj` 会写 `results\probe-probe-sj.json`，健康时
   `jam_area_count=128`、`target_resolved=true`、`target_area_id=141`。
7. **8GB 目标机**：本机 64GB，private/WS 绝对值不能直接外推到 8GB；但：
   - managed / slot 增量与分配量（GC 次数）是可迁移的相对指标；
   - 8GB 机器上 `GcAfterLoadState` / `MemoryThreshold` 等设置的影响会更大，需在目标机复测。
8. **normal 模式依赖 `FreezeAfterLoadStateType=Off`**：否则 SRT 会冻结等按键，自动化无法继续
   （harness 会最多等 20s 后强制 `OutOfFreeze` 兜底）。
9. **帧时间基准**：vsync 开、60fps，所以"正常帧"≈16.67ms；`>16.7ms` 帧数才有意义。
10. **benchmark 环境改动要写进报告**：FrostHelper optional deps 删除、`StartedFromBeginning=false`、
    不装 TASHelper 的版本约束等；这些不影响 SRT 本体。

---

## 11. 附录：已安装 mod 清单

- 机器可读清单：`D:\DSH\Celeste\bench\installed-mods.json`
  （`{generated, mods_dir, sj_mod, mods:[{name,version,gamebanana_mod_id,gamebanana_file_id}], missing:[]}`）
- 数量：SJ map pack + Assets + AudioA/B/C + 53 个 required helper = 58 个 mod 目录；
  加上用户原有的 SpeedrunTool / CelesteTAS / BodyDouble / CeilingUltra / LozenTestMod 等，
  Everest 实际加载 **60 个 modules**（见每次 `results\log-<tag>.txt` 的 `Loaded N modules`）。
- 关键版本：SJ `StrawberryJam2021 1.0.13`、CollabUtils2 `1.13.6`、FrostHelper `1.81.1`、
  MaxHelpingHand `1.40.11`、CelesteTAS `3.46.2`、SpeedrunTool 见当前 `Mods\SpeedrunTool\everest.yaml`。
- 校验依赖：`python D:\DSH\Celeste\bench\check_deps.py`（需 `install_sj.py` 的解析器）。
