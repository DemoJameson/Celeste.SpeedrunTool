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

        public static void OnLoad() {
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            loadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
            // On.Monocle.Entity.Added += EntityOnAdded;
            CustomEntityId2Utils.OnLoad();
            ILHookAllEntity();
        }

        public static void Unload() {
            origLoadLevelHook.Dispose();
            loadCustomEntityHook.Dispose();
            // On.Monocle.Entity.Added -= EntityOnAdded;
            CustomEntityId2Utils.OnUnload();
            ilHooks.ForEach(hook => hook.Dispose());
            ilHooks.Clear();
        }

        private const BindingFlags constructorFlags = BindingFlags.Instance | BindingFlags.Public |
                                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                                                      BindingFlags.CreateInstance;

        // ReSharper disable once InconsistentNaming
        private static void ILHookAllEntity() {
            MethodInfo attachConstructorInfoMethodInfo = typeof(AttachEntityId2Utils).GetMethod("AttachConstructorInfo",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodBase getCurrentMethodMethodBase =
                typeof(MethodBase).GetMethod("GetCurrentMethod", BindingFlags.Static | BindingFlags.Public);
            Func<Assembly[]> assemblies = AppDomain.CurrentDomain.GetAssemblies;
            foreach (Assembly assembly in assemblies()) {
                if (assembly == Assembly.GetExecutingAssembly()) continue;

                IEnumerable<Type> entityTypes = assembly.GetTypes().Where(type =>
                    type.IsSameOrSubclassOf(typeof(Entity)) && !type.IsAbstract && !type.IsGenericType);
                foreach (Type entityType in entityTypes) {
                    foreach (ConstructorInfo constructorInfo in entityType.GetConstructors(constructorFlags)) {
                        Type[] argTypes = constructorInfo.GetParameters().Select(info => info.ParameterType).ToArray();

                        ilHooks.Add(new ILHook(constructorInfo, il => {
                            ILCursor ilCursor = new ILCursor(il);

                            // entity
                            ilCursor.Emit(OpCodes.Ldarg_0);

                            // MethodBase.GetCurrentMethod()
                            ilCursor.Emit(OpCodes.Call, getCurrentMethodMethodBase);

                            // object[] parameters
                            ilCursor.Emit(OpCodes.Ldc_I4, argTypes.Length);
                            ilCursor.Emit(OpCodes.Newarr, typeof(object));

                            for (int i = 0; i < argTypes.Length; i++) {
                                Type argType = argTypes[i];
                                if (argType.IsByRef) {
                                    argType = argType.GetElementType();
                                }

                                ilCursor.Emit(OpCodes.Dup);
                                ilCursor.Emit(OpCodes.Ldc_I4, i);
                                ilCursor.Emit(OpCodes.Ldarg, i + 1);
                                if (argType.IsValueType) {
                                    ilCursor.Emit(OpCodes.Box, argType);
                                }

                                ilCursor.Emit(OpCodes.Stelem_Ref);
                            }

                            // AttachConstructorInfo(Entity entity, MethodBase rtDynamicMethod, params object[] parameters)
                            ilCursor.Emit(OpCodes.Call, attachConstructorInfoMethodInfo);
                        }));
                    }
                }
            }
        }

        // ReSharper disable once UnusedMember.Local
        private static void AttachConstructorInfo(Entity entity, MethodBase rtDynamicMethod,
            params object[] parameters) {
            if (Engine.Scene.IsNotType<LevelLoader>() && Engine.Scene.IsNotType<Level>()) return;

            // rtDynamicMethod.GetType() = DynamicMethod+RTDynamicMethod
            ParameterInfo[] parameterInfos =
                (ParameterInfo[]) rtDynamicMethod.InvokeMethod(rtDynamicMethod.GetType(), "LoadParameters");

            // 只在目标类型目标构造函数上执行下面的操作，重载的构造函数或者父类的构造函数不管
            if (entity.GetConstructorInfo() != null) return;

            // skip[1] because the first type is returnType
            Type[] parameterTypes = parameterInfos.Skip(1).Select(info => info.ParameterType).ToArray();

            ConstructorInfo constructor = rtDynamicMethod.GetConstructorInfo();
            if (constructor == null &&
                parameterInfos[0].ParameterType.GetConstructor(constructorFlags, null, parameterTypes, null) is
                    ConstructorInfo
                    constructorInfo) {
                constructor = constructorInfo;
                rtDynamicMethod.SetConstructorInfo(constructorInfo);
            }

            // 应该不可能发生
            if (constructor == null) return;

            entity.SetConstructorInfo(constructor);
            entity.SetParameters(parameters);

            if (parameters.FirstOrDefault(o => o.IsType<EntityData>()) is EntityData entityData) {
                entity.SetEntityData(entityData);
                entity.SetEntityId2(entityData.ToEntityId(), false);
            } else if (parameters.FirstOrDefault(o => o.IsType<EntityID>()) is EntityID entityId) {
                entity.SetEntityId2(entityId, false);
            } else {
                entity.SetEntityId2(parameters, false);
            }
        }
    }

    public static class ConstructorInfoExtensions {
        private const string ConstructorInfoKey = "ConstructorInfoKey";
        private const string ParametersKey = "ParametersKey";

        public static ConstructorInfo GetConstructorInfo(this MethodBase methodBase) {
            return methodBase.GetExtendedDataValue<ConstructorInfo>(ConstructorInfoKey);
        }

        public static void SetConstructorInfo(this MethodBase methodBase, ConstructorInfo constructorInfo) {
            methodBase.SetExtendedDataValue(ConstructorInfoKey, constructorInfo);
        }

        public static ConstructorInfo GetConstructorInfo(this Entity entity) {
            return entity.GetExtendedDataValue<ConstructorInfo>(ConstructorInfoKey);
        }

        public static void SetConstructorInfo(this Entity entity, ConstructorInfo constructorInfo) {
            entity.SetExtendedDataValue(ConstructorInfoKey, constructorInfo);
        }

        public static object[] GetParameters(this Entity entity) {
            return entity.GetExtendedDataValue<object[]>(ParametersKey);
        }

        public static void SetParameters(this Entity entity, object[] parameters) {
            entity.SetExtendedDataValue(ParametersKey, parameters);
        }

        public static Entity Recreate(this Entity entity) {
            object[] parameters = entity.GetParameters();
            if (parameters == null) return null;
            
            object[] parametersCopy = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++) {
                object parameter = parameters[i];
                if (parameter == null || parameter.GetType().IsSimple()) {
                    parametersCopy[i] = parameter;
                } else if (parameter.TryFindOrCloneObject() is object objCopy){
                    parametersCopy[i] = objCopy;
                } else {
                    return null;
                }
            }
            
            return entity.GetConstructorInfo()?.Invoke(parameters) as Entity;
        }
    }
}