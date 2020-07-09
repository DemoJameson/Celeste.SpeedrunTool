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
        }
    }

    public static class ComponentExtensions {
        private static bool IsSameAs(this Component component, Component otherComponent) {
            if (component is Tween tween && otherComponent is Tween otherTween) {
                return tween.IsSameAs(otherTween);
            }
            if (component is Alarm alarm && otherComponent is Alarm otherAlarm) {
                return alarm.IsSameAs(otherAlarm);
            }

            throw new ArgumentException("Unsupported component type");
        }

        private static bool IsSameAs(this Tween tween, Tween otherTween) {
            return tween.OnStart?.Method == otherTween.OnStart?.Method &&
                   tween.OnUpdate?.Method == otherTween.OnUpdate?.Method &&
                   tween.OnComplete?.Method == otherTween.OnComplete?.Method;
        }

        private static bool IsSameAs(this Alarm component, Alarm otherComponent) {
            return component.OnComplete?.Method == otherComponent.OnComplete?.Method &&
                   component.Mode == otherComponent.Mode &&
                   Math.Abs(component.Duration - otherComponent.Duration) < 0.01;
        }

        public static void RestoreComponent<T>(this Entity loaded, Entity saved) where T : Component {
            List<T> loadedComponents = loaded.Components.GetAll<T>().ToList();
            List<T> savedComponents = saved.Components.GetAll<T>().ToList();

            // 查找相同类型的 Component 恢复状态
            foreach (T loadedComponent in loadedComponents) {
                if (savedComponents.FirstOrDefault(component => component.IsSameAs(loadedComponent)) is Component savedComponent) {
                    loadedComponent.CopySpecifiedType(savedComponent);
                    loadedComponent.DebugLog("相同类型的 Component 恢复状态", loaded);
                }
            }

            // 查找需要重新创建还原的 Component
            foreach (T savedComponent in savedComponents) {
                if (loadedComponents.Any(component => component.IsSameAs(savedComponent))) continue;
                loaded.Add((Component) savedComponent.CreateOrFindSpecifiedType());
                savedComponent.DebugLog("需要重新创建还原的 Component", loaded);
            }
        }
    }
}