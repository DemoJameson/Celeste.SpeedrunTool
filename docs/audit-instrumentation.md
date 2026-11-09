# SaveLoad 插桩方案（可直接落地的 profiling 代码建议）

> 配套 `docs/audit.md` 的 E 节。**本文件只给建议代码，没有修改任何源文件**。
> 目标：让 bench-env / Lead 在 **Release 构建** 上拿到 save/load 的分段耗时、分配量、克隆类型分布、内存占用，从而验证 audit 里的热点排序。

## 0. 为什么需要新方案

- 现有 profiling 全部包在 `#if DEBUG`（`Source/DebugTool/Config.cs`、`Source/DebugTool/MemoryTracker.cs`）→ Release 构建**没有任何数据**，而性能问题只会在 Release/旧机上出现差异（JIT 分层、GC 配置）。
- 现有 `SaveLoadAction.OnSaveState/OnLoadState` 的 per-action 计时（`SaveLoadAction.cs:183-225`）依赖 `ActionDescription`，它也在 `#if DEBUG` 里。

建议：新增一个**不依赖 DEBUG** 的轻量 profiler，用环境变量/设置项开启，默认零开销（`if (!Enabled) return;`）。

## 1. 建议新增 `Source/SaveLoad/Profiling/SaveLoadProfiler.cs`

```csharp
namespace Celeste.Mod.SpeedrunTool.SaveLoad.Profiling;

internal static class SaveLoadProfiler {
    // 开关：环境变量 SRT_PROFILE=1 或 ModSettings.Profiling（建议加隐藏设置项）
    internal static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SRT_PROFILE") == "1";

    private static readonly System.Diagnostics.Stopwatch Sw = new();
    private static long _lastTicks;
    private static readonly System.Text.StringBuilder Line = new();

    internal static void Begin(string tag) {
        if (!Enabled) return;
        Sw.Restart();
        _lastTicks = 0;
        Line.Clear();
        Line.Append("SRT_PROFILE ").Append(tag);
    }

    internal static void Mark(string seg) {
        if (!Enabled) return;
        long now = Sw.ElapsedTicks;
        double ms = (now - _lastTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastTicks = now;
        Line.Append(' ').Append(seg).Append('=').Append(ms.ToString("F2"));
    }

    internal static void End() {
        if (!Enabled) return;
        long alloc = GC.GetAllocatedBytesForCurrentThread();
        var info = GC.GetGCMemoryInfo();
        Line.Append(" heapMB=").Append((info.HeapSizeBytes / 1048576.0).ToString("F1"))
            .Append(" fragMB=").Append((info.FragmentedBytes / 1048576.0).ToString("F1"))
            .Append(" gc=").Append(GC.CollectionCount(0)).Append('/').Append(GC.CollectionCount(1)).Append('/').Append(GC.CollectionCount(2));
        Logger.Info("SpeedrunTool/Profile", Line.ToString());
    }
}
```

> 注意：`GC.GetAllocatedBytesForCurrentThread()` 只统计当前线程；preclone 在后台线程时要在线程内单独取。

## 2. `StateManager` 分段（最重要）

`Source/SaveLoad/StateManager.cs`：

**`SaveStateImpl`（`:157-240`）**
```csharp
SaveLoadProfiler.Begin("save");
...
SaveLoadAction.InitSlots();                       SaveLoadProfiler.Mark("initSlots");
SaveLoadAction.OnBeforeSaveState(level);          SaveLoadProfiler.Mark("beforeSave");
level.DeepCloneToShared(savedLevel = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level)));
                                                  SaveLoadProfiler.Mark("cloneLevel");
savedSaveData = SaveData.Instance.DeepCloneShared(); SaveLoadProfiler.Mark("cloneSaveData");
SaveLoadAction.OnSaveState(level);                SaveLoadProfiler.Mark("saveActions");
PreCloneSavedEntities();                          SaveLoadProfiler.Mark("precloneStart");
// 末尾
SaveLoadProfiler.End();
```

**`LoadStateImpl`（`:243-319`）**
```csharp
SaveLoadProfiler.Begin("load");
...
SaveLoadAction.OnBeforeLoadState(level);          SaveLoadProfiler.Mark("beforeLoad");
DeepClonerUtils.SetSharedDeepCloneState(preCloneTask?.Result);
                                                  SaveLoadProfiler.Mark("waitPreclone");
UpdateTimeAndDeaths(level);                       SaveLoadProfiler.Mark("timeDeaths");
UnloadLevel(level);                               SaveLoadProfiler.Mark("unloadLevel");
Tracker.Refresh(savedLevel);                      SaveLoadProfiler.Mark("trackerRefresh");
savedLevel.DeepCloneToShared(level);              SaveLoadProfiler.Mark("cloneBack");
SaveData.Instance = savedSaveData.DeepCloneShared(); SaveLoadProfiler.Mark("restoreSaveData");
SaveLoadAction.OnLoadState(level);                SaveLoadProfiler.Mark("loadActions");
PreCloneSavedEntities();                          SaveLoadProfiler.Mark("precloneStart2");
SaveLoadProfiler.End();
```

**`PreCloneSavedEntities`（`:538-549`）**：在线程内单独测
```csharp
preCloneTask = Task.Run(() => {
    long a0 = GC.GetAllocatedBytesForCurrentThread();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    DeepCloneState deepCloneState = new();
    savedLevel.Entities.DeepClone(deepCloneState);
    savedLevel.RendererList.DeepClone(deepCloneState);
    savedSaveData.DeepClone(deepCloneState);
    if (SaveLoadProfiler.Enabled)
        Logger.Info("SpeedrunTool/Profile",
            $"preclone ms={sw.Elapsed.TotalMilliseconds:F1} allocMB={(GC.GetAllocatedBytesForCurrentThread()-a0)/1048576.0:F2} entries={deepCloneState.Count}");
    return deepCloneState;
});
```
（`DeepCloneState` 需要加一个 `public int Count => _loops.Count;`，见 audit D2/B2。）

**`ClearStateImpl`（`:321-353`）**：`Begin("clear")` + 在 `GcCollect` 前后 `Mark("gc")`。

## 3. 每 action 计时（Release 可用）

`Source/SaveLoad/SaveLoadAction.cs:183-225`：
- 把 `ActionDescription` 从 `#if DEBUG` 移出（注册时算一次，`StackTrace` 成本只发生在注册期），`#if DEBUG` 只保留“日志级别更高”。
- `OnSaveState`/`OnLoadState` 的循环里，若 `SaveLoadProfiler.Enabled`：
```csharp
foreach (var a in SharedActions) {
    long t0 = Stopwatch.GetTimestamp();
    a.saveState?.Invoke(dict[a.dictionaryId], level);
    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
    if (ms >= 2) Logger.Info("SpeedrunTool/Profile", $"[saveAction] {ms,7:F2} ms  [{a.dictionaryId}] {a.ActionDescription}");
}
```
> 这能直接定位 `SupportSimpleStaticFields`（`SaveLoadAction.cs:595`）和 `SupportModSessionAndSaveData`（`:911`）到底占多少。

## 4. DeepCloner 每类型统计

`SpeedrunTool/DeepCloner/Helpers/DeepClonerGenerator.cs`，新增：
```csharp
internal static class DeepCloneStats {
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("SRT_CLONE_STATS") == "1";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, long[]> Data = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, long> Ticks = new();
    internal static void Hit(Type t, long startTicks) {
        if (!Enabled) return;
        Data.GetOrAdd(t, _ => new long[1])[0]++;
        Ticks.AddOrUpdate(t, Stopwatch.GetTimestamp() - startTicks, (_, v) => v + Stopwatch.GetTimestamp() - startTicks);
    }
    internal static void Dump() {
        if (!Enabled) return;
        foreach (var kv in Data.OrderByDescending(k => kv.Value[0]).Take(30))
            Ticks.TryGetValue(kv.Key, out long tk);
            Logger.Info("SpeedrunTool/Profile", $"[clone] {kv.Key.FullName} x{kv.Value[0]} ticks={Ticks.GetValueOrDefault(kv.Key)}");
    }
}
```
插入到 `CloneClassInternal`（`:60-95`）与 `CloneStructInternal`（`:97-115`）：入口 `var t0 = DeepCloneStats.Enabled ? Stopwatch.GetTimestamp() : 0;`，return 前 `DeepCloneStats.Hit(type, t0);`（注意多个 return 分支）。有 knownRef 命中的分支也要计（它能告诉你 load 时“命中率”）。
- 同时想要“冷/热”区分：在 `GenerateCloner`（`:259-274`）里单独计时并记录“新生成类型数/总耗时”，预期 ≈0.19ms/类型（audit 附录 1）。

## 5. 冻结/卡顿帧级统计

`Source/SaveLoad/GameFreezeManager.cs`：在 `OnAfterUpdate_EvenIfGameFreeze`（`:20`）或 `MakeGameFreezeAfterSaveLoad`（`:91`）里每帧记录 `Engine.RawDeltaTime`：
```csharp
// 环形缓冲 1024 帧，SRT_PROFILE 时启用
FrameStats.Record(Engine.RawDeltaTime * 1000.0);
// 输出 P50/P95/P99/max + >16.7ms / >33.3ms 帧数（每分钟或状态切换时）
```
这是回答“带 savestate 卡不卡”的唯一直接指标；也建议同时记录 `GC.CollectionCount` 增量（区分 GC 造成的尖峰）。

## 6. 内存对比

在 `StateManager` 的 save/load 前后、以及“存 0/1/2/3 槽”节点：
```csharp
private static void LogMem(string tag) {
    if (!SaveLoadProfiler.Enabled) return;
    GC.Collect(); GC.WaitForPendingFinalizers();   // 只在显式 benchmark 时做
    long managed = GC.GetTotalMemory(false);
    var info = GC.GetGCMemoryInfo();
    using var p = Process.GetCurrentProcess();
    Logger.Info("SpeedrunTool/Profile",
        $"[mem] {tag} managedMB={managed/1048576.0:F1} heapMB={info.HeapSizeBytes/1048576.0:F1} " +
        $"privateMB={p.PrivateMemorySize64/1048576.0:F1} wsMB={p.WorkingSet64/1048576.0:F1}");
}
```
建议在 benchmark 脚本里对每个槽 save 后调用一次 → 得到“每槽增量”，直接验证 audit B1/B2/B3。

## 7. 建议的输出约定（方便 bench-env 解析）

- 统一 `Logger.Info("SpeedrunTool/Profile", "SRT_PROFILE ...")`，一行一个 JSON-like / key=value 对。
- 每行带 `tag=`（save/load/clear/preclone/snapshot/clone/mem/frame）与 `sessionId`（进程启动时随机），方便 harness grep。
- 游戏日志路径：`Celeste/Logs/log.txt`（Everest）；bench-env 可在每次 SL 后截断/归档。

## 8. 实施顺序（建议）

1. `SaveLoadProfiler` + `StateManager` 分段（2 处）→ 先拿到 save/load 总账。
2. `OnSave/OnLoadState` per-action（2 处）→ 找到“最贵的 action”。
3. `PreCloneSavedEntities` 线程内计时 + `HandleSwitchScene` 等待时间 → 验证 C3。
4. `Snapshot` 捕获分段（`Snapshot.cs:54-94`）→ 验证 C4。
5. `DeepClonerGenerator` 类型统计（可选，代码侵入最大）→ 验证 C1/C6/B1/B5。
6. 帧级统计 → 最终验收“卡顿”。
