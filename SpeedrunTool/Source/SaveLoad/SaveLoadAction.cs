using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.Helpers;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.Utils;
using FMOD.Studio;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed class SaveLoadAction {
    public delegate void SlAction(Dictionary<Type, Dictionary<string, object>> savedValues, Level level);

    // 第一次 SL 时才初始化，避免通过安装依赖功能解除禁用的 Mod 被忽略
    private static bool initialized;

    public static readonly List<VirtualAsset> VirtualAssets = new();
    public static readonly List<EventInstance> ClonedEventInstancesWhenSave = new();
    public static readonly List<EventInstance> ClonedEventInstancesWhenPreClone = new();

    private static readonly List<SaveLoadAction> All = new();

    private static Dictionary<Type, FieldInfo[]> simpleStaticFields;
    private static Dictionary<Type, FieldInfo[]> modModuleFields;

    private readonly Action clearState;
    private readonly Action<Level> beforeSaveState;
    private readonly Action<Level> beforeLoadState;
    private readonly Action preCloneEntities;
    private readonly SlAction loadState;

    private readonly Dictionary<Type, Dictionary<string, object>> savedValues = new();
    private readonly SlAction saveState;

    private Action<Level, List<Entity>, Entity> unloadLevel;

    public SaveLoadAction(SlAction saveState = null, SlAction loadState = null, Action clearState = null,
        Action<Level> beforeSaveState = null, Action preCloneEntities = null) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.preCloneEntities = preCloneEntities;
    }

    public SaveLoadAction(SlAction saveState, SlAction loadState, Action clearState,
        Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.beforeLoadState = beforeLoadState;
        this.preCloneEntities = preCloneEntities;
    }

    // ReSharper disable once UnusedMember.Global
    [Obsolete("crash on macOS if speedrun tool is not installed, use SafeAdd() instead")]
    public static void Add(SaveLoadAction saveLoadAction) {
        All.Add(saveLoadAction);
    }

    // ReSharper disable once MemberCanBePrivate.Global
    // ReSharper disable once UnusedMethodReturnValue.Global
    public static object SafeAdd(Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState = null,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState = null, Action clearState = null,
        Action<Level> beforeSaveState = null, Action preCloneEntities = null) {
        SaveLoadAction saveLoadAction = new(CreateSlAction(saveState), CreateSlAction(loadState), clearState, beforeSaveState, preCloneEntities);
        All.Add(saveLoadAction);
        return saveLoadAction;
    }

    public static object SafeAdd(Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState, Action clearState,
        Action<Level> beforeSaveState, Action<Level> beforeLoadState, Action preCloneEntities = null) {
        SaveLoadAction saveLoadAction = new(CreateSlAction(saveState), CreateSlAction(loadState), clearState, beforeSaveState, beforeLoadState, preCloneEntities);
        All.Add(saveLoadAction);
        return saveLoadAction;
    }

    private static SlAction CreateSlAction(Action<Dictionary<Type, Dictionary<string, object>>, Level> action) {
        return (SlAction)action?.Method.CreateDelegate(typeof(SlAction), action.Target);
    }

    public static bool Remove(SaveLoadAction saveLoadAction) {
        return All.Remove(saveLoadAction);
    }

    internal static void OnSaveState(Level level) {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.saveState?.Invoke(saveLoadAction.savedValues, level);
        }
    }

    internal static void OnLoadState(Level level) {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.loadState?.Invoke(saveLoadAction.savedValues, level);
        }
    }

    internal static void OnClearState() {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.savedValues.Clear();
            saveLoadAction.clearState?.Invoke();
        }
    }

    internal static void OnBeforeSaveState(Level level) {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.beforeSaveState?.Invoke(level);
        }
    }

    internal static void OnBeforeLoadState(Level level) {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.beforeLoadState?.Invoke(level);
        }
    }

    internal static void OnPreCloneEntities() {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.preCloneEntities?.Invoke();
        }
    }

    internal static void OnUnloadLevel(Level level, List<Entity> entities, Entity entity) {
        foreach (SaveLoadAction saveLoadAction in All) {
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
                } else {
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
                    } else {
                        type.SetPropertyValue(memberName, pair.Value[memberName].DeepCloneShared());
                    }
                }
            }
        }
    }

    [Unload]
    private static void Unload() {
        All.Clear();
    }

    public static void InitActions() {
        if (initialized) {
            return;
        }

        initialized = true;
        InitFields();
        SupportSimpleStaticFields();
        SupportModModuleFields();
        FixSaveLoadIcon();
        BetterCasualPlay();
        SupportExternalMember();
        SupportCalcRandom();
        SupportMInput();
        SupportInput();
        SupportAudioMusic();
        FixVertexLight();
        MuteAudioUtils.AddAction();
        ExternalAction();
        SupportModSessionAndSaveData();
        SupportMaxHelpingHand();
        SupportPandorasBox();
        SupportCrystallineHelper();
        SupportSpringCollab2020();
        SupportExtendedVariants();
        SupportXaphanHelper();
        SupportIsaGrabBag();
        SupportSpirialisHelper();
        DeathTrackerHelper.AddSupport();
        SupportCommunalHelper();
        SupportBrokemiaHelper();
        FrostHelperUtils.SupportFrostHelper();
        SupportVivHelper();

        // 放最后，确保收集了所有克隆的 VirtualAssets 与 EventInstance
        ReloadVirtualAssets();
        ReleaseEventInstances();
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

    private static void FilterStaticFields() {
        // 过滤掉非法的字段
        // 例如未安装 DJMapHelper 时 ExtendedVariantsMode 的 AutoDestroyingReverseOshiroModder.stateMachine
        foreach (Type type in simpleStaticFields.Keys.ToArray()) {
            FieldInfo[] fieldInfos = simpleStaticFields[type].Where(info => {
                try {
                    info.GetValue(null);
                    return true;
                } catch (TargetInvocationException) {
                    return false;
                }
            }).ToArray();

            if (fieldInfos.Length > 0) {
                simpleStaticFields[type] = fieldInfos;
            } else {
                simpleStaticFields.Remove(type);
            }
        }
    }

    private static void InitModuleFields() {
        foreach (Type type in Everest.Modules.Select(module => module.GetType())) {
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
                } else {
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

    private static void SupportSimpleStaticFields() {
        SafeAdd(
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
        SafeAdd(
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
        SafeAdd(loadState: (_, _) => {
            // 修复右下角存档图标残留
            if (!UserIO.savingInternal) {
                SaveLoadIcon.Hide();
            }
        });
    }

    private static void BetterCasualPlay() {
        SafeAdd(beforeSaveState: level => {
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
        SafeAdd(
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
        SafeAdd(
            (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(Calc),
                nameof(Calc.Random), nameof(Calc.randomStack)),
            (savedValues, _) => LoadStaticMemberValues(savedValues));
    }

    private static void SupportMInput() {
        SafeAdd(
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
        SafeAdd(
            (savedValues, _) => {
                SaveStaticMemberValues(savedValues, typeof(MInput), nameof(MInput.VirtualInputs));

                Dictionary<string, object> inputDict = new();
                foreach (FieldInfo fieldInfo in typeof(Input).GetFields(BindingFlags.Public | BindingFlags.Static).Where(info =>
                             info.FieldType.IsSameOrSubclassOf(typeof(VirtualInput)))) {
                    inputDict[fieldInfo.Name] = fieldInfo.GetValue(null);
                }

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
                    } else {
                        object fieldValue = inputType.GetFieldValue(fieldName);

                        if (fieldValue is VirtualJoystick virtualJoystick &&
                            virtualInput is VirtualJoystick savedVirtualJoystick) {
                            virtualJoystick.InvertedX = savedVirtualJoystick.InvertedX;
                            virtualJoystick.InvertedY = savedVirtualJoystick.InvertedY;
                        } else if (fieldValue is VirtualIntegerAxis virtualIntegerAxis &&
                                   virtualInput is VirtualIntegerAxis savedVirtualIntegerAxis) {
                            virtualIntegerAxis.Inverted = savedVirtualIntegerAxis.Inverted;
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
        SafeAdd(
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

                // 冲刺残影方向错误，干脆移除屏幕不显示了
                level.Tracker.GetEntities<TrailManager.Snapshot>()
                    .ForEach(entity => entity.Position = level.Camera.Position - Vector2.One * 100);

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
        SafeAdd(
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
        SafeAdd(
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
        SafeAdd(loadState: (_, level) => {
            VertexLight[] lights = level.Lighting.lights;
            for (int i = 0; i < lights.Length; i++) {
                if (lights[i] is { } light) {
                    light.Index = -1;
                    lights[i] = null;
                }
            }
        });
    }

    private static void SupportPandorasBox() {
        // TimeField.targetPlayer 和 TimeField.lingeringTarget 等
        // WeakReference<T> 类型的实例在 SL 多次并且内存回收之后 target 可能会指向错误的对象，原因未知
        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.TimeField") is { } timeFieldType
            && Delegate.CreateDelegate(typeof(On.Celeste.Player.hook_Update), timeFieldType.GetMethodInfo("PlayerUpdateHook")) is
                On.Celeste.Player.hook_Update hookUpdate) {
            SafeAdd(
                loadState: (_, _) => {
                    if (timeFieldType.GetFieldValue<bool>("hookAdded")) {
                        On.Celeste.Player.Update -= hookUpdate;
                        On.Celeste.Player.Update += hookUpdate;
                    } else {
                        On.Celeste.Player.Update -= hookUpdate;
                    }
                }
            );
        }

        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.MarioClearPipeHelper") is { } pipeHelper) {
            if (pipeHelper.GetFieldInfo("CurrentlyTransportedEntities") != null) {
                SafeAdd(
                    (savedValues, _) => SaveStaticMemberValues(savedValues, pipeHelper, "CurrentlyTransportedEntities"),
                    (savedValues, _) => LoadStaticMemberValues(savedValues)
                );
            }

            if (pipeHelper.GetMethodInfo("AllowComponentsForList") != null && pipeHelper.GetMethodInfo("ShouldAddComponentsForList") != null) {
                SafeAdd((_, level) => {
                    if (pipeHelper.InvokeMethod("ShouldAddComponentsForList", level.Entities) as bool? == true) {
                        pipeHelper.InvokeMethod("AllowComponentsForList", StateManager.Instance.SavedLevel.Entities);
                    }
                }, (_, level) => {
                    if (pipeHelper.InvokeMethod("ShouldAddComponentsForList", StateManager.Instance.SavedLevel.Entities) as bool? == true) {
                        pipeHelper.InvokeMethod("AllowComponentsForList", level.Entities);
                    }
                });
            }
        }

        // Fixed: Game crashes after save DustSpriteColorController
        SafeAdd(
            (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(DustStyles), "Styles"),
            (savedValues, _) => LoadStaticMemberValues(savedValues)
        );
    }

    private static void SupportMaxHelpingHand() {
        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController") is { } colorControllerType
            && colorControllerType.GetFieldInfo("rainbowSpinnerHueHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (colorControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorAreaController") is { } colorAreaControllerType
            && colorAreaControllerType.GetFieldInfo("rainbowSpinnerHueHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue2
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (colorAreaControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue2;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue2;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue2;
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.SeekerBarrierColorController") is
                { } seekerBarrierColorControllerType
            && seekerBarrierColorControllerType.GetFieldInfo("seekerBarrierRendererHooked") != null
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (seekerBarrierColorControllerType.GetFieldValue<bool>("seekerBarrierRendererHooked")) {
                        seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                        seekerBarrierColorControllerType.InvokeMethod("hookSeekerBarrierRenderer");
                    } else {
                        seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger") is { } gradientDustTriggerType
            && gradientDustTriggerType.GetFieldInfo("hooked") != null
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (gradientDustTriggerType.GetFieldValue<bool>("hooked")) {
                        gradientDustTriggerType.InvokeMethod("unhook");
                        gradientDustTriggerType.InvokeMethod("hook");
                    } else {
                        // hooked 为 true 时，unhook 方法才能够正常执行
                        gradientDustTriggerType.SetFieldValue("hooked", true);
                        gradientDustTriggerType.InvokeMethod("unhook");
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.ParallaxFadeOutController") is { } parallaxFadeOutControllerType
            && parallaxFadeOutControllerType.GetFieldInfo("backdropRendererHooked") != null
            && Delegate.CreateDelegate(typeof(ILContext.Manipulator),
                parallaxFadeOutControllerType.GetMethodInfo("onBackdropRender")) is ILContext.Manipulator onBackdropRender
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (parallaxFadeOutControllerType.GetFieldValue<bool>("backdropRendererHooked")) {
                        IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                        IL.Celeste.BackdropRenderer.Render += onBackdropRender;
                    } else {
                        IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                    }
                }
            );
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.ParallaxFadeSpeedController") is
                { } parallaxFadeSpeedControllerType
            && parallaxFadeSpeedControllerType.GetFieldInfo("backdropHooked") != null
            && Delegate.CreateDelegate(typeof(ILContext.Manipulator),
                parallaxFadeSpeedControllerType.GetMethodInfo("modBackdropUpdate")) is ILContext.Manipulator modBackdropUpdate
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (parallaxFadeSpeedControllerType.GetFieldValue<bool>("backdropHooked")) {
                        IL.Celeste.Parallax.Update -= modBackdropUpdate;
                        IL.Celeste.Parallax.Update += modBackdropUpdate;
                    } else {
                        IL.Celeste.Parallax.Update -= modBackdropUpdate;
                    }
                }
            );
        }

        CloneModTypeFields("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Effects.BlackholeCustomColors", "colorsMild");
    }

    private static void SupportCrystallineHelper() {
        CloneModTypeFields("CrystallineHelper", "vitmod.VitModule", "timeStopScaleTimer", "timeStopType", "noMoveScaleTimer");
        CloneModTypeFields("CrystallineHelper", "vitmod.TriggerTrigger", "collidedEntities");
    }

    private static void SupportSpringCollab2020() {
        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController") is { } colorControllerType
            && colorControllerType.GetFieldInfo("colorControllerType") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (colorControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                    }
                }
            );
        }

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController") is
                { } colorAreaControllerType
            && colorAreaControllerType.GetFieldInfo("rainbowSpinnerHueHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookSpinnerGetHue
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (colorAreaControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookSpinnerGetHue;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                    }
                }
            );
        }

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.SpikeJumpThroughController") is
                { } spikeJumpThroughControllerType
            && spikeJumpThroughControllerType.GetFieldInfo("SpikeHooked") != null
            && Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide),
                spikeJumpThroughControllerType.GetMethodInfo("OnCollideHook")) is On.Celeste.Spikes.hook_OnCollide onCollideHook
           ) {
            SafeAdd(
                loadState: (_, _) => {
                    if (spikeJumpThroughControllerType.GetFieldValue<bool>("SpikeHooked")) {
                        On.Celeste.Spikes.OnCollide -= onCollideHook;
                        On.Celeste.Spikes.OnCollide += onCollideHook;
                    } else {
                        On.Celeste.Spikes.OnCollide -= onCollideHook;
                    }
                }
            );
        }
    }

    private static void SupportExtendedVariants() {
        // 静态字段在 InitExtendedVariantsFields() 中处理了

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule") is not { } moduleType) {
            return;
        }

        // 修复玩家死亡后不会重置设置
        SafeAdd((savedValues, _) => {
            if (Everest.Modules.FirstOrDefault(everestModule => everestModule.Metadata?.Name == "ExtendedVariantMode") is { } module &&
                module.GetFieldValue("TriggerManager") is { } triggerManager) {
                savedValues[moduleType] = new Dictionary<string, object> {{"TriggerManager", triggerManager.DeepCloneShared()}};
            }
        }, (savedValues, _) => {
            if (savedValues.TryGetValue(moduleType, out Dictionary<string, object> dictionary) &&
                dictionary.TryGetValue("TriggerManager", out object savedTriggerManager) &&
                Everest.Modules.FirstOrDefault(everestModule => everestModule.Metadata?.Name == "ExtendedVariantMode") is { } module) {
                if (module.GetFieldValue("TriggerManager") is { } triggerManager) {
                    savedTriggerManager.DeepCloneToShared(triggerManager);
                }
            }
        });

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsSettings") is not { } settingsType) {
            return;
        }

        List<PropertyInfo> settingProperties = settingsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead
                               && property.CanWrite
                               && property.GetCustomAttribute<SettingIgnoreAttribute>() != null
                               && !property.Name.StartsWith("Display")
            ).ToList();

        SafeAdd(
            (savedValues, _) => {
                if (moduleType.GetPropertyValue("Settings") is not { } settingsInstance) {
                    return;
                }

                Dictionary<string, object> dict = new();
                foreach (PropertyInfo property in settingProperties) {
                    dict[property.Name] = property.GetValue(settingsInstance);
                }

                savedValues[settingsType] = dict.DeepCloneShared();
            },
            (savedValues, _) => {
                // 本来打算将关卡中 ExtendedVariantsTrigger 涉及相关的值强制 SL，想想还是算了
                if (!ModSettings.SaveExtendedVariants && !StateManager.Instance.SavedByTas) {
                    return;
                }

                if (moduleType.GetPropertyValue("Settings") is not { } settingsInstance) {
                    return;
                }

                if (savedValues.TryGetValue(settingsType, out Dictionary<string, object> dict)) {
                    dict = dict.DeepCloneShared();
                    foreach (string propertyName in dict.Keys) {
                        settingsInstance.SetPropertyValue(propertyName, dict[propertyName]);
                    }
                }
            });
    }

    private static void SupportXaphanHelper() {
        CloneModTypeFields("XaphanHelper", "Celeste.Mod.XaphanHelper.Upgrades.SpaceJump", "jumpBuffer");
    }

    private static void SupportIsaGrabBag() {
        // 解决 v1.6.0 之前的版本读档后影像残留在屏幕中
        if (ModUtils.GetModule("IsaGrabBag") is { } module && module.Metadata.Version < new Version(1, 6, 0) &&
            ModUtils.GetType("IsaGrabBag", "Celeste.Mod.IsaGrabBag.DreamSpinnerBorder") is { } borderType) {
            SafeAdd(
                loadState: (_, level) => level.Entities.FirstOrDefault(entity => entity.GetType() == borderType)?.Update()
            );
        }

        // 解决读档后冲进 DreamSpinner 会被刺死
        CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.GrabBagModule", "ZipLineState", "playerInstance");
        CloneModTypeFields("IsaGrabBag", "Celeste.Mod.IsaGrabBag.BadelineFollower", "booster", "LookForBubble");
    }

    private static void SupportSpirialisHelper() {
        CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.TimePlayerSettings", "instance", "stoppedX", "stoppedY");
        CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.CustomRainBG", "timeSinceFreeze");
        CloneModTypeFields("SpirialisHelper", "Celeste.Mod.Spirialis.BoostCapModifier", "xCap", "yCap");

        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeController") is { } timeControllerType) {
            var action = SafeAdd(
                loadState: (_, level) => {
                    if (level.Entities.FirstOrDefault(entity => entity.GetType() == timeControllerType) is not { } timeController) {
                        return;
                    }

                    if (Delegate.CreateDelegate(typeof(ILContext.Manipulator), timeController, timeControllerType.GetMethodInfo("CustomLevelRender"))
                        is not ILContext.Manipulator manipulator) {
                        return;
                    }

                    if (Delegate.CreateDelegate(typeof(On.Monocle.EntityList.hook_Update), timeController,
                            timeControllerType.GetMethodInfo("CustomELUpdate")) is not On.Monocle.EntityList.hook_Update customELUpdate) {
                        return;
                    }

                    IL.Celeste.Level.Render += manipulator;
                    if (timeController.GetFieldValue<bool>("hookAdded")) {
                        On.Monocle.EntityList.Update += customELUpdate;
                    }
                }
            );

            ((SaveLoadAction)action).unloadLevel = (_, entities, entity) => {
                if (entity.GetType() == timeControllerType) {
                    entities.Add(entity);
                }
            };
        }

        if (ModUtils.GetType("SpirialisHelper", "Celeste.Mod.Spirialis.TimeZipMover") is { } timeZipMoverType) {
            if (typeof(ZipMover).GetMethod("Sequence", BindingFlags.Instance | BindingFlags.NonPublic).GetStateMachineTarget() is not
                { } sequenceMethodInfo) {
                return;
            }

            SafeAdd(
                loadState: (_, level) => {
                    if (!level.Tracker.Entities.TryGetValue(timeZipMoverType, out List<Entity> zips)) {
                        return;
                    }

                    foreach (Entity entity in zips) {
                        if (Delegate.CreateDelegate(typeof(ILContext.Manipulator), entity, timeZipMoverType.GetMethodInfo("ZipSequence")) is
                            ILContext.Manipulator manipulator) {
                            entity.SetFieldValue("TimeStreetlightUpdate", new ILHook(sequenceMethodInfo, manipulator));
                        }
                    }
                }
            );
        }
    }

    private static void SupportCommunalHelper() {
        CloneModTypeFields("CommunalHelper", "Celeste.Mod.CommunalHelper.DashStates.DreamTunnelDash",
            "StDreamTunnelDash",
            "hasDreamTunnelDash",
            "dreamTunnelDashAttacking",
            "dreamTunnelDashTimer",
            "nextDashFeather",
            "FeatherMode",
            "overrideDreamDashCheck",
            "DreamTrailColorIndex");

        CloneModTypeFields("CommunalHelper", "Celeste.Mod.CommunalHelper.DashStates.SeekerDash",
            "hasSeekerDash",
            "seekerDashAttacking",
            "seekerDashTimer",
            "seekerDashLaunched",
            "launchPossible");
    }

    private static void SupportBrokemiaHelper() {
        if (ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.Vineinator") is { } vineinatorType &&
            ModUtils.GetType("BrokemiaHelper", "BrokemiaHelper.PixelRendered.RWLizard") is { } lizardType) {
            SafeAdd(
                loadState: (_, level) => {
                    foreach (Entity entity in level.Entities) {
                        Type type = entity.GetType();
                        if (type == vineinatorType || type == lizardType) {
                            object pixelComponent = entity.GetFieldValue("pixelComponent");
                            pixelComponent.SetFieldValue("textureChunks", null);
                            pixelComponent.InvokeMethod("CommitChunks");
                        }
                    }
                });
        }
    }

    private static void SupportVivHelper() {
        if (ModUtils.GetAssembly("VivHelper") is not { } vivHelper) {
            return;
        }

        CloneModTypeFields("VivHelper", "VivHelper.Entities.RefillCancel", "inSpace", "DashRefillRestrict", "DashRestrict", "StaminaRefillRestrict", "p");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.SpeedPowerup", "Store", "Launch");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.BooMushroom", "color", "mode");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.BoostFunctions", "dyn");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.OrangeBoost", "timer");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.PinkBoost", "timer");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Boosters.WindBoost", "timer");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.ExplodeLaunchModifier", "DisableFreeze", "DetectFreeze", "bumperWrapperType");
        CloneModTypeFields("VivHelper", "VivHelper.Entities.Blockout", "alphaFade");
        CloneModTypeFields("VivHelper", "VivHelper.MoonHooks", "FloatyFix");
        CloneModTypeFields("VivHelper", "VivHelper.HelperEntities", "AllUpdateHelperEntity");
        CloneModTypeFields("VivHelper", "VivHelper.Module__Extensions__Etc.TeleportV2Hooks", "HackedFocusPoint");
    }

    private static void ReloadVirtualAssets() {
        SafeAdd(
            loadState: (_, _) => {
                foreach (VirtualAsset virtualAsset in VirtualAssets) {
                    switch (virtualAsset) {
                        case VirtualTexture {IsDisposed: true} virtualTexture:
                            // Fix: 全屏切换然后读档煤球红边消失
                            if (!virtualTexture.Name.StartsWith("dust-noise-")) {
                                virtualTexture.Reload();
                            }

                            break;
                        case VirtualRenderTarget {IsDisposed: true} virtualRenderTarget:
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
        SafeAdd(
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

    private static void CloneModTypeFields(string modName, string typeFullName, params string[] fields) {
        if (ModUtils.GetType(modName, typeFullName) is { } modType) {
            SafeAdd(
                (savedValues, _) => SaveStaticMemberValues(savedValues, modType, fields),
                (savedValues, _) => LoadStaticMemberValues(savedValues));
        }
    }
}