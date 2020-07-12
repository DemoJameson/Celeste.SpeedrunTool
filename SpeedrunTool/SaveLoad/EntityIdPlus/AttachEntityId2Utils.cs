using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus {
    public static class AttachEntityId2Utils{
        private static readonly List<Type> ExcludeTypes = new List<Type> {
            // typeof(Entity), // Booster 红色气泡的虚线就是用 Entity 显示的，所以需要 EntityId2 来同步状态
            typeof(Cobweb),
            typeof(Decal),
            typeof(HangingLamp),
            typeof(Lamp),
            typeof(ParticleSystem),
            typeof(Wire),
        };

        private static readonly List<string> SpecialNestedPrivateTypes = new List<string> {
            "Celeste.ForsakenCitySatellite+CodeBird",
        };

        private static ILHook origLoadLevelHook;
        private static ILHook loadCustomEntityHook;

        // 将 EntityData 与 EntityID 附加到 Entity 的实例上
        private static void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.HasParameters &&
                     m.Parameters.Any(parameter => parameter.ParameterType.Name == "EntityData"))) {
                if (cursor.TryFindPrev(out var results,
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v &&
                         v.VariableType.Name == "EntityData")) {
                    // cursor.Previous.DebugLog();
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, results[0].Next.Operand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityId);
                }
            }
        }

        // 将 EntityData 与 EntityID 附加到 Entity 的实例上
        private static void ModLoadCustomEntity(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand.ToString().Contains("::.ctor(Celeste.EntityData"))) {
                // cursor.Previous.DebugLog();
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityId);
            }

            // TODO is possible to attach data to entity created by LoadEntity Event?
            // if (Everest.Events.Level.LoadEntity(level, levelData, offset, entityData)) { return true; }
            
            /*
            if (EntityLoaders.TryGetValue(entityData.Name, out EntityLoader value)) {
				Entity entity = value(level, levelData, offset, entityData);
				if (entity != null) {
				    // insert AttachEntityId(entity, entityData);
					level.Add(entity);
					return true;
				}
			}
            */
            cursor.Goto(0);
            if (cursor.TryGotoNext(i => i.OpCode == OpCodes.Callvirt && i.Operand.ToString().Contains("EntityLoader::Invoke"))) {
                if (cursor.TryGotoNext(i => i.MatchCallvirt<Scene>("Add"))) {
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityId);
                }
            }
        }

        private static void AttachEntityId(Entity entity, EntityData data) {
            if (entity.IsGlobalButNotCassetteManager()) return;
            entity.SetEntityId2(data.ToEntityId2(entity));
            entity.SetEntityData(data);
        }

        // 处理其他没有 EntityData 的物体
        private static void EntityOnAdded(On.Monocle.Entity.orig_Added orig, Entity self, Scene scene) {
            orig(self, scene);

            Type type = self.GetType();

            if (!(scene is Level)) return;
            if (self.IsGlobalButNotCassetteManager()) return;
            if (self.HasEntityId2()) return;
            if (ExcludeTypes.Contains(type)) return;

            string entityIdParam = self.Position.ToString();
            if (type.IsNestedPrivate) {
                if (!SpecialNestedPrivateTypes.Contains(type.FullName)) return;
                entityIdParam = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(info => info.FieldType.IsSimple() && info.DeclaringType.IsNestedPrivate).Aggregate(
                        entityIdParam,
                        (current, fieldInfo) => current + (fieldInfo.GetValue(self)?.ToString() ?? "null"));
            }

            EntityId2 entityId2 = self.CreateEntityId2(entityIdParam);
            self.SetEntityId2(entityId2);
            self.SetStartPosition(self.Position);
        }

        public static void OnLoad() {
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            loadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
            On.Monocle.Entity.Added += EntityOnAdded;
            CustomEntityId2Utils.OnLoad();
        }

        public static void Unload() {
            origLoadLevelHook.Dispose();
            loadCustomEntityHook.Dispose();
            On.Monocle.Entity.Added -= EntityOnAdded;
            CustomEntityId2Utils.OnUnload();
        }
    }
}