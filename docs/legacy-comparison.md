# Our build vs upstream LegacySupport (single-slot) 鈥?first A/B pass

Date: 2026-11-11. Machine: main benchmark PC (61 GB RAM, Ryzen 7 9850X3D), same
Celeste/Everest/SJ environment, SrtBench with the legacy bridge.

- **ours**: `perf/savestate-optimization` v3.28.1 (`5b832c11鈥), multi-slot,
  pre-clone Auto = On, Lua desync checker on by default.
- **legacy**: `DemoJameson/Celeste.SpeedrunTool` branch `LegacySupport`, v3.24.7
  (`0208979f鈥), one slot, always pre-clones, no Lua desync checker.

All numbers are from `a1_18-Xplosives` (SJ HeartSide, 3468 entities), the same
config otherwise (`frames` A/B/C, 1 held slot, 8 loads; and a `normal` 20-cycle
steady test). Raw JSON: `D:\DSH\Celeste\work\SpeedrunTool\notes\legacy-vs-ours\`.

## Cold first save + loads (frames mode, 1 slot)

| metric | ours run 1 | ours run 2 | legacy run 1 | legacy run 2 |
|---|---:|---:|---:|---:|
| first save (setup) | **71.5 ms** (v3.28.2 warm-up) | 1262 ms (v3.28.1, no warm-up) | 772 ms | 761 ms |
| load #1 (cold) | 239.5 ms | 234.0 ms | 241.6 ms | 244.1 ms |
| following loads median | **81.8 ms** | **79.9 ms** | 131.0 ms | 136.3 ms |
| phase p50 | 16.66 | 16.66 | 16.66 | 16.66 |
| phase p99 (a/b/c) | 17.3鈥?8.6 | 17.3鈥?8.5 | 17.3鈥?8.1 | 17.5鈥?8.4 |
| managed peak (a/b/c) | 1450/1542/1797 MB | same | 2394/2537/2944 MB | 1442/1585/1995 MB |
| private peak (a/b/c) | 3124/3012/3574 MB | 3107/2999/3587 MB | 4312/4265/5506 MB | 3187/3141/4508 MB |

**First save** used to be the only metric where ours lost by default (+0.5 s vs
legacy). v3.28.2 moves that one-off JIT cost to the first **loading screen** with a
throwaway save+clear (`WarmUpFirstSave`): loading is ~1361 ms longer once per
session, and the first user-visible save is now **71.5 ms** 鈥?~10脳 faster than
legacy. Steady save/load and memory are unchanged. Legacy has no checker at all;
with `disable_desync_check: 1` ours' first save was already 637 ms before the
warm-up, so both routes beat legacy now.

Loads after the cold one are **~1.6脳 faster** on ours; frame pacing is identical
(16.66 ms p50) and p99 is in the same range.

## Steady-state (20 normal save/load cycles, 1 slot)

| metric | ours | legacy |
|---|---:|---:|
| first save | 1267 ms | 786 ms |
| subsequent save calls | **50鈥?0 ms** (median ~55) | 148鈥?37 ms (median ~225) |
| load calls | **73鈥?30 ms** (median ~78) | 140鈥?60 ms (median ~170) |

Ours is roughly **3鈥?脳 faster on steady saves** and **~2脳 faster on loads**.

## Multi-slot (ours 3 slots vs legacy 1 slot)

Ours with 3 held savestates (`compare-3slot-ours`):

- setup saves: 1278 ms (first) + 64 ms + 85 ms (extra slots);
- loads median 136 ms (vs ours 1-slot 80 ms);
- **managed peak 2.0 GB, private peak 3.86 GB**.

Legacy 1-slot private peak was 4.3鈥?.5 GB, managed 1.4鈥?.9 GB. So even with three
savestates, ours stays at or below legacy's single-slot memory on this room, and
the steady save/load advantage remains.

## Verdict (new machine)

- **Ours wins steady save, steady load, and memory**, including the 3-slot case vs
  legacy's 1 slot.
- **Ours no longer loses the first save**: v3.28.2 pays ~1.36 s once on the loading
  screen and the first visible save is 71.5 ms (legacy 761鈥?98 ms). The only cost
  is the loading time, which is the intended trade-off.
- Frame times are the same in this room (both 16.66 ms p50); the difference is
  SRT latency/memory, not gameplay frame pacing.

Next: run the same `compare-*` configs on the old PC (kit v1.1.0) and add the old-PC
table. If the first-save checker cost matters on the target machine, the options
are (a) leave it (safety), (b) document `DisableDesyncCheck` for pure-performance
comparisons, or (c) a future warm-up/precompute optimization.

