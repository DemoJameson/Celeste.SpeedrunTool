using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD.Studio;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    public sealed class SaveLoadAction {
        private static readonly List<SaveLoadAction> All = new List<SaveLoadAction>();

        private readonly Dictionary<Type, Dictionary<string, object>> savedValues = new Dictionary<Type, Dictionary<string, object>>();
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
        }

        private static void SaveStaticFieldValues(Dictionary<Type, Dictionary<string, object>> values, Type type,
            params string[] fieldNames) {
            if (type == null) return;

            if (!values.ContainsKey(type)) {
                values[type] = new Dictionary<string, object>();
            }

            foreach (var fieldName in fieldNames) {
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
        }

        // code mod 需要等待此时才正式加载，才能通过 Type 查找
        internal static void OnLoadContent() {
            InitStaticFields();
            SupportEntitySimpleStaticFields();
            SupportAudioMusic();
            SupportExtendedVariants();
            SupportMaxHelpingHand();
            // SupportPandorasBox();
            SupportCrystallineHelper();
            SupportSpringCollab2020();
        }

        internal static void OnUnload() {
            All.Clear();
        }

        private static Dictionary<Type, FieldInfo[]> EntityStaticFields;

        private static void InitStaticFields() {
            EntityStaticFields = new Dictionary<Type, FieldInfo[]>();
            IEnumerable<Type> entityTypes = Everest.Modules.SelectMany(module => module.GetType().Assembly.GetTypesSafe().Where(type =>
                !type.IsGenericType
                && type.FullName != null
                && !type.FullName.StartsWith("Celeste.Mod.SpeedrunTool")
                && !type.IsSubclassOf(typeof(Oui))
                && type.IsSameOrSubclassOf(typeof(Entity))));

            foreach (Type entityType in entityTypes) {
                FieldInfo[] fieldInfos = entityType.GetFieldInfos(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(info => !info.IsLiteral).ToArray();
                if (fieldInfos.Length == 0) continue;
                EntityStaticFields[entityType] = fieldInfos;
            }
        }

        private static void SupportCalcRandom() {
            Type type = typeof(Calc);
            All.Add(new SaveLoadAction(
                (savedValues, level) => {
                    SaveStaticFieldValues(savedValues, type, "Random", "randomStack");
                }, (savedValues, level) => {
                    LoadStaticFieldValues(savedValues);
                }
            ));
        }

        private static void SupportEntitySimpleStaticFields() {
            All.Add(new SaveLoadAction(
                (dictionary, level) => {
                    foreach (Type type in EntityStaticFields.Keys) {
                        FieldInfo[] fieldInfos = EntityStaticFields[type];
                        // ("\n\n" + string.Join("\n", fieldInfos.Select(info => type.FullName + " " + info.Name + " " + info.FieldType))).DebugLog();
                        Dictionary<string, object> values = new Dictionary<string, object>();

                        foreach (FieldInfo fieldInfo in fieldInfos) {
                            object value = fieldInfo.GetValue(null);
                            Type fieldType = fieldInfo.FieldType;
                            if (value == null) {
                                values[fieldInfo.Name] = null;
                            } else if (fieldType.IsSimpleClass(extraType => {
                                return fieldType == type || fieldType == typeof(MTexture) || fieldType == typeof(CrystalStaticSpinner);
                            })) {
                                values[fieldInfo.Name] = value;
                            }
                        }

                        if (values.Keys.Count > 0) {
                            dictionary[type] = values.DeepCloneShared();
                        }
                    }
                }, (dictionary, level) => {
                    Dictionary<Type, Dictionary<string, object>> clonedDict = dictionary.DeepCloneShared();
                    foreach (Type type in clonedDict.Keys) {
                        Dictionary<string,object> values = clonedDict[type];
                        // ("\n\n" + string.Join("\n", values.Select(pair => type.FullName + " " + pair.Key + " " + pair.Value))).DebugLog();
                        foreach (KeyValuePair<string,object> pair in values) {
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
                    Dictionary<string, object> dictionary = new Dictionary<string, object> {
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

        private static void SupportAudioMusic() {
            All.Add(new SaveLoadAction(
                (savedValues, level) => {
                    Dictionary<string, object> saved = new Dictionary<string, object> {
                        {
                            "currentMusicEvent",
                            (typeof(Audio).GetFieldValue("currentMusicEvent") as EventInstance)?.NeedManualClone().DeepCloneShared()
                        },
                        {"CurrentAmbienceEventInstance", Audio.CurrentAmbienceEventInstance?.NeedManualClone().DeepCloneShared()}, {
                            "currentAltMusicEvent",
                            (typeof(Audio).GetFieldValue("currentAltMusicEvent") as EventInstance)?.NeedManualClone().DeepCloneShared()
                        },
                        {"MusicUnderwater", Audio.MusicUnderwater}
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

                    Audio.MusicUnderwater = (bool) saved["MusicUnderwater"];
                }
            ));
        }

        private static void SupportPandorasBox() {
            if (Type.GetType("Celeste.Mod.PandorasBox.TimeField, PandorasBox") is Type timeFieldType
                && Delegate.CreateDelegate(typeof(On.Celeste.Player.hook_Update), timeFieldType.GetMethodInfo("PlayerUpdateHook")) is
                    On.Celeste.Player.hook_Update hookUpdate) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) timeFieldType.GetFieldValue("hookAdded")) {
                            On.Celeste.Player.Update += hookUpdate;
                        }
                    }
                ));
            }

            // Fixed: Game crashes after save DustSpriteColorController
            All.Add(new SaveLoadAction(
                (savedValues, level) => { SaveStaticFieldValues(savedValues, typeof(DustStyles), "Styles"); },
                (savedValues, level) => LoadStaticFieldValues(savedValues)
            ));
        }

        private static void SupportMaxHelpingHand() {
            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.RainbowSpinnerColorController, MaxHelpingHand") is Type colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) colorControllerType.GetFieldValue("rainbowSpinnerHueHooked")) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                        } else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.SeekerBarrierColorController, MaxHelpingHand") is Type seekerBarrierColorControllerType) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) seekerBarrierColorControllerType.GetFieldValue("seekerBarrierRendererHooked")) {
                            seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                            seekerBarrierColorControllerType.InvokeMethod("hookSeekerBarrierRenderer");
                        } else {
                            seekerBarrierColorControllerType.InvokeMethod("unhookSeekerBarrierRenderer");
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Triggers.GradientDustTrigger, MaxHelpingHand") is Type gradientDustTriggerType) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) gradientDustTriggerType.GetFieldValue("hooked")) {
                            gradientDustTriggerType.InvokeMethod("unhook");
                            gradientDustTriggerType.InvokeMethod("hook");
                        } else {
                            gradientDustTriggerType.SetFieldValue("hooked", true);
                            gradientDustTriggerType.InvokeMethod("unhook");
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Entities.ParallaxFadeOutController, MaxHelpingHand") is Type parallaxFadeOutControllerType
                && Delegate.CreateDelegate(typeof(ILContext.Manipulator),
                        parallaxFadeOutControllerType.GetMethodInfo("onBackdropRender")) is ILContext.Manipulator onBackdropRender
            ) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) parallaxFadeOutControllerType.GetFieldValue("backdropRendererHooked")) {
                            IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                            IL.Celeste.BackdropRenderer.Render += onBackdropRender;
                        } else {
                            IL.Celeste.BackdropRenderer.Render -= onBackdropRender;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.MaxHelpingHand.Effects.BlackholeCustomColors, MaxHelpingHand") is Type blackHoleCustomColorsType) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => {
                        SaveStaticFieldValues(savedValues, blackHoleCustomColorsType, "colorsMild");
                    },
                    (savedValues, level) => {
                        LoadStaticFieldValues(savedValues);
                    }
                ));
            }
        }

        private static void SupportCrystallineHelper() {
            Type vitModuleType = Type.GetType("vitmod.VitModule, vitmod");
            if (vitModuleType == null) return;
            All.Add(new SaveLoadAction(
                (savedValues, level) => { SaveStaticFieldValues(savedValues, vitModuleType, "timeStopScaleTimer", "noMoveScaleTimer"); },
                (savedValues, level) => LoadStaticFieldValues(savedValues)
            ));
        }

        private static void SupportSpringCollab2020() {
            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorController, SpringCollab2020") is Type colorControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookGetHue
            ) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) colorControllerType.GetFieldValue("rainbowSpinnerHueHooked")) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookGetHue;
                        } else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookGetHue;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.RainbowSpinnerColorAreaController, SpringCollab2020") is Type
                    colorAreaControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.CrystalStaticSpinner.hook_GetHue),
                        colorAreaControllerType.GetMethodInfo("getRainbowSpinnerHue")) is
                    On.Celeste.CrystalStaticSpinner.hook_GetHue hookSpinnerGetHue
            ) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) colorAreaControllerType.GetFieldValue("rainbowSpinnerHueHooked")) {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                            On.Celeste.CrystalStaticSpinner.GetHue += hookSpinnerGetHue;
                        } else {
                            On.Celeste.CrystalStaticSpinner.GetHue -= hookSpinnerGetHue;
                        }
                    }
                ));
            }

            if (Type.GetType("Celeste.Mod.SpringCollab2020.Entities.SpikeJumpThroughController, SpringCollab2020") is Type spikeJumpThroughControllerType
                && Delegate.CreateDelegate(typeof(On.Celeste.Spikes.hook_OnCollide),
                        spikeJumpThroughControllerType.GetMethodInfo("OnCollideHook")) is On.Celeste.Spikes.hook_OnCollide OnCollideHook
            ) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if ((bool) spikeJumpThroughControllerType.GetFieldValue("SpikeHooked")) {
                            On.Celeste.Spikes.OnCollide -= OnCollideHook;
                            On.Celeste.Spikes.OnCollide += OnCollideHook;
                        } else {
                            On.Celeste.Spikes.OnCollide -= OnCollideHook;
                        }
                    }
                ));
            }
        }

        private static void SupportExtendedVariants() {
            // 修复：ExtendedVariantTrigger 设置的值在 SL 之后失效
            if (Type.GetType("ExtendedVariants.ExtendedVariantTrigger, ExtendedVariantMode") is Type extendedVariantTrigger) {
                All.Add(new SaveLoadAction(
                    loadState: (savedValues, level) => {
                        if (!(Engine.Scene.GetPlayer() is Player player) ||
                            !(player.GetFieldValue("triggersInside") is HashSet<Trigger> triggersInside)) return;
                        foreach (Trigger trigger in triggersInside.Where(trigger =>
                            trigger.GetType() == extendedVariantTrigger && (bool) trigger.GetFieldValue(trigger.GetType(), "revertOnLeave"))) {
                            trigger.OnEnter(player);
                        }
                    }));
            }

            if (Type.GetType("ExtendedVariants.Variants.JumpCount, ExtendedVariantMode") is Type jumpCountType) {
                All.Add(new SaveLoadAction(
                    (savedValues, level) => SaveStaticFieldValues(savedValues, jumpCountType, "jumpBuffer"),
                    (savedValues, level) => LoadStaticFieldValues(savedValues)));
            }
        }
    }
}