using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class StaticMoverAction : AbstractEntityAction {
        private readonly Dictionary<EntityID, StaticMover> savedStaticMovers = new Dictionary<EntityID, StaticMover>();

        public override void OnQuickSave(Level level) {
            var staticMovers = level.Tracker.GetComponents<StaticMover>();
            foreach (StaticMover staticMover in staticMovers) {
                var entityId = staticMover.Entity.GetEntityId();
                if (staticMover.Entity != null && !entityId.Equals(default(EntityID))) {
                    savedStaticMovers.Add(staticMover.Entity.GetEntityId(), staticMover);
                }
            }
        }

        private void ModStaticMoverChecker(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            // if (component.IsRiding(this) && component.Platform == null)
            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdfld<StaticMover>("Platform"))) {
                String className = cursor.Method.Parameters[0].ParameterType.Name;
                Logger.Log("SpeedrunTool/StaticMoverAction",
                    $"Adding code to avoid attaching spikes to the wrong entities at index {cursor.Index} in CIL code for {className}.{cursor.Method.Name}");
                cursor.Emit(className == "Solid" ? OpCodes.Ldloc_2 : OpCodes.Ldloc_1);
                cursor.Emit(OpCodes.Ldarg_0); // this: Solid or JumpThru
                cursor.EmitDelegate<Func<bool, StaticMover, Platform, bool>>(CheckIfNotAttachStaticMover);
            }
        }

        private bool CheckIfNotAttachStaticMover(bool shouldNotAttach, StaticMover staticMover, Platform platform) {
            if (IsLoadStart && !shouldNotAttach) {
                var entityId = staticMover.Entity.GetEntityId();
                var platformEntityId = platform.GetEntityId();
                EntityID defaultEntityId = default(EntityID);
                if (entityId.Equals(defaultEntityId) || platformEntityId.Equals(defaultEntityId)) {
                    return false;
                }
                
                if (savedStaticMovers.ContainsKey(entityId)) {
                    var savedStaticMover = savedStaticMovers[entityId];
                    // 之前依附的 Platform 与本次查找的 Platform 非同一个则不依附
                    if (savedStaticMover.Platform == null || !savedStaticMover.Platform.GetEntityId().Equals(platformEntityId)) {
                        return true;
                    }
                }
            }

            return shouldNotAttach;
        }

        public override void OnClear() {
            savedStaticMovers.Clear();
        }

        public override void OnLoad() {
            IL.Celeste.Solid.Awake += ModStaticMoverChecker;
            IL.Celeste.JumpThru.Awake += ModStaticMoverChecker;
        }

        public override void OnUnload() {
            IL.Celeste.Solid.Awake -= ModStaticMoverChecker;
            IL.Celeste.JumpThru.Awake -= ModStaticMoverChecker;
        }
    }
}