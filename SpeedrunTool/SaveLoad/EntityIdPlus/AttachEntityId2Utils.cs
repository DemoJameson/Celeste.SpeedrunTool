using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Entity = On.Monocle.Entity;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus {
    public static class AttachEntityId2Utils {
        private static ILHook origLoadLevelHook;
        private static ILHook LoadCustomEntityHook;

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

        
        private static void EntityOnAdded(Entity.orig_Added orig, Monocle.Entity self, Scene scene) {
            orig(self, scene);

            if (!(scene is Level)) return;

            // Type type = self.GetType();
            // if (type.Namespace != "Celeste") return;
            // if (ExcludeTypes.Contains(type)) return;
            // if (ExcludeTypeNames.Contains(type.FullName)) return;

            self.TrySetEntityId2(self.Position.ToString());
        }

        // 将 EntityData 与 EntityID 附加到 Entity 的实例上
        private static void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.HasParameters &&
                     m.Parameters.Any(parameter => parameter.ParameterType.Name == "EntityData"))) {
                if (cursor.TryFindPrev(out var results,
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v &&
                         v.VariableType.Name == "EntityData")) {
                    // cursor.Previous.Log();
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, results[0].Next.Operand);
                    cursor.EmitDelegate<Action<Monocle.Entity, EntityData>>(AttachEntityId);
                }
            }
        }

        // 将 EntityData 与 EntityID 附加到 Entity 的实例上
        private static void ModLoadCustomEntity(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand.ToString().Contains("::.ctor(Celeste.EntityData"))) {
                // cursor.Previous.Log();
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<Monocle.Entity, EntityData>>(AttachEntityId);
            }
        }

        private static void AttachEntityId(Monocle.Entity entity, EntityData data) {
            entity.SetEntityId2(data.ToEntityId2(entity));
            entity.SetEntityData(data);

            // entity.Log("IL Set EntityId2: ", entity.GetEntityId2().ToString());
        }

        public static void Load() {
            Entity.Added += EntityOnAdded;
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            LoadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
        }

        public static void Unload() {
            Entity.Added -= EntityOnAdded;
            origLoadLevelHook.Dispose();
            LoadCustomEntityHook.Dispose();
        }

    }
}