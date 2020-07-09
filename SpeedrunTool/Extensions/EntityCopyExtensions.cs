using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class EntityCopyExtensions {
        public static void CopyAllFrom(this object destinationObj, object sourceObj,
            params Type[] skipTypes) {
            destinationObj.CopyAllFrom(sourceObj, destinationObj.GetType(), destinationObj.GetType(), skipTypes);
        }

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
                // Component 需要从Entity中移除（适用于第六章 BOSS 在 Sprite 和 PlayerSprite 之前切换）
                if (destinationValue is Component component) {
                    component.RemoveSelf();
                }

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
                    // 不为空
                    if (destinationValue is IList destList && sourceValue is IList sourceList) {
                        if (destList.Count == sourceList.Count) {
                            // 数量一致
                            for (int i = 0; i < destList.Count; i++) {
                                CopySpecifiedType(destList[i], sourceList[i]);
                            }
                        } else if (genericType.IsSameOrSubclassOf(typeof(Entity))) {
                            // 数量不一致，例如 FinalBoos 的 fallingBlocks
                            if (sourceList.Count == 0) {
                                destList.Clear();
                            } else if ((sourceList[0] as Entity)?.HasEntityId2() == true) {
                                // 确保可以通过 EntityId2 查找
                                destList.Clear();
                                foreach (object o in sourceList) {
                                    object destValue = CreateOrFindSpecifiedType(o);
                                    if (destValue != null) {
                                        destList.Add(destValue);
                                    }
                                }
                            }
                        } else {
                            // TODO 其他类型
                            genericType.DebugLog("TODO 列表里是复杂类型");
                        }
                    }
                }
            } else if (memberType.IsArray && memberType.GetElementType() is Type elementType) {
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
                    } else {
                        destinationObj.DebugLog("Copy",
                            $"memberName={memberName} memberType={memberType} on {currentObjType} failed\t sourceValue={sourceValue}");
                    }
                } else {
                    // 不为空则复制里面的值
                    CopySpecifiedType(destinationValue, sourceValue);
                }
            }
        }

        // destinationValue and sourceValue always are the same type.
        public static void CopySpecifiedType(this object destValue, object sourceValue) {
            if (destValue.IsCompilerGenerated()) {
                destValue.CopyAllFrom(sourceValue);
            } else if (sourceValue is Component) {
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

                if (destValue is SoundSource destSound && sourceValue is SoundSource sourceSound &&
                    sourceSound.LoadPlayingValue()) {
                    destSound.Play(destSound.EventName);
                    destSound.SetTime(sourceSound);
                    destSound.Pause(); // 先暂停等待 Player 复活完毕再继续播放
                    SoundSourceAction.PlayingSoundSources.Add(destSound);
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

        public static object FindSpecifiedType(object sourceValue) {
            object destValue = null;
            if (sourceValue is Entity entity) {
                if (Engine.Scene.FindFirst(entity.GetEntityId2()) is Entity destinationEntity) {
                    destValue = destinationEntity;
                } else if (Engine.Scene.Entities.FirstOrDefault(e => e.GetType() == sourceValue.GetType()) is Entity findFirstEntity) {
                    "Can't Find the match entity, so find the first same type one".DebugLog(findFirstEntity);
                    destValue = findFirstEntity;
                }
            } else if (sourceValue is Level && Engine.Scene is Level level) {
                destValue = level;
            }

            return destValue;
        }

        public static object CreateSpecifiedType(object sourceValue) {
            object destValue = null;

            if (sourceValue.IsCompilerGenerated()) {
                destValue = sourceValue.CreateCompilerGeneratedCopy();
            } else if (sourceValue is Delegate @delegate) {
                destValue = @delegate.CloneDelegate();
            } else if (sourceValue is SoundEmitter soundEmitter) {
                destValue = SoundEmitter.Play(soundEmitter.Source.EventName, new Entity(soundEmitter.Position));
            } else if (sourceValue is Component sourceComponent) {
                // TODO Recreate Other Component
                Component destComponent = null;
                Entity sourceEntity = sourceComponent.Entity;
                Entity destEntity = Engine.Scene.FindFirst(sourceEntity?.GetEntityId2());
                if (sourceValue is SoundSource || sourceValue is BloomPoint) {
                    destComponent = (Component) FormatterServices.GetUninitializedObject(sourceValue.GetType());
                } else if (sourceValue is PlayerSprite sourcePlayerSprite) {
                    destComponent = new PlayerSprite(sourcePlayerSprite.Mode);
                } else if (sourceValue is Sprite sourceSprite) {
                    destComponent = sourceSprite.InvokeMethod("CreateClone") as Sprite;
                } else if (sourceValue is Tween sourceTween) {
                    destComponent = Tween.Create(sourceTween.Mode, sourceTween.Easer, sourceTween.Duration,
                        sourceTween.Active);
                } else if (sourceValue is Alarm sourceAlarm) {
                    destComponent = Alarm.Create(sourceAlarm.Mode, null, sourceAlarm.Duration, sourceAlarm.Active);
                }
                // TODO PlayerHair 和 PlayerSprite 是关联的，还没想清楚怎么处理

                if (destComponent != null) {
                    destEntity?.Add(destComponent);
                }

                destValue = destComponent;
            } else if (sourceValue is BadelineDummy sourceDummy) {
                // Bug BadelineBoost 解除后立即重复保存时，BadelineDummy 会残留
                Entity destEntity  = new BadelineDummy(sourceDummy.Position);
                destValue = destEntity;
                destEntity.CopyEntityId2(sourceDummy);
                Engine.Scene.Add(destEntity);
            }

            destValue?.CopySpecifiedType(sourceValue);

            return destValue;
        }


        public static object CreateOrFindSpecifiedType(this object sourceValue) {
            object destValue = FindSpecifiedType(sourceValue);
            if (destValue != null) return destValue;

            destValue = CreateSpecifiedType(sourceValue);
            return destValue;
        }


        private static object CreateCompilerGeneratedCopy(Type type) {
            if (!type.IsCompilerGenerated()) return null;
            object newObj = Activator.CreateInstance(type);
            foreach (FieldInfo fieldInfo in type.GetFields().Where(info => info.FieldType.IsCompilerGenerated())) {
                object newFieldObj = CreateCompilerGeneratedCopy(fieldInfo.FieldType);
                fieldInfo.SetValue(newObj, newFieldObj);
            }

            return newObj;
        }

        // 编译器自动生成的类型，先创建实例，最后统一复制字段
        private static object CreateCompilerGeneratedCopy(this object obj) {
            object newObj = CreateCompilerGeneratedCopy(obj.GetType());
            if (newObj == null) return null;
            newObj.CopyAllFrom(obj);
            return newObj;
        }

        private static object CloneDelegate(this Delegate @delegate) {
            object target = @delegate.Target.CreateCompilerGeneratedCopy();
            return @delegate.Method.CreateDelegate(@delegate.GetType(), target);
        }
    }
}