using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD;
using FMOD.Studio;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class SaveLoadAction {
        private static readonly List<SaveLoadAction> All = new();

        private readonly Dictionary<Type, Dictionary<string, object>> savedValues = new();
        private readonly Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState;
        private readonly Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState;

        public SaveLoadAction(
            Action<Dictionary<Type, Dictionary<string, object>>, Level> saveState = null,
            Action<Dictionary<Type, Dictionary<string, object>>, Level> loadState = null) {
            this.saveState = saveState;
            this.loadState = loadState;
        }

        public static void Add(SaveLoadAction saveLoadAction) {
            All.Add(saveLoadAction);
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
            }

            RequireMuteAudios.Clear();
        }

        private static void SaveStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values, Type type, params string[] memberNames) {
            if (type == null) {
                return;
            }

            if (!values.ContainsKey(type)) {
                values[type] = new Dictionary<string, object>();
            }

            foreach (string memberName in memberNames) {
                if (type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } memberInfos) {
                    if (memberInfos.First().IsField()) {
                        values[type][memberName] = type.GetFieldValue(memberName).DeepCloneShared();
                    } else {
                        values[type][memberName] = type.GetPropertyValue(memberName).DeepCloneShared();
                    }
                }
            }
        }

        private static void LoadStaticMemberValues(Dictionary<Type, Dictionary<string, object>> values) {
            foreach (KeyValuePair<Type, Dictionary<string, object>> pair in values) {
                foreach (string memberName in pair.Value.Keys) {
                    Type type = pair.Key;
                    if (type.GetMember(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) is { } memberInfos) {
                        if (memberInfos.First().IsField()) {
                            type.SetFieldValue(memberName, pair.Value[memberName].DeepCloneShared());
                        } else {
                            type.SetPropertyValue(memberName, pair.Value[memberName].DeepCloneShared());
                        }
                    }
                }
            }
        }

        internal static void OnLoad() {
            SupportExternalMember();
            SupportCalcRandom();
            SupportMInput();
            SupportInput();
            SupportAudioMusic();
            MuteAnnoyingAudios();
            On.FMOD.Studio.EventDescription.createInstance += EventDescriptionOnCreateInstance;
        }

        // code mod 需要等待此时才正式加载，才能通过 Type 查找
        internal static void OnLoadContent() {
            SupportModSessionAndSaveData();
            InitStaticFields();
            SupportEntitySimpleStaticFields();
            SupportMaxHelpingHand();
            SupportPandorasBox();
            SupportCrystallineHelper();
            SupportSpringCollab2020();
            SupportExtendedVariants();
            SupportXaphanHelper();
            SupportIsaGrabBag();
        }

        internal static void OnUnload() {
            All.Clear();
            On.FMOD.Studio.EventDescription.createInstance -= EventDescriptionOnCreateInstance;
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

        private static Dictionary<Type, FieldInfo[]> entityStaticFields;

        private static void InitStaticFields() {
            entityStaticFields = new Dictionary<Type, FieldInfo[]>();
            IEnumerable<Type> entityTypes = Everest.Modules.SelectMany(module => module.GetType().Assembly.GetTypesSafe().Where(type =>
                !type.IsGenericType
                && type.FullName != null
                && !type.FullName.StartsWith("Celeste.Mod.SpeedrunTool")
                && !type.IsSubclassOf(typeof(Oui))
                && type.IsSameOrSubclassOf(typeof(Entity))));

            foreach (Type entityType in entityTypes) {
                FieldInfo[] fieldInfos = entityType.GetFieldInfos(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(info => !info.IsLiteral).ToArray();
                if (fieldInfos.Length == 0) {
                    continue;
                }

                entityStaticFields[entityType] = fieldInfos;
            }
        }

        private static void SupportExternalMember() {
            Add(new SaveLoadAction(
                (savedValues, _) => {
                    SaveStaticMemberValues(savedValues, typeof(Engine), "DashAssistFreeze", "DashAssistFreezePress", "DeltaTime", "FrameCounter",
                        "FreezeTimer", "RawDeltaTime", "TimeRate", "TimeRateB");
                    SaveStaticMemberValues(savedValues, typeof(Glitch), "Value");
                    SaveStaticMemberValues(savedValues, typeof(Distort), "Anxiety", "GameRate");
                },
                (savedValues, _) => LoadStaticMemberValues(savedValues)));
        }

        private static void SupportCalcRandom() {
            Type type = typeof(Calc);
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, type, "Random", "randomStack"),
                (savedValues, _) => LoadStaticMemberValues(savedValues)));
        }

        private static void SupportEntitySimpleStaticFields() {
            Add(new SaveLoadAction(
                (dictionary, _) => {
                    foreach (Type type in entityStaticFields.Keys) {
                        FieldInfo[] fieldInfos = entityStaticFields[type];
                        // ("\n\n" + string.Join("\n", fieldInfos.Select(info => type.FullName + " " + info.Name + " " + info.FieldType))).DebugLog();
                        Dictionary<string, object> values = new();

                        foreach (FieldInfo fieldInfo in fieldInfos) {
                            object value = fieldInfo.GetValue(null);
                            Type fieldType = fieldInfo.FieldType;

                            // 避免 SL SaveLoadIcon.Instance 这种不需要克隆的字段
                            if (fieldType == type && value is Entity entity && entity.TagCheck(Tags.Global)) {
                                continue;
                            }

                            if (value == null) {
                                values[fieldInfo.Name] = null;
                            } else if (fieldType.IsSimpleClass(_ =>
                                fieldType == type || fieldType == typeof(MTexture) || fieldType == typeof(CrystalStaticSpinner))) {
                                values[fieldInfo.Name] = value;
                            }
                        }

                        if (values.Keys.Count > 0) {
                            dictionary[type] = values.DeepCloneShared();
                        }
                    }
                }, (dictionary, _) => {
                    Dictionary<Type, Dictionary<string, object>> clonedDict = dictionary.DeepCloneShared();
                    foreach (Type type in clonedDict.Keys) {
                        Dictionary<string, object> values = clonedDict[type];
                        // ("\n\n" + string.Join("\n", values.Select(pair => type.FullName + " " + pair.Key + " " + pair.Value))).DebugLog();
                        foreach (KeyValuePair<string, object> pair in values) {
                            object value = pair.Value;

                            // 避免 SL SaveLoadIcon.Instance 这种不需要克隆的字段
                            if (value == null && type.GetFieldValue(pair.Key) is Entity entity && entity.GetType() == type &&
                                entity.TagCheck(Tags.Global)) {
                                continue;
                            }

                            type.SetFieldValue(pair.Key, pair.Value);
                        }
                    }
                }
            ));
        }

        private static void SupportMInput() {
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(MInput), "Active", "Disabled", "Keyboard", "Mouse", "GamePads"),
                (savedValues, _) => LoadStaticMemberValues(savedValues)));
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

        private static void MuteAnnoyingAudios() {
            Add(new SaveLoadAction(loadState: (_, _) => {
                foreach (EventInstance sfx in RequireMuteAudios) {
                    sfx.setVolume(0f);
                }

                RequireMuteAudios.Clear();
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
                (savedValues, _) => {
                    Dictionary<string, object> saved = savedValues[typeof(Audio)];

                    Audio.SetMusic(Audio.GetEventName(saved["currentMusicEvent"] as EventInstance));
                    Audio.CurrentMusicEventInstance?.CopyParametersFrom(saved["currentMusicEvent"] as EventInstance);

                    Audio.SetAmbience(Audio.GetEventName(saved["CurrentAmbienceEventInstance"] as EventInstance));
                    Audio.CurrentAmbienceEventInstance?.CopyParametersFrom(saved["CurrentAmbienceEventInstance"] as EventInstance);

                    Audio.SetAltMusic(Audio.GetEventName(saved["currentAltMusicEvent"] as EventInstance));
                    (typeof(Audio).GetFieldValue("currentAltMusicEvent") as EventInstance)?.CopyParametersFrom(
                        saved["currentAltMusicEvent"] as EventInstance);

                    Audio.MusicUnderwater = (bool) saved["MusicUnderwater"];
                    Audio.PauseMusic = (bool) saved["PauseMusic"];
                    Audio.PauseGameplaySfx = (bool) saved["PauseGameplaySfx"];
                }
            ));
        }

        private static void SupportPandorasBox() {
            // if (Type.GetType("Celeste.Mod.PandorasBox.TimeField, PandorasBox") is { } timeFieldType
            //     && Delegate.CreateDelegate(typeof(On.Celeste.Player.hook_Update), timeFieldType.GetMethodInfo("PlayerUpdateHook")) is
            //         On.Celeste.Player.hook_Update hookUpdate) {
            //     Add(new SaveLoadAction(
            //         loadState: (savedValues, level) => {
            //             if ((bool) timeFieldType.GetFieldValue("hookAdded")) {
            //                 On.Celeste.Player.Update += hookUpdate;
            //             }
            //         }
            //     ));
            // }

            // Fixed: Game crashes after save DustSpriteColorController
            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(DustStyles), "Styles"),
                (savedValues, _) => LoadStaticMemberValues(savedValues)
            ));
        }

        private static void SupportMaxHelpingHand() {
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is { } colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                Add(new SaveLoadAction(
                    loadState: (_, _) => {
                        if ((bool) colorControllerType.GetFieldValue("rainbowSpinnerHueHooked")) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                        } else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.SeekerBarrierColorController, MaxHelpingHand") is
                { } seekerBarrierColorControllerType) {
                Add(new SaveLoadAction(
                    loadState: (savedValues, _) => {
                        if ((bool) seekerBarrierColorControllerType.GetFieldValue("seekerBarrierRendererHooked")) {
                            seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                            seekerBarrierColorControllerType.InvokeMethod("hookSeekerBarrierRenderer");
                        } else {
                            seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger, MaxHelpingHand") is { } gradientDustTriggerType) {
                Add(new SaveLoadAction(
                    loadState: (savedValues, _) => {
                        if ((bool) gradientDustTriggerType.GetFieldValue("hooked")) {
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

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.ParallaxFadeOutController, MaxHelpingHand") is { } parallaxFadeOutControllerType
                && Delegate.CreateDelegate(typeof(ILContext.Manipulator),
                    parallaxFadeOutControllerType.GetMethodInfo("onBackdropRender")) is ILContext.Manipulator onBackdropRender
            ) {
                Add(new SaveLoadAction(
                    loadState: (savedValues, _) => {
                        if ((bool) parallaxFadeOutControllerType.GetFieldValue("backdropRendererHooked")) {
                            IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                            IL.Celeste.BackdropRenderer.Render += onBackdropRender;
                        } else {
                            IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Effects.BlackholeCustomColors, MaxHelpingHand") is { } blackHoleCustomColorsType) {
                Add(new SaveLoadAction(
                    (savedValues, _) => SaveStaticMemberValues(savedValues, blackHoleCustomColorsType, "colorsMild"),
                    (savedValues, _) => LoadStaticMemberValues(savedValues)));
            }
        }

        private static void SupportCrystallineHelper() {
            Type vitModuleType = Type.GetType("vitmod.VitModule, vitmod");
            if (vitModuleType == null) {
                return;
            }

            Add(new SaveLoadAction(
                (savedValues, _) => SaveStaticMemberValues(savedValues, vitModuleType, "timeStopScaleTimer", "noMoveScaleTimer"),
                (savedValues, _) => LoadStaticMemberValues(savedValues)
            ));
        }

        private static void SupportSpringCollab2020() {
            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController, SpringCollab2020") is { } colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                Add(new SaveLoadAction(
                    loadState: (savedValues, _) => {
                        if ((bool) colorControllerType.GetFieldValue("rainbowSpinnerHueHooked")) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                        } else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController, SpringCollab2020") is
                    { } colorAreaControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookSpinnerGetHue
            ) {
                Add(new SaveLoadAction(
                    loadState: (savedValues, _) => {
                        if ((bool) colorAreaControllerType.GetFieldValue("rainbowSpinnerHueHooked")) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookSpinnerGetHue;
                        } else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.SpikeJumpThroughController, SpringCollab2020") is
                    { } spikeJumpThroughControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide),
                    spikeJumpThroughControllerType.GetMethodInfo("OnCollideHook")) is On.Celeste.Spikes.hook_OnCollide onCollideHook
            ) {
                Add(new SaveLoadAction(
                    loadState: (savedValues, _) => {
                        if ((bool) spikeJumpThroughControllerType.GetFieldValue("SpikeHooked")) {
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
            // 修复：ExtendedVariantTrigger 设置的值在 SL 之后失效
            if (Type.GetType("ExtendedVariants.ExtendedVariantTrigger, ExtendedVariantMode") is { } extendedVariantTrigger) {
                Add(new SaveLoadAction(
                    loadState: (savedValues, _) => {
                        if (Engine.Scene.GetPlayer() is not { } player ||
                            player.GetFieldValue("triggersInside") is not HashSet<Trigger> triggersInside) {
                            return;
                        }

                        foreach (Trigger trigger in triggersInside.Where(trigger =>
                            trigger.GetType() == extendedVariantTrigger && (bool) trigger.GetFieldValue(trigger.GetType(), "revertOnLeave"))) {
                            trigger.OnEnter(player);
                        }
                    }));
            }

            if (Type.GetType("ExtendedVariants.Variants.JumpCount, ExtendedVariantMode") is { } jumpCountType) {
                Add(new SaveLoadAction(
                    (savedValues, _) => SaveStaticMemberValues(savedValues, jumpCountType, "jumpBuffer"),
                    (savedValues, _) => LoadStaticMemberValues(savedValues)));
            }
        }

        private static void SupportXaphanHelper() {
            if (Type.GetType("Celeste.Mod.XaphanHelper.Upgrades.SpaceJump, XaphanHelper") is { } spaceJumpType) {
                Add(new SaveLoadAction(
                    (savedValues, _) => SaveStaticMemberValues(savedValues, spaceJumpType, "jumpBuffer"),
                    (savedValues, _) => LoadStaticMemberValues(savedValues))
                );
            }
        }

        private static void SupportIsaGrabBag() {
            // 解决 DreamSpinnerBorder 读档后影像残留在屏幕中
            if (Type.GetType("Celeste.Mod.IsaGrabBag.DreamSpinnerBorder, IsaMods") is { } borderType) {
                Add(new SaveLoadAction(
                        loadState: (_, level) => level.Entities.FirstOrDefault(entity => entity.GetType() == borderType)?.Update()
                    )
                );
            }

            // 解决读档后冲进 DreamSpinner 会被刺死
            if (Type.GetType("Celeste.Mod.IsaGrabBag.GrabBagModule, IsaMods") is { } grabBagModuleType) {
                Add(new SaveLoadAction(
                    (savedValues, _) => SaveStaticMemberValues(savedValues, grabBagModuleType, "ZipLineState".ToBackingField(),
                        "playerInstance".ToBackingField()),
                    (savedValues, _) => LoadStaticMemberValues(savedValues))
                );
            }
        }
    }
}