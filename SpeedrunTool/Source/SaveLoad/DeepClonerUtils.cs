using System.Collections;
using System.Collections.Generic;
using System.Threading;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework.Graphics;
using NLua;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public static class DeepClonerUtils {
    // 共用 DeepCloneState 可使多次 DeepClone 复用相同对象避免多次克隆同一对象
    private static DeepCloneState sharedDeepCloneState = new();

    [Load]
    private static void Config() {
        // Clone 开始时，判断哪些类型是直接使用原对象而不 DeepClone 的
        // Before cloning, determine which types use the original object directly
        DeepCloner.AddKnownTypesProcessor(type => {
            if (
                // Celeste Singleton
                type == typeof(Celeste)
                || type == typeof(Settings)

                // Everest
                || type.IsSubclassOf(typeof(ModAsset))
                || type.IsSubclassOf(typeof(EverestModule))
                || type == typeof(EverestModuleMetadata)

                // Monocle
                || type == typeof(GraphicsDevice)
                || type == typeof(GraphicsDeviceManager)
                || type == typeof(Monocle.Commands)
                || type == typeof(BitTag)
                || type == typeof(Atlas)

                // XNA GraphicsResource
                || type.IsSubclassOf(typeof(GraphicsResource))

                // NLua
                || type == typeof(Lua)
                || type.IsSubclassOf(typeof(LuaBase))

                // CelesteNet
                || type.FullName != null && type.FullName.StartsWith("Celeste.Mod.CelesteNet.") && !type.IsSubclassOf(typeof(Entity))
            ) {
                return true;
            }

            return null;
        });

        // Clone 对象的字段前，判断哪些类型是直接使用原对象或者自行通过其它方法 clone
        // Before cloning object's field, determine which types are directly used by the original object
        DeepCloner.AddPreCloneProcessor((sourceObj, deepCloneState) => {
            if (sourceObj == null) {
                return null;
            }

            lock (sourceObj) {
                if (sourceObj is Scene) {
                    if (sourceObj is Level) {
                        // 金草莓死亡或者 PageDown/Up 切换房间后等等改变 Level 实例的情况
                        // After golden strawberry deaths or changing rooms w/ Page Down / Up
                        if (Engine.Scene is Level level) {
                            return level;
                        }
                    }
                    return sourceObj;
                }

                // 稍后重新创建正在播放的 SoundSource 里的 EventInstance 实例
                if (sourceObj is SoundSource {Playing: true} source &&
                    source.GetFieldValue("instance") is EventInstance instance) {
                    if (string.IsNullOrEmpty(source.EventName)) {
                        return null;
                    }

                    instance.NeedManualClone();
                    instance.SaveTimelinePosition(instance.LoadTimelinePosition());

                    return null;
                }

                if (sourceObj is CassetteBlockManager manager) {
                    // isLevelMusic = true 时 sfx 自动等于 Audio.CurrentMusicEventInstance，无需重建
                    if (manager.GetFieldValue("sfx") is EventInstance sfx &&
                        !(bool)manager.GetFieldValue("isLevelMusic")) {
                        sfx.NeedManualClone();
                    }

                    if (manager.GetFieldValue("snapshot") is EventInstance snapshot) {
                        snapshot.NeedManualClone();
                    }

                    return null;
                }

                // 重新创建正在播放的 EventInstance 实例
                if (sourceObj is EventInstance eventInstance && eventInstance.IsNeedManualClone()) {
                    return eventInstance.Clone();
                }

                // Fixes: 克隆 WeakReference 后 Target 没有一起被克隆的问题，修复 dynData.Weak 克隆不完整导致的一些报错
                // System.Reflection.TargetException: 非静态字段需要一个目标。
                // 在 System.Reflection.RtFieldInfo.CheckConsistency(Object target)
                // 在 System.Reflection.RtFieldInfo.InternalGetValue(Object obj, StackCrawlMark& stackMark)
                // 在 System.Reflection.RtFieldInfo.GetValue(Object obj)
                // 在 MonoMod.Utils.DynData`1.<>c__DisplayClass19_0.<.cctor>b__1(TTarget obj)
                // 在 MonoMod.Utils.DynData`1.get_Item(String name)
                // 在 Celeste.Mod.MaxHelpingHand.Entities.CustomizableRefill.<>c__DisplayClass0_0.<.ctor>b__0(Player player)
                if (sourceObj is WeakReference sourceWeak) {
                    return new WeakReference(sourceWeak.Target.DeepClone(deepCloneState), sourceWeak.TrackResurrection);
                }
            }

            return null;
        });

        // Clone 对象的字段后，进行自定的处理
        // After cloning, perform custom processing
        DeepCloner.AddPostCloneProcessor((sourceObj, clonedObj, deepCloneState) => {
            if (clonedObj == null) {
                return null;
            }

            lock (sourceObj) {
                // 修复：DeepClone 的 hashSet.Containes(里面存在的引用对象) 总是返回 False
                // 原因：没有重写 GetHashCode 方法 https://github.com/force-net/DeepCloner/issues/17#issuecomment-678650032
                // Fix: DeepClone's hashSet.Contains (ReferenceType) always returns false, Dictionary has no such problem
                if (clonedObj.GetType().IsHashSet(out Type hashSetElementType) && !hashSetElementType.IsSimple()) {
                    IEnumerator enumerator = ((IEnumerable)clonedObj).GetEnumerator();

                    List<object> backup = new();
                    while (enumerator.MoveNext()) {
                        backup.Add(enumerator.Current);
                    }

                    if (backup.Count == 0) {
                        return clonedObj;
                    }

                    clonedObj.InvokeMethod("Clear");
                    backup.ForEach(obj => {
                        if (obj != null) {
                            clonedObj.InvokeMethod("Add", obj);
                        }
                    });
                }

                // 同上
                if (clonedObj.GetType().IsIDictionary(out Type dictKeyType, out Type _)
                    && !dictKeyType.IsSimple() && clonedObj is IDictionary {Count: > 0} clonedDict
                   ) {
                    Dictionary<object, object> backupDict = new();
                    backupDict.AddRangeSafe(clonedDict);
                    clonedDict.Clear();
                    clonedDict.AddRangeSafe(backupDict);
                }

                // LightingRenderer 需要，不然不会发光
                if (clonedObj is VertexLight vertexLight) {
                    vertexLight.Index = -1;
                }

                // Clone dynData.Data
                if (sourceObj.GetType() is {IsClass: true} objType && !DynDataUtils.IgnoreObjects.TryGetValue(sourceObj, out object _)) {
                    bool cloned = false;

                    do {
                        if (DynDataUtils.NotExistDynData(objType, out object dataMap)) {
                            continue;
                        }

                        object[] parameters = {sourceObj, null};
                        if (false == (bool)dataMap.InvokeMethod("TryGetValue", parameters)) {
                            continue;
                        }

                        object sourceValue = parameters[1];
                        if (sourceValue.GetFieldValue("Data") is not Dictionary<string, object> data || data.Count == 0) {
                            continue;
                        }

                        dataMap.InvokeMethod("Add", clonedObj, sourceValue.DeepClone(deepCloneState));
                        cloned = true;
                    } while ((objType = objType.BaseType) != null && objType.IsSameOrSubclassOf(typeof(object)));

                    if (!cloned) {
                        DynDataUtils.IgnoreObjects.Add(clonedObj, null);
                    }
                }

                // Clone DynamicData
                if (DynDataUtils.ExistDynamicData(sourceObj)) {
                    object[] parameters = {sourceObj, null};
                    if ((bool)DynDataUtils.DynamicDataMap.InvokeMethod("TryGetValue", parameters)) {
                        object sourceValue = parameters[1];
                        if (sourceValue.GetFieldValue("Data") is Dictionary<string, object> data && data.Count != 0) {
                            DynDataUtils.DynamicDataMap.InvokeMethod("Add", clonedObj, sourceValue.DeepClone(deepCloneState));
                            DynDataUtils.RecordDynamicDataObject(clonedObj);
                        }
                    }
                }

                // 预克隆的资源需要等待 LoadState 中移除实体之后才能判断是否需要 Reload，必须等待主线程中再操作
                if (clonedObj is VirtualAsset virtualAsset
                    && (Thread.CurrentThread.Name != "Main Thread" || StateManager.Instance.State == State.Loading)) {
                    SaveLoadAction.VirtualAssets.Add(virtualAsset);
                }
            }

            return clonedObj;
        });
    }

    [Unload]
    private static void Clear() {
        DeepCloner.ClearKnownTypesProcessors();
        DeepCloner.ClearPreCloneProcessors();
        DeepCloner.ClearPostCloneProcessors();
    }

    private static void InitSharedDeepCloneState() {
        sharedDeepCloneState ??= new DeepCloneState();
    }

    internal static void ClearSharedDeepCloneState() {
        sharedDeepCloneState = null;
    }

    internal static void SetSharedDeepCloneState(DeepCloneState deepCloneState) {
        sharedDeepCloneState = deepCloneState;
    }

    public static T DeepCloneShared<T>(this T obj) {
        InitSharedDeepCloneState();
        return obj.DeepClone(sharedDeepCloneState);
    }

    public static TTo DeepCloneToShared<TFrom, TTo>(this TFrom objFrom, TTo objTo) where TTo : class, TFrom {
        InitSharedDeepCloneState();
        return objFrom.DeepCloneTo(objTo, sharedDeepCloneState);
    }

    public static TTo ShallowCloneToShared<TFrom, TTo>(this TFrom objFrom, TTo objTo) where TTo : class, TFrom {
        InitSharedDeepCloneState();
        return objFrom.ShallowCloneTo(objTo, sharedDeepCloneState);
    }
}