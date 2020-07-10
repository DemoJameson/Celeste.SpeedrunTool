using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class ComponentRestoreAction : RestoreAction {
        public ComponentRestoreAction() : base(typeof(Entity)) { }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            loadedEntity.RestoreComponent<Tween>(savedEntity);
            loadedEntity.RestoreComponent<Alarm>(savedEntity);
            // TODO Restore More Component
        }
    }

    public static class ComponentExtensions {
        private delegate bool IsSame(Component component, Component otherComponent);

        private static readonly Dictionary<Type, IsSame> FindableComponentTypes = new Dictionary<Type, IsSame> {
            {
                typeof(Tween), delegate(Component component, Component otherComponent) {
                    Tween tween = (Tween) component;
                    Tween otherTween = (Tween) otherComponent;
                    return tween.OnStart?.Method == otherTween.OnStart?.Method &&
                           tween.OnUpdate?.Method == otherTween.OnUpdate?.Method &&
                           tween.OnComplete?.Method == otherTween.OnComplete?.Method;
                }
            }, {
                typeof(Alarm), delegate(Component component, Component otherComponent) {
                    Alarm alarm = (Alarm) component;
                    Alarm otherTween = (Alarm) otherComponent;
                    return alarm.OnComplete?.Method == otherTween.OnComplete?.Method;
                }
            },
        };

        public static bool IsFindabel<T>(this T component) where T : Component {
            return FindableComponentTypes.ContainsKey(typeof(T));
        }

        private static bool IsSameAs<T>(this T component, T otherComponent) where T : Component {
            Type type = typeof(T);
            if (!FindableComponentTypes.ContainsKey(type)) {
                throw new ArgumentException($"Component type {type} is not supported to check if is the same as other.");
            }

            return FindableComponentTypes[type].Invoke(component, otherComponent);
        }

        public static List<T> GetAll<T>(this Entity entity) where T : Component {
            return entity.Components.GetAll<T>().ToList();
        }

        public static T FindComponent<T>(this Entity entity, T component) where T : Component {
            Type type = typeof(T);
            if (!FindableComponentTypes.ContainsKey(type)) {
                throw new ArgumentException($"Component type {type} is not supported to check if is the same as other.");
            }
            List<T> components = entity.GetAll<T>();

            return components.FirstOrDefault(otherComponent => FindableComponentTypes[type].Invoke(component, otherComponent));
        }

        public static void RestoreComponent<T>(this Entity loaded, Entity saved) where T : Component {
            List<T> loadedComponents = loaded.Components.GetAll<T>().ToList();
            List<T> savedComponents = saved.Components.GetAll<T>().ToList();

            // 查找相同类型的 Component 恢复状态
            foreach (T loadedComponent in loadedComponents) {
                if (savedComponents.FirstOrDefault(component => component.IsSameAs(loadedComponent)) is Component
                    savedComponent) {
                    loadedComponent.CopySpecifiedType(savedComponent);
                }
            }

            // 查找需要重新创建还原的 Component
            foreach (T savedComponent in savedComponents) {
                if (loadedComponents.Any(component => component.IsSameAs(savedComponent))) continue;
                loaded.Add((Component) savedComponent.FindOrCreateSpecifiedType());
            }
        }
    }
}