using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;

/// <summary>
/// DEBUG-only snapshot diagnostics split out of Snapshot so its capture flow reads as plain calls.
/// [Conditional("DEBUG")] removes these calls from Release; the helpers they call only exist there.
/// </summary>
internal partial class Snapshot {
    [Conditional("DEBUG")]
    internal static void Debug_ReportEmptyLevelBuffer(int width, int height, bool captured) {
#if DEBUG
        if (!captured) {
            ReportEmptyLevelBuffer(width, height);
        }
#endif
    }

    [Conditional("DEBUG")]
    internal static void Debug_ReportNoUsableFrame() {
#if DEBUG
        ReportNoUsableFrame();
#endif
    }

    [Conditional("DEBUG")]
    internal static void Debug_AssertNoUnknownRendererHookers() {
#if DEBUG
        AssertNoUnknownRendererHookers();
#endif
    }

#if DEBUG
    // DEBUG-only tripwire. Snapshot assumes GameplayBuffers.Level holds the composed frame; a mod that
    // renders the level through its own target breaks that assumption in a way pixels cannot prove.
    // A DEBUG build is meant to break loudly instead of quietly storing wrong thumbnails, so an
    // installer we have not reviewed throws -- once per installer, on the first save that needs a
    // snapshot, not at startup.
    // Every installer seen on GameplayRenderer.Render and reviewed by hand (see
    // work/SpeedrunTool/notes/mod-render-review.md). A new name here means a new mod to read.
    private static readonly string[] ReviewedRenderHookers = {
        "MotionSmoothing",        // redirects the level into its own hires buffers -- the reason for the list
        // Takes over the whole gameplay render when material layers exist. NOT VERIFIED: no map with
        // layers was tested, so it may well produce the same broken frames as MotionSmoothing. If a
        // snapshot comes out wrong on such a map, move this name to the redirecting list (that is,
        // make RenderTargetModLoaded match it) instead of leaving it here.
        "auspicioushelper",
        "ScugHelper",             // orig first, then rim-light post-process on Gameplay
        "CelesteTAS-EverestInterop", // hide-gameplay / hitbox overlays
        "CommunalHelper",         // show-hitbox trigger
        "StrawberryJam2021",      // show-hitbox trigger
        "StrawberryTool",         // detector info overlay
        "VidPlayer",              // extra gameplay-HUD renderer
        "ExtendedVariants",       // zoom/blur variants, only when enabled
    };

    private static readonly HashSet<string> ReportedRenderHookers = new();

    private static bool reportedEmptyLevelBuffer;
    private static bool reportedNoUsableFrame;

    // The fallback to the back buffer is itself the signal that some mod owns the level render: a
    // complete frame cannot be told from a partial one, so if the level buffer held no visible pixel,
    // something redirected it. Unlike the hook tripwire above, this fires on the symptom and therefore
    // also catches mods that never touch GameplayRenderer.Render (custom wipes, skipped frames, ...).
    private static void ReportEmptyLevelBuffer(int width, int height) {
        if (reportedEmptyLevelBuffer) {
            return;
        }

        reportedEmptyLevelBuffer = true;
        Logger.Error("SpeedrunTool/Snapshot",
            $"GameplayBuffers.Level ({width}x{height}) had no visible pixel, so a mod is redirecting the level " +
            $"render; the snapshot fell back to the back buffer. Enabled mods: {LoadedModNames()}");
    }

    private static void ReportNoUsableFrame() {
        if (reportedNoUsableFrame) {
            return;
        }

        reportedNoUsableFrame = true;
        Logger.Error("SpeedrunTool/Snapshot",
            "Neither GameplayBuffers.Level nor the back buffer contained a frame; the thumbnail was dropped. " +
            $"Enabled mods: {LoadedModNames()}");
    }

    private static string LoadedModNames() {
        IEnumerable<string> names = Everest.Modules?.Select(module => module.Metadata?.Name) ?? Enumerable.Empty<string>();
        return string.Join(", ", names.Where(name => !string.IsNullOrWhiteSpace(name)));
    }

    private static void AssertNoUnknownRendererHookers() {
        // Only third-party installers count. The detour chain starts at a root node that represents the
        // target method itself, so walking it yields "Celeste :: Celeste.GameplayRenderer.Render" for the
        // original; vanilla and runtime hooks must not trip a wire meant for mods.
        foreach (string installer in GameplayRendererHookInstallers().Where(IsFromLoadedModule)) {
            if (ReviewedRenderHookers.Any(known => installer.StartsWith(known + " :: ", StringComparison.Ordinal))) {
                continue;
            }

            if (!ReportedRenderHookers.Add(installer)) {
                continue;
            }

            Logger.Error("SpeedrunTool/Snapshot",
                $"Unreviewed mod hooks GameplayRenderer.Render: {installer}. " +
                "GameplayBuffers.Level may not be the composed frame; read its render code, then add it to " +
                "ReviewedRenderHookers (safe) or to the render-redirecting list.");
            throw new Exception($"Unreviewed GameplayRenderer.Render hook: {installer}");
        }
    }

    // Same reflection the HookProbe tool uses (work/SpeedrunTool/tools/hookprobe): MonoMod keeps every
    // managed detour in DetourManager.detourStates, and each state carries the chain of installed
    // handlers. Private fields, so this is DEBUG-only on purpose.
    private static List<string> GameplayRendererHookInstallers() {
        var installers = new List<string>();
        Type manager = typeof(MonoMod.RuntimeDetour.Hook).Assembly.GetType("MonoMod.RuntimeDetour.DetourManager");
        FieldInfo statesField = manager?.GetField("detourStates", BindingFlags.NonPublic | BindingFlags.Static);
        if (statesField?.GetValue(null) is not IEnumerable table) {
            return installers;
        }

        foreach (object entry in table) {
            if (entry.GetType().GetProperty("Key")?.GetValue(entry) is not MethodBase target) {
                continue;
            }

            string name = (target.DeclaringType?.FullName ?? "") + "." + target.Name;
            if (name != "Celeste.GameplayRenderer.Render") {
                continue;
            }

            // MonoMod keeps the target method itself at the root of the detour chain (and as the
            // Entry of the shims below it), so the walk reports the vanilla method as an installer.
            // That string is exactly the one this tripwire must never report, and it cannot be
            // filtered by assembly name: Everest's own core module lives in Celeste.dll, so the
            // game assembly counts as "a loaded mod" too.
            string targetDescription = Describe(target);

            void Add(string description) {
                if (description != targetDescription) {
                    installers.Add(description);
                }
            }

            object state = entry.GetType().GetProperty("Value")?.GetValue(entry);
            if (state is null) {
                continue;
            }

            object node = Field(state, "detourList");
            for (int guard = 0; node is not null && guard < 512; guard++) {
                if (Field(node, "FirstDelegate") is Delegate hook) {
                    Add(Describe(hook.Method));
                }
                else if (Property(node, "Entry") is MethodBase entryMethod) {
                    Add(Describe(entryMethod));
                }

                node = Field(node, "Next");
            }

            if (Field(state, "noConfigIlhooks") is IEnumerable ilHooks) {
                foreach (object ilHook in ilHooks) {
                    if (Property(ilHook, "Manip") is Delegate manip) {
                        Add(Describe(manip.Method));
                    }
                }
            }

            object chain = Field(Field(state, "ilhookGraph"), "ListHead");
            for (int guard = 0; chain is not null && guard < 512; guard++) {
                if (Field(chain, "ChainNode") is { } ilEntry && Property(ilEntry, "Manip") is Delegate manip) {
                    Add(Describe(manip.Method));
                }

                chain = Field(chain, "Next");
            }
        }

        return installers;
    }

    private static bool IsFromLoadedModule(string installer) {
        // Whitelist rather than blacklist: only an assembly that hosts a loaded Everest module counts,
        // so MonoMod.Core, Mono.Cecil and System.* helpers are ignored. Note that this does NOT mean
        // "only mods": Everest's core module is defined in Celeste.dll, so "Celeste" passes here. The
        // chain root is excluded by method identity in GameplayRendererHookInstallers instead.
        string assembly = installer.Split(" :: ")[0];
        if (assembly == "SpeedrunTool") {
            return false;
        }

        // A DynamicMethod has no DeclaringType, so Describe() reports it as "? :: ?.Method". Keep
        // those: a hook installed through one is still a hook, and the whitelist exists to drop
        // MonoMod/System plumbing, not anonymous installers.
        if (assembly == "?") {
            return true;
        }

        return Everest.Modules?.Any(module => module.GetType().Assembly.GetName().Name == assembly) == true;
    }

    private static string Describe(MethodBase method) {
        string assembly = method.DeclaringType?.Assembly.GetName().Name ?? "?";
        return assembly + " :: " + (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
    }

    private static object Field(object target, string name) {
        for (Type t = target?.GetType(); t is not null; t = t.BaseType) {
            FieldInfo field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is not null) {
                return field.GetValue(target);
            }
        }

        return null;
    }

    private static object Property(object target, string name) {
        for (Type t = target?.GetType(); t is not null; t = t.BaseType) {
            PropertyInfo property = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property is not null) {
                return property.GetValue(target);
            }
        }

        return null;
    }
#endif
}
