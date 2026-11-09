# Celeste Speedrun Tool

[中文说明](./README_CN.md)

## Download
[GameBanana](https://gamebanana.com/tools/6597)

## Features

- Create save states* to practice certain segments of the room, automatically loaded after death. Customizable keyboard/controller shortcut for saving and loading. 
- Room timer compatible with Summit flags. Personal best is recorded.
- Statistics on deaths, including location, time lost, etc.
- Controller support for debug map: use confirm to teleport, right analog to zoom.
- Debug mode F6 teleport bug-fix – Dream blocks and Badeline not activating in Old Site, incorrect ice/fire state in Core, Celestial Resort huge mess hall being strangely lit, wind sound does not play in Summit 2000m.

## Performance

A save state has to keep a deep copy of the room's object graph, so it is heavy by nature. Recent
versions keep that cost predictable and expose the main memory/speed trade-off:

- **Pre-clone for Faster Load** (State settings, default `Auto`): the object graph needed by the
  next load is prepared on a background thread, which makes loading noticeably faster but keeps a
  second copy of the saved entities in memory. `Auto` enables it only on machines with more than
  ~10 GB of RAM; on a low-memory machine keep it `Auto` or set it to `Off` to roughly halve the
  savestate footprint.
- Save-state thumbnails are stored as small textures (<= 480 px on the long edge), not as
  full-screen captures, and capture buffers are reused.
- The cloner code generation needed by the first save/load of a session is warmed up on a
  background thread while a level loads, so the once-per-session first-save hitch (several
  hundred ms on slow CPUs) is hidden behind the loading screen instead.
- The existing "GC after Load State" / "GC after Clear State" options still control when the mod
  forces a collection; if you see a hitch while clearing or loading states, try turning them off.
- Diagnostics (opt-in, zero cost in the shipped build): a normal Release build compiles the
  profiler out completely (the call sites are erased, so there is nothing to pay for). To
  diagnose, build with `-p:SrtProfiling=true`, put an empty `srt-profile.flag` next to
  SpeedrunTool.dll (or set `SRT_PROFILE=1`), and log.txt will contain one `SRT_PROFILE ...`
  line per save/load with per-segment milliseconds, allocation and GC/heap numbers.

Other mods and tools can drive save states through the `SpeedrunTool.SaveLoad` ModInterop API
(`SaveState`, `LoadState`, `SaveStateTas`, `LoadStateTas`, `ClearState`, `SwitchSlot`, `IsSaved`,
and the pre-clone toggle).

## Previews

![preview1](./Preview/preview1.jpg)

![preview2](./Preview/preview2.jpg)

## Acknowledgments

- A huge shout-out to [Extremely OK Games, Ltd.](https://exok.com/) for designing [Celeste](http://www.celestegame.com/) , it has been my favorite platformer ever since.
- [Everest](https://everestapi.github.io/) – Celeste Mod Loader and modding API, making this Mod a reality.
- [Ahorn](https://github.com/CelestialCartographers/Ahorn) – Map maker and level editor, useful for identifying different entities in the game.
- [Power Up Audio](https://www.powerupaudio.com/) – Curated library of high-quality sound effects for games, used to acquire Celeste audio project
- [西瓜](https://space.bilibili.com/18705633), [buhbai](https://www.speedrun.com/user/buhbai), [Evansfight](https://www.speedrun.com/user/Evansfight), [Lev067](https://www.speedrun.com/user/Lev067), [sonicyellow](https://www.speedrun.com/user/sonicyellow), [nameguy](https://gamebanana.com/members/1653060) – Mod tester.
- [rjdimo](https://steamcommunity.com/id/rjdimo/) – Shameless self-promoting translator of this readme file.