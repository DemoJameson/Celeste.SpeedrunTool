# Possible Issues

- TAS not working properly after load state (keep logging)

- TAS interop changes

- Some third party mods need to re-support SRT. (though i don't find? except tas related tools)

- CelesteTAS and TAS Helper need to re-support SRT.

- CelesteTAS can have multi save slots too?

- CelesteTAS.EntityDataHelper sometimes crashes when switching slots? (only happen after using a tas slot?)

- Re-support third party mods. See SaveLoadAction class, some hooks are added when load, which seems suspicious when we have multiple save slots?

- Some features of SRT not working properly - currently i only focus on multi-saveslot save/load, and i'm not familiar with some other speedrun part of SRT. So tell me if you find some issues!

- Some render may not work properly - I've met these kinds of bugs but can't reproduce.

If you have any feedback, ping/DM me @Lozen0956#0 in discord, or open an issue/PR in github.