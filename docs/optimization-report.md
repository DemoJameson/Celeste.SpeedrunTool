# SpeedrunTool 性能优化报告（perf/savestate-optimization 分支）

> 目标：降低 savestate 的内存开销、提高 save/load 速度、并让游戏在持有 savestate 时不再因
> 它产生额外卡顿（目标场景：8GB 内存旧机器游玩草莓酱 Grandmaster Heartside）。
> 分支：`perf/savestate-optimization`（基于 upstream master `752e6f5`）。
> 所有数字来自同一台开发机（RTX 5070 Ti / 64GB RAM / Windows / .NET 8）的自动化 benchmark，
> D 盘游戏目录 `D:\Games\SteamLibrary\steamapps\common\Celeste`（Everest 1.6662）。
> 结果数字见 `docs/bench-results.md`（由 bench 环境生成）。

## 0. 结论摘要

（待 benchmark 数据回填）

## 1. 方法

1. `code-audit` 通读 save/load 全链路，产出 `docs/audit.md`（内存/性能热点排序、bug 清单、
   实测微基准）；本报告只列最终落地项。
2. `bench-env` 用 Strawberry Jam + 其全部依赖搭出真实场景，写了一个独立的 benchmark 模组
   `SrtBench`（不在本仓库内），通过 ModInterop API 自动执行 save/load 循环，记录耗时、
   GC、托管/进程内存、帧时间。
3. 本分支每个改动都单独提交，benchmark 对比 upstream 构建与优化构建。

## 2. 已落地改动

### 2.1 内存

| 改动 | 说明 | 预期收益 |
|------|------|----------|
| Snapshot 缩略图 | 多槽存档缩略图从整屏纹理改为长边 ≤480px，捕获 buffer 复用，清除/覆盖时 Dispose，重复存档不再泄漏纹理 | VRAM 8.3MB/槽 → ≤0.9MB/槽；每次 save 的 8.3MB LOH 垃圾 → 0 |
| Pre-clone 可开关（Auto/On/Off） | 预克隆在后台保留第二份实体图以加速读档；Auto 在 ≤10GB 内存机器上关闭 | 内存受限机器 savestate 常驻内存约减半 |
| 移除 load 路径的无用克隆 | `UpdateTimeAndDeaths` 里 Session/SaveData 克隆后从未使用；`UnloadLevel` 的 LINQ Distinct、`RestoreAudio_1` 的每实体 `ToArray()`、重复 VirtualAsset | 每次 load 少一次 SaveData 全量克隆和一批临时分配 |
| 值字典/纹理生命周期修复 | Snapshot `ClearAll`/热重载 Dispose；同槽重复存档 Dispose 旧纹理 | 消除 VRAM 泄漏 |

### 2.2 save/load 速度

| 改动 | 说明 | 预期收益 |
|------|------|----------|
| DeepCloner 处理器优化 | 每对象的 2 次 `lock` 去掉；`is` 链、`IsHashSet`/`IsIDictionary`（会分配数组）等类型判断按类型缓存成 bitmask | 克隆开销（含处理器）预计 2.7-3.5x → 接近无处理器水平 |
| Snapshot 不再重复渲染色 | 从 `Level.BeforeRender` 里手动再渲染一遍改为直接读回当前帧的 backbuffer | save 帧少一次整房间渲染 + 一次 GPU 同步 |
| 动作表预热 | 全 mod 类型/静态字段扫描从“第一次 save”挪到“第一次进入关卡” | 消除首次 save 的一次性卡顿 |
| 去掉无用克隆/分配 | 同上 | load 时间下降 |

### 2.3 稳定性 / 兼容性（顺带修复）

- save/load 抛异常不再让游戏卡死：`SaveStateImpl`/`LoadStateImpl` 全程 try/catch，失败时复位
  状态机；load 失败且实体已卸载时自动 reload 关卡；单个实体 `Removed()` 抛异常不再中断 load。
- 恢复被写反的 desync 检测条件：普通地图的 late check 之前永远不执行（Lua 状态实体保护失效）。
- 模组运行期增删 SaveLoadAction 后，已保存槽位的值字典版本不一致时拒绝读档（而不是 NRE/写错
  action）。
- `Areas_Safe`（List）及其 `Modes` 数组的越界检查。
- `InitFields` 的 `&& ||` 优先级 bug：SRT 自己的 Component/Renderer 类型不再被扫描进静态字段表。
- 补充缺失的 CJK 字体 page：fresh clone 构建出的模组在中文语言下启动即崩（`chinese.fnt`
  引用的 png 未入库）。
- `.gitignore` 的 `Dialog`/`Graphics` 规则锚定到仓库根，避免误伤 `SpeedrunTool/Dialog`、
  `SpeedrunTool/Graphics` 下的新资源。

### 2.4 工程/重构

- DeepCloner 从 git submodule 内嵌进仓库（含原工作区未提交的 WIP 改动），`git clone` 后直接
  可构建；新增 `DeepCloner/UPSTREAM.md` 记录来源与原因。
- csproj：`CelestePrefix` / `CelesteTasPath` / `ModRoot` / `DeployDir` 可配置，不再要求必须
  在游戏目录内构建；新增 `DeployToGame` 目标。
- `SaveLoadAction` 拆分为注册/派发（主文件）与内置 action 注册（`SaveLoadAction.BuiltinActions.cs`）。
- 新增 Release 可用的 `SaveLoadProfiler`（`SRT_PROFILE=1`），log.txt 输出分段耗时/GC/内存。
- 新增 save/load ModInterop API（供 TAS/工具/benchmark 使用）。

## 3. 验证

- 构建：`dotnet build -c Release -p:CelestePrefix=<Celeste> -p:DeployDir=<Celeste>/Mods/SpeedrunTool`。
- 单元验证：`bench/` 下的 SrtBench 自动执行：save → load ×N → 内存/GC/帧时间统计，并对比
  upstream 构建；同时覆盖 1 槽 / 3 槽、Pre-clone on/off、TAS 变体与普通变体。
- 实机验证：草莓酱 1.0.13 + 全部依赖，目标地图 `StrawberryJam2021/5-Grandmaster/ZZ-HeartSide`。

## 4. 已知限制 / 后续可做

- pre-clone 与“只保留一份实体图”的取舍是本质的：读档速度与常驻内存二选一，当前用 Auto
  模式按物理内存选择；如果以后要两者兼得，需要把“模板”压缩成实体清单 + 重建 Tracker 的方案
  （`docs/audit.md` B1 档 3），改动面较大。
- `ForceSetField`（DeepCloneTo 的 readonly 字段）仍是反射实现，只用于 Level 根对象字段，
  开销小；如果要统一成 IL 生成器，见 `docs/audit.md` C6/D2。
- 第三方 mod 支持仍是白名单式（`ThirdParty.cs`）；SJ 依赖里的 FrostHelper 等没有专门支持，
  相关风险与建议见 `docs/sj-compat.md`。
