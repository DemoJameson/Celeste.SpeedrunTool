using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class EntityAction : AbstractEntityAction {
        private ILHook origLoadLevelHook;

        private static readonly List<Type> ExcludeTypes = new List<Type> {
            typeof(ParticleSystem),
            typeof(Wire),
            typeof(Cobweb),
            typeof(Decal),
            typeof(Lamp),
            typeof(HangingLamp),
        };

        private static readonly List<string> ExcludeTypeNames = new List<string> {
            "Celeste.CrystalStaticSpinner+Border",
            "Celeste.DustGraphic+Eyeballs",
            "Celeste.TalkComponent+TalkComponentUI",
            "Celeste.ZipMover+ZipMoverPathRenderer",
        };

        public override void OnQuickSave(Level level) { }

        public override void OnClear() { }

        private static void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self,
            Vector2 position) {
            orig(self, position);

            if (!(Engine.Scene is LevelLoader) && !(Engine.Scene is Level)) return;

            Type type = self.GetType();
            if (type.Namespace != "Celeste") return;
            if (ExcludeTypes.Contains(type)) return;
            if (ExcludeTypeNames.Contains(type.FullName)) return;

            self.TrySetEntityId(position.ToString());
        }

        private void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Ldarg_0, // this
                i => i.OpCode == OpCodes.Ldloc_S, // EntityData
                i => i.OpCode == OpCodes.Ldloc_S, // Vector2
                i => i.OpCode == OpCodes.Newobj &&
                     i.Operand.ToString().Contains("::.ctor(Celeste.EntityData,"), // Entity::ctor(EntityData, Vector2)
                i => i.MatchCall(typeof(Scene).GetMethod("Add", new[] {typeof(Entity)})) // level.Add(entity)
            )) {
                // TODO Attach entityData to entity
            }
        }

        public override void OnLoad() {
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
            // origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
        }

        public override void OnUnload() {
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
            // origLoadLevelHook.Dispose();
        }
    }
}