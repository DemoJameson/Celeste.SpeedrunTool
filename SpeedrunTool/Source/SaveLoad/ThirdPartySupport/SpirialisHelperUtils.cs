using Celeste.Mod.SpeedrunTool.Utils;
using MonoMod.Cil;
using MonoMod.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
internal static class SpirialisHelperUtils {

    internal static void Support() {
        SaveLoadAction.CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.TimePlayerSettings", "instance", "stoppedX", "stoppedY");
        SaveLoadAction.CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.CustomRainBG", "timeSinceFreeze");
        SaveLoadAction.CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.BoostCapModifier", "xCap", "yCap");

        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeController") is { } timeControllerType) {
            SaveLoadAction action = SaveLoadAction.InternalSafeAdd(
                loadState: (_, level) => {
                    if (level.Entities.FirstOrDefault(entity => entity.GetType() == timeControllerType) is not { } timeController) {
                        return;
                    }

                    if (Delegate.CreateDelegate(typeof(ILContext.Manipulator), timeController, timeControllerType.GetMethodInfo("CustomLevelRender"))
                        is not ILContext.Manipulator manipulator) {
                        return;
                    }

                    if (Delegate.CreateDelegate(typeof(On.Monocle.EntityList.hook_Update), timeController,
                            timeControllerType.GetMethodInfo("CustomELUpdate")) is not On.Monocle.EntityList.hook_Update customELUpdate) {
                        return;
                    }

                    IL.Celeste.Level.Render += manipulator;
                    if (timeController.GetFieldValue<bool>("hookAdded")) {
                        On.Monocle.EntityList.Update += customELUpdate;
                    }
                }
            );

            action.unloadLevel = (_, entities, entity) => {
                if (entity.GetType() == timeControllerType) {
                    entities.Add(entity);
                }
            };
        }

        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeZipMover") is { } timeZipMoverType) {
            if (typeof(ZipMover).GetMethod("Sequence", BindingFlags.Instance | BindingFlags.NonPublic).GetStateMachineTarget() is not
                { } sequenceMethodInfo) {
                return;
            }

            SaveLoadAction.InternalSafeAdd(
                    loadState: (_, level) => {
                        if (!level.Tracker.Entities.TryGetValue(timeZipMoverType, out List<Entity> zips)) {
                            return;
                        }

                        foreach (Entity entity in zips) {
                            if (entity.GetFieldValue<Helpers.LegacyMonoMod.LegacyILHook>("TimeStreetlightUpdate") is { } ilhook) {
                                ilhook?.Dispose();
                            }

                            if (Delegate.CreateDelegate(typeof(ILContext.Manipulator), entity, timeZipMoverType.GetMethodInfo("ZipSequence")) is
                                ILContext.Manipulator manipulator) {
                                entity.SetFieldValue("TimeStreetlightUpdate", new Helpers.LegacyMonoMod.LegacyILHook(sequenceMethodInfo, manipulator));
                            }
                        }
                    }
                );
        }
    }
}

