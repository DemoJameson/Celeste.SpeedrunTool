using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions;
using Monocle;
using EventInstance = FMOD.Studio.EventInstance;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public static class EntityCopyCore {
        private static readonly HashSet<object> CopyingObjects = new HashSet<object>();
        private static readonly Dictionary<object, object> CreatedObjectsDict = new Dictionary<object, object>();

        public static void ClearCachedObjects() {
            CopyingObjects.Clear();
            CreatedObjectsDict.Clear();
        }

        public static void CopyAllFrom<T>(this object destObj, object sourceObj, bool onlySimpleType = false) {
            CopyAllFrom(destObj, typeof(T), sourceObj, onlySimpleType);
        }

        public static void CopyAllFrom(this object destObj, object sourceObj, bool onlySimpleType = false) {
            CopyAllFrom(destObj, destObj.GetType(), sourceObj, onlySimpleType);
        }

        private static void CopyAllFrom(this object destObj, Type baseType, object sourceObj,
            bool onlySimpleType = false) {
            if (destObj.GetType() != sourceObj.GetType()) {
                throw new ArgumentException("destObj and sourceObj not the same type.");
            }

            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 从给定的父类开始复制字段，直到 System.Object
            Type currentObjType = baseType;

            if (CopyingObjects.Contains(destObj)) {
                // StackOverflow Exception is watching you.
                destObj.DebugLog("Prevents copying of an object that is being copied");
                return;
            }

            CopyingObjects.Add(destObj);
            while (currentObjType.IsSubclassOf(typeof(object))) {
                // 必须先设置属性再设置字段，不然字段的值会在设置属性后发生改变
                PropertyInfo[] properties = currentObjType.GetProperties(bindingFlags);
                foreach (PropertyInfo propertyInfo in properties) {
                    // 只处理能读取+写入的属性
                    if (!propertyInfo.CanRead || !propertyInfo.CanWrite) continue;

                    Type memberType = propertyInfo.PropertyType;
                    if (!memberType.IsSimple() && onlySimpleType) continue;

                    string memberName = propertyInfo.Name;
                    object destValue = destObj.GetProperty(currentObjType, memberName);
                    object sourceValue = sourceObj.GetProperty(currentObjType, memberName);

                    CopyMember(currentObjType, memberType, memberName, destObj, destValue,
                        sourceValue, SetProperty);
                }

                FieldInfo[] fields = currentObjType.GetFields(bindingFlags);
                foreach (FieldInfo fieldInfo in fields) {
                    Type memberType = fieldInfo.FieldType;
                    if (!memberType.IsSimple() && onlySimpleType) continue;

                    string memberName = fieldInfo.Name;
                    object destValue = destObj.GetField(currentObjType, memberName);
                    object sourceValue = sourceObj.GetField(currentObjType, memberName);

                    CopyMember(currentObjType, memberType, memberName, destObj, destValue, sourceValue,
                        SetField);
                }

                currentObjType = currentObjType.BaseType;
            }

            CopyingObjects.Remove(destObj);
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
            if (destValue == sourceValue) return;

            // Logger.Log("SpeedrunTool", $"currentObjType={currentObjType}\tmemberType={memberType}\tmemberName={memberName}\tdestValue={destValue}\tsourceValue={sourceValue}\tdestObj={destObj}");

            if (sourceValue == null) {
                // Component 需要从Entity中移除（适用于第六章 Boss 会在 Sprite 和 PlayerSprite 之间切换，使字段变为 null）
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
                bool destValueIsNull = destValue == null;
                destValue = CopyList(destValue, sourceValue, genericType);
                if (destValueIsNull) {
                    setMember(destObj, currentObjType, memberName, destValue);
                }
            } else if (memberType.IsArray && memberType.GetElementType() is Type elementType) {
                // 一维数组且数量相同
                if (destValue is Array destArray && destArray.Rank == 1 && sourceValue is Array sourceArray &&
                    destArray.Length == sourceArray.Length) {
                    for (int i = 0; i < destArray.Length; i++) {
                        if (elementType.IsSimple()) {
                            destArray.SetValue(sourceArray.GetValue(i), i);
                        } else {
                            TryCopyObject(destArray.GetValue(i), sourceArray.GetValue(i));
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
                    // Player.triggersInside Hashset<Trigger>
                    destValue.InvokeMethod("Clear");
                    if (sourceValue is IEnumerable sourceEnumerable) {
                        IEnumerator enumerator = sourceEnumerable.GetEnumerator();
                        while (enumerator.MoveNext() &&
                               enumerator.Current.TryFindOrCloneObject() is Entity entity) {
                            destValue.InvokeMethod("Add", entity);
                        }
                    }
                }
            } else if (destValue is Stack<IEnumerator> destEnumerators &&
                       sourceValue is Stack<IEnumerator> sourceEnumerators) {
                // 用于恢复 Coroutine
                Stack<IEnumerator> temp = new Stack<IEnumerator>();
                foreach (IEnumerator functionCall in sourceEnumerators) {
                    if (TryFindOrCloneObject(functionCall) is IEnumerator destFunctionCall) {
                        temp.Push(destFunctionCall);
                    }
                }

                destEnumerators.Clear();
                foreach (IEnumerator enumerator in temp) {
                    destEnumerators.Push(enumerator);
                }
            } else {
                // 复杂类型
                if (destValue != null) {
                    // 不为空则复制里面的值
                    TryCopyObject(destValue, sourceValue);
                } else {
                    // 为空则根据情况创建新实例或者查找当前场景的实例S
                    destValue = TryFindOrCloneObject(sourceValue);
                    if (destValue != null) {
                        setMember(destObj, currentObjType, memberName, destValue);
                    } else {
                        Logger.Log("SpeedrunTool",
                            $"TryFindOrCloneObject Faild: currentObjType={currentObjType}\tmemberType={memberType}\tmemberName={memberName}\tdestValue=null\tsourceValue={sourceValue}\tdestObj={destObj}");
                    }
                }
            }
        }

        private static object CopyList(object destValue, object sourceValue, Type genericType) {
            // 列表
            // 列表为空则创建空列表
            if (destValue == null) {
                destValue = Activator.CreateInstance(sourceValue.GetType());
            }

            if (!(destValue is IList destList) || !(sourceValue is IList sourceList)) {
                return destValue;
            }

            if (genericType.IsSimple()) {
                // 列表里是简单数据，则清除后全部假如
                destList.Clear();
                foreach (object obj in sourceList) {
                    destList.Add(obj);
                }
            } else {
                // 列表里是复杂类型
                if (destList.Count == sourceList.Count) {
                    // 数量一致
                    for (int i = 0; i < destList.Count; i++) {
                        TryCopyObject(destList[i], sourceList[i]);
                    }
                } else {
                    // 数量不一致时
                    // 例如 FinalBoos 的 fallingBlocks
                    destList.Clear();
                    foreach (object o in sourceList) {
                        object destElement = TryFindOrCloneObject(o);

                        if (destElement != null) {
                            destList.Add(destElement);
                        }
                    }
                }
            }

            return destValue;
        }

        public static void TryCopyObject(this object destValue, object sourceValue) {
            if (sourceValue.IsCompilerGenerated()) {
                destValue.CopyAllFrom(sourceValue);
            } else if (sourceValue is Component) {
                if (sourceValue is StateMachine || // only copy some fields later
                    sourceValue is DustGraphic || // sometimes game crash after savestate
                    sourceValue is VertexLight // switch between room will make light disappear
                ) {
                    destValue.CopyAllFrom<Component>(sourceValue);
                } else {
                    destValue.CopyAllFrom(sourceValue);
                }

                switch (destValue) {
                    case StateMachine destMachine when sourceValue is StateMachine sourceMachine:
                        object destCoroutine = destMachine.GetField("currentCoroutine");
                        object sourceCoroutine = sourceMachine.GetField("currentCoroutine");
                        destCoroutine.CopyAllFrom(sourceCoroutine);
                        destMachine.SetField("state", sourceMachine.State);
                        break;
                    case DustGraphic destDustGraphic when sourceValue is DustGraphic sourceDustGraphic:
                        destDustGraphic.EyeDirection = sourceDustGraphic.EyeDirection;
                        destDustGraphic.EyeTargetDirection = sourceDustGraphic.EyeTargetDirection;
                        destDustGraphic.EyeFlip = sourceDustGraphic.EyeFlip;
                        break;
                    case VertexLight destLight when sourceValue is VertexLight sourceLight:
                        destLight.Alpha = sourceLight.Alpha;
                        destLight.Position = sourceLight.Position;
                        destLight.Color = sourceLight.Color;
                        destLight.StartRadius = sourceLight.StartRadius;
                        destLight.EndRadius = sourceLight.EndRadius;
                        break;
                    case Sprite destSprite when sourceValue is Sprite sourceSprite:
                        sourceSprite.InvokeMethod("CloneInto", destSprite);
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
                sourceEntity.DebugLog("entity have different EntityId2");
            }
        }

        public static object TryFindOrCloneObject(this object sourceValue) {
            object destValue = TryFindObject(sourceValue);
            if (destValue != null) return destValue;

            destValue = TryCloneObject(sourceValue);

            if (destValue == null) {
                sourceValue.DebugLog("FindOrCreateSpecifiedType Failed:");
            }

            return destValue;
        }

        private static object TryFindObject(object sourceValue) {
            object destValue = null;
            if (sourceValue is Delegate @delegate && @delegate.Target == null) {
                // If mod Hook a method return IEnumerator will produce something like this.
                // Still not know much about delegate, maybe because the delegate method orig is static.
                destValue = sourceValue;
            } else if (sourceValue is Level) {
                destValue = Engine.Scene.GetLevel();
            } else if (sourceValue is Session) {
                destValue = Engine.Scene.GetSession();
            } else if (sourceValue is Entity entity) {
                if (sourceValue is SolidTiles) {
                    destValue = Engine.Scene.GetLevel()?.SolidTiles;
                } else if (Engine.Scene.FindFirst(entity.GetEntityId2()) is Entity destEntity) {
                    destValue = destEntity;
                }
            } else if (sourceValue is Leader) {
                destValue = Engine.Scene.GetPlayer()?.Leader;
            } else if (sourceValue is Follower savedFollower) {
                Entity followEntity = Engine.Scene.FindFirst(savedFollower.Entity.GetEntityId2());
                if (followEntity == null) {
                    savedFollower.Entity.DebugLog("Can't find the follower entity");
                    return null;
                }

                FieldInfo followerFieldInfo = followEntity.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(fieldInfo => fieldInfo.FieldType == typeof(Follower));
                if (followerFieldInfo?.GetValue(followEntity) is Follower follower) {
                    destValue = follower;
                }
            } else if (sourceValue is Holdable sourceHoldable) {
                destValue = (TryFindObject(sourceHoldable.Entity) as Entity)?.Get<Holdable>();
            } else if (sourceValue is Component sourceComponent && sourceComponent.Entity != null &&
                       TryFindObject(sourceComponent.Entity) is Entity componentEntity) {
                if (sourceComponent.IsFindabel() &&
                    componentEntity.FindComponent(sourceComponent) is Component foundComponent) {
                    sourceValue.DebugLog("Find a component instead of recreating a new one");
                    destValue = foundComponent;
                }
            }

            return destValue;
        }

        private static object TryCloneObject(object sourceValue) {
            if (CreatedObjectsDict.ContainsKey(sourceValue)) {
                return CreatedObjectsDict[sourceValue];
            }

            object destValue = null;
            Type sourceType = sourceValue.GetType();

            if (sourceValue.IsCompilerGenerated()) {
                destValue = sourceValue.CreateCompilerGeneratedCopy();
            } else if (sourceValue is Delegate @delegate) {
                destValue = @delegate.CloneDelegate();
            } else if (sourceValue is Stopwatch) {
                destValue = new Stopwatch();
            } else if (sourceValue is EventInstance eventInstance) {
                destValue = eventInstance.Clone();
            } else if (sourceValue is Entity sourceEntity) {
                // 还原 Coroutine 时有些 Entity 创建了而未被添加到 Level 中，所以他们无法在 EntitiesSavedButNotLoaded 中还原，所以在这里克隆一个添加进去
                destValue = RestoreAction.CreateEntityCopy(sourceEntity, "FindSpecifiedType");
            } else if (sourceValue is Component sourceComponent) {
                Component destComponent = null;
                Entity sourceComponentEntityEntity = sourceComponent.Entity;
                Entity destEntity = Engine.Scene.FindFirst(sourceComponentEntityEntity?.GetEntityId2());
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
                        destPlayerSprite.TryCopyObject(sourcePlayerHair.Sprite);
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
                } else if (sourceValue is VertexLight) {
                    destComponent = new VertexLight();
                } else if (sourceValue is Image sourceImage) {
                    destComponent = new Image(sourceImage.Texture);
                    return destComponent;
                } else if (sourceValue is Coroutine sourceCoroutine) {
                    destComponent = new Coroutine(sourceCoroutine.RemoveOnComplete);
                } else if (sourceValue is Follower sourceFollower) {
                    destComponent = new Follower(sourceFollower.ParentEntityID);
                }

                if (destComponent == null) {
                    destComponent = sourceValue.ForceCreateInstance("CreateSpecifiedType Component") as Component;
                }

                if (destComponent != null) {
                    destEntity?.Add(destComponent);
                }

                destValue = destComponent;
            } else if (sourceValue is MTexture) {
                destValue = sourceValue;
            } else if (sourceType.IsList(out Type genericType)) {
                destValue = CopyList(null, sourceValue, genericType);
            }

            // 尝试给未处理的类型创建实例
            if (destValue == null) {
                destValue = sourceValue.ForceCreateInstance("CreateSpecifiedType End");
            }

            if (destValue != null) {
                CreatedObjectsDict[sourceValue] = destValue;
            }

            destValue?.TryCopyObject(sourceValue);

            return destValue;
        }


        private static object CreateCompilerGeneratedCopy(Type type) {
            if (!type.IsCompilerGenerated()) return null;
            // Delegate
            object newObj = type.GetConstructor(new Type[] { })?.Invoke(new object[] { });

            // Coroutine
            if (newObj == null) {
                newObj = type.GetConstructor(new[] {typeof(int)})?.Invoke(new object[] {0});
            }

            if (newObj == null) {
                type.DebugLog("CreateCompilerGeneratedCopy Failed:");
                return null;
            }

            foreach (FieldInfo fieldInfo in type.GetFields().Where(info => info.FieldType.IsCompilerGenerated())) {
                object newFieldObj = type == fieldInfo.FieldType
                    ? newObj
                    : CreateCompilerGeneratedCopy(fieldInfo.FieldType);
                fieldInfo.SetValue(newObj, newFieldObj);
            }

            return newObj;
        }

        // 编译器自动生成的类型，先创建实例，最后统一复制字段
        private static object CreateCompilerGeneratedCopy(this object obj) {
            return CreateCompilerGeneratedCopy(obj.GetType());
        }

        private static object CloneDelegate(this Delegate @delegate) {
            if (@delegate.Target == null) return null;
            object target = @delegate.Target.TryFindOrCloneObject();
            return @delegate.Method.CreateDelegate(@delegate.GetType(), target);
        }
    }
}