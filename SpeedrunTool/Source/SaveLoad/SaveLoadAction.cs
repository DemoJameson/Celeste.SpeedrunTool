using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.SaveLoad.ThirdPartySupport;
using Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
using Celeste.Mod.SpeedrunTool.Utils;
using FMOD.Studio;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed partial class SaveLoadAction {

    public delegate void SlAction(Dictionary<Type, Dictionary<string, object>> savedValues, Level level);

    // 核心的一些 SaveLoadAction 在第一次 SL 时才初始化，避免通过安装依赖功能解除禁用的 Mod 被忽略.

    private static bool internalActionInitialized = false;

    private static bool needInitializeDictionaryId = false;
    private static bool slotInitialized {
        get => SaveSlotsManager.Slot.ValueDictionaryInitialized;
        set {
            SaveSlotsManager.Slot.ValueDictionaryInitialized = value;
        }
    }
    public static List<EventInstance> ClonedEventInstancesWhenSave => SaveSlotsManager.Slot.ClonedEventInstancesWhenSave;
    public static List<EventInstance> ClonedEventInstancesWhenPreClone => SaveSlotsManager.Slot.ClonedEventInstancesWhenPreClone;

    // only actions, no values stored. Share among all save slots
    private static List<SaveLoadAction> SharedActions = [];

    // values, belong to each save slot

    internal static Dictionary<int, Dictionary<Type, Dictionary<string, object>>> AllSavedValues {
        get => SaveSlotsManager.Slot.AllSavedValues;
        set {
            SaveSlotsManager.Slot.AllSavedValues = value;
        }
    }

    internal static Dictionary<int, Dictionary<Type, Dictionary<string, object>>> InitValueDictionary() {
        Dictionary<int, Dictionary<Type, Dictionary<string, object>>> dict = [];
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            dict[saveLoadAction.dictionaryId] = [];
        }
        return dict;
    }

    private static Dictionary<Type, FieldInfo[]> simpleStaticFields;
    private static Dictionary<Type, FieldInfo[]> modModuleFields;

#if DEBUG
    public string ActionDescription = "";
#endif

    private int dictionaryId;

    private readonly Action clearState;
    private readonly Action<Level> beforeSaveState;
    private readonly Action<Level> beforeLoadState;
    private readonly Action preCloneEntities;
    internal readonly SlAction loadState;
    private readonly SlAction saveState;
    private int executeOrder;

    private static int? contextOrder = null;
    private const int Order_InternalCoreAction = 0;
    private const int Order_InternalOtherAction = 100;
    private const int Order_ExternalAction = 10000;
    private const int Order_ExecuteAtLast = 1000000;

    /// <summary>
    /// Bumped whenever the set/order of registered actions changes. A slot whose values were
    /// captured under a different version cannot be loaded safely: dictionary ids are
    /// positional, so the old values would be replayed into different actions.
    /// </summary>
    internal static int ActionsVersion { get; private set; }

    public SaveLoadAction(SlAction saveState = null, SlAction loadState = null, Action clearState = null,
        Action<Level> beforeSaveState = null, Action preCloneEntities = null) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.preCloneEntities = preCloneEntities;
        needInitializeDictionaryId = true;
    }

    public SaveLoadAction(SlAction saveState, SlAction loadState, Action clearState,
        Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.beforeLoadState = beforeLoadState;
        this.preCloneEntities = preCloneEntities;
        needInitializeDictionaryId = true;
    }

    // used by CelesteTAS
    [Obsolete("crash on macOS if speedrun tool is not installed, use SafeAdd() instead")]
    public static void Add(SaveLoadAction saveLoadAction) {
        SharedActions.Add(saveLoadAction);
    }

    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once UnusedMethodReturnValue.Global

    internal static SaveLoadAction InternalSafeAdd(Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState = null,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState = null, Action clearState = null,
        Action<Level> beforeSaveState = null, Action<Level> beforeLoadState = null, Action preCloneEntities = null, int skipFrames = 2) {
        if (internalActionInitialized) {
            throw new Exception("Internal SaveLoad actions are added after expected time!");
        }
        SaveLoadAction saveLoadAction = new(CreateSlAction(saveState), CreateSlAction(loadState), clearState, beforeSaveState, beforeLoadState, preCloneEntities);
        saveLoadAction.executeOrder = contextOrder.GetValueOrDefault(Order_InternalOtherAction);
#if DEBUG
        AddDebugDescription(saveLoadAction, internalCall: true, skipFrame: skipFrames);
#endif
        SharedActions.Add(saveLoadAction);
        return saveLoadAction;
    }

    private static SlAction CreateSlAction(Action<Dictionary<Type, Dictionary<string, object>>, Level> action) {
        return (SlAction)action?.Method.CreateDelegate(typeof(SlAction), action.Target);
    }

    /// <summary>
    /// For third party mods
    /// </summary>
    public static object SafeAdd(Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState, Action clearState,
        Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities = null) {
        SaveLoadAction saveLoadAction = new(CreateSlAction(saveState), CreateSlAction(loadState), clearState, beforeSaveState, beforeLoadState, preCloneEntities);
        saveLoadAction.executeOrder = Order_ExternalAction;
#if DEBUG
        AddDebugDescription(saveLoadAction, internalCall: false, skipFrame: 3);
#endif
        SharedActions.Add(saveLoadAction);
        return saveLoadAction;
    }

#if DEBUG
    private static void AddDebugDescription(SaveLoadAction action, bool internalCall, int skipFrame) {
        System.Diagnostics.StackFrame stackFrame = new System.Diagnostics.StackTrace(skipFrame).GetFrame(0);
        if (stackFrame.GetMethod() is { } method) {
            if (internalCall) {
                action.ActionDescription = $"{method.DeclaringType?.FullName?.
                    Replace("Celeste.Mod.SpeedrunTool.SaveLoad.", "")?.
                    Replace("Celeste.Mod.SpeedrunTool.", "") ?? "<UnknownType>"} @ {method.Name ?? "<UnknownMethod>"}";
            }
            else {
                action.ActionDescription = $"<<External>> {method.DeclaringType?.FullName ?? "<UnknownType>"} @ {method.Name ?? "<UnknownMethod>"}";
            }
        }
        else {
            action.ActionDescription = "<BadStackFrame>";
        }
    }
#endif

    /// <summary>
    /// For third party mods
    /// </summary>
    public static bool Remove(SaveLoadAction saveLoadAction) {
        bool b = SharedActions.Remove(saveLoadAction);
        if (b) {
            needInitializeDictionaryId = true;
        }
        return b;
    }

    internal static void Remove(Func<SaveLoadAction, bool> predicate) {
        List<SaveLoadAction> toRemove = [];
        foreach (SaveLoadAction action in SharedActions) {
            if (predicate(action)) {
                toRemove.Add(action);
            }
        }
        if (toRemove is not null) {
            foreach (SaveLoadAction action in toRemove) {
                Remove(action);
            }
        }
    }

    internal static void OnSaveState(Level level) {
        Dictionary<int, Dictionary<Type, Dictionary<string, object>>> dict = AllSavedValues ??= InitValueDictionary();
        EnsureValueDictionaries(dict);
#if DEBUG
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            sw.Restart();
            var before = DebugTool.MemoryTracker.TakeGcSample();
            saveLoadAction.saveState?.Invoke(dict[saveLoadAction.dictionaryId], level);
            if (sw.ElapsedMilliseconds > 10 && EachAction_Profilling) {
                Logger.Debug($"SpeedrunTool/Save",
                    $"{sw.ElapsedMilliseconds,3} ms, " +
                    $"{DebugTool.MemoryTracker.FormatDiff(before, DebugTool.MemoryTracker.TakeGcSample(), simplified: true)},    " +
                    $"[{saveLoadAction.dictionaryId}] {saveLoadAction.ActionDescription}");
            }
        }
#else
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.saveState?.Invoke(dict[saveLoadAction.dictionaryId], level);
        }
#endif
    }

    internal static void OnLoadState(Level level) {
        Dictionary<int, Dictionary<Type, Dictionary<string, object>>> dict = AllSavedValues ??= InitValueDictionary();
        if (!EnsureValueDictionaries(dict)) {
            Logger.Warn("SpeedrunTool/SaveLoadAction",
                "Some save/load actions were registered after this state was saved; loading it may be incomplete.");
        }
#if DEBUG
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            sw.Restart();
            var before = DebugTool.MemoryTracker.TakeGcSample();
            saveLoadAction.loadState?.Invoke(dict[saveLoadAction.dictionaryId], level);
            if (sw.ElapsedMilliseconds > 10 && EachAction_Profilling) {
                Logger.Debug($"SpeedrunTool/Load",
                    $"{sw.ElapsedMilliseconds,3} ms, " +
                    $"{DebugTool.MemoryTracker.FormatDiff(before, DebugTool.MemoryTracker.TakeGcSample(), simplified: true)},    " +
                    $"[{saveLoadAction.dictionaryId}] {saveLoadAction.ActionDescription}");
            }
        }
#else
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.loadState?.Invoke(dict[saveLoadAction.dictionaryId], level);
        }
#endif
    }

    /// <summary>
    /// Makes sure every currently registered action has a value dictionary for this slot.
    /// Returns false when entries had to be created (i.e. the slot was saved with a different
    /// set of actions, which happens if a mod adds/removes actions at runtime).
    /// </summary>
    private static bool EnsureValueDictionaries(Dictionary<int, Dictionary<Type, Dictionary<string, object>>> dict) {
        bool complete = true;
        foreach (SaveLoadAction action in SharedActions) {
            if (!dict.ContainsKey(action.dictionaryId)) {
                dict[action.dictionaryId] = [];
                complete = false;
            }
        }
        return complete;
    }

    /// <summary>
    /// Whether the current slot holds a value dictionary for every registered action, captured
    /// under the current action layout.
    /// </summary>
    internal static bool AreValueDictionariesComplete() {
        Dictionary<int, Dictionary<Type, Dictionary<string, object>>> dict = AllSavedValues;
        if (dict is null || !SaveSlotsManager.Slot.ValueDictionaryInitialized) {
            return false;
        }
        if (SaveSlotsManager.Slot.AllSavedValuesVersion != ActionsVersion) {
            return false;
        }
        foreach (SaveLoadAction action in SharedActions) {
            if (!dict.ContainsKey(action.dictionaryId)) {
                return false;
            }
        }
        return true;
    }

    internal static void OnClearState(bool clearBeforeSave = false) {
        if (clearBeforeSave) {
            Dictionary<int, Dictionary<Type, Dictionary<string, object>>> dict = AllSavedValues;
            foreach (SaveLoadAction saveLoadAction in SharedActions) {
                foreach (KeyValuePair<Type, Dictionary<string, object>> pair in dict[saveLoadAction.dictionaryId]) {
                    pair.Value.Clear();
                    // avoid creating new Dictionary instances
                }
            }
            slotInitialized = true;
        }
        else {
            AllSavedValues = null;
            slotInitialized = false;
        }
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.clearState?.Invoke();
        }
    }

    internal static void OnBeforeSaveState(Level level) {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.beforeSaveState?.Invoke(level);
        }
    }

    internal static void OnBeforeLoadState(Level level) {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.beforeLoadState?.Invoke(level);
        }
    }

    internal static void OnPreCloneEntities() {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.preCloneEntities?.Invoke();
        }
    }

    public static void SaveStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values, Type type, params string[] memberNames) {
        if (type == null) {
            return;
        }

        if (!values.TryGetValue(type, out Dictionary<string, object> dict)) {
            values[type] = dict = [];
        }

        foreach (string memberName in memberNames) {
            if (type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } memberInfos) {
                if (memberInfos.Length == 0) {
                    $"SaveStaticMemberValues: No member found for type {type.FullName} and member name {memberName}".Log(LogLevel.Verbose);
                    continue;
                }

                if (memberInfos.First().IsField()) {
                    dict[memberName] = type.GetFieldValue(memberName).DeepCloneShared();
                }
                else {
                    dict[memberName] = type.GetPropertyValue(memberName).DeepCloneShared();
                }
            }
        }
    }
    public static void LoadStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values) {
        // 使用时总是 (多个 SaveStaticMemberValues + 一个 LoadStaticMemberValues) 捆绑成一个 SaveLoadAction 使用
        // 由于每个 SaveLoadAction 都有自己独立的字典, 因此这边的读取实际上并不会多读什么
        // type 和 memberNames 自动地在这个双层字典中可以读出来, 不需要我们提供
        foreach (KeyValuePair<Type, Dictionary<string, object>> pair in values) {
            foreach (string memberName in pair.Value.Keys) {
                Type type = pair.Key;
                if (type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } memberInfos) {
                    if (memberInfos.Length == 0) {
                        $"LoadStaticMemberValues: No member found for type {type.FullName} and member name {memberName}".Log(LogLevel.Verbose);
                        continue;
                    }

                    if (memberInfos.First().IsField()) {
                        type.SetFieldValue(memberName, pair.Value[memberName].DeepCloneShared());
                    }
                    else {
                        type.SetPropertyValue(memberName, pair.Value[memberName].DeepCloneShared());
                    }
                }
            }
        }
    }


    [Load]
    private static void RegisterWarmUp() {
        On.Monocle.Scene.Begin += WarmUpOnFirstLevel;
    }

    [Unload]
    private static void UnregisterWarmUp() {
        On.Monocle.Scene.Begin -= WarmUpOnFirstLevel;
    }

    // Configuring the built-in actions scans every type/static field of every loaded mod. That
    // used to happen inside the first save, i.e. as a one-time hitch on the save path. Do it when
    // the first level starts loading instead: all mods are loaded by then and the player is
    // already waiting for the level.
    private static void WarmUpOnFirstLevel(On.Monocle.Scene.orig_Begin orig, Scene self) {
        orig(self);

        if (self is not Level) {
            return;
        }

        On.Monocle.Scene.Begin -= WarmUpOnFirstLevel;

        try {
            InitSlots();
        }
        catch (Exception exception) {
            Logger.Warn("SpeedrunTool/SaveLoadAction", $"Failed to initialize save/load actions: {exception.GetType().Name}: {exception.Message}");
            Logger.LogDetailed(exception, "SpeedrunTool");
        }
    }

    [Unload]
    private static void Unload() {
        SharedActions.Clear();
    }


    private static void InitActions() {
        if (internalActionInitialized) {
            return;
        }

        contextOrder = Order_InternalCoreAction;
        InitFields();
        SupportSimpleStaticFields();
        SupportModModuleFields();
        FixSaveLoadIcon();
        BetterCasualPlay();
        SupportExternalMember();
        SupportCalcRandom();
        SupportSettings();
        SupportMInput();
        SupportInput();
        SupportAudioMusic();
        FixVertexLight();
        MuteAudioUtils.AddAction();
        ExternalAction();

        // mod support
        SupportModSessionAndSaveData();
        ThirdParty.Support();

        // 放最后，确保收集了所有克隆的 VirtualAssets 与 EventInstance
        contextOrder = Order_ExecuteAtLast;
        GraphicResourcesHandler.AddSaveLoadAction();
        ReleaseEventInstances();
        contextOrder = null;

        internalActionInitialized = true;
        needInitializeDictionaryId = true;
    }

    public static void InitSlots() {
        // most mods might add saveload actions before InitActions is executed
        InitActions();
        // but some might add during gameplay (why would you do that?)

        // anyway, we rebuild the dictionary
        if (needInitializeDictionaryId) {
            SharedActions = [.. SharedActions.OrderBy(x => x.executeOrder)]; // it's a stable sort
            int i = 0;
            foreach (SaveLoadAction action in SharedActions) {
                i++;
                action.dictionaryId = i;
            }
            ActionsVersion++;
#if DEBUG
            if (Log_AllActions) {
                System.Text.StringBuilder sb = new();
                sb.Append("\n===== Initializing SharedActions... =====\n");
                foreach (SaveLoadAction action in SharedActions) {
                    sb.Append($"--- [{action.dictionaryId}] {action.ActionDescription} ---\n");
                }
                Logger.Debug("SpeedrunTool", sb.ToString());
            }
#endif

            SaveSlotsManager.RequireReInit();
            needInitializeDictionaryId = false;
        }

        if (slotInitialized) {
            return;
        }

        AllSavedValues = InitValueDictionary();
        SaveSlotsManager.Slot.AllSavedValuesVersion = ActionsVersion;
        slotInitialized = true;
    }

#if DEBUG
#pragma warning disable IDE0060 // 删除未使用的参数
    internal static void LogSavedValues(Level level) {
        System.Text.StringBuilder sb = new();

        foreach (SaveLoadAction slAction in SharedActions) {
            sb.Append($"\n======  {slAction.ActionDescription}  ======\n");
            int count = 1;
            foreach (KeyValuePair<Type, Dictionary<string, object>> pair in AllSavedValues[slAction.dictionaryId]) {
                sb.Append($"---  Type {count.ToString().PadRight(3)} =   {pair.Key.FullName}  ---\n");
                foreach (KeyValuePair<string, object> pair2 in pair.Value) {
                    sb.Append($" ├  {PadToMultiple(pair2.Key, 32)} | {pair2.Value}\n");
                }
                sb.Append('\n');
                count++;
            }
        }

        if (Log_SavedLevelEntities) {
            sb.Append("\n======  Saved Level (Part) ======");
            sb.Append("\n[0] Level\n");
            AppendInstanceField(typeof(Level), level);
            int index = 0;
            foreach (KeyValuePair<Type, List<Entity>> entity in level.Tracker.Entities) {
                if (entity.Value.Count == 0) {
                    continue;
                }
                index++;
                sb.Append($"\n[{index}] {entity.Key}    x {entity.Value.Count}\n");
                AppendInstanceField(entity.Key, entity.Value.First());
            }
        }

        Logger.Log(LogLevel.Debug, "SpeedrunTool", sb.ToString());

        void AppendInstanceField(Type type, object obj) {
            // modified from DeepClonerMsilGenerator.GenerateClonerInternal
            List<FieldInfo> list = [];
            Type tp = type;
            do {
                // don't do anything with this dark magic!
                if (tp == typeof(ContextBoundObject)) {
                    break;
                }

                list.AddRange(tp.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly));
                tp = tp.BaseType;
            } while (tp != null);
            if (obj is not null) {
                foreach (FieldInfo fi in list) {
                    sb.Append($" ├  {PadToMultiple(fi.Name, 32)} | {PadToMultiple(fi.GetValue(obj), 32)} | {(Force.DeepCloner.Helpers.DeepClonerSafeTypes.CanReturnSameObject(fi.FieldType) ? "" : "Complex")}\n");
                }
            }
            else {
                foreach (FieldInfo fi in list) {
                    sb.Append($" ├  {PadToMultiple(fi.Name, 32)} | {PadToMultiple(fi.FieldType.Name, 32)} | {(Force.DeepCloner.Helpers.DeepClonerSafeTypes.CanReturnSameObject(fi.FieldType) ? "" : "Complex")}\n");
                }
            }
        }

        string PadToMultiple(object obj, int b) {
            string str = obj?.ToString() ?? " ";
            int length = str.Length;
            int targetLength = ((length - 1) / b + 1) * b;
            return str.PadRight(targetLength);
        }
    }
#pragma warning restore IDE0060 // 删除未使用的参数
#endif

    private static void InitFields() {
        simpleStaticFields = [];
        modModuleFields = [];

        IEnumerable<Type> types = ModUtils.GetAllTypes().Where(type =>
            !type.IsGenericType
            && type.FullName != null
            && !type.FullName.StartsWith("Celeste.Mod.SpeedrunTool")
            && !type.IsSubclassOf(typeof(Oui))
            && (type.IsSameOrSubclassOf(typeof(Entity))
                || type.IsSameOrSubclassOf(typeof(Component))
                || type.IsSameOrSubclassOf(typeof(Renderer))));

        foreach (Type type in types) {
            try {
                FieldInfo[] fieldInfos = [.. type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(info => {
                        Type fieldType = info.FieldType; // might throw exception
                        return !info.IsConst() && fieldType.IsSimpleClass(genericType =>
                            fieldType == type
                            || fieldType == typeof(Level)
                            || fieldType == typeof(Player)
                            || fieldType == typeof(MTexture)
                            || fieldType == typeof(CrystalStaticSpinner)
                            || fieldType == typeof(Solid)
                            || fieldType.IsSubclassOf(typeof(Renderer))
                            || fieldType.IsSubclassOf(typeof(VirtualAsset))
                            || genericType == type
                            || genericType == typeof(Level)
                            || genericType == typeof(Player)
                            || genericType == typeof(MTexture)
                            || genericType == typeof(CrystalStaticSpinner)
                            || genericType == typeof(Solid)
                            || genericType.IsSubclassOf(typeof(Renderer))
                            || genericType.IsSubclassOf(typeof(VirtualAsset))
                        );
                    })];

                if (fieldInfos.Length == 0) {
                    continue;
                }

                simpleStaticFields[type] = fieldInfos;
            }
            catch {
                /* possibly a FileNotFoundException
                 * GetTypesSafe() just checks if the type can be loaded, and if the FullyQualifiedName, declaringType, baseType of the type is safe, etc.
                 * but doesn't check if each memberInfo is "safe"
                 * only method calls or something which use the "dangerous" memberInfo will immediately lead to an Exception
                 * 
                 * e.g: when you enable KyfexHelper, but don't enable auspicioushelper
                 * then KyfexHelper.KevinFace.tcComponent will give you a surprise
                 */
                continue;
            }
        }

        InitModuleFields();
        InitExtendedVariantsFields();
        FilterStaticFields();
    }

    private static void InitModuleFields() {
        foreach (EverestModule everestModule in Everest.Modules) {
            Type type = everestModule.GetType();
            List<FieldInfo> staticFields = [];
            List<FieldInfo> instanceFields = [];

            FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (FieldInfo fieldInfo in fieldInfos.Where(info => {
                Type fieldType = info.FieldType;
                return !info.IsInitOnly &&
                       (fieldType == typeof(Level) ||
                        fieldType == typeof(Session) ||
                        fieldType.IsSameOrSubclassOf(typeof(Entity)) && !fieldType.IsSubclassOf(typeof(Oui)) ||
                        fieldType == typeof(Vector2));
            })) {
                if (fieldInfo.IsStatic) {
                    staticFields.Add(fieldInfo);
                }
                else {
                    instanceFields.Add(fieldInfo);
                }
            }

            if (staticFields.Count > 0) {
                simpleStaticFields[type] = [.. staticFields];
            }

            if (instanceFields.Count > 0) {
                modModuleFields[type] = [.. instanceFields];
            }
        }
    }

    private static void InitExtendedVariantsFields() {
        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Variants.AbstractExtendedVariant") is { } variantType) {
            foreach (Type type in variantType.Assembly.GetTypesSafe().Where(type => type.IsSubclassOf(variantType))) {
                FieldInfo[] fieldInfos = [.. type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(info => !info.IsConst() && info.FieldType.IsSimpleClass())];

                if (fieldInfos.Length == 0) {
                    continue;
                }

                simpleStaticFields[type] = fieldInfos;
            }
        }
    }


    private static void FilterStaticFields() {
        // 过滤掉非法的字段
        // 例如未安装 DJMapHelper 时 ExtendedVariantsMode 的 AutoDestroyingReverseOshiroModder.stateMachine
        // 不止会有 TargetInvocationException, 此外 LeniencyHelper 还会有未绑定泛型参数造成的异常
        foreach (Type type in simpleStaticFields.Keys.ToArray()) {
            FieldInfo[] fieldInfos = [.. simpleStaticFields[type].Where(info => {
                try {
                    info.GetValue(null);
                    return true;
                }
                catch {
                    return false;
                }
            })];

            if (fieldInfos.Length > 0) {
                simpleStaticFields[type] = fieldInfos;
            }
            else {
                simpleStaticFields.Remove(type);
            }
        }
    }
}
