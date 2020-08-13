using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public class IgnoreSaveLoadComponent : Component {
        public IgnoreSaveLoadComponent() : base(false, false) { }
    }

    internal static class IgnoreSaveLoadComponentExtensions {
        public static T IgnoreSaveLoad<T>(this T entity) where T : Entity {
            if (entity.Get<IgnoreSaveLoadComponent>() == null) {
                entity.Add(new IgnoreSaveLoadComponent());
            }

            return entity;
        }

        public static bool IsIgnoreSaveLoad(this Entity entity) {
            return entity.Get<IgnoreSaveLoadComponent>() != null;
        }
    }
}