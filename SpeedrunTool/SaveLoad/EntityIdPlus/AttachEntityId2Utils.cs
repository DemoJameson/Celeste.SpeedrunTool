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
    public static class AttachEntityId2Utils {
        private static readonly List<ILHook> ilHooks = new List<ILHook>();
        private static ILHook origLoadLevelHook;
        private static ILHook loadCustomEntityHook;
        
        private static readonly HashSet<Type> ExcludeTypes = new HashSet<Type> {
            // Booster 红色气泡的虚线就是用 Entity 显示的，所以需要 EntityId2 来同步状态
            // typeof(Entity),

            // 装饰
            typeof(Cobweb),
            typeof(Decal),
            typeof(HangingLamp),
            typeof(Lamp),
            typeof(ParticleSystem),
            typeof(Wire),
            typeof(WaterSurface),

            // TalkComponent.UI 在保存后变为 null，导致 copyAll 之后出现两个对话图案
            typeof(TalkComponent.TalkComponentUI),
        };

        private static readonly HashSet<string> SpecialNestedPrivateTypes = new HashSet<string> {
            "Celeste.ForsakenCitySatellite+CodeBird",
        };

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
                    cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityIdFromLoadLevel);
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
                cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityIdFromLoadLevel);
            }

            cursor.Goto(0);
            if (cursor.TryGotoNext(i =>
                i.OpCode == OpCodes.Callvirt && i.Operand.ToString().Contains("EntityLoader::Invoke"))) {
                if (cursor.TryGotoNext(i => i.MatchCallvirt<Scene>("Add"))) {
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityIdFromLoadLevel);
                }
            }
        }

        private static void AttachEntityIdFromLoadLevel(Entity entity, EntityData data) {
            if (entity.IsGlobalButExcludeSomeTypes()) return;
            if (entity.NoEntityId2()) {
                $"{entity} No EntityId2".DebugLog();
                EntityId2 entityId2 = data.ToEntityId2(entity);
                entity.SetEntityId2(entityId2);
                entity.SetEntityData(data);
            }

            // $"AttachEntityId: {entityId2}".DebugLog();
        }

        private static void AttachEntityId(Entity entity, EntityData data) {
            if (entity.IsGlobalButExcludeSomeTypes()) return;
            EntityId2 entityId2 = data.ToEntityId2(entity);

            entity.SetEntityId2(entityId2);
            entity.SetEntityData(data);

            // $"AttachEntityId: {entityId2}".DebugLog();
        }

        // 处理其他没有 EntityData 的物体
        private static void EntityOnAdded(On.Monocle.Entity.orig_Added orig, Entity self, Scene scene) {
            orig(self, scene);

            Type type = self.GetType();

            if (!(scene is Level)) return;
            if (self.IsGlobalButExcludeSomeTypes()) return;
            if (self.HasEntityId2()) return;
            if (ExcludeTypes.Contains(type)) return;
            if (self.GetType().Assembly == Assembly.GetExecutingAssembly()) return;

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
            ILHookAllEntity();
        }

        public static void Unload() {
            origLoadLevelHook.Dispose();
            loadCustomEntityHook.Dispose();
            On.Monocle.Entity.Added -= EntityOnAdded;
            CustomEntityId2Utils.OnUnload();
            ilHooks.ForEach(hook => hook.Dispose());
            ilHooks.Clear();
        }

        // ReSharper disable once InconsistentNaming
        private static void ILHookAllEntity() {
            MethodInfo methodInfo =
                typeof(AttachEntityId2Utils).GetMethod("AttachEntityId", BindingFlags.Static | BindingFlags.NonPublic);
            Func<Assembly[]> assemblies = AppDomain.CurrentDomain.GetAssemblies;
            foreach (Assembly assembly in assemblies()) {
                IEnumerable<Type> entityTypes = assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(Entity)));
                foreach (Type entityType in entityTypes) {
                    foreach (ConstructorInfo constructorInfo in entityType.GetConstructors()) {
                        List<Type> parameterTypes = constructorInfo.GetParameters().Select(info => info.ParameterType).ToList();
                        if (parameterTypes.All(type => type != typeof(EntityData)))
                            continue;

                        $"{constructorInfo.DeclaringType} = {string.Join(";\t", parameterTypes)}"
                            .DebugLog();
                        ilHooks.Add(new ILHook(constructorInfo, il => {
                            ILCursor ilCursor = new ILCursor(il);
                            ilCursor.Emit(OpCodes.Ldarg_0);
                            ilCursor.Emit(OpCodes.Ldarg_S, (byte) (parameterTypes.IndexOf(typeof(EntityData)) + 1));
                            ilCursor.Emit(OpCodes.Call, methodInfo);
                        }));
                    }
                }
            }
        }
    }
}