using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Fasterflect;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus {
    public static class AttachEntityId2Utils {
        private const BindingFlags ConstructorFlags = BindingFlags.Instance | BindingFlags.Public |
                                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly List<ILHook> IlHooks = new List<ILHook>();

        public static void OnLoad() {
            On.Monocle.Entity.Awake += EntityOnAwake;
            CustomEntityId2Utils.OnLoad();
        }

        public static void Unload() {
            On.Monocle.Entity.Awake -= EntityOnAwake;
            CustomEntityId2Utils.OnUnload();
            IlHooks.ForEach(hook => hook.Dispose());
            IlHooks.Clear();
        }

        private static void EntityOnAwake(On.Monocle.Entity.orig_Awake orig, Entity self, Scene scene) {
            orig(self, scene);
            if (self.IsSidEmpty()) {
                self.SetEntityId2(self.Position.ToString());
            }
        }

        // ReSharper disable once InconsistentNaming
        public static void ILHookAllEntityConstructor() {
            MethodInfo attachConstructorInfoMethodInfo = typeof(AttachEntityId2Utils).GetMethod("AttachConstructorInfo",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodBase getCurrentMethodMethodBase =
                typeof(MethodBase).GetMethod("GetCurrentMethod", BindingFlags.Static | BindingFlags.Public);
            Func<Assembly[]> assemblies = AppDomain.CurrentDomain.GetAssemblies;
            foreach (Assembly assembly in assemblies()) {
                // seems not work?
                if (assembly == Assembly.GetExecutingAssembly()) continue;

                IEnumerable<ConstructorInfo> constructorInfos = assembly.GetTypes().Where(type =>
                    !type.IsAbstract
                    && !type.IsGenericType
                    && type.IsSameOrSubclassOf(typeof(Entity))
                ).SelectMany(type => type.GetConstructors(ConstructorFlags));

                foreach (ConstructorInfo constructorInfo in constructorInfos) {
                    // $"{constructorInfo.DeclaringType}={string.Join(", ", constructorInfo.GetParameters().Select(info => info.ParameterType.Name))}".DebugLog();
                    Type[] argTypes = constructorInfo.GetParameters().Select(info => info.ParameterType).ToArray();
                    IlHooks.Add(new ILHook(constructorInfo, il => {
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

        // ReSharper disable once UnusedMember.Local
        private static void AttachConstructorInfo(Entity entity, MethodBase methodBase,
            params object[] parameters) {
            if (Engine.Scene.IsNotType<LevelLoader>() && Engine.Scene.IsNotType<Level>()) return;

            // methodBase.GetType() = DynamicMethod+RTDynamicMethod || System.Reflection.MonoMethod
            ParameterInfo[] parameterInfos = methodBase.GetParameters();

            // 只在目标类型目标构造函数上执行下面的操作，重载的构造函数或者父类的构造函数不管
            if (entity.GetConstructorInvoker() != null) return;

            // skip[1] because the first type is returnType
            Type[] parameterTypes = parameterInfos.Skip(1).Select(info => info.ParameterType).ToArray();

            ConstructorInvoker constructorInvoker = methodBase.GetConstructorInvoker();
            if (constructorInvoker == null &&
                parameterInfos[0].ParameterType.GetConstructor(ConstructorFlags, null, parameterTypes, null) is
                    ConstructorInfo constructorInfo) {
                constructorInvoker = Reflect.Constructor(constructorInfo);
                methodBase.SetConstructorInvoker(constructorInvoker);
            }

            // 应该不可能发生
            if (constructorInvoker == null) return;

            entity.SetConstructorInvoker(constructorInvoker);
            entity.SetParameters(parameters);

            if (parameters.FirstOrDefault(o => o.IsType<EntityID>()) is EntityID entityId) {
                entity.SetEntityId2(entityId, false);
            } else if (parameters.FirstOrDefault(o => o.IsType<EntityData>()) is EntityData entityData) {
                entity.SetEntityId2(entityData.ToEntityId(), false);
            } else {
                entity.SetEntityId2(parameters, false);
            }
        }
    }

    internal static class ConstructorInfoExtensions {
        private const string ConstructorInvokerKey = "ConstructorInvoker-Key";
        private const string ParametersKey = "Parameters-Key";

        public static ConstructorInvoker GetConstructorInvoker(this MethodBase methodBase) {
            return methodBase.GetExtendedDataValue<ConstructorInvoker>(ConstructorInvokerKey);
        }

        public static void SetConstructorInvoker(this MethodBase methodBase, ConstructorInvoker constructorInvoker) {
            methodBase.SetExtendedDataValue(ConstructorInvokerKey, constructorInvoker);
        }

        public static ConstructorInvoker GetConstructorInvoker(this Entity entity) {
            return entity.GetExtendedDataValue<ConstructorInvoker>(ConstructorInvokerKey);
        }

        public static void SetConstructorInvoker(this Entity entity, ConstructorInvoker constructorInvoker) {
            entity.SetExtendedDataValue(ConstructorInvokerKey, constructorInvoker);
        }

        public static object[] GetParameters(this Entity entity) {
            return entity.GetExtendedDataValue<object[]>(ParametersKey);
        }

        public static void SetParameters(this Entity entity, object[] parameters) {
            entity.SetExtendedDataValue(ParametersKey, parameters);
        }

        public static T Recreate<T>(this T entity, bool tryForceCreateEntity = true) where T : Entity {
            object[] parameters = entity.GetParameters();
            if (parameters == null) return null;

            object[] parametersCopy = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) {
                object parameter = parameters[i];
                if (parameter == null || parameter.GetType().IsSimple()) {
                    parametersCopy[i] = parameter;
                } else if (parameter.TryFindOrCloneObject(tryForceCreateEntity) is object objCopy) {
                    parametersCopy[i] = objCopy;
                } else {
                    return null;
                }
            }

            return entity.GetConstructorInvoker()?.Invoke(parametersCopy) as T;
        }
    }
}