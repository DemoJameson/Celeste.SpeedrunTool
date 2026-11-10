# 老电脑 SRT benchmark 分析（DESKTOP-40H8K5Q）

数据：`DESKTOP-40H8K5Q-SrtBench-20261110-235848.zip`（SHA256 `d22ad831…`），
kit v1.0.0，SRT `86147858…`（v3.28.0），SrtBench `281b554a…`。6 项核心测试全部 `status=ok`；
每项是独立启动一次进程跑的，`session.json` 有 note 说明。

## 机器

| 项目 | 值 |
|---|---|
| CPU | Intel Core i5-1035G1（4C/8T，基础 1.0 GHz） |
| GPU | Intel UHD Graphics + AMD Radeon 630（双显卡） |
| RAM | 7.71 GB（测试开始时空闲 **2.62 GB**） |
| 系统 | Windows 10 Pro 19045 |
| 显示 | 1920×1080 @60 Hz |
| Mods | 54 loaded（SJ + 依赖） |

## JSON schema 提醒

`frames` 模式顶层的字段是
`tag / status / mode / error / generated_utc / harness / config / env / phases[] / setup / phase_c_loads_ms / all_frames`。
没有 `summary`（那是 `probe` 模式的 schema）。

- `phases[]`：每项是 A/B/C 一个阶段，含 `frames`、`p50/p95/p99/max/mean`、
  `over16ms/20/33/50`、`cpu_*`、`gc0/1/2`、`alloc_bytes`、`managed/ws/private_peak`、`max_at_s` 等；
- `setup.savestate_slots` / `setup.setup_save_ms`：测试正式开始前建立的存档槽数量和 save 耗时；
- `phase_c_loads_ms`：phase C 的 8 次 load 各自耗时（第一次通常包含冷启动/JIT，明显偏大）；
- `all_frames`：本次测试所有采样帧的汇总（n/p50/p95/p99/max）；
- `env`：`srt_ready`、`srt_version`、`gc_*`、`env_DOTNET_GCHeapHardLimit` 等；
- 05/06 的 `DOTNET_GCHeapHardLimit=0x80000000` 在 `env.gc_env_GCHeapHardLimit` /
  `env.env_DOTNET_GCHeapHardLimit` 两处都是 `0x80000000`，确认生效。

## SRT 存档/读档成本（旧机，1 槽，preclone On/Off）

| 测试 | setup save | load 第一次 | 后续 load 中位数 | load 均值 | 备注 |
|---|---:|---:|---:|---:|---|
| `frames-a0-Start-abc`（preclone On） | **2922 ms** | 735.9 ms | **78.9 ms** | 161.8 ms | 轻房间 |
| `frames-a1_18-Xplosives-abc`（preclone On） | **6312.8 ms** | 864.3 ms | **267.9 ms** | 352.3 ms | 重房间（3451+ 实体） |
| `frames-a1_18-Xplosives-abc-preclone-off` | **6196.6 ms** | 1125.3 ms | **434.5 ms** | 515.8 ms | 关 preclone |

- 冷启动 save 在旧机上是 **3–6.3 秒**（a0 2.9 s，a1 6.2–6.3 s），比主测试机（~1.27 s）慢约 5 倍；
  这是 SRT 的已知首次 JIT/冷启动问题，在老 CPU 上被放大（主测试机最新一次为 **620.6 ms**）。
- a1 的后续 load 中位数 **268 ms（preclone On）**，关 preclone 后 **434 ms**；
  preclone 只多用约 20–30 MB managed/private，却省掉约 40% load 时间。
  → **当前 `PreCloneMode=Auto` 在 ≤10 GB 机器上自动选 Off 对这台机器不划算**，
  建议显式 `On`（或重新评估 Auto 阈值/文案）。
- a0 轻房间的 load 中位数只有 **79 ms**，说明 SRT 本身在轻负载房间可用；
  问题集中在 a1_18-Xplosives 这种重房间。

## 帧时间：a0 vs a1 vs SRT on/off

| 测试/阶段 | frames | p50 | p95 | p99 | max(剔除首帧) | >33 ms | >50 ms | CPU p50 | CPU p99 | GC0/1/2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| a0 phase a | 665 | 16.62 | 17.75 | 34.62 | 1398.6（首帧） | 7 | 2 | 5.07 | 12.04 | 5/0/0 |
| a0 phase b | 749 | 16.66 | 17.32 | 17.99 | 32.78 | 0 | 0 | 4.60 | 8.50 | 6/3/0 |
| a0 phase c | 721 | 16.66 | 17.51 | 23.58 | 43.40 | 3 | 0 | 4.56 | 9.94 | 5/1/0 |
| a1 phase a（preclone On） | 684 | 11.02 | 27.11 | 31.88 | 1361.7（首帧） | 5 | 3 | **22.45** | 35.56 | 8/0/0 |
| a1 phase b（preclone On） | 748 | 16.59 | 25.55 | 38.30 | 62.21 | 10 | 2 | **21.73** | 35.96 | 11/3/0 |
| a1 phase c（preclone On） | 720 | 19.99 | 26.03 | 38.88 | 102.98 | 10 | 1 | **21.63** | 40.53 | 9/2/0 |
| a1 phase a（preclone Off） | 685 | 10.96 | 27.18 | 34.06 | 1342.6（首帧） | 8 | 3 | **22.65** | 35.40 | 8/0/0 |
| a1 phase b（preclone Off） | 750 | 17.15 | 26.94 | 34.43 | 52.12 | 8 | 1 | **21.55** | 39.20 | 9/1/0 |
| a1 phase c（preclone Off） | 720 | 19.98 | 27.32 | 38.37 | 78.18 | 10 | 1 | **21.89** | 39.56 | 9/2/0 |

关键现象：

1. **a0 是稳定的 60 fps**（p50 16.6 / p95 17.3–17.8，>33 ms 0–7 帧/12 s，CPU p50 4.6–5.1 ms）。
2. **a1_18-Xplosives 在旧 CPU 上已经超预算**：CPU p50 约 **21.6–22.6 ms**，
   高于 16.7 ms 的 60 fps 预算；帧分布呈双峰（p50 11 ms + p95 27 ms，>16.7 ms 的帧约一半），
   即"有些帧 11 ms、有些帧 27 ms"的 **frame pacing 抖动**，而不是稳定 30/60 fps。
3. preclone On/Off 对 a1 的 phase a/b/c 帧分布影响很小（CPU p50 都是 ~22 ms），
   说明瓶颈是房间本身的 CPU/渲染负载，不是 preclone 的 CPU 开销。

## 2 GB hard limit：SRT on（05）vs off（06）

两项都是 `DOTNET_GCHeapHardLimit=0x80000000`，`env.gc_total_available_bytes=2147483648` 生效；
05 的 `srt_ready=true`（并持有 1 个 savestate），06 的 `srt_ready=false`（`SpeedrunTool assembly not loaded`，
`setup.savestate_slots=0`）。两次都是 25 s / 约 1462–1466 帧。

| 指标 | SRT on（05） | SRT off（06） | 差异 |
|---|---:|---:|---|
| p50 | 11.04 | 12.93 | on 略低 |
| p95 | 28.96 | 26.92 | on +7.6% |
| p99 | **57.86** | **34.06** | on **+70%** |
| >33 ms 帧 | **32** | **16** | on ×2 |
| >50 ms 帧 | **18** | **4** | on ×4.5 |
| CPU p50 | 21.94 | 21.62 | 基本相同 |
| CPU p99 | **52.77** | **36.15** | on **+46%** |
| CPU max | **176.7** | **88.6** | on ×2 |
| managed peak | 751 MB | 744 MB | 基本相同 |
| private peak | 2981 MB | 2993 MB | 基本相同 |
| GC0/1/2 | 19/1/0 | 19/1/0 | 相同 |
| alloc | 69.1 MB | 68.9 MB | 相同 |

结论：

- 2 GB managed hard limit **没有成为约束**（managed peak 约 750 MB，远低于 2 GB）；
  两者 private 峰值都约 2.98 GB，机器总内存 7.7 GB、测试开始时只剩 2.6 GB 空闲 → 真实压力来自
  **总体内存占用/OS 内存压力**，不是 GC heap limit。
- 在同样的重房间、同样的 2 GB 限制下，**SRT on 把尾部延迟显著放大**：
  p99 +70%、>50 ms 帧 4.5 倍、CPU p99 +46%；而中位数/平均内存/GC 次数基本不变。
  这与"SRT 持有 savestate 后对象图更大/更多写屏障/GC 扫描"一致；06 没有 savestate，
  所以严格说这是"SRT + 一个 savestate"的开销，不是纯 mod 加载开销。
- 该对比适合回答"开着 SRT 玩重房间是否更卡"：**是，尾部更差**；但不适合把锅全给 SRT——
  06 本身已经有 16 个 >33 ms、4 个 >50 ms 帧，a1 房间在旧 CPU 上已经不稳。

## 与主测试机（61 GB / 强 CPU）对比

| 指标 | 旧机 | 主测试机 | 倍数（旧/主） |
|---|---:|---:|---:|
| a1 setup save | 6313 ms | 620.6 ms | **10.2×** |
| a1 load 中位数（preclone On） | 268 ms | 76.6 ms | ~3.5× |
| a1 CPU p50（phase a） | 22.4 ms | 7.17 ms | ~3.1× |
| a1 p99 | 31.9–38.9 ms | 16.95–17.33 ms | ~2× |
| a0 p50 | 16.6 ms | 16.7 ms | ~1×（都锁 60） |

旧机 CPU 单核性能约为测试机的 1/3，这是 a1 房间直接掉出 60 fps 预算的根因；
冷启动 save 慢约 10 倍、load 慢约 3.5 倍，则同时来自弱 CPU 与首次 JIT/冷启动放大；内存方面 7.7 GB vs 61 GB，private 峰值 2.98 GB 时只剩 2.6 GB 空闲，
比主测试机紧张得多。

## 对 SRT 的结论/建议

1. **旧机卡顿的主因是 a1_18-Xplosives 房间本身对 4C/1.0 GHz CPU 太重**（CPU p50 ~22 ms），
   不是 SRT 的 GC 策略或 2 GB 限制；SRT 在此基础上进一步放大尾部（p99 +70%、>50 ms ×4.5）。
2. **`PreCloneMode=Auto` 在 ≤10 GB 上选 Off 对这台机器不利**：preclone On 的 load 中位数
   268 ms vs Off 434 ms，内存只多 ~20–30 MB；建议这台机器显式设为 **On**，或把 Auto 的
   内存阈值/文案重新评估（"低内存机器关 preclone"的收益没有原先假设的大）。
3. **冷启动 save 缩放严重**：a0 2.9 s / a1 6.2–6.3 s（主测试机 0.62 s，约 5–10 倍）。首次 save 的 JIT
   冷启动在弱 CPU 上被放大 5 倍；建议在文档/设置里明确"进图后第一次 F7 可能卡数秒"，
   并继续探索隔离预热（不能污染主缓存）。
4. **2 GB hard limit 不是这台机器的有效旋钮**（managed 只用 750 MB）；真实内存压力来自
   private ~3 GB + 8 GB 总内存 + 其他程序。旧机更值得做的是：关闭后台程序/降分辨率减少
   GPU/内存压力、只保留 1 个 savestate、preclone 显式 On。
5. **SRT 侧可继续做的**：降低 savestate 常驻对象图/写屏障成本（对尾部延迟有直接收益）；
   若要进一步确认 a1 的 CPU 时间花在哪，需要在旧机上做 CPU profiler / 逐 mod 归因，
   现有 harness 只给出总帧时间与 GC 计数，不能区分实体更新/渲染/脚本。

## 局限

- 每项测试独立进程；05/06 的对比包含"SRT + 一个 savestate vs 无 SRT 无 state"，
  不是纯粹的 mod 加载开销。
- `frames` 模式的 `p50` 与 `over16ms` 计数可能来自双峰分布（11 ms/27 ms 交替），
  因此 p50 低于 16.7 ms 不代表稳定 60 fps；看 p95/p99/over33/over50 更可靠。
- 2 GB hard limit 只限制 managed heap；private/WS 不受它限制。
- 旧机日志未包含 harness 的每帧明细（只有 JSON 汇总），无法逐帧定位具体卡顿来源；
  如需继续，可加一个"记录最差 N 帧的帧号/房间/GC 状态"的选项。
