using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.ActorActions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class EntityRestoreAction : AbstractRestoreAction {
        public static List<AbstractRestoreAction> AllRestoreActions => AllRestoreActionsLazy.Value;

        private static readonly Lazy<List<AbstractRestoreAction>> AllRestoreActionsLazy =
            new Lazy<List<AbstractRestoreAction>>(
                () => {
                    List<AbstractRestoreAction> result = new List<AbstractRestoreAction>();

                    void AddThisAndSubclassRestoreActions(AbstractRestoreAction action) {
                        result.Add(action);
                        action.SubclassRestoreActions.ForEach(AddThisAndSubclassRestoreActions);
                    }

                    AddThisAndSubclassRestoreActions(Instance);

                    return result;
                });

        private static readonly EntityRestoreAction Instance = new EntityRestoreAction(
            new List<AbstractRestoreAction> {
                // new PlayerRestoreAction(),
                // new TestPlayerRestoreAction(),

                new ActorRestoreAction(),
                // new PlatformRestoreAction(),

                // EntityActions
                new BoosterRestoreAction(),
                // new FlyFeatherRestoreAction(),
                // new KeyRestoreAction(),
                // new SpikesRestoreAction(),
                // new StrawberryRestoreAction(),
            }
        );

        private EntityRestoreAction(List<AbstractRestoreAction> subclassRestoreActions) : base(typeof(Entity),
            subclassRestoreActions) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            // loadedEntity.Active = savedEntity.Active;
            // loadedEntity.Depth = savedEntity.Depth;
            // loadedEntity.Collidable = savedEntity.Collidable;
            // loadedEntity.Collider = savedEntity.Collider;
            // loadedEntity.Position = savedEntity.Position;
            // loadedEntity.Tag = savedEntity.Tag;
            // loadedEntity.Visible = savedEntity.Visible;

            // loadedEntity.CopyAll(savedEntity, typeof(Entity));
            // return;

            if (loadedEntity is Player) {
                return;
            }

            AutoMapperUtils.GetMapper(loadedEntity.GetType()).Map(savedEntity, loadedEntity,
                savedEntity.GetType(), loadedEntity.GetType());
        }
    }


    public static class EntityRestoreExtensions {
        public static void CopyAll<T>(this T targetObj, T sourceObj, Type baseType) {
            Type type = targetObj.GetType();

            // Player 不能太早还原，需要等待复活
            if (type == typeof(Player)) {
                type = type.BaseType;
            }

            while (type.IsSameOrSubclassOf(baseType)) {
                BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                // 必须先设置属性再设置字段，不然字段的值会在设置属性后发生改变
                PropertyInfo[] properties = type.GetProperties(bindingFlags);
                foreach (PropertyInfo propertyInfo in properties) {
                    if (!propertyInfo.CanRead) continue;

                    string name = propertyInfo.Name;
                    object sourceValue = sourceObj.GetProperty(type, name);
                    Type propertyType = propertyInfo.PropertyType;

                    if (ShouldCopyDirectly(propertyType) && propertyInfo.CanWrite) {
                        targetObj.SetProperty(type, name, sourceValue);
                    } else if (sourceValue == null && propertyInfo.CanWrite) {
                        // null 的意义
                        targetObj.SetProperty(type, name, null);
                        // $"Copy Property: type={type} propertyType={propertyType} name={name} value=null".Log();
                    } else {
                        // $"Not Copy Property: type={type} propertyType={propertyType} name={name} value={value}".Log();
                        object targetValue = targetObj.GetField(type, name);
                        if (targetValue != null) {
                            CopySpecifiedType(targetValue, sourceValue);
                        } else {
                            if (propertyInfo.CanWrite) {
                                targetValue = CreateSpecifiedType(sourceValue);
                                if (targetValue != null) {
                                    targetObj.SetProperty(type, name, targetValue);
                                }
                            }
                        }
                    }
                }

                FieldInfo[] fields = type.GetFields(bindingFlags);
                foreach (FieldInfo fieldInfo in fields) {
                    Type fieldType = fieldInfo.FieldType;
                    string name = fieldInfo.Name;
                    object sourceValue = sourceObj.GetField(type, name);

                    if (ShouldCopyDirectly(fieldType)) {
                        targetObj.SetField(type, name, sourceValue);
                    } else if (sourceValue == null) {
                        // null 的意义
                        fieldInfo.SetValue(targetObj, null);
                        //     $"Copy Field: type={type} fieldType={fieldType} name={name} value=null".Log();
                    } else {
                        //  $"Not Copy Field: type={type} fieldType={fieldType} name={name} value={value}".Log();
                        object targetValue = targetObj.GetField(type, name);
                        if (targetValue != null) {
                            CopySpecifiedType(targetValue, sourceValue);
                        } else {
                            targetValue = CreateSpecifiedType(sourceValue);
                            if (targetValue != null) {
                                targetObj.SetField(type, name, targetValue);
                            }
                        }
                    }
                }

                type = type.BaseType;
            }
        }

        public static void CopySpecifiedType(this object targetValue, object sourceValue) {
            if (targetValue != null && sourceValue is Component && !(sourceValue is Coroutine) &&
                !(sourceValue is StateMachine)) {
                targetValue.CopyAll(sourceValue, typeof(Component));
            }
        }

        public static object CreateSpecifiedType(object sourceValue) {
            if (sourceValue is Entity sourceEntity &&
                Engine.Scene.FindFirst(sourceEntity.GetEntityId2()) is Entity targetEntity) {
                return targetEntity;
            }

            return null;
        }

        private static bool ShouldCopyDirectly(Type type) {
            return type.IsPrimitive || type.IsValueType || type.IsEnum || type == typeof(string);
        }
    }
}