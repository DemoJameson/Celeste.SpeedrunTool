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

// The built-in save/load actions: they capture everything that lives outside the Level object
// graph (static fields, Everest module state, audio, input, third-party mod state, ...).
// Split out of SaveLoadAction.cs, which keeps the action registry and dispatch.
public sealed partial class SaveLoadAction {
    private static void SupportSimpleStaticFields() {
        InternalSafeAdd(
            (dictionary, _) => {
                foreach (Type type in simpleStaticFields.Keys) {
                    FieldInfo[] fieldInfos = simpleStaticFields[type];
                    // Logger.Debug("SpeedrunTool", "\n\n" + string.Join("\n", fieldInfos.Select(info => type.FullName + " " + info.Name + " " + info.FieldType)));
                    Dictionary<string, object> values = [];

                    foreach (FieldInfo fieldInfo in fieldInfos) {
                        values[fieldInfo.Name] = fieldInfo.GetValue(null);
                    }

                    dictionary[type] = values.DeepCloneShared();
                }
            },
            (dictionary, _) => {
                Dictionary<Type, Dictionary<string, object>> clonedDict = dictionary.DeepCloneShared();
                foreach (Type type in clonedDict.Keys) {
                    Dictionary<string, object> values = clonedDict[type];
                    // Logger.Debug("SpeedrunTool", "\n\n" + string.Join("\n", values.Select(pair => type.FullName + " " + pair.Key + " " + pair.Value)));
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
                    Dictionary<string, object> dict = [];
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
            }
        );
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
#pragma warning disable CS0618 // 类型或成员已过时
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
#pragma warning restore CS0618 // 类型或成员已过时
                SaveStaticMemberValues(savedValues, typeof(Glitch), nameof(Glitch.Value));
                SaveStaticMemberValues(savedValues, typeof(Distort), nameof(Distort.Anxiety), nameof(Distort.GameRate));
                SaveStaticMemberValues(savedValues, typeof(ScreenWipe), nameof(ScreenWipe.WipeColor));
                SaveStaticMemberValues(savedValues, typeof(Audio), nameof(Audio.currentCamera));
                // Fixed: Game crashes after save PandorasBoxUtils.DustSpriteColorController
                SaveStaticMemberValues(savedValues, typeof(DustStyles), nameof(DustStyles.Styles));
            },
            (savedValues, _) => LoadStaticMemberValues(savedValues)
        );
    }

    private static void SupportCalcRandom() {
        InternalSafeAdd(
            (savedValues, _) => SaveStaticMemberValues(savedValues, typeof(Calc),
                nameof(Calc.Random), nameof(Calc.randomStack)),
            (savedValues, _) => LoadStaticMemberValues(savedValues)
        );
    }

    private static void SupportSettings() {
        InternalSafeAdd(
            (savedValues, _) => {
                if (Settings.Instance is { } settings) {
                    Dictionary<string, object> dict = new() {
                        [nameof(Settings.GrabMode)] = settings.GrabMode,
                        [nameof(Settings.CrouchDashMode)] = settings.CrouchDashMode
                    };
                    savedValues[typeof(Settings)] = dict;
                }
            },
            (savedValues, _) => {
                if (Settings.Instance is { } settings && savedValues.TryGetValue(typeof(Settings), out Dictionary<string, object> dict)) {
                    settings.GrabMode = (GrabModes)dict[nameof(Settings.GrabMode)];
                    settings.CrouchDashMode = (CrouchDashModes)dict[nameof(Settings.CrouchDashMode)];

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
            }
        );
    }

    // Fix https://github.com/DemoJameson/CelesteSpeedrunTool/issues/19
    private static void SupportInput() {
        Type inputType = typeof(Input);
        InternalSafeAdd(
            (savedValues, _) => {
                SaveStaticMemberValues(savedValues, typeof(MInput), nameof(MInput.VirtualInputs));

                Dictionary<string, object> inputDict = [];
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

                    Dictionary<string, object> settingsDict = [];
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

                        if (!savedValues.TryGetValue(settingsType, out Dictionary<string, object> settingsDict)) {
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
            }
        );
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
    }}
