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
| first save (setup) | **71.5 ms** (v3.28.2 warm-up) | 1262 ms (v3.28.1, no warm-up) | 772 ms | 761 ms |
| load #1 (cold) | 239.5 ms | 234.0 ms | 241.6 ms | 244.1 ms |
| following loads median | **81.8 ms** | **79.9 ms** | 131.0 ms | 136.3 ms |
| phase p50 | 16.66 | 16.66 | 16.66 | 16.66 |
| phase p99 (a/b/c) | 17.3–18.6 | 17.3–18.5 | 17.3–18.1 | 17.5–18.4 |
| managed peak (a/b/c) | 1450/1542/1797 MB | same | 2394/2537/2944 MB | 1442/1585/1995 MB |
| private peak (a/b/c) | 3124/3012/3574 MB | 3107/2999/3587 MB | 4312/4265/5506 MB | 3187/3141/4508 MB |

**First save** used to be the only metric where ours lost by default (+0.5 s vs
legacy). v3.28.2 moves that one-off JIT cost to the first **loading screen** with a
throwaway save+clear (`WarmUpFirstSave`): loading is ~1361 ms longer once per
session, and the first user-visible save is now **71.5 ms** — ~10× faster than
legacy. Steady save/load and memory are unchanged. Legacy has no checker at all;
with `disable_desync_check: 1` ours' first save was already 637 ms before the
warm-up, so both routes beat legacy now.

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

## Old PC (DESKTOP-40H8K5Q: i5-1035G1, 7.71 GB RAM, 1920×1080@60)

Measured again on the **fixed SrtBench v1.4.3** (the old 2 s `Engine.Scene =
new LevelLoader` fallback was overriding LevelEnter and produced a level with
entities but no player/HUD/tiles; every old-PC number before this fix is
discarded). Session `DESKTOP-40H8K5Q-20261113-121520-session`, same room/configs,
SrtBench `9c8bf5fe…`.

| metric | ours 1-slot | legacy 1-slot |
|---|---:|---:|
| first save (setup) | **443 ms** | 3511 ms |
| loads median (cold excluded) | **195 ms** | 824 ms |
| steady save median (20 cycles) | **551 ms** | 1567 ms |
| steady load median | **166 ms** | 767 ms |
| managed peak (phases a/b/c) | 0.85 / 0.90 / 1.08 GB | 1.72 / 1.83 / 2.04 GB |
| private peak | 2.29 / 2.30 / 2.34 GB | 3.09 / 3.13 / 3.60 GB |
| frame p50 | 16.69 / 16.66 / 16.63 ms | 16.42 / 16.58 / 16.88 ms |
| frame p99 | 31.2 / 29.9 / 32.4 ms | 32.9 / 36.3 / 33.8 ms |
| first level load cost (warm-up) | **+4.4–4.8 s once** | — |

Ours 3 slots on the same fixed-harness run: setup saves 725 + 484 + 361 ms,
loads median ~629 ms, managed peak 1.75–2.01 GB, private peak 3.05–3.26 GB;
phase-c p99 **91.6 ms** with `gc_pause_ms=1009` (the 3-slot GC tail). Multi-slot
is best-effort: it stays below legacy's single-slot private peak, but its p99 is
not a guaranteed target on 8 GB.

**Verdict (old PC, fixed harness):** ours wins every 1-slot save/load/memory
metric, including an 8× faster first save and ~4× faster loads; the only cost is
~4.4–4.8 s extra on the first level load (once per session). No OOM at 7.71 GB.

## Verdict (both machines)

- **Ours wins steady save, steady load, and memory on both machines** on the
  1-slot path; the 3-slot case stays below legacy's single-slot memory but is
  best-effort performance-wise (8 GB p99 ~92 ms).
- **Ours no longer loses the first save**: v3.28.2 pays 1.36 s on the new machine
  and 4.4–4.8 s on the old machine once on the loading screen; the first visible
  save is 71.5 ms (new) / 443 ms (old) vs legacy's 761–798 / 3511 ms.
- Frame times are the same in this room (17 ms p50 on the old PC); the difference
  is SRT latency/memory, not gameplay frame pacing.
