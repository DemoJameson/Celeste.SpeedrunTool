using System;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus {
    public static class AttachEntityId2Utils {
        private static ILHook origLoadLevelHook;
        private static ILHook LoadCustomEntityHook;

        private static void EntityOnAdded(On.Monocle.Entity.orig_Added orig, Entity self, Scene scene) {
            orig(self, scene);

            if (self.HasEntityId2()) return;
            if (!(scene is Level)) return;

            EntityId2 entityId2 = self.CreateEntityId2(self.Position.ToString());
            // Too Slow
            // var dict = scene.FindAllToDict(self.GetType());
            // while (dict.ContainsKey(entityId2)) {
            //     entityId2 = new EntityId2(new EntityID(entityId2.EntityId.Level, entityId2.EntityId.ID.ToString().GetHashCode()), self.GetType());
            // }

            self.SetEntityId2(entityId2);
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
                cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityId);
            }
        }

        private static void AttachEntityId(Entity entity, EntityData data) {
            entity.SetEntityId2(data.ToEntityId2(entity));
            entity.SetEntityData(data);

            // entity.Log("IL Set EntityId2: ", entity.GetEntityId2().ToString());
        }

        public static void Load() {
            On.Monocle.Entity.Added += EntityOnAdded;
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            LoadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
        }

        public static void Unload() {
            On.Monocle.Entity.Added -= EntityOnAdded;
            origLoadLevelHook.Dispose();
            LoadCustomEntityHook.Dispose();
        }
    }
}