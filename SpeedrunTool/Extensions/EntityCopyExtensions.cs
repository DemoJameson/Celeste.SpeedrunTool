using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.SaveLoad.Actions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class EntityCopyExtensions {
        public static void CopyAllFrom<T>(this object destObj, object sourceObj, params Type[] skipTypes) {
            CopyAllFrom(destObj, typeof(T), sourceObj, skipTypes);
        }

        public static void CopyAllFrom(this object destObj, object sourceObj, params Type[] skipTypes) {
            CopyAllFrom(destObj, destObj.GetType(), sourceObj, skipTypes);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        private static void CopyAllFrom(this object destObj, Type baseType, object sourceObj, params Type[] skipTypes) {
            if (destObj.GetType() != sourceObj.GetType()) {
                throw new ArgumentException("destObj and sourceObj not the same type.");
            }

            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 从给定的父类开始复制字段，直到 System.Object
            Type currentObjType = baseType;

            while (currentObjType.IsSubclassOf(typeof(object))) {
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
                    object destValue = destObj.GetProperty(currentObjType, memberName);
                    object sourceValue = sourceObj.GetProperty(currentObjType, memberName);

                    CopyMember(currentObjType, memberType, memberName, destObj, destValue,
                        sourceValue, SetProperty);
                }

                FieldInfo[] fields = currentObjType.GetFields(bindingFlags);
                foreach (FieldInfo fieldInfo in fields) {
                    Type memberType = fieldInfo.FieldType;

                    string memberName = fieldInfo.Name;
                    object destValue = destObj.GetField(currentObjType, memberName);
                    object sourceValue = sourceObj.GetField(currentObjType, memberName);

                    CopyMember(currentObjType, memberType, memberName, destObj, destValue, sourceValue,
                        SetField);
                }

                currentObjType = currentObjType.BaseType;
            }
        }

        private delegate void SetMember(object destObj, Type currentObjType, string memberName,
            object sourceValue);

        private static void SetProperty(object destObj, Type currentObjType, string memberName,
            object sourceValue) {
            destObj.SetProperty(currentObjType, memberName, sourceValue);
        }

        private static void SetField(object destObj, Type currentObjType, string memberName,
            object sourceValue) {
            destObj.SetField(currentObjType, memberName, sourceValue);
        }

        private static void CopyMember(Type currentObjType, Type memberType, string memberName, object destObj,
            object destValue, object sourceValue, SetMember setMember) {
            if (sourceValue == null) {
                // Component 需要从Entity中移除（适用于第六章 BOSS 在 Sprite 和 PlayerSprite 之前切换）
                if (destValue is Component component) {
                    component.RemoveSelf();
                }

                // null 也是有意义的
                setMember(destObj, currentObjType, memberName, null);
                return;
            }

            if (memberType.IsSimple()) {
                // 简单类型直接复制
                setMember(destObj, currentObjType, memberName, sourceValue);
            } else if (memberType.IsList(out Type genericType)) {
                // 列表
                // 列表为空则创建空列表
                if (destValue == null) {
                    destValue = Activator.CreateInstance(memberType);
                    setMember(destObj, currentObjType, memberName, destValue);
                }

                if (genericType.IsSimple()) {
                    // 列表里是简单数据，则清除后全部假如
                    if (destValue is IList destList && sourceValue is IList sourceList) {
                        destList.Clear();
                        foreach (object obj in sourceList) {
                            destList.Add(obj);
                        }
                    }
                } else {
                    // 列表里是复杂类型
                    // 不为空
                    if (destValue is IList destList && sourceValue is IList sourceList) {
                        if (destList.Count == sourceList.Count) {
                            // 数量一致
                            for (int i = 0; i < destList.Count; i++) {
                                CopySpecifiedType(destList[i], sourceList[i]);
                            }
                        } else if (sourceList.Count == 0) {
                            // 数量不一致时
                            // 例如 FinalBoos 的 fallingBlocks
                            destList.Clear();
                        } else if (genericType.IsSameOrSubclassOf(typeof(Entity)) &&
                                   (sourceList[0] as Entity)?.HasEntityId2() == true ||
                                   genericType == typeof(Follower)) {
                            // 确保可以通过 EntityId2 查找才进行修改
                            destList.Clear();
                            foreach (object o in sourceList) {
                                object destElement = FindOrCreateSpecifiedType(o);
                                if (destElement != null) {
                                    destList.Add(destElement);
                                }
                            }
                        } else {
                            // TODO 其他类型
                            genericType.DebugLog("TODO UnhandeTypel List element type ->",
                                $"memberName={memberName} memberType={memberType} on {currentObjType}\tsourceValue={sourceValue}");
                        }
                    }
                }
            } else if (memberType.IsArray && memberType.GetElementType() is Type elementType) {
                if (destValue is Array destArray && destArray.Rank == 1 && sourceValue is Array sourceArray &&
                    destArray.Length == sourceArray.Length) {
                    for (int i = 0; i < destArray.Length; i++) {
                        if (elementType.IsSimple()) {
                            destArray.SetValue(sourceArray.GetValue(i), i);
                        } else {
                            CopySpecifiedType(destArray.GetValue(i), sourceArray.GetValue(i));
                        }
                    }
                }
            } else if (memberType.IsHashSet(out Type hashElementType)) {
                if (destValue == null) {
                    destValue = Activator.CreateInstance(memberType);
                    setMember(destObj, currentObjType, memberName, destValue);
                }

                if (hashElementType.IsSimple()) {
                    // 列表里是简单数据，则清除后全部加入
                    destValue.InvokeMethod("Clear");
                    if (sourceValue is IEnumerable sourceEnumerable) {
                        IEnumerator enumerator = sourceEnumerable.GetEnumerator();
                        while (enumerator.MoveNext()) {
                            destValue.InvokeMethod("Add", enumerator.Current);
                        }
                    }
                } else if (hashElementType.IsSameOrSubclassOf(typeof(Entity))) {
                    // TODO Player.triggersInside Hashset<Trigger>
                }
            } else {
                // 复杂类型
                if (destValue == null) {
                    // 为空则根据情况创建新实例或者查找当前场景的实例
                    destValue = FindOrCreateSpecifiedType(sourceValue);
                    if (destValue != null) {
                        setMember(destObj, currentObjType, memberName, destValue);
                    } else {
                        destObj.DebugLog("Copy",
                            $"memberName={memberName} memberType={memberType} on {currentObjType} failed\t sourceValue={sourceValue}");
                    }
                } else {
                    // 不为空则复制里面的值
                    CopySpecifiedType(destValue, sourceValue);
                }
            }
        }

        // destValue and sourceValue always are the same type.
        public static void CopySpecifiedType(this object destValue, object sourceValue) {
            if (sourceValue.IsCompilerGenerated()) {
                destValue.CopyAllFrom(sourceValue);
            } else if (sourceValue is Component) {
                destValue.CopyAllFrom(sourceValue,
                    // CoroutineAction takes care of them 
                    typeof(Coroutine),
                    typeof(StateMachine),
                    // crash sometimes so skip it, only copy some fields later.
                    typeof(DustGraphic)
                );
                switch (destValue) {
                    case Sprite destSprite when sourceValue is Sprite sourceSprite:
                        sourceSprite.InvokeMethod("CloneInto", destSprite);
                        break;
                    case StateMachine destMachine when sourceValue is StateMachine sourceMachine:
                        // Only Set the field, CoroutineAction takes care of the rest
                        destMachine.SetField("state", sourceMachine.State);
                        break;
                    case DustGraphic destDustGraphic when sourceValue is DustGraphic sourceDustGraphic:
                        destDustGraphic.EyeDirection = sourceDustGraphic.EyeDirection;
                        destDustGraphic.EyeTargetDirection = sourceDustGraphic.EyeTargetDirection;
                        destDustGraphic.EyeFlip = sourceDustGraphic.EyeFlip;
                        break;
                    case SoundSource destSound
                        when sourceValue is SoundSource sourceSound && sourceSound.LoadPlayingValue():
                        destSound.Play(destSound.EventName);
                        destSound.SetTime(sourceSound);
                        destSound.Pause(); // 先暂停等待 Player 复活完毕再继续播放
                        SoundSourceAction.PlayingSoundSources.Add(destSound);
                        break;
                }
            } else if (destValue is Entity destEntity && sourceValue is Entity sourceEntity &&
                       sourceEntity.GetEntityId2() != destEntity.GetEntityId2()) {
                
            }
        }

        public static object FindOrCreateSpecifiedType(this object sourceValue) {
            object destValue = FindSpecifiedType(sourceValue);
            if (destValue != null) return destValue;

            destValue = CreateSpecifiedType(sourceValue);
            return destValue;
        }

        private static object FindSpecifiedType(object sourceValue) {
            object destValue = null;
            if (sourceValue is Entity entity) {
                if (Engine.Scene.FindFirst(entity.GetEntityId2()) is Entity destEntity) {
                    destValue = destEntity;
                } else if (Engine.Scene.Entities.FirstOrDefault(e => e.GetType() == sourceValue.GetType()) is Entity
                    findFirstEntity) {
                    "Can't Find the match entity, so find the first same type one".DebugLog(findFirstEntity);
                    destValue = findFirstEntity;
                }
            } else if (sourceValue is Level && Engine.Scene is Level level) {
                destValue = level;
            } else if (sourceValue is Leader) {
                destValue = Engine.Scene.GetPlayer()?.Leader;
            } else if (sourceValue is Follower savedFollower) {
                Entity followEntity = Engine.Scene.FindFirst(savedFollower.Entity.GetEntityId2());
                if (followEntity == null) return destValue;

                FieldInfo followerFieldInfo = followEntity.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(fieldInfo => fieldInfo.FieldType == typeof(Follower));
                if (followerFieldInfo?.GetValue(followEntity) is Follower follower) {
                    destValue = follower;
                }
            } else if (sourceValue is Holdable sourceHoldable) {
                destValue = (FindSpecifiedType(sourceHoldable.Entity) as Entity)?.Get<Holdable>();
            }

            return destValue;
        }

        private static object CreateSpecifiedType(object sourceValue) {
            object destValue = null;
            Type sourceType = sourceValue.GetType();

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
                if (sourceValue is SoundSource) {
                    destComponent = new SoundSource();
                } else if (sourceValue is LightOcclude) {
                    destComponent = new LightOcclude();
                } else if (sourceValue is BloomPoint sourceBloomPoint) {
                    destComponent = new BloomPoint(sourceBloomPoint.Position, sourceBloomPoint.Alpha,
                        sourceBloomPoint.Radius);
                } else if (sourceValue is PlayerSprite sourcePlayerSprite) {
                    destComponent = new PlayerSprite(sourcePlayerSprite.Mode);
                } else if (sourceValue is PlayerHair sourcePlayerHair) {
                    if (destEntity?.Get<PlayerSprite>() is PlayerSprite destPlayerSprite) {
                        destComponent = new PlayerHair(destPlayerSprite);
                    } else {
                        destPlayerSprite = new PlayerSprite(sourcePlayerHair.Sprite.Mode);
                        destPlayerSprite.CopySpecifiedType(sourcePlayerHair.Sprite);
                        destEntity?.Add(destPlayerSprite);
                        destComponent = new PlayerHair(destPlayerSprite);
                    }
                } else if (sourceValue is Sprite sourceSprite) {
                    destComponent = sourceSprite.InvokeMethod("CreateClone") as Sprite;
                } else if (sourceValue is Tween sourceTween) {
                    destComponent = Tween.Create(sourceTween.Mode, sourceTween.Easer, sourceTween.Duration,
                        sourceTween.Active);
                } else if (sourceValue is Alarm sourceAlarm) {
                    destComponent = Alarm.Create(sourceAlarm.Mode, null, sourceAlarm.Duration, sourceAlarm.Active);
                }

                if (sourceType != typeof(StateMachine) && sourceType != typeof(Coroutine)) {
                    // 尝试给未处理的类型创建实例
                    // destComponent = (Component) sourceType.ForceCreateInstance();
                }

                if (destComponent != null) {
                    destEntity?.Add(destComponent);
                }

                destValue = destComponent;
            } else if (sourceValue is MTexture) {
                destValue = new MTexture();
            }

            // 尝试给未处理的类型创建实例
            // if (destValue == null) {
            //     destValue = sourceType.ForceCreateInstance();
            // }

            destValue?.CopySpecifiedType(sourceValue);

            if (destValue == null) {
                destValue.DebugLog("Create Instance Failed", sourceValue);
            }

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
            object target = @delegate.Target.FindOrCreateSpecifiedType();
            return @delegate.Method.CreateDelegate(@delegate.GetType(), target);
        }
    }
}