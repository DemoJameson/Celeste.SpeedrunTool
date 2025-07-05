//#define LOG
using Celeste.Mod.Helpers;
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
using System.Runtime.CompilerServices;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed class SaveLoadAction {
    public delegate void SlAction(Dictionary<Type, Dictionary<string, object>> savedValues, Level level);

    // 第一次 SL 时才初始化，避免通过安装依赖功能解除禁用的 Mod 被忽略

    private static bool internalActionInitialized = false;

    private static bool modActionInitialized = false;
    private static bool slotInitialized {
        get => SaveSlotsManager.Slot.ValueDictionaryInitialized;
        set {
            SaveSlotsManager.Slot.ValueDictionaryInitialized = value;
        }
    }

    public static readonly List<VirtualAsset> VirtualAssets = new();
    public static readonly List<EventInstance> ClonedEventInstancesWhenSave = new();
    public static readonly List<EventInstance> ClonedEventInstancesWhenPreClone = new();

    // only actions, no values stored. Share among all save slots
    private static readonly List<SaveLoadAction> SharedActions = new();

    // values, belong to each save slot

    internal static Dictionary<int, Dictionary<Type, Dictionary<string, object>>> AllSavedValues {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => SaveSlotsManager.Slot.AllSavedValues;

        set {
            SaveSlotsManager.Slot.AllSavedValues = value;
        }
    }

    internal static Dictionary<int, Dictionary<Type, Dictionary<string, object>>> InitValueDictionary() {
        Dictionary<int, Dictionary<Type, Dictionary<string, object>>> dict = new();
        for (int i = 1; i<= createdActions; i++) {
            dict[i] = new Dictionary<Type, Dictionary<string, object>>();
        }
        return dict;
    }

    private static Dictionary<Type, FieldInfo[]> simpleStaticFields;
    private static Dictionary<Type, FieldInfo[]> modModuleFields;

#if LOG
    public string ActionDescription = "";
#endif

    private static int createdActions = 0;
    private readonly int id;

    private readonly Action clearState;
    private readonly Action<Level> beforeSaveState;
    private readonly Action<Level> beforeLoadState;
    private readonly Action preCloneEntities;
    internal readonly SlAction loadState;
    private readonly SlAction saveState;
    internal Action<Level, List<Entity>, Entity> unloadLevel;

    public SaveLoadAction(SlAction saveState = null, SlAction loadState = null, Action clearState = null,
        Action<Level> beforeSaveState = null, Action preCloneEntities = null) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.preCloneEntities = preCloneEntities;
        createdActions++;
        id = createdActions;
    }

    public SaveLoadAction(SlAction saveState, SlAction loadState, Action clearState,
        Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.beforeLoadState = beforeLoadState;
        this.preCloneEntities = preCloneEntities;
        createdActions++;
        id = createdActions;
    }

    public SaveLoadAction(SlAction saveState, SlAction loadState, Action clearState,
        Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities, Action<Level, List<Entity>, Entity> unloadLevel) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.beforeLoadState = beforeLoadState;
        this.preCloneEntities = preCloneEntities;
        this.unloadLevel = unloadLevel;
        createdActions++;
        id = createdActions;
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
        Action<Level> beforeSaveState = null, Action preCloneEntities = null) {
        SaveLoadAction saveLoadAction = new(CreateSlAction(saveState), CreateSlAction(loadState), clearState, beforeSaveState, preCloneEntities);
#if LOG
        AddDebugDescription(saveLoadAction, internalCall: true);
#endif
        SharedActions.Add(saveLoadAction);
        return saveLoadAction;
    }

    internal static SaveLoadAction InternalSafeAdd(Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState, Action clearState,
        Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities = null) {
        SaveLoadAction saveLoadAction = new(CreateSlAction(saveState), CreateSlAction(loadState), clearState, beforeSaveState, beforeLoadState, preCloneEntities);
#if LOG
        AddDebugDescription(saveLoadAction, internalCall: true);
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
#if LOG
        AddDebugDescription(saveLoadAction, internalCall: false);
#endif
        SharedActions.Add(saveLoadAction);
        modActionInitialized = true;
        return saveLoadAction;
    }

#if LOG
    private static void AddDebugDescription(SaveLoadAction action, bool internalCall = true) {
        int frame = internalCall ? 2 : 3;
        System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace();
        System.Diagnostics.StackFrame stackFrame = stackTrace.GetFrame(frame);
        if (stackFrame.GetMethod() is { } method) {
            if (internalCall) {
                action.ActionDescription = $"{method.DeclaringType?.FullName ?? "<UnknownType>"} @ {method.Name ?? "<UnknownMethod>"}";
            } else {
                action.ActionDescription = $"<<External>> {((Delegate)action.loadState ?? (Delegate)action.saveState ?? (Delegate)action.clearState)
                ?.Target?.GetType()?.FullName?.Replace("+<>c", "") ?? "<UnknownType>"} @ {method.Name ?? "<UnknownMethod>"}";
            }
        } else {
            action.ActionDescription = "<BadStackFrame>";
        }
        Logger.Log(LogLevel.Debug, "SpeedrunTool", $"=== {action.ActionDescription} ===");
    }
#endif

    /// <summary>
    /// For third party mods
    /// </summary>
    public static bool Remove(SaveLoadAction saveLoadAction) {
        return SharedActions.Remove(saveLoadAction);
    }

    internal static void Remove(Func<SaveLoadAction, bool> predicate) {
        List<SaveLoadAction> toRemove = new List<SaveLoadAction>();
        foreach (SaveLoadAction action in SharedActions) {
            if (predicate(action)) {
                toRemove.Add(action);
            }
        }
        if (toRemove is not null) {
            foreach (SaveLoadAction action in toRemove) {
                SharedActions.Remove(action);
            }
        }
    }

    internal static void OnSaveState(Level level) {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.saveState?.Invoke(AllSavedValues[saveLoadAction.id], level);
        }
    }

    internal static void OnLoadState(Level level) {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.loadState?.Invoke(AllSavedValues[saveLoadAction.id], level);
        }
    }

    internal static void OnClearState() {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            AllSavedValues = InitValueDictionary();
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

    internal static void OnUnloadLevel(Level level, List<Entity> entities, Entity entity) {
        foreach (SaveLoadAction saveLoadAction in SharedActions) {
            saveLoadAction.unloadLevel?.Invoke(level, entities, entity);
        }
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static void SaveStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values, Type type, params string[] memberNames) {
        if (type == null) {
            return;
        }

        if (!values.TryGetValue(type, out Dictionary<string, object> dict)) {
            values[type] = dict = new Dictionary<string, object>();
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

    // ReSharper disable once MemberCanBePrivate.Global
    public static void LoadStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values) {
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

    [Unload]
    private static void Unload() {
        SharedActions.Clear();
    }

    private static void InitActions() {
        if (internalActionInitialized) {
            return;
        }

        internalActionInitialized = true;
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
        MaxHelpingHandUtils.Support();
        PandorasBoxUtils.Support();
        CrystallineHelperUtils.Support();
        SpringCollab2020Utils.Support();
        ExtendedVariantsUtils.Support();
        XaphanHelperUtils.Support();
        IsaGrabBagUtils.Support();
        SpirialisHelperUtils.Support();
        DeathTrackerHelperUtils.Support();
        CommunalHelperUtils.Support();
        BrokemiaHelperUtils.Support();
        FrostHelperUtils.Support();
        VivHelperUtils.Support();

        // 放最后，确保收集了所有克隆的 VirtualAssets 与 EventInstance
        ReloadVirtualAssets();
        ReleaseEventInstances();
    }

    public static void InitSlots() {
        InitActions();

        if (modActionInitialized) {
            SaveSlotsManager.ModRequireReInit();
            modActionInitialized = false;
        }

        if (slotInitialized) {
            return;
        }

        AllSavedValues = InitValueDictionary();
        slotInitialized = true;
    }


    internal static void LogSavedValues() {
#if LOG
        foreach (SaveLoadAction slAction in SharedActions) {
            Logger.Log(LogLevel.Debug, "SpeedrunTool", $"=== {slAction.ActionDescription} ===");
            foreach (KeyValuePair<Type, Dictionary<string, object>> pair in AllSavedValues[slAction.id]) {
                Logger.Log(LogLevel.Info, "SpeedrunTool", pair.Key.FullName);
            }
        }
#endif
    }


    private static void InitFields() {
        simpleStaticFields = new Dictionary<Type, FieldInfo[]>();
        modModuleFields = new Dictionary<Type, FieldInfo[]>();

        IEnumerable<Type> types = FakeAssembly.GetFakeEntryAssembly().GetTypes().Where(type =>
            !type.IsGenericType
            && type.FullName != null
            && !type.FullName.StartsWith("Celeste.Mod.SpeedrunTool")
            && !type.IsSubclassOf(typeof(Oui))
            && type.IsSameOrSubclassOf(typeof(Entity)) || type.IsSameOrSubclassOf(typeof(Component)) ||
            type.IsSameOrSubclassOf(typeof(Renderer)));

        foreach (Type type in types) {
            FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(info => {
                    Type fieldType = info.FieldType;
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
                }).ToArray();

            if (fieldInfos.Length == 0) {
                continue;
            }

            simpleStaticFields[type] = fieldInfos;
        }

        InitModuleFields();
        InitExtendedVariantsFields();
        FilterStaticFields();
    }

    private static void InitModuleFields() {
        foreach (EverestModule everestModule in Everest.Modules) {
            Type type = everestModule.GetType();
            List<FieldInfo> staticFields = new();
            List<FieldInfo> instanceFields = new();

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
                simpleStaticFields[type] = staticFields.ToArray();
            }

            if (instanceFields.Count > 0) {
                modModuleFields[type] = instanceFields.ToArray();
            }
        }
    }

    private static void InitExtendedVariantsFields() {
        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Variants.AbstractExtendedVariant") is { } variantType) {
            foreach (Type type in variantType.Assembly.GetTypesSafe().Where(type => type.IsSubclassOf(variantType))) {
                FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(info => !info.IsConst() && info.FieldType.IsSimpleClass()).ToArray();

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
        foreach (Type type in simpleStaticFields.Keys.ToArray()) {
            FieldInfo[] fieldInfos = simpleStaticFields[type].Where(info => {
                try {
                    info.GetValue(null);
                    return true;
                }
                catch (TargetInvocationException) {
                    return false;
                }
            }).ToArray();

            if (fieldInfos.Length > 0) {
                simpleStaticFields[type] = fieldInfos;
            }
            else {
                simpleStaticFields.Remove(type);
            }
        }
    }

    private static void SupportSimpleStaticFields() {
        InternalSafeAdd(
            (dictionary, _) => {
                foreach (Type type in simpleStaticFields.Keys) {
                    FieldInfo[] fieldInfos = simpleStaticFields[type];
                    // ("\n\n" + string.Join("\n", fieldInfos.Select(info => type.FullName + " " + info.Name + " " + info.FieldType))).DebugLog();
                    Dictionary<string, object> values = new();

                    foreach (FieldInfo fieldInfo in fieldInfos) {
                        values[fieldInfo.Name] = fieldInfo.GetValue(null);
                    }

                    dictionary[type] = values.DeepCloneShared();
                }
            }, (dictionary, _) => {
                Dictionary<Type, Dictionary<string, object>> clonedDict = dictionary.DeepCloneShared();
                foreach (Type type in clonedDict.Keys) {
                    Dictionary<string, object> values = clonedDict[type];
                    // ("\n\n" + string.Join("\n", values.Select(pair => type.FullName + " " + pair.Key + " " + pair.Value))).DebugLog();
                    foreach (KeyValuePair<string, object> pair in values) {
                        type.SetFieldValue(pair.Key, pair.Value);
                    }
                }
            }
        );
    }

    private static void SupportModModuleFields() {
        InternalSafeAdd(
            (savedValues, _) => {
                foreach (EverestModule module in Everest.Modules) {
                    Dictionary<string, object> dict = new();
                    Type moduleType = module.GetType();
                    if (modModuleFields.TryGetValue(moduleType, out FieldInfo[] moduleFields)) {
                        foreach (FieldInfo fieldInfo in moduleFields) {
                            dict[fieldInfo.Name] = fieldInfo.GetValue(module);
                        }

                        savedValues[moduleType] = dict.DeepCloneShared();
                    }
                }
            },
            (savedValues, _) => {
                Dictionary<Type, Dictionary<string, object>> clonedValues = savedValues.DeepCloneShared();
                foreach (EverestModule module in Everest.Modules) {
                    if (clonedValues.TryGetValue(module.GetType(), out Dictionary<string, object> dict)) {
                        foreach (string fieldName in dict.Keys) {
                            module.SetFieldValue(fieldName, dict[fieldName]);
                        }
                    }
                }
            });
    }

    private static void FixSaveLoadIcon() {
        InternalSafeAdd(loadState: (_, _) => {
            // 修复右下角存档图标残留
            if (!UserIO.savingInternal) {
                SaveLoadIcon.Hide();
            }
        });
    }

    private static void BetterCasualPlay() {
        InternalSafeAdd(beforeSaveState: level => {
            level.Session.SetFlag("SpeedrunTool_Reset_unpauseTimer", false);
            if (StateManager.Instance.SavedByTas) {
                return;
            }

            // 移除冻结帧，移除暂停黑屏
            Engine.FreezeTimer = 0f;
            level.HudRenderer.BackgroundFade = 0f;

            // 移除暂停帧
            if (level.GetFieldValue<float>("unpauseTimer") > 0f || !level.Paused && level.wasPaused) {
                level.Session.SetFlag("SpeedrunTool_Reset_unpauseTimer");

                if (level.GetFieldValue<float>("unpauseTimer") > 0f) {
                    level.SetFieldValue("unpauseTimer", 0f);
                }

                level.wasPaused = false;
                level.EndPauseEffects();
            }
        });
    }

    private static void SupportExternalMember() {
        InternalSafeAdd(
            (savedValues, _) => {
                SaveStaticMemberValues(savedValues, typeof(Engine),
                    nameof(Engine.DashAssistFreeze),
                    nameof(Engine.DashAssistFreezePress),
                    nameof(Engine.DeltaTime),
                    nameof(Engine.FrameCounter),
                    nameof(Engine.FreezeTimer),
                    nameof(Engine.RawDeltaTime),
                    nameof(Engine.TimeRate),
                    nameof(Engine.TimeRateB),
                    nameof(Engine.Pooler)
                );
                SaveStaticMemberValues(savedValues, typeof(Glitch), nameof(Glitch.Value));
                SaveStaticMemberValues(savedValues, typeof(Distort), nameof(Distort.Anxiety), nameof(Distort.GameRate));
                SaveStaticMemberValues(savedValues, typeof(ScreenWipe), nameof(ScreenWipe.WipeColor));
                SaveStaticMemberValues(savedValues, typeof(Audio), nameof(Audio.currentCamera));
            },
            (savedValues, _) => LoadStaticMemberValues(savedValues));
    }

    private static void SupportCalcRandom() {
        InternalSafeAdd(
            (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(Calc),
                nameof(Calc.Random), nameof(Calc.randomStack)),
            (savedValues, _) => LoadStaticMemberValues(savedValues));
    }

    private static void SupportSettings() {
        InternalSafeAdd(
            (savedValues, _) => {
                if (Settings.Instance is { } settings) {
                    Dictionary<string, object> dict = new();
                    dict["GrabMode"] = settings.GrabMode;
                    savedValues[typeof(Settings)] = dict;
                }
            },
            (savedValues, _) => {
                if (Settings.Instance is { } settings && savedValues.TryGetValue(typeof(Settings), out Dictionary<string, object> dict)) {
                    settings.GrabMode = (GrabModes)dict["GrabMode"];

                }
            }
        );
    }

    private static void SupportMInput() {
        InternalSafeAdd(
            (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(MInput),
                nameof(MInput.Active), nameof(MInput.Disabled), nameof(MInput.Keyboard), nameof(MInput.Mouse), nameof(MInput.GamePads)),
            (savedValues, _) => {
                LoadStaticMemberValues(savedValues);

                // 关闭手柄震动
                MInput.GamePads[Input.Gamepad].Rumble(0f, 0f);
            });
    }

    // Fix https://github.com/DemoJameson/CelesteSpeedrunTool/issues/19
    private static void SupportInput() {
        Type inputType = typeof(Input);
        InternalSafeAdd(
            (savedValues, _) => {
                SaveStaticMemberValues(savedValues, typeof(MInput), nameof(MInput.VirtualInputs));

                Dictionary<string, object> inputDict = new();
                foreach (FieldInfo fieldInfo in typeof(Input).GetFields(BindingFlags.Public | BindingFlags.Static).Where(info =>
                             info.FieldType.IsSameOrSubclassOf(typeof(VirtualInput)))) {
                    inputDict[fieldInfo.Name] = fieldInfo.GetValue(null);
                }

                inputDict["grabToggle"] = Input.grabToggle;
                inputDict["LastAim"] = Input.LastAim;

                savedValues[inputType] = inputDict.DeepCloneShared();

                foreach (EverestModule everestModule in Everest.Modules) {
                    if (everestModule.Metadata?.Name == "CelesteTAS" || everestModule == SpeedrunToolModule.Instance) {
                        continue;
                    }

                    if (everestModule.GetPropertyValue("SettingsType") is not Type settingsType) {
                        continue;
                    }

                    if (everestModule.GetPropertyValue("_Settings") is not { } settingsInstance) {
                        continue;
                    }

                    Dictionary<string, object> settingsDict = new();
                    foreach (PropertyInfo propertyInfo in settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                        if (propertyInfo.PropertyType != typeof(ButtonBinding)) {
                            continue;
                        }

                        settingsDict[propertyInfo.Name] = propertyInfo.GetValue(settingsInstance);
                    }

                    if (settingsDict.Count == 0) {
                        continue;
                    }

                    savedValues[settingsType] = settingsDict.DeepCloneShared();
                }
            }, (savedValues, _) => {
                savedValues = savedValues.DeepCloneShared();

                if (StateManager.Instance.LoadByTas) {
                    MInput.VirtualInputs = (List<VirtualInput>)savedValues[typeof(MInput)][nameof(MInput.VirtualInputs)];
                }

                Dictionary<string, object> dictionary = savedValues[inputType];
                foreach (string fieldName in dictionary.Keys) {
                    object virtualInput = dictionary[fieldName];

                    if (StateManager.Instance.LoadByTas) {
                        inputType.SetFieldValue(fieldName, virtualInput);
                    }
                    else {
                        if (fieldName == "grabToggle") {
                            Input.grabToggle = (bool)virtualInput;
                        }
                        else if (fieldName == "LastAim") {
                            Input.LastAim = (Vector2)virtualInput;
                        }
                        else {
                            object fieldValue = inputType.GetFieldValue(fieldName);
                            if (fieldValue is VirtualJoystick virtualJoystick &&
                                virtualInput is VirtualJoystick savedVirtualJoystick) {
                                virtualJoystick.InvertedX = savedVirtualJoystick.InvertedX;
                                virtualJoystick.InvertedY = savedVirtualJoystick.InvertedY;
                            }
                            else if (fieldValue is VirtualIntegerAxis virtualIntegerAxis &&
                                       virtualInput is VirtualIntegerAxis savedVirtualIntegerAxis) {
                                virtualIntegerAxis.Inverted = savedVirtualIntegerAxis.Inverted;
                            }
                        }
                    }
                }

                if (StateManager.Instance.LoadByTas) {
                    foreach (EverestModule everestModule in Everest.Modules) {
                        if (everestModule.Metadata?.Name == "CelesteTAS" || everestModule == SpeedrunToolModule.Instance) {
                            continue;
                        }

                        if (everestModule.GetPropertyValue("SettingsType") is not Type settingsType) {
                            continue;
                        }

                        if (!savedValues.TryGetValue(settingsType, out var settingsDict)) {
                            continue;
                        }

                        if (everestModule.GetPropertyValue("_Settings") is not { } settingsInstance) {
                            continue;
                        }

                        foreach (string propertyName in settingsDict.Keys) {
                            settingsInstance.SetPropertyValue(propertyName, settingsDict[propertyName]);
                        }
                    }
                }
            }
        );
    }

    private static void ExternalAction() {
        InternalSafeAdd(
            saveState: (_, level) => {
                IgnoreSaveLoadComponent.ReAddAll(level);
                EndPoint.AllReadyForTime();
            },
            loadState: (_, level) => {
                RoomTimerManager.ResetTime();
                DeathStatisticsManager.Clear();
                IgnoreSaveLoadComponent.ReAddAll(level);
                EndPoint.AllReadyForTime();
            },
            clearState: () => {
                RoomTimerManager.ClearPbTimes(!StateManager.Instance.ClearBeforeSave);
                DeepClonerUtils.ClearSharedDeepCloneState();
                DynDataUtils.ClearCached();
            },
            beforeSaveState: level => {
                RoomTimerManager.ClearPbTimes(false);
                DeepClonerUtils.ClearSharedDeepCloneState();
                DynDataUtils.ClearCached();

                IgnoreSaveLoadComponent.RemoveAll(level);
                ClearBeforeSaveComponent.RemoveAll(level);

                // 冲刺残影方向错误，干脆移除
                // TODO: 正确 SL 残影
                TrailManager.Clear();

                if (ModUtils.IsInstalled("CelesteNet.Client")) {
                    Type ghostEmoteWheelType = ModUtils.GetType("CelesteNet.Client", "Celeste.Mod.CelesteNet.Client.Entities.GhostEmoteWheel");
                    IEnumerable<Entity> entities =
                        level.Entities.Where(entity => entity.GetType().FullName?.StartsWith("Celeste.Mod.CelesteNet.") == true);
                    foreach (Entity entity in entities) {
                        if (ghostEmoteWheelType != null && entity.GetType() == ghostEmoteWheelType && entity.GetFieldValue<bool>("timeRateSet")) {
                            // Normally GhostEmoteWheel in CelesteNet does this when it gets closed again
                            Engine.TimeRate = 1f;
                            ghostEmoteWheelType = null;
                        }

                        entity.RemoveSelf();
                    }
                }
            },
            beforeLoadState: IgnoreSaveLoadComponent.RemoveAll
        );
    }

    private static void SupportModSessionAndSaveData() {
        InternalSafeAdd(
            (savedValues, _) => {
                foreach (EverestModule module in Everest.Modules.Where(module => module.GetType().Name != "NullModule")) {
                    savedValues[module.GetType()] = new Dictionary<string, object> {
                        {"_Session", module._Session},
                        {"_SaveData", module._SaveData},
                    }.DeepCloneShared();
                }
            },
            (savedValues, _) => {
                Dictionary<Type, Dictionary<string, object>> clonedValues = savedValues.DeepCloneShared();
                foreach (EverestModule module in Everest.Modules.Where(module => module.GetType().Name != "NullModule")) {
                    if (clonedValues.TryGetValue(module.GetType(), out Dictionary<string, object> dictionary)) {
                        module._Session = dictionary["_Session"] as EverestModuleSession;
                        module._SaveData = dictionary["_SaveData"] as EverestModuleSaveData;
                    }
                }
            });
    }

    private static void SupportAudioMusic() {
        InternalSafeAdd(
            (savedValues, level) => {
                Dictionary<string, object> saved = new() {
                    {"currentMusicEvent", Audio.GetEventName(Audio.CurrentMusicEventInstance)},
                    {"currentMusicEventParameters", Audio.CurrentMusicEventInstance.GetSavedParameterValues()},
                    {"CurrentAmbienceEventInstance", Audio.GetEventName(Audio.CurrentAmbienceEventInstance)},
                    {"CurrentAmbienceEventInstanceParameters", Audio.CurrentAmbienceEventInstance.GetSavedParameterValues()},
                    {"currentAltMusicEvent", Audio.GetEventName(Audio.currentAltMusicEvent)},
                    {"currentAltMusicEventParameters", Audio.currentAltMusicEvent.GetSavedParameterValues()},
                    {"MusicUnderwater", Audio.MusicUnderwater},
                    {"PauseMusic", Audio.PauseMusic},
                    {"PauseGameplaySfx", Audio.PauseGameplaySfx},
                };

                // EndPauseEffects() 之后立即读取该值依然为 true，所以需要加上这个判断
                if (level.Session.GetFlag("SpeedrunTool_Reset_unpauseTimer")) {
                    saved["PauseMusic"] = false;
                    saved["PauseGameplaySfx"] = false;
                }

                savedValues[typeof(Audio)] = saved.DeepCloneShared();
            },
            (savedValues, level) => {
                Dictionary<string, object> saved = savedValues[typeof(Audio)];

                Audio.SetMusic(saved["currentMusicEvent"] as string);
                Audio.CurrentMusicEventInstance?.CopyParametersFrom(saved["currentMusicEventParameters"] as ConcurrentDictionary<string, float>);

                Audio.SetAmbience(saved["CurrentAmbienceEventInstance"] as string);
                Audio.CurrentAmbienceEventInstance?.CopyParametersFrom(
                    saved["CurrentAmbienceEventInstanceParameters"] as ConcurrentDictionary<string, float>);

                Audio.SetAltMusic(saved["currentAltMusicEvent"] as string);
                Audio.currentAltMusicEvent?.CopyParametersFrom(saved["currentAltMusicEventParameters"] as ConcurrentDictionary<string, float>);

                Audio.MusicUnderwater = (bool)saved["MusicUnderwater"];
                Audio.PauseMusic = (bool)saved["PauseMusic"];
                Audio.PauseGameplaySfx = (bool)saved["PauseGameplaySfx"];
                if (!level.Paused && Level._PauseSnapshot != null) {
                    Audio.ReleaseSnapshot(Level._PauseSnapshot);
                    Level.PauseSnapshot = null;
                }
            }
        );
    }

    // fix: 5A黑暗房间读档后灯光问题
    private static void FixVertexLight() {
        InternalSafeAdd(loadState: (_, level) => {
            VertexLight[] lights = level.Lighting.lights;
            for (int i = 0; i < lights.Length; i++) {
                if (lights[i] is { } light) {
                    light.Index = -1;
                    lights[i] = null;
                }
            }
        });
    }


    private static void ReloadVirtualAssets() {
        InternalSafeAdd(
            loadState: (_, _) => {
                foreach (VirtualAsset virtualAsset in VirtualAssets) {
                    switch (virtualAsset) {
                        case VirtualTexture { IsDisposed: true } virtualTexture:
                            // Fix: 全屏切换然后读档煤球红边消失
                            if (!virtualTexture.Name.StartsWith("dust-noise-")) {
                                virtualTexture.Reload();
                            }

                            break;
                        case VirtualRenderTarget { IsDisposed: true } virtualRenderTarget:
                            virtualRenderTarget.Reload();
                            break;
                    }
                }

                VirtualAssets.Clear();
            },
            clearState: () => VirtualAssets.Clear(),
            preCloneEntities: () => VirtualAssets.Clear()
        );
    }

    private static void ReleaseEventInstances() {
        InternalSafeAdd(
            clearState: () => {
                foreach (EventInstance eventInstance in ClonedEventInstancesWhenSave.Union(ClonedEventInstancesWhenPreClone)) {
                    Audio.ReleaseSnapshot(eventInstance);
                }

                ClonedEventInstancesWhenSave.Clear();
                ClonedEventInstancesWhenPreClone.Clear();
            },
            preCloneEntities: () => ClonedEventInstancesWhenPreClone.Clear()
        );
    }

    internal static void CloneModTypeFields(string modName, string typeFullName, params string[] fields) {
        if (ModUtils.GetType(modName, typeFullName) is { } modType) {
            InternalSafeAdd(
                (savedValues, _) => SaveStaticMemberValues(savedValues, modType, fields),
                (savedValues, _) => LoadStaticMemberValues(savedValues));
        }
    }
}