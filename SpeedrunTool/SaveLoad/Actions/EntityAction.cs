using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class EntityAction : AbstractEntityAction {
        public override void OnQuickSave(Level level) { }
        public override void OnClear() { }

        private void PlatformOnCtor(On.Celeste.Platform.orig_ctor orig, Platform self, Vector2 position, bool safe) {
            orig(self, position, safe);
            SetEntityId(self, self.ToString().GetHashCode() + position.GetHashCode() + safe.GetHashCode());
        }

        private void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self, Vector2 position) {
            orig(self, position);
            SetEntityId(self, self.ToString().GetHashCode() + position.GetHashCode());
        }

        private static void SetEntityId(Entity entity, int hashCode) {
            EntityID entityId = entity.GetEntityId();
            if (entityId.Equals(default(EntityID))) {
                Level level = null;
                if (Engine.Scene is Level) {
                    level = (Level) Engine.Scene;
                }
                else if (Engine.Scene is LevelLoader levelLoader) {
                    level = levelLoader.Level;
                }

                if (level?.Session?.Level == null) {
                    return;
                }

                entityId = new EntityID(level.Session.Level, hashCode);
                entity.SetEntityId(entityId);
            }
        }


        public override void OnLoad() {
            On.Celeste.Platform.ctor += PlatformOnCtor;
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
        }

        public override void OnUnload() {
            On.Celeste.Platform.ctor -= PlatformOnCtor;
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
        }
    }
}