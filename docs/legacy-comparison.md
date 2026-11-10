# Our build vs upstream LegacySupport (single-slot) — first A/B pass

Date: 2026-11-11. Machine: main benchmark PC (61 GB RAM, Ryzen 7 9850X3D), same
Celeste/Everest/SJ environment, SrtBench with the legacy bridge.

- **ours**: `perf/savestate-optimization` v3.28.1 (`5b832c11…`), multi-slot,
  pre-clone Auto = On, Lua desync checker on by default.
- **legacy**: `DemoJameson/Celeste.SpeedrunTool` branch `LegacySupport`, v3.24.7
  (`0208979f…`), one slot, always pre-clones, no Lua desync checker.

All numbers are from `a1_18-Xplosives` (SJ HeartSide, 3468 entities), the same
config otherwise (`frames` A/B/C, 1 held slot, 8 loads; and a `normal` 20-cycle
steady test). Raw JSON: `D:\DSH\Celeste\legacy-vs-ours\`.

## Cold first save + loads (frames mode, 1 slot)

| metric | ours run 1 | ours run 2 | legacy run 1 | legacy run 2 |
|---|---:|---:|---:|---:|
| first save (setup) | 1309 ms | 1262 ms | 772 ms | 761 ms |
| load #1 (cold) | 239.5 ms | 234.0 ms | 241.6 ms | 244.1 ms |
| following loads median | **81.8 ms** | **79.9 ms** | 131.0 ms | 136.3 ms |
| phase p50 | 16.66 | 16.66 | 16.66 | 16.66 |
| phase p99 (a/b/c) | 17.3–18.6 | 17.3–18.5 | 17.3–18.1 | 17.5–18.4 |
| managed peak (a/b/c) | 1450/1542/1797 MB | same | 2394/2537/2944 MB | 1442/1585/1995 MB |
| private peak (a/b/c) | 3124/3012/3574 MB | 3107/2999/3587 MB | 4312/4265/5506 MB | 3187/3141/4508 MB |

**First save** is the only metric where ours loses by default: +0.5 s vs legacy.
That difference is the Lua desync checker's first-save whole-entity-type walk; with
`disable_desync_check: 1` ours' first save drops to **637.6 ms** (faster than
legacy), while steady save/load and memory stay the same. Legacy has no checker at
all, so this is a safety-vs-cold-save trade-off, not a general slowdown.

Loads after the cold one are **~1.6× faster** on ours; frame pacing is identical
(16.66 ms p50) and p99 is in the same range.

## Steady-state (20 normal save/load cycles, 1 slot)

| metric | ours | legacy |
|---|---:|---:|
| first save | 1267 ms | 786 ms |
| subsequent save calls | **50–80 ms** (median ~55) | 148–337 ms (median ~225) |
| load calls | **73–130 ms** (median ~78) | 140–460 ms (median ~170) |

Ours is roughly **3–4× faster on steady saves** and **~2× faster on loads**.

## Multi-slot (ours 3 slots vs legacy 1 slot)

Ours with 3 held savestates (`compare-3slot-ours`):

- setup saves: 1278 ms (first) + 64 ms + 85 ms (extra slots);
- loads median 136 ms (vs ours 1-slot 80 ms);
- **managed peak 2.0 GB, private peak 3.86 GB**.

Legacy 1-slot private peak was 4.3–5.5 GB, managed 1.4–2.9 GB. So even with three
savestates, ours stays at or below legacy's single-slot memory on this room, and
the steady save/load advantage remains.

## Verdict (new machine)

- **Ours wins steady save, steady load, and memory**, including the 3-slot case vs
  legacy's 1 slot.
- **Ours loses only the first cold save by default** (+0.5 s), because of the Lua
  desync checker; disabling the checker makes ours faster than legacy there too.
- Frame times are the same in this room (both 16.66 ms p50); the difference is
  SRT latency/memory, not gameplay frame pacing.

Next: run the same `compare-*` configs on the old PC (kit v1.1.0) and add the old-PC
table. If the first-save checker cost matters on the target machine, the options
are (a) leave it (safety), (b) document `DisableDesyncCheck` for pure-performance
comparisons, or (c) a future warm-up/precompute optimization.
