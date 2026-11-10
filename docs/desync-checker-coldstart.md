# Desync checker: cold start vs pre-warm (measured)

Date: 2026-11-11. Machine: main benchmark PC (Ryzen 7 9850X3D, 61 GB), SJ
`5-Grandmaster/ZZ-HeartSide` → `a1_18-Xplosives` (3468 entities), SrtBench
`normal` mode, 20 save/load cycles, 1 slot, preclone On.

## Measurements

| configuration | first save | subsequent saves | loads |
|---|---:|---:|---:|
| default (Lua desync checker on) | **1267–1333 ms** | 50–80 ms | 73–130 ms |
| `DisableDesyncCheck = true` | **637 ms** | 50–80 ms | 73–130 ms |
| legacy 3.24.7 (no checker) | 761–798 ms | 148–337 ms | 140–460 ms |

So the checker adds ~0.6 s to the **first** save and effectively nothing to later
saves. Initial hypotheses were "type graph scan" / "cold type cache", so I measured
the checker itself after instrumenting `Check`:

- first save: `Check` called **3621** times, `Check` total **~69.5k Stopwatch
  ticks ≈ 6.9 ms**, of which `CanSyncType` ≈ 6.7 ms; `KnownTypes` = 1640 entries;
- later saves: 10844 calls, `Check` ≈ 0.35 ms.

The whole late-check type walk is therefore **~7 ms**, not 600 ms. The cold cost is
first-call JIT/tiered codegen of the save/clone path (the checker being registered
changes the first-call code path), not the cache warm-up.

## Warm-up experiments (both reverted)

1. **Background warm-up at first Level.Begin** over `level.Tracker.Entities.Keys`
   (1718 entity types): the scan did not finish before the first save, gave no
   reduction, and competes for CPU with the game; reverted.
2. **Parallel `CanSyncType` warm-up in `OnBeforeSaveState`** over unique entity
   types: first save 1297 ms (no change); reverted.

This is consistent with the instrumentation: the type graph is cheap, so
pre-computing it cannot remove the 0.6 s cost.

## What this means in practice

- In real play the cost is **once per game session**, on the first save after
  entering a level; later saves are 50–80 ms. Pre-warming at level load would just
  move the same JIT cost into the loading screen (plus background CPU), and would
  not make the first save faster unless the whole clone/save path is pre-JITed.
- Doing a real isolated JIT warm-up (e.g. a throwaway clone in a separate
  AssemblyLoadContext) is the only way to remove it without weakening the checker;
  the earlier global warm-up attempts were reverted because they polluted
  DeepCloner caches (see `docs/optimization-report.md` §2.5).
- For pure performance comparisons with legacy (which has no checker), use
  `DisableDesyncCheck` in the benchmark config; with it on, our build's first save
  is 637 ms (faster than legacy's 761–798 ms) and steady save/load is already 2–4×
  faster with much lower memory.
- Default product decision: **keep the checker on and the cold start as a documented
  one-time cost**. Pre-warming is not worth the complexity/risk with the current
  data.

## Suggested next step (only if the first-save hitch becomes a real complaint)

Isolate a JIT warm-up of the save path in a disposable context (not the shared
DeepCloner caches), or run the late check on a background thread after the clone
with a safe rollback before the state is exposed. Both are larger changes than the
warm-up attempts above and should be validated on the old PC before shipping.
