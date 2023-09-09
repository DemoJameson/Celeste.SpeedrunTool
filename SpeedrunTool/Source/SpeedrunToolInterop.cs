using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using MonoMod.ModInterop;

namespace Celeste.Mod.SpeedrunTool;

public static class SpeedrunToolInterop {
    private static readonly List<Func<Type, bool>> returnSameObjectPredicates = new ();
    private static readonly List<Func<object, object>> customDeepCloneProcessors = new ();

    internal static bool CanReturnSameObject(Type type) {
        return returnSameObjectPredicates.Any(predicate => predicate(type));
    }

    internal static object CustomDeepCloneObject(object sourceObject) {
        foreach (Func<object,object> processor in customDeepCloneProcessors) {
            if (processor.Invoke(sourceObject) is { } clonedObject) {
                return clonedObject;
            }
        }

        return null;
    }

    [Load]
    private static void Initialize() {
        typeof(SaveLoadExports).ModInterop();
    }

    // ReSharper disable once MemberCanBePrivate.Global
    [ModExportName("SpeedrunTool.SaveLoad")]
    public static class SaveLoadExports {
        /// <summary>
        /// Register SaveLoadAction
        /// </summary>
        /// <param name="saveState"></param>
        /// <param name="loadState"></param>
        /// <param name="clearState"></param>
        /// <param name="beforeSaveState"></param>
        /// <param name="beforeLoadState"></param>
        /// <param name="preCloneEntities"></param>
        /// <returns>SaveLoadAction instance, used for unregister</returns>
        public static object RegisterSaveLoadAction(Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
            Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState, Action clearState,
            Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities) {
            return SaveLoadAction.SafeAdd(saveState, loadState, clearState, beforeSaveState, beforeLoadState, preCloneEntities);
        }

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
        /// Unregister the SaveLoadAction return from RegisterStaticTypes()/RegisterSaveLoadAction()
        /// </summary>
        /// <param name="obj">The object return from RegisterStaticTypes()/RegisterSaveLoadAction()</param>
        public static void Unregister(object obj) {
            SaveLoadAction.Remove((SaveLoadAction)obj);
        }

        /// <summary>
        /// Ignore the entities when saving state. They will be removed before saving state and then added into level after loading state.
        /// </summary>
        /// <param name="entity">Ignored entity</param>
        /// <param name="based">The Added/Removed method of the entity will not be triggered when based is true</param>
        public static void IgnoreSaveState(Entity entity, bool based) {
            entity.Add(new IgnoreSaveLoadComponent());
        }

        /// <summary>
        /// Determine which types need to return the same object when deep cloning.
        /// </summary>
        /// <param name="predicate">Returning true means that an object of the type returns the same object when cloned.</param>
        public static void AddReturnSameObjectProcessor(Func<Type, bool> predicate) {
            returnSameObjectPredicates.Add(predicate);
        }

        /// <summary>
        /// Remove previously added predicate.
        /// </summary>
        /// <param name="predicate"></param>
        public static void RemoveReturnSameObjectProcessor(Func<Type, bool> predicate) {
            returnSameObjectPredicates.Remove(predicate);
        }

        /// <summary>
        /// Customize the cloning process.
        /// </summary>
        /// <param name="processor">Return the original object directly or construct it by yourself, return null for normal cloning</param>
        public static void AddCustomDeepCloneProcessor(Func<object, object> processor) {
            customDeepCloneProcessors.Add(processor);
        }

        /// <summary>
        /// Remove previously added processor.
        /// </summary>
        /// <param name="processor"></param>
        public static void RemoveCustomDeepCloneProcessor(Func<object, object> processor) {
            customDeepCloneProcessors.Remove(processor);
        }
    }
}