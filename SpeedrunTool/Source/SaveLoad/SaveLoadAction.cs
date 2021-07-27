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
            CachedAudios.Clear();
        }

        private static void SaveStaticFieldValues(Dictionary<Type, Dictionary<string, object>> values, Type type,
            params string[] fieldNames) {
            if (type == null) {
                return;
            }

            if (!values.ContainsKey(type)) {
                values[type] = new Dictionary<string, object>();
            }

            foreach (string fieldName in fieldNames) {
                values[type][fieldName] = type.GetFieldValue(fieldName).DeepCloneShared();
            }
        }

        private static void LoadStaticFieldValues(Dictionary<Type, Dictionary<string, object>> values) {
            foreach (KeyValuePair<Type, Dictionary<string, object>> pair in values) {
                foreach (string fieldName in pair.Value.Keys) {
                    pair.Key.SetFieldValue(fieldName, pair.Value[fieldName].DeepCloneShared());
                }
            }
        }

        internal static void OnLoad() {
            SupportCalcRandom();
            SupportMInput();
            SupportAudioMusic();
            MuteSomeAudios();
            On.FMOD.Studio.EventDescription.createInstance += EventDescriptionOnCreateInstance;
        }

        // code mod 需要等待此时才正式加载，才能通过 Type 查找
        internal static void OnLoadContent() {
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

        private static void SupportCalcRandom() {
            Type type = typeof(Calc);
            All.Add(new SaveLoadAction(
                (savedValues, _) => { SaveStaticFieldValues(savedValues, type, "Random", "randomStack"); },
                (savedValues, _) => { LoadStaticFieldValues(savedValues); }
            ));
        }

        private static void SupportEntitySimpleStaticFields() {
            All.Add(new SaveLoadAction(
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
            Type type = typeof(MInput);
            All.Add(new SaveLoadAction(
                (savedValues, level) => {
                    Dictionary<string, object> dictionary = new() {
                        ["Active"] = MInput.Active,
                        ["Disabled"] = MInput.Disabled,
                        ["Keyboard"] = MInput.Keyboard,
                        ["Mouse"] = MInput.Mouse,
                        ["GamePads"] = MInput.GamePads,
                    };
                    savedValues[type] = dictionary.DeepCloneShared();
                }, (savedValues, level) => {
                    Dictionary<string, object> dictionary = savedValues[type].DeepCloneShared();
                    MInput.Active = (bool) dictionary["Active"];
                    MInput.Disabled = (bool) dictionary["Disabled"];
                    type.SetPropertyValue("Keyboard", dictionary["Keyboard"]);
                    type.SetPropertyValue("Mouse", dictionary["Mouse"]);
                    type.SetPropertyValue("GamePads", dictionary["GamePads"]);
                }
            ));
        }

        private static readonly HashSet<string> RequireMuteAudios = new() {
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

        private static readonly List<EventInstance> CachedAudios = new();

        private static void MuteSomeAudios() {
            All.Add(new SaveLoadAction(loadState: (_, _) => {
                foreach (EventInstance sfx in CachedAudios) {
                    sfx.setVolume(0f);
                }
                CachedAudios.Clear();
            }));
        }

        private static RESULT EventDescriptionOnCreateInstance(On.FMOD.Studio.EventDescription.orig_createInstance orig, EventDescription self,
            out EventInstance instance) {
            RESULT result = orig(self, out instance);

            if (StateManager.Instance.IsSaved && instance != null && self.getPath(out string path) == RESULT.OK && path != null && RequireMuteAudios.Contains(path)) {
                CachedAudios.Add(instance);
            }

            return result;
        }

        private static void SupportAudioMusic() {
            All.Add(new SaveLoadAction(
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
            //     All.Add(new SaveLoadAction(
            //         loadState: (savedValues, level) => {
            //             if ((bool) timeFieldType.GetFieldValue("hookAdded")) {
            //                 On.Celeste.Player.Update += hookUpdate;
            //             }
            //         }
            //     ));
            // }

            // Fixed: Game crashes after save DustSpriteColorController
            All.Add(new SaveLoadAction(
                (savedValues, _) => { SaveStaticFieldValues(savedValues, typeof(DustStyles), "Styles"); },
                (savedValues, _) => LoadStaticFieldValues(savedValues)
            ));
        }

        private static void SupportMaxHelpingHand() {
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is { } colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
                    (savedValues, _) => { SaveStaticFieldValues(savedValues, blackHoleCustomColorsType, "colorsMild"); },
                    (savedValues, _) => { LoadStaticFieldValues(savedValues); }
                ));
            }
        }

        private static void SupportCrystallineHelper() {
            Type vitModuleType = Type.GetType("vitmod.VitModule, vitmod");
            if (vitModuleType == null) {
                return;
            }

            All.Add(new SaveLoadAction(
                (savedValues, _) => { SaveStaticFieldValues(savedValues, vitModuleType, "timeStopScaleTimer", "noMoveScaleTimer"); },
                (savedValues, _) => LoadStaticFieldValues(savedValues)
            ));
        }

        private static void SupportSpringCollab2020() {
            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController, SpringCollab2020") is { } colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
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
                All.Add(new SaveLoadAction(
                    (savedValues, _) => SaveStaticFieldValues(savedValues, jumpCountType, "jumpBuffer"),
                    (savedValues, _) => LoadStaticFieldValues(savedValues)));
            }
        }

        private static void SupportXaphanHelper() {
            if (Type.GetType("Celeste.Mod.XaphanHelper.Upgrades.SpaceJump, XaphanHelper") is { } spaceJumpType) {
                All.Add(new SaveLoadAction(
                    (savedValues, _) => SaveStaticFieldValues(savedValues, spaceJumpType, "jumpBuffer"),
                    (savedValues, _) => LoadStaticFieldValues(savedValues))
                );
            }
        }

        private static void SupportIsaGrabBag() {
            // 解决 DreamSpinnerBorder 读档后影像残留在屏幕中
            if (Type.GetType("Celeste.Mod.IsaGrabBag.DreamSpinnerBorder, IsaMods") is { } borderType) {
                All.Add(new SaveLoadAction(
                        loadState: (_, level) => level.Entities.FirstOrDefault(entity => entity.GetType() == borderType)?.Update()
                    )
                );
            }

            // 解决读档后冲进 DreamSpinner 会被刺死
            if (Type.GetType("Celeste.Mod.IsaGrabBag.GrabBagModule, IsaMods") is { } grabBagModuleType) {
                All.Add(new SaveLoadAction(
                    (savedValues, _) => SaveStaticFieldValues(savedValues, grabBagModuleType, "ZipLineState".ToBackingField(),
                        "playerInstance".ToBackingField()),
                    (savedValues, _) => LoadStaticFieldValues(savedValues))
                );
            }
        }
    }
}