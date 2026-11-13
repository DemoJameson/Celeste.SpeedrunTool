# Desync checker cold start: moving the one-off cost to level loading

Date: 2026-11-12. Machine: main benchmark PC (Ryzen 7 9850X3D, 61 GB), SJ
`5-Grandmaster/ZZ-HeartSide` → `a1_18-Xplosives` (3468 entities), SrtBench
`normal` mode, 20 save/load cycles, 1 slot, preclone On.

## Results

| configuration | loading screen | first save | first load | subsequent saves |
|---|---:|---:|---:|---:|
| v3.28.1, no warm-up | — | **1267–1333 ms** | 243–259 ms | 50–80 ms |
| v3.28.1, `DisableDesyncCheck` | — | 637 ms | 243 ms | 50–80 ms |
| legacy 3.24.7 (no checker) | — | 761–798 ms | 241–244 / 321 ms | 148–337 ms |
| **v3.28.2 with `WarmUpFirstSave`** | **+1361 ms** | **71.5 ms** | **259 ms** | 50–80 ms |

The warm-up dry-run is executed on the first `Level.Begin` (while the level is still
on the loading screen), so the player-visible first save is now as fast as the
following ones. The 1361 ms is a one-time cost per game session, not per room or
per save.

## Why type-only warm-up did not work

Initial attempts warmed `CanSyncType` for every entity type (background at level
begin, then parallel in `OnBeforeSaveState`). Neither reduced the first save.
Instrumentation showed why:

- first save: `Check` called 3621 times, total `Check` time ≈ 6.9 ms
  (`CanSyncType` ≈ 6.7 ms), `KnownTypes` = 1640 entries;
- later saves: `Check` ≈ 0.35 ms.

The expensive part is not the type graph but the **first execution of the whole
save/clone path** (`cloneLevel` 371 ms + `saveActions` 265 ms in the profiler);
pre-computing type safety cannot remove JIT of that path.

## Implemented fix (v3.28.2)

`WarmUpFirstSave` runs once, after `InitSlots`, on the first Level's `Scene.Begin`:

1. calls `StateManager.Instance.SaveStateImpl(false, out _)` against the freshly
   loaded level (this executes the real clone, save actions, checker, preclone
   start — the whole first-save path);
2. if a temporary state was created, it is cleared immediately with
   `ClearStateImpl(false)`; no savestate is exposed to the player;
3. skips if `StateManager.State != None` (e.g. a cross-chapter pending load) or if
   the hidden setting `WarmUpFirstSave` is false.

Log line: `SpeedrunTool/SaveLoadAction: Warmed the first-save path during level
load: 1361 ms (temporary state created=True)`.

Validated with a 20-cycle steady test, a full a1_18-Xplosives frames test and the
TAS playback test (`tasplay-ff-a1-abc`: status=ok, phases sampled). The temporary
state is cleared (`IsSaved == false` before the test starts).

## Trade-offs / caveats

- The loading screen is ~1.4 s longer **once per session** on the benchmark PC.
  On the 8 GB i5 the JIT is slower, so this may be several seconds; that is the
  intended place to pay it (the player is waiting anyway).
- The dry-run briefly creates a full savestate, so peak memory during loading goes
  up. On 8 GB machines this must be watched; if it causes OOM, set
  `WarmUpFirstSave: false` in the mod settings YAML (hidden option, default true)
  or guard it by available memory.
- If the first level cannot be saved (e.g. active Lua cutscene refused by the
  checker), the dry-run fails safe, no state remains, and the first real save
  simply pays the cost as before.
- For pure performance comparisons against legacy, use `DisableDesyncCheck`; for
  default-product behavior, keep the warm-up on.

## Old PC next step

## Old PC validation (2026-11-12)

Kit session `DESKTOP-40H8K5Q-SrtBench-20261113-121520` on the fixed SrtBench
v1.4.3 (i5-1035G1, 7.71 GB RAM):

- warm-up log: `Warmed the first-save path during level load: 4427–4821 ms`
  (once per session, on the loading screen);
- first save: **443 ms** (vs legacy 3511 ms; before v3.28.2 the old-PC first save
  was ~6.3 s);
- later loads median ~195 ms; steady save median 551 ms, load median 166 ms;
- memory: managed peak 0.85–1.08 GB, private peak 2.31–2.36 GB (legacy 1-slot:
  1.72–2.15 GB / 3.15–3.66 GB); no OOM at 7.71 GB during the dry-run.

The old PC pays ~4.4–4.8 s extra on its first level load, which is acceptable for
a one-time per-session cost; the first-save gap against legacy is closed (416 ms
vs 3207 ms).
