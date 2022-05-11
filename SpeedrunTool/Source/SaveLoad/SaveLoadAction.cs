using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.Helpers;
using Celeste.Mod.SpeedrunTool.DeathStatistics;
using Celeste.Mod.SpeedrunTool.RoomTimer;
using Celeste.Mod.SpeedrunTool.Utils;
using FMOD;
using FMOD.Studio;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public sealed class SaveLoadAction {
    public delegate void SlAction(Dictionary<Type, Dictionary<string, object>> savedValues, Level level);

    public static readonly List<VirtualAsset> VirtualAssets = new();

    private static readonly List<SaveLoadAction> All = new();

    private static Dictionary<Type, FieldInfo[]> simpleStaticFields;
    private static Dictionary<Type, FieldInfo[]> modModuleFields;

    private static readonly HashSet<string> RequireMuteAudioPaths = new() {
        "event:/game/general/strawberry_get",
        "event:/game/general/strawberry_laugh",
        "event:/game/general/strawberry_flyaway",
        "event:/game/general/seed_complete_main",
        "event:/game/general/key_get",
        "event:/game/general/cassette_get",
        "event:/game/05_mirror_temple/eyewall_destroy",
        "event:/char/badeline/boss_hug",
        "event:/char/badeline/boss_laser_fire",
    };

    private static readonly List<EventInstance> RequireMuteAudios = new();
    private readonly Action clearState;
    private readonly Action<Level> beforeSaveState;
    private readonly Action preCloneEntities;
    private readonly SlAction loadState;

    private readonly Dictionary<Type, Dictionary<string, object>> savedValues = new();
    private readonly SlAction saveState;

    public SaveLoadAction(SlAction saveState = null, SlAction loadState = null, Action clearState = null,
        Action<Level> beforeSaveState = null, Action preCloneEntities = null) {
        this.saveState = saveState;
        this.loadState = loadState;
        this.clearState = clearState;
        this.beforeSaveState = beforeSaveState;
        this.preCloneEntities = preCloneEntities;
    }

    public static void Add(SaveLoadAction saveLoadAction) {
        All.Add(saveLoadAction);
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

        RequireMuteAudios.Clear();
    }

    internal static void OnBeforeSaveState(Level level) {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.beforeSaveState?.Invoke(level);
        }
    }

    internal static void OnPreCloneEntities() {
        foreach (SaveLoadAction saveLoadAction in All) {
            saveLoadAction.preCloneEntities?.Invoke();
        }
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static void SaveStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values, Type type, params string[] memberNames) {
        if (type == null) {
            return;
        }

        if (!values.ContainsKey(type)) {
            values[type] = new Dictionary<string, object>();
        }

        foreach (string memberName in memberNames) {
            if (type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } memberInfos) {
                if (memberInfos.Length == 0) {
                    $"SaveStaticMemberValues: No member found for type {type.FullName} and member name {memberName}".Log();
                    continue;
                }

                if (memberInfos.First().IsField()) {
                    values[type][memberName] = type.GetFieldValue(memberName).DeepCloneShared();
                } else {
                    values[type][memberName] = type.GetPropertyValue(memberName).DeepCloneShared();
                }
            }
        }
    }

    // ReSharper disable once MemberCanBePrivate.Global
    private static void LoadStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values) {
        foreach (KeyValuePair<Type, Dictionary<string, object>> pair in values) {
            foreach (string memberName in pair.Value.Keys) {
                Type type = pair.Key;
                if (type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } memberInfos) {
                    if (memberInfos.Length == 0) {
                        $"LoadStaticMemberValues: No member found for type {type.FullName} and member name {memberName}".Log();
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

    [Load]
    private static void Load() {
        On.FMOD.Studio.EventDescription.createInstance += EventDescriptionOnCreateInstance;
    }

    [Unload]
    private static void Unload() {
        All.Clear();
        On.FMOD.Studio.EventDescription.createInstance -= EventDescriptionOnCreateInstance;
    }

    // 第一次 SL 时才初始化，避免通过安装依赖功能解除禁用的 Mod 被忽略
    private static bool initialized;

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
        MuteAnnoyingAudios();
        ExternalAction();
        SupportModSessionAndSaveData();
        SupportMaxHelpingHand();
        SupportPandorasBox();
        SupportCrystallineHelper();
        SupportSpringCollab2020();
        SupportExtendedVariants();
        SupportXaphanHelper();
        SupportIsaGrabBag();
        SupportDeathTracker();
        SupportCommunalHelper();
        SupportBounceHelper();

        // 放最后，确保收集了所有克隆的 VirtualAssets
        ReloadVirtualAssets();
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
                    return !info.IsConst() && fieldType.IsSimpleClass(_ =>
                        fieldType == type
                        || fieldType == typeof(Level)
                        || fieldType == typeof(MTexture)
                        || fieldType == typeof(CrystalStaticSpinner)
                        || fieldType == typeof(Solid)
                        || fieldType.IsSubclassOf(typeof(VirtualAsset))
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
            foreach (FieldInfo fieldInfo in fieldInfos.Where(info => !info.IsInitOnly && (info.FieldType == typeof(Level) || info.FieldType == typeof(Session)))) {
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
        Add(new SaveLoadAction(
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
        ));
    }

    private static void SupportModModuleFields() {
        Add(new SaveLoadAction(
            (savedValues, _) => {
                foreach (EverestModule module in Everest.Modules) {
                    Dictionary<string, object> dict = new();
                    Type moduleType = module.GetType();
                    if (modModuleFields.ContainsKey(moduleType)) {
                        foreach (FieldInfo fieldInfo in modModuleFields[moduleType]) {
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
            }));
    }

    private static void FixSaveLoadIcon() {
        Add(new SaveLoadAction(loadState: (_, _) => {
            // 修复右下角存档图标残留
            if (!typeof(UserIO).GetFieldValue<bool>("savingInternal")) {
                SaveLoadIcon.Hide();
            }
        }));
    }

    private static void BetterCasualPlay() {
        Add(new SaveLoadAction(beforeSaveState: level => {
            if (StateManager.Instance.SavedByTas) {
                return;
            }

            // 移除冻结帧，移除暂停帧，移除暂停黑屏
            Engine.FreezeTimer = 0f;
            level.SetFieldValue("unpauseTimer", 0f);
            level.HudRenderer.BackgroundFade = 0f;
        }));
    }

    private static void SupportExternalMember() {
        Add(new SaveLoadAction(
            (savedValues, _) => {
                SaveStaticMemberValues(savedValues, typeof(Engine), "DashAssistFreeze", "DashAssistFreezePress", "DeltaTime", "FrameCounter",
                    "FreezeTimer", "RawDeltaTime", "TimeRate", "TimeRateB", "Pooler");
                SaveStaticMemberValues(savedValues, typeof(Glitch), "Value");
                SaveStaticMemberValues(savedValues, typeof(Distort), "Anxiety", "GameRate");
                SaveStaticMemberValues(savedValues, typeof(ScreenWipe), "WipeColor");
                SaveStaticMemberValues(savedValues, typeof(Audio), "currentCamera");
            },
            (savedValues, _) => LoadStaticMemberValues(savedValues)));
    }

    private static void SupportCalcRandom() {
        Type type = typeof(Calc);
        Add(new SaveLoadAction(
            (savedValues, _) => SaveStaticMemberValues(savedValues, type, "Random", "randomStack"),
            (savedValues, _) => LoadStaticMemberValues(savedValues)));
    }

    private static void SupportMInput() {
        Add(new SaveLoadAction(
            (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(MInput), "Active", "Disabled", "Keyboard", "Mouse", "GamePads"),
            (savedValues, _) => {
                LoadStaticMemberValues(savedValues);
                Input.RumbleSpecific(0f, 0f);
            }));
    }

    private static void SupportInput() {
        Type type = typeof(Input);
        Add(new SaveLoadAction(
            (savedValues, level) => {
                Dictionary<string, object> dictionary = new();
                foreach (FieldInfo fieldInfo in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(info =>
                             info.FieldType == typeof(VirtualJoystick) || info.FieldType == typeof(VirtualIntegerAxis))) {
                    if (fieldInfo.GetValue(null) is VirtualJoystick virtualJoystick) {
                        dictionary[fieldInfo.Name] = virtualJoystick;
                    } else if (fieldInfo.GetValue(null) is VirtualIntegerAxis virtualIntegerAxis) {
                        dictionary[fieldInfo.Name] = virtualIntegerAxis;
                    }
                }

                savedValues[type] = dictionary.DeepCloneShared();
            }, (savedValues, level) => {
                Dictionary<string, object> dictionary = savedValues[type];
                foreach (string fieldName in dictionary.Keys) {
                    if (type.GetFieldValue(fieldName) is VirtualJoystick virtualJoystick &&
                        dictionary[fieldName] is VirtualJoystick savedVirtualJoystick) {
                        virtualJoystick.InvertedX = savedVirtualJoystick.InvertedX;
                        virtualJoystick.InvertedY = savedVirtualJoystick.InvertedY;
                    } else if (type.GetFieldValue(fieldName) is VirtualIntegerAxis virtualIntegerAxis &&
                               dictionary[fieldName] is VirtualIntegerAxis savedVirtualIntegerAxis) {
                        virtualIntegerAxis.Inverted = savedVirtualIntegerAxis.Inverted;
                    }
                }
            }
        ));
    }

    private static void MuteAnnoyingAudios() {
        Add(new SaveLoadAction(loadState: (_, level) => {
            level.Entities.FindAll<SoundEmitter>().ForEach(emitter => {
                if (emitter.Source.GetFieldValue("instance") is EventInstance eventInstance) {
                    eventInstance.setVolume(0f);
                }
            });

            foreach (EventInstance sfx in RequireMuteAudios) {
                sfx.setVolume(0f);
            }

            RequireMuteAudios.Clear();
        }));
    }

    private static void ExternalAction() {
        Add(new SaveLoadAction(
                saveState: (_, level) => {
                    level.Entities.UpdateLists();
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
                    RoomTimerManager.ClearPbTimes();
                    DeepClonerUtils.ClearSharedDeepCloneState();
                    DynDataUtils.ClearCached();
                },
                beforeSaveState: level => {
                    RoomTimerManager.ClearPbTimes(false);
                    DeepClonerUtils.ClearSharedDeepCloneState();
                    DynDataUtils.ClearCached();

                    IgnoreSaveLoadComponent.RemoveAll(level);
                    ClearBeforeSaveComponent.RemoveAll(level);

                    foreach (Entity entity in level.Entities.Where(entity =>
                                 entity.GetType().FullName?.StartsWith("Celeste.Mod.CelesteNet.") == true)) {
                        entity.RemoveSelf();
                    }

                    // 冲刺残影方向错误，干脆移除屏幕不显示了
                    level.Tracker.GetEntities<TrailManager.Snapshot>()
                        .ForEach(entity => entity.Position = level.Camera.Position - Vector2.One * 100);
                }
            )
        );
    }
    
    private static void SupportModSessionAndSaveData() {
        Add(new SaveLoadAction(
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
            }));
    }

    private static RESULT EventDescriptionOnCreateInstance(On.FMOD.Studio.EventDescription.orig_createInstance orig, EventDescription self,
        out EventInstance instance) {
        RESULT result = orig(self, out instance);

        if (StateManager.Instance.IsSaved && instance != null && self.getPath(out string path) == RESULT.OK && path != null &&
            RequireMuteAudioPaths.Contains(path)) {
            RequireMuteAudios.Add(instance);
        }

        return result;
    }

    private static void SupportAudioMusic() {
        Add(new SaveLoadAction(
            (savedValues, _) => {
                Dictionary<string, object> saved = new() {
                    {
                        "currentMusicEvent",
                        (typeof(Audio).GetFieldValue("currentMusicEvent") as EventInstance)?.NeedManualClone().DeepCloneShared()
                    },
                    {"CurrentAmbienceEventInstance", Audio.CurrentAmbienceEventInstance?.NeedManualClone().DeepCloneShared()}, {
                        "currentAltMusicEvent",
                        (typeof(Audio).GetFieldValue("currentAltMusicEvent") as EventInstance)?.NeedManualClone().DeepCloneShared()
                    },
                    {"MusicUnderwater", Audio.MusicUnderwater},
                    {"PauseMusic", Audio.PauseMusic},
                    {"PauseGameplaySfx", Audio.PauseGameplaySfx},
                };
                savedValues[typeof(Audio)] = saved;
            },
            (savedValues, level) => {
                Dictionary<string, object> saved = savedValues[typeof(Audio)];

                Audio.SetMusic(Audio.GetEventName(saved["currentMusicEvent"] as EventInstance));
                Audio.CurrentMusicEventInstance?.CopyParametersFrom(saved["currentMusicEvent"] as EventInstance);

                Audio.SetAmbience(Audio.GetEventName(saved["CurrentAmbienceEventInstance"] as EventInstance));
                Audio.CurrentAmbienceEventInstance?.CopyParametersFrom(saved["CurrentAmbienceEventInstance"] as EventInstance);

                Audio.SetAltMusic(Audio.GetEventName(saved["currentAltMusicEvent"] as EventInstance));
                (typeof(Audio).GetFieldValue("currentAltMusicEvent") as EventInstance)?.CopyParametersFrom(
                    saved["currentAltMusicEvent"] as EventInstance);

                Audio.MusicUnderwater = (bool)saved["MusicUnderwater"];
                Audio.PauseMusic = (bool)saved["PauseMusic"];
                Audio.PauseGameplaySfx = (bool)saved["PauseGameplaySfx"];
                if (!level.Paused && Level._PauseSnapshot != null) {
                    Audio.ReleaseSnapshot(Level._PauseSnapshot);
                    typeof(Level).SetFieldValue("PauseSnapshot", null);
                }
            }
        ));
    }

    private static void SupportPandorasBox() {
        // 部分支持，因为 TimeField.targetPlayer 和 TimeField.lingeringTarget 未进行 SL
        // 之所以不处理该字段是因为 WeakReference<T> 类型的实例在 SL 多次并且内存回收之后 target 可能会指向错误的对象，原因未知
        if (ModUtils.GetType("PandorasBox", "Celeste.Mod.PandorasBox.TimeField") is { } timeFieldType
            && Delegate.CreateDelegate(typeof(On.Celeste.Player.hook_Update), timeFieldType.GetMethodInfo("PlayerUpdateHook")) is
                On.Celeste.Player.hook_Update hookUpdate) {
            Add(new SaveLoadAction(
                loadState: (_, _) => {
                    if (timeFieldType.GetFieldValue<bool>("hookAdded")) {
                        On.Celeste.Player.Update -= hookUpdate;
                        On.Celeste.Player.Update += hookUpdate;
                    } else {
                        On.Celeste.Player.Update -= hookUpdate;
                    }
                }
            ));
        }

        // Fixed: Game crashes after save DustSpriteColorController
        Add(new SaveLoadAction(
            (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(DustStyles), "Styles"),
            (savedValues, _) => LoadStaticMemberValues(savedValues)
        ));
    }

    private static void SupportMaxHelpingHand() {
        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController") is { } colorControllerType
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
           ) {
            Add(new SaveLoadAction(
                loadState: (_, _) => {
                    if (colorControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                    }
                }
            ));
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.SeekerBarrierColorController") is
            { } seekerBarrierColorControllerType) {
            Add(new SaveLoadAction(
                loadState: (savedValues, _) => {
                    if (seekerBarrierColorControllerType.GetFieldValue<bool>("seekerBarrierRendererHooked")) {
                        seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                        seekerBarrierColorControllerType.InvokeMethod("hookSeekerBarrierRenderer");
                    } else {
                        seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                    }
                }
            ));
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger") is { } gradientDustTriggerType) {
            Add(new SaveLoadAction(
                loadState: (savedValues, _) => {
                    if (gradientDustTriggerType.GetFieldValue<bool>("hooked")) {
                        gradientDustTriggerType.InvokeMethod("unhook");
                        gradientDustTriggerType.InvokeMethod("hook");
                    } else {
                        // hooked 为 true 时，unhook 方法才能够正常执行
                        gradientDustTriggerType.SetFieldValue("hooked", true);
                        gradientDustTriggerType.InvokeMethod("unhook");
                    }
                }
            ));
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.ParallaxFadeOutController") is { } parallaxFadeOutControllerType
            && Delegate.CreateDelegate(typeof(ILContext.Manipulator),
                parallaxFadeOutControllerType.GetMethodInfo("onBackdropRender")) is ILContext.Manipulator onBackdropRender
           ) {
            Add(new SaveLoadAction(
                loadState: (savedValues, _) => {
                    if (parallaxFadeOutControllerType.GetFieldValue<bool>("backdropRendererHooked")) {
                        IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                        IL.Celeste.BackdropRenderer.Render += onBackdropRender;
                    } else {
                        IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                    }
                }
            ));
        }

        if (ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Effects.BlackholeCustomColors") is { } blackHoleCustomColorsType) {
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, blackHoleCustomColorsType, "colorsMild"),
                (savedValues, _) => LoadStaticMemberValues(savedValues)));
        }
    }

    private static void SupportCrystallineHelper() {
        Type vitModuleType = ModUtils.GetType("CrystallineHelper", "vitmod.VitModule");
        if (vitModuleType == null) {
            return;
        }

        Add(new SaveLoadAction(
            (savedValues, _) => SaveStaticMemberValues(savedValues, vitModuleType, "timeStopScaleTimer", "noMoveScaleTimer"),
            (savedValues, _) => LoadStaticMemberValues(savedValues)
        ));
    }

    private static void SupportSpringCollab2020() {
        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController") is { } colorControllerType
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
           ) {
            Add(new SaveLoadAction(
                loadState: (savedValues, _) => {
                    if (colorControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                    }
                }
            ));
        }

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController") is
                { } colorAreaControllerType
            && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                    colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                On.Celeste.CrystalStaticSpinner.hook_GetHue hookSpinnerGetHue
           ) {
            Add(new SaveLoadAction(
                loadState: (savedValues, _) => {
                    if (colorAreaControllerType.GetFieldValue<bool>("rainbowSpinnerHueHooked")) {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                        On.Celeste.CrystalStaticSpinner.GetHue += hookSpinnerGetHue;
                    } else {
                        On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                    }
                }
            ));
        }

        if (ModUtils.GetType("SpringCollab2020", "Celeste.Mod.SpringCollab2020.Entities.SpikeJumpThroughController") is
                { } spikeJumpThroughControllerType
            && Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide),
                spikeJumpThroughControllerType.GetMethodInfo("OnCollideHook")) is On.Celeste.Spikes.hook_OnCollide onCollideHook
           ) {
            Add(new SaveLoadAction(
                loadState: (savedValues, _) => {
                    if (spikeJumpThroughControllerType.GetFieldValue<bool>("SpikeHooked")) {
                        On.Celeste.Spikes.OnCollide -= onCollideHook;
                        On.Celeste.Spikes.OnCollide += onCollideHook;
                    } else {
                        On.Celeste.Spikes.OnCollide -= onCollideHook;
                    }
                }
            ));
        }
    }

    private static void SupportExtendedVariants() {
        // 静态字段在 InitExtendedVariantsFields() 中处理了

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsModule") is not { } moduleType) {
            return;
        }

        if (ModUtils.GetType("ExtendedVariantMode", "ExtendedVariants.Module.ExtendedVariantsSettings") is not { } settingsType) {
            return;
        }

        List<PropertyInfo> settingProperties = settingsType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead
                               && property.CanWrite
                               && property.GetCustomAttribute<SettingIgnoreAttribute>() != null
                               && !property.Name.StartsWith("Display")
            ).ToList();

        Add(new SaveLoadAction(
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
            }));
    }

    private static void SupportXaphanHelper() {
        if (ModUtils.GetType("XaphanHelper", "Celeste.Mod.XaphanHelper.Upgrades.SpaceJump") is { } spaceJumpType) {
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, spaceJumpType, "jumpBuffer"),
                (savedValues, _) => LoadStaticMemberValues(savedValues))
            );
        }
    }

    private static void SupportIsaGrabBag() {
        // 解决 DreamSpinnerBorder 读档后影像残留在屏幕中
        if (ModUtils.GetType("IsaGrabBag", "Celeste.Mod.IsaGrabBag.DreamSpinnerBorder") is { } borderType) {
            Add(new SaveLoadAction(
                    loadState: (_, level) => level.Entities.FirstOrDefault(entity => entity.GetType() == borderType)?.Update()
                )
            );
        }

        // 解决读档后冲进 DreamSpinner 会被刺死
        if (ModUtils.GetType("IsaGrabBag", "Celeste.Mod.IsaGrabBag.GrabBagModule") is { } grabBagModuleType) {
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, grabBagModuleType, "ZipLineState",
                    "playerInstance"),
                (savedValues, _) => LoadStaticMemberValues(savedValues))
            );
        }
    }

    private static void SupportDeathTracker() {
        if (ModUtils.GetType("DeathTracker", "CelesteDeathTracker.DeathTrackerModule+<>c__DisplayClass6_0")?.GetMethodInfo("<Load>b__2") is {} modPlayerSpawn &&
            ModUtils.GetType("DeathTracker", "CelesteDeathTracker.DeathDisplay") is {} deathDisplayType) {
            modPlayerSpawn.ILHook((ilCursor, _) => {
                // display => player.Scene.Entities.FindFirst<DeathDisplay>()
                if (ilCursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Ldarg_0,
                        ins => ins.OpCode == OpCodes.Ldfld && ins.Operand.ToString().EndsWith("::display"))) {
                    ilCursor.Emit(OpCodes.Pop)
                        .Emit(OpCodes.Ldarg_1)
                        .Emit(OpCodes.Callvirt, typeof(Entity).GetProperty("Scene").GetGetMethod())
                        .Emit(OpCodes.Callvirt, typeof(Scene).GetProperty("Entities").GetGetMethod())
                        .Emit(OpCodes.Callvirt, typeof(EntityList).GetMethod("FindFirst").MakeGenericMethod(deathDisplayType));
                }
            });
        }
    }

    private static void SupportCommunalHelper() {
        if (ModUtils.GetType("CommunalHelper", "Celeste.Mod.CommunalHelper.Entities.DreamTunnelDash") is { } dreamTunnelDashType) {
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, dreamTunnelDashType,
                    "StDreamTunnelDash",
                    "hasDreamTunnelDash",
                    "dreamTunnelDashAttacking",
                    "dreamTunnelDashTimer",
                    "overrideDreamDashCheck",
                    "dreamTunnelDashAttacking"
                ),
                (savedValues, _) => LoadStaticMemberValues(savedValues))
            );
        }
    }

    private static void SupportBounceHelper() {
        if (ModUtils.GetType("BounceHelper", "Celeste.Mod.BounceHelper.BounceHelperModule") is { } bounceHelperModule) {
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, bounceHelperModule, "enabled"),
                (savedValues, _) => LoadStaticMemberValues(savedValues))
            );
        }
    }

    private static void ReloadVirtualAssets() {
        Add(new SaveLoadAction(
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
            )
        );
    }
}