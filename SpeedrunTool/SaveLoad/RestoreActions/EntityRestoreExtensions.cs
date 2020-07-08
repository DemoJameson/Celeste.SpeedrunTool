using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions;
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

                    if (destinationValue is IList destList && sourceValue is IList sourceList) {
                        destList.Clear();
                        foreach (object obj in sourceList) {
                            destList.Add(obj);
                        }
                    }
                } else {
                    // 列表里是复杂类型
                    // TODO 考虑 destinationValue 为空时的情况

                    // 不为空且数量一致
                    if (destinationValue is IList destList && sourceValue is IList sourceList &&
                        destList.Count == sourceList.Count) {
                        for (int i = 0; i < destList.Count; i++) {
                            CopySpecifiedType(destList[i], sourceList[i]);
                        }
                    }
                }
            }else if (memberType.IsArray && memberType.GetElementType() is Type elementType) {
                if (destinationValue is Array destArray && destArray.Rank == 1 && sourceValue is Array sourceArray &&
                    destArray.Length == sourceArray.Length) {
                    for (int i = 0; i < destArray.Length; i++) {
                        if (elementType.IsSimple()) {
                            destArray.SetValue(sourceArray.GetValue(i), i);
                        } else {
                            CopySpecifiedType(destArray.GetValue(i), sourceArray.GetValue(i));
                        }
                    }
                }
            } else {
                // 复杂类型
                if (destinationValue == null) {
                    // 为空则根据情况创建新实例或者查找当前场景的实例
                    destinationValue = CreateOrFindSpecifiedType(sourceValue);
                    if (destinationValue != null) {
                        setMember(destinationObj, currentObjType, memberName, destinationValue);
                    }
                } else {
                    // 不为空则复制里面的值
                    CopySpecifiedType(destinationValue, sourceValue);
                }
            }
        }

        // destinationValue and sourceValue are the same type.
        public static void CopySpecifiedType(this object destValue, object sourceValue) {
            if (sourceValue is Component) {
                destValue.CopyAllFrom(sourceValue, typeof(Component), destValue.GetType(),
                    // CoroutineAction takes care of them 
                    typeof(Coroutine),
                    typeof(StateMachine),
                    // crash sometimes so skip it, only copy some fields later.
                    typeof(DustGraphic));
                if (destValue is Sprite destSprite && sourceValue is Sprite sourceSprite) {
                    sourceSprite.InvokeMethod("CloneInto", destSprite);
                }

                if (destValue is StateMachine destMachine && sourceValue is StateMachine sourceMachine) {
                    // Only Set the field, CoroutineAction takes care of the rest
                    destMachine.SetField("state", sourceMachine.State);
                }

                if (destValue is DustGraphic destDustGraphic && sourceValue is DustGraphic sourceDustGraphic) {
                    destDustGraphic.EyeDirection = sourceDustGraphic.EyeDirection;
                    destDustGraphic.EyeTargetDirection = sourceDustGraphic.EyeTargetDirection;
                    destDustGraphic.EyeFlip = sourceDustGraphic.EyeFlip;
                }

                if (destValue is SoundSource destSound && sourceValue is SoundSource sourceSound && sourceSound.LoadPlayingValue()) {
                    destSound.Play(destSound.EventName);
                    destSound.SetTime(sourceSound);
                    destSound.Pause();
                    SoundSourceAction.PausedSoundSources.Add(destSound);
                }
            } else if (sourceValue is Entity sourceEntity && destValue is Entity destinationEntity) {
                // TODO 指向的 Entity 不同，可能不会出现这种情况，先记录一下 Log
                if (sourceEntity.HasEntityId2() && sourceEntity.GetEntityId2() != destinationEntity.GetEntityId2()) {
                    "EntityId2 different need to be restore".DebugLog(sourceEntity.GetEntityId2(),
                        destinationEntity.GetEntityId2());
                    throw new Exception("EntityId2 different need to be restore");
                }
            }
        }

        public static object CreateOrFindSpecifiedType(object sourceValue) {
            if (sourceValue is Entity entity) {
                if (Engine.Scene.FindFirst(entity.GetEntityId2()) is Entity destinationEntity) {
                    return destinationEntity;
                }
            } else if (sourceValue is Component sourceComponent) {
                // TODO Recreate Component
                Entity sourceEntity = sourceComponent.Entity;
                Entity destEntity = Engine.Scene.FindFirst(sourceEntity?.GetEntityId2());
                if (sourceValue is SoundSource || sourceValue is BloomPoint) {
                    Component destSound = (Component) FormatterServices.GetUninitializedObject(sourceValue.GetType());
                    CopySpecifiedType(destSound, sourceValue);
                    destEntity?.Add(destSound);
                    return destSound;
                }
            }

            return null;
        }
    }
}