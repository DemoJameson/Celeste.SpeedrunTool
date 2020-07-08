using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public static class EntityRestoreExtensions {
        public static void CopyAllFrom(this object destinationObj, object sourceObj, Type baseType,
            params Type[] skipTypes) {
            destinationObj.CopyAllFrom(sourceObj, baseType, destinationObj.GetType(), skipTypes);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static void CopyAllFrom(this object destinationObj, object sourceObj, Type baseType, Type derivedType,
            params Type[] skipTypes) {
            if (destinationObj.GetType() != sourceObj.GetType()) {
                throw new ArgumentException("destinationObj and sourceObj not the same type.");
            }

            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 从给定的子类开始复制字段，直到给的父类复制完毕，包括父类
            Type currentObjType = derivedType;

            while (currentObjType.IsSameOrSubclassOf(baseType)) {
                // 跳过特定子类型
                if (skipTypes.Contains(currentObjType)) {
                    currentObjType = currentObjType.BaseType;
                    continue;
                }

                // 必须先设置属性再设置字段，不然字段的值会在设置属性后发生改变
                PropertyInfo[] properties = currentObjType.GetProperties(bindingFlags);
                foreach (PropertyInfo propertyInfo in properties) {
                    // 只处理能读取+写入的属性
                    if (!propertyInfo.CanRead || !propertyInfo.CanWrite) continue;

                    Type memberType = propertyInfo.PropertyType;

                    string memberName = propertyInfo.Name;
                    object destinationValue = destinationObj.GetProperty(currentObjType, memberName);
                    object sourceValue = sourceObj.GetProperty(currentObjType, memberName);

                    CopyMember(currentObjType, memberType, memberName, destinationObj, destinationValue,
                        sourceValue, SetProperty);
                }

                FieldInfo[] fields = currentObjType.GetFields(bindingFlags);
                foreach (FieldInfo fieldInfo in fields) {
                    Type memberType = fieldInfo.FieldType;

                    string memberName = fieldInfo.Name;
                    object destinationValue = destinationObj.GetField(currentObjType, memberName);
                    object sourceValue = sourceObj.GetField(currentObjType, memberName);

                    CopyMember(currentObjType, memberType, memberName, destinationObj, destinationValue, sourceValue,
                        SetField);
                }

                currentObjType = currentObjType.BaseType;
            }
        }

        private delegate void SetMember(object destinationObj, Type currentObjType, string memberName,
            object sourceValue);

        private static void SetProperty(object destinationObj, Type currentObjType, string memberName,
            object sourceValue) {
            destinationObj.SetProperty(currentObjType, memberName, sourceValue);
        }

        private static void SetField(object destinationObj, Type currentObjType, string memberName,
            object sourceValue) {
            destinationObj.SetField(currentObjType, memberName, sourceValue);
        }

        private static void CopyMember(Type currentObjType, Type memberType, string memberName, object destinationObj,
            object destinationValue, object sourceValue, SetMember setMember) {
            if (sourceValue == null) {
                // null 也是有意义的
                setMember(destinationObj, currentObjType, memberName, null);
                return;
            }

            if (memberType.IsSimple()) {
                // 简单类型直接复制
                setMember(destinationObj, currentObjType, memberName, sourceValue);
            } else if (memberType.IsList(out Type genericType)) {
                // 列表
                if (genericType.IsSimple()) {
                    // 列表里是简单类型，则重建列表并且全部复制进去
                    if (destinationValue == null) {
                        destinationValue = Activator.CreateInstance(memberType);
                        setMember(destinationObj, currentObjType, memberName, destinationValue);
                    }

                    if (destinationValue is IList destinationList && sourceValue is IList sourceList) {
                        destinationList.Clear();
                        foreach (object obj in sourceList) {
                            destinationList.Add(obj);
                        }
                    }
                } else {
                    // TODO 列表里是复杂类型
                }
            } else {
                // 复杂类型
                if (destinationValue != null) {
                    // 不为空则复制里面的值
                    CopySpecifiedType(destinationValue, sourceValue);
                } else {
                    // 为空则根据情况创建新实例或者查找当前场景的实例
                    destinationValue = CreateOrFindSpecifiedType(sourceValue);
                    if (destinationValue != null) {
                        setMember(destinationObj, currentObjType, memberName, destinationValue);
                    }
                }
            }
        }

        // destinationValue and sourceValue are the same type.
        private static void CopySpecifiedType(this object destinationValue, object sourceValue) {
            if (sourceValue is Component) {
                destinationValue.CopyAllFrom(sourceValue, typeof(Component), destinationValue.GetType(),
                    typeof(Coroutine), typeof(StateMachine));
                if (destinationValue is Sprite destSprite && sourceValue is Sprite sourceSprite) {
                    sourceSprite.InvokeMethod("CloneInto", destSprite);
                }

                if (destinationValue is StateMachine destMachine && sourceValue is StateMachine sourceMachine) {
                    // Only Set the field, CoroutineAction takes care of the rest
                    destMachine.SetField("state", sourceMachine.State);
                }
            } else if (sourceValue is Entity sourceEntity && destinationValue is Entity destinationEntity) {
                // TODO 恢复指向的 Entity，不一定会出现这种情况，先检查一下 Log
                if (sourceEntity.HasEntityId2() && sourceEntity.GetEntityId2() != destinationEntity.GetEntityId2()) {
                    "EntityId2 different".DebugLog(sourceEntity.GetEntityId2(), destinationEntity.GetEntityId2());
                }
            }
        }

        private static object CreateOrFindSpecifiedType(object sourceValue) {
            if (sourceValue is Entity sourceEntity &&
                Engine.Scene.FindFirst(sourceEntity.GetEntityId2()) is Entity destinationEntity) {
                return destinationEntity;
            }
            // TODO Recreate Component

            return null;
        }
    }
}