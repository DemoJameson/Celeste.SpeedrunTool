using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public static class EntityRestoreExtensions {
        public static void CopyAllFrom(this object destinationObj, object sourceObj, Type baseType) {
            destinationObj.CopyAllFrom(destinationObj.GetType(), sourceObj, baseType);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static void CopyAllFrom(this object destinationObj, Type type, object sourceObj, Type baseType) {
            if (destinationObj.GetType() != sourceObj.GetType()) {
                throw new ArgumentException("destinationObj and sourceObj must be the same type.");
            }
            
            while (type.IsSameOrSubclassOf(baseType)) {
                BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                // 必须先设置属性再设置字段，不然字段的值会在设置属性后发生改变
                PropertyInfo[] properties = type.GetProperties(bindingFlags);
                foreach (PropertyInfo propertyInfo in properties) {
                    if (!propertyInfo.CanRead) continue;

                    Type memberType = propertyInfo.PropertyType;

                    string name = propertyInfo.Name;
                    object sourceValue = sourceObj.GetProperty(type, name);

                    if (sourceValue == null && propertyInfo.CanWrite) {
                        destinationObj.SetProperty(type, name, null);
                        continue;
                    }

                    if (IsSimpleType(memberType) && propertyInfo.CanWrite) {
                        destinationObj.SetProperty(type, name, sourceValue);
                    } else if (IsListType(memberType, out Type genericType)) {
                        if (IsSimpleType(genericType)) {
                            object destinationValue = destinationObj.GetField(name);
                            if (destinationValue == null && !propertyInfo.CanWrite) {
                                continue;
                            }

                            if (destinationValue == null) {
                                destinationValue = Activator.CreateInstance(memberType);
                                destinationObj.SetProperty(type, name, destinationValue);
                            }

                            if (destinationValue is IList destinationList && sourceValue is IList sourceList) {
                                destinationList.Clear();
                                foreach (object obj in sourceList) {
                                    destinationList.Add(obj);
                                }
                            }
                        }
                    } else {
                        object destinationValue = destinationObj.GetField(type, name);
                        if (destinationValue != null) {
                            CopySpecifiedType(destinationValue, sourceValue);
                        } else if (propertyInfo.CanWrite) {
                            destinationValue = CreateSpecifiedType(sourceValue);
                            if (destinationValue != null) {
                                destinationObj.SetProperty(type, name, destinationValue);
                            }
                        }
                    }
                }

                FieldInfo[] fields = type.GetFields(bindingFlags);
                foreach (FieldInfo fieldInfo in fields) {
                    Type memberType = fieldInfo.FieldType;

                    string name = fieldInfo.Name;
                    object sourceValue = sourceObj.GetField(type, name);

                    if (sourceValue == null) {
                        // null 也是有意义的
                        destinationObj.SetField(name, null);
                        continue;
                    }

                    // 基本类型
                    if (IsSimpleType(memberType)) {
                        destinationObj.SetField(type, name, sourceValue);
                    } else if (IsListType(memberType, out Type genericType)) {
                        // 列表
                        if (IsSimpleType(genericType)) {
                            object destinationValue = destinationObj.GetField(name);
                            if (destinationValue == null) {
                                destinationValue = Activator.CreateInstance(memberType);
                                destinationObj.SetField(name, destinationValue);
                            }

                            if (destinationValue is IList destinationList && sourceValue is IList sourceList) {
                                destinationList.Clear();
                                foreach (object obj in sourceList) {
                                    destinationList.Add(obj);
                                }
                            }
                        }
                    } else {
                        // 对象
                        object destinationValue = destinationObj.GetField(type, name);
                        if (destinationValue != null) {
                            CopySpecifiedType(destinationValue, sourceValue);
                        } else {
                            destinationValue = CreateSpecifiedType(sourceValue);
                            if (destinationValue != null) {
                                destinationObj.SetField(type, name, destinationValue);
                            }
                        }
                    }
                }

                type = type.BaseType;
            }
        }

        // destinationValue and sourceValue are the same type.
        private static void CopySpecifiedType(this object destinationValue, object sourceValue) {
            if (destinationValue is StateMachine dest && sourceValue is StateMachine source) {
                // Only Set the field, CoroutineAction takes care of the rest
                dest.SetField("state", source.State);
            }
            
            if (sourceValue is Component && !(sourceValue is Coroutine) && !(sourceValue is StateMachine)) {
                destinationValue.CopyAllFrom(destinationValue.GetType(), sourceValue, typeof(Component));
                if (destinationValue is Sprite destinationSprite && sourceValue is Sprite sourceSprite) {
                    sourceSprite.InvokeMethod("CloneInto", destinationSprite);
                }
            }
        }

        private static object CreateSpecifiedType(object sourceValue) {
            if (sourceValue is Entity sourceEntity &&
                Engine.Scene.FindFirst(sourceEntity.GetEntityId2()) is Entity destinationEntity) {
                return destinationEntity;
            }

            return null;
        }

        public static bool IsSimpleType(this Type type) {
            return type.IsPrimitive || type.IsValueType || type.IsEnum || type == typeof(string);
        }

        private static bool IsListType(Type type, out Type genericType) {
            bool result = type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(List<>))
                                             && type.GenericTypeArguments.Length == 1;

            genericType = result ? type.GenericTypeArguments[0] : null;

            return result;
        }
    }
}