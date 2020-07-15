using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class ComponentRestoreAction : RestoreAction {
        public ComponentRestoreAction() : base(typeof(Entity)) { }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            loadedEntity.RestoreComponent<Tween>(savedEntity);
            loadedEntity.RestoreComponent<Alarm>(savedEntity);
            loadedEntity.RestoreComponent<SineWave>(savedEntity);
            loadedEntity.RestoreComponent<Coroutine>(savedEntity);
        }
    }

    public static class ComponentExtensions {
        private delegate bool IsSame(Component component, Component otherComponent);

        private static readonly Dictionary<Type, IsSame> FindableComponentTypes = new Dictionary<Type, IsSame> {
            {
                typeof(Tween), (component, otherComponent) => {
                    Tween tween = (Tween) component;
                    Tween otherTween = (Tween) otherComponent;
                    return tween.OnStart?.Method == otherTween.OnStart?.Method &&
                           tween.OnUpdate?.Method == otherTween.OnUpdate?.Method &&
                           tween.OnComplete?.Method == otherTween.OnComplete?.Method &&
                           tween.Mode == otherTween.Mode
                        ;
                }
            }, {
                typeof(Alarm), (component, otherComponent) => {
                    Alarm alarm = (Alarm) component;
                    Alarm otherTween = (Alarm) otherComponent;
                    return alarm.OnComplete?.Method == otherTween.OnComplete?.Method &&
                           alarm.Mode == otherTween.Mode
                        ;
                }
            }, {
                typeof(SineWave), (component, otherComponent) => {
                    SineWave sineWave = (SineWave) component;
                    SineWave otherTween = (SineWave) otherComponent;
                    return sineWave.OnUpdate?.Method == otherTween.OnUpdate?.Method;
                }
            }, {
                typeof(Coroutine), (component, otherComponent) => {
                    Coroutine coroutine = (Coroutine) component;
                    Coroutine otherCoroutine = (Coroutine) otherComponent;

                    Stack<IEnumerator> enumerators = (Stack<IEnumerator>) coroutine.GetField("enumerators");
                    Stack<IEnumerator> otherEnumerators = (Stack<IEnumerator>) otherCoroutine.GetField("enumerators");

                    if (enumerators.Count != otherEnumerators.Count) return false;

                    List<IEnumerator> enumeratorList = enumerators.ToList();
                    List<IEnumerator> otherEnumeratorList = otherEnumerators.ToList();

                    for (int i = 0; i < enumeratorList.Count; i++) {
                        if (enumeratorList[i].GetType() != otherEnumeratorList[i].GetType()) return false;
                    }

                    return true;
                }
            },
        };

        public static void RestoreComponent<T>(this Entity loaded, Entity saved) where T : Component {
            List<T> loadedComponents = loaded.Components.GetAll<T>().ToList();
            List<T> savedComponents = saved.Components.GetAll<T>().ToList();

            // 把 loadedComponents 里不存在于 savedComponents 的 Component 都清掉
            for (var i = loadedComponents.Count - 1; i >= 0; i--) {
                T loadedComponent = loadedComponents[i];
                if (savedComponents.Any(component => loadedComponent.IsSameAs(component))) continue;

                loadedComponents.Remove(loadedComponent);
                loadedComponent.RemoveSelf();
            }

            // 查找执行相同操作的 Component 恢复状态
            foreach (T loadedComponent in loadedComponents) {
                if (savedComponents.FirstOrDefault(component => loadedComponent.IsSameAs(component)) is Component
                    savedComponent) {
                    loadedComponent.TryCopyObject(savedComponent);
                }
            }

            // 查找需要重新创建还原的 Component
            foreach (T savedComponent in savedComponents) {
                if (loadedComponents.Any(component => savedComponent.IsSameAs(component))) continue;
                Component loadedComponent = savedComponent.TryFindOrCloneObject() as Component;
                if (loadedComponent == null) continue;
                loaded.Add(loadedComponent);
            }
        }

        public static bool IsFindabel(this Component component) {
            return FindableComponentTypes.ContainsKey(component.GetType());
        }

        private static bool IsSameAs<T>(this T component, T otherComponent) where T : Component {
            Type type = typeof(T);
            if (!FindableComponentTypes.ContainsKey(type)) {
                throw new ArgumentException(
                    $"Component type {type} is not supported to check if is the same as other.");
            }

            return FindableComponentTypes[type].Invoke(component, otherComponent);
        }

        public static Component FindComponent(this Entity entity, Component component) {
            Type type = component.GetType();
            if (!FindableComponentTypes.ContainsKey(type)) {
                throw new ArgumentException(
                    $"Component type {type} is not supported to check if is the same as other.");
            }

            return entity.Components.FirstOrDefault(otherComponent =>
                otherComponent.GetType() == type && FindableComponentTypes[type].Invoke(component, otherComponent));
        }
    }
}