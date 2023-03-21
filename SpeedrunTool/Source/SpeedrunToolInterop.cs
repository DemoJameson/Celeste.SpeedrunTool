using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool;

public static class SpeedrunToolInterop {
    [Load]
    private static void Initialize() {
        typeof(SaveLoadExports).ModInterop();
    }

    // ReSharper disable once MemberCanBePrivate.Global
    [ModExportName("SpeedrunTool.SaveLoad")]
    public static class SaveLoadExports {
        /// <summary>
        /// Specify the static members to be cloned
        /// </summary>
        /// <returns>SaveLoadAction instance, used for unregister</returns>
        public static object RegisterStaticTypes(Type type, params string[] memberNames) {
            return SaveLoadAction.SafeAdd(
                (savedValues, _) => SaveLoadAction.SaveStaticMemberValues(savedValues, type, memberNames),
                (savedValues, _) => SaveLoadAction.LoadStaticMemberValues(savedValues));
        }

        /// <summary>
        /// Unregister the SaveLoadAction return from RegisterStaticTypes()
        /// </summary>
        /// <param name="obj">The object return from RegisterStaticTypes()</param>
        public static void Unregister(object obj) {
            SaveLoadAction.Remove((SaveLoadAction)obj);
        }

        /// <summary>
        /// Ignore the entities when saving state
        /// </summary>
        /// <param name="entity">Ignored entity</param>
        /// <param name="based">The Added/Removed method of the entity will not be triggered when based is true</param>
        public static void IgnoreSaveState(Entity entity, bool based) {
            entity.Add(new IgnoreSaveLoadComponent());
        }
    }
}