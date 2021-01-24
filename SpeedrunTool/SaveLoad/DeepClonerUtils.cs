using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Utils;
using NLua;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class DeepClonerUtils {
        public static void Config() {
            // Clone 开始时，判断哪些类型是直接使用原对象而不 DeepClone 的
            // Before cloning, determine which types use the original object directly
            DeepCloner.AddKnownTypesProcessor((type) => {
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
                    || type == typeof(Pooler)
                    || type == typeof(BitTag)
                    || type == typeof(Atlas)

                    // XNA GraphicsResource
                    || type.IsSubclassOf(typeof(GraphicsResource))

                    // NLua
                    || type == typeof(Lua)
                    || type.IsSubclassOf(typeof(LuaBase))
                ) {
                    return true;
                }

                return null;
            });

            // Clone 对象的字段前，判断哪些类型是直接使用原对象或者自行通过其它方法 clone
            // Before cloning object's field, determine which types are directly used by the original object
            DeepCloner.AddPreCloneProcessor((sourceObj, deepCloneState) => {
                lock (sourceObj) {
                    if (sourceObj is Level) {
                        // 金草莓死亡或者 PageDown/Up 切换房间后等等改变 Level 实例的情况
                        // After golden strawberry deaths or changing rooms w/ Page Down / Up
                        if (Engine.Scene is Level level) return level;
                        return sourceObj;
                    }

                    if (sourceObj is Entity entity
                        && entity.TagCheck(Tags.Global)
                        && !(entity is CassetteBlockManager)
                        && !(entity is SeekerBarrierRenderer)
                        && !(entity is LightningRenderer)
                        && !(entity is SpeedrunTimerDisplay)
                        // Fixes: Glyph Teleport Area Effect
                        && entity.GetType().FullName != "Celeste.Mod.AcidHelper.Entities.InstantTeleporterRenderer"
                        && entity.GetType().FullName != "VivHelper.Entities.HoldableBarrierRenderer"
                    ) return sourceObj;

                    lock (sourceObj) {
                        // 稍后重新创建正在播放的 SoundSource 里的 EventInstance 实例
                        if (sourceObj is SoundSource source
                            // TODO SoundEmitter 的声音会存留在关卡中，切换房间后保存依然会播放
                            && !(source.Entity is SoundEmitter)
                            && source.Playing
                            && source.GetFieldValue("instance") is EventInstance instance) {
                            if (string.IsNullOrEmpty(source.EventName)) return null;
                            instance.NeedManualClone();
                            instance.SaveTimelinePosition(instance.LoadTimelinePosition());

                            return null;
                        }

                        if (sourceObj is CassetteBlockManager manager) {
                            // isLevelMusic = true 时 sfx 自动等于 Audio.CurrentMusicEventInstance，无需重建
                            if (manager.GetFieldValue("sfx") is EventInstance sfx &&
                                !(bool) manager.GetFieldValue("isLevelMusic")) {
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
                            return new WeakReference(sourceWeak.Target.DeepClone(deepCloneState));
                        }

                        // 修复启用 CelesteNet 后保存状态时的崩溃
                        // System.ObjectDisposedException: 无法访问已释放的对象。
                        // 对象名:“RenderTarget2D”。
                        if (sourceObj is RenderTarget2D renderTarget2D && renderTarget2D.IsDisposed) {
                            return new RenderTarget2D(
                                renderTarget2D.GraphicsDevice,
                                renderTarget2D.Width,
                                renderTarget2D.Height,
                                renderTarget2D.LevelCount != 1,
                                renderTarget2D.Format,
                                renderTarget2D.DepthStencilFormat,
                                0,
                                renderTarget2D.RenderTargetUsage
                            );
                        }
                    }
                }

                return null;
            });

            // Clone 对象的字段后，进行自定的处理
            // After cloning, perform custom processing
            DeepCloner.AddPostCloneProcessor((sourceObj, clonedObj, deepCloneState) => {
                if (clonedObj == null) return null;

                lock (sourceObj) {
                    // 修复：DeepClone 的 hashSet.Containes(里面存在的引用对象) 总是返回 False，Dictionary 无此问题
                    // 原因：没有重写 GetHashCode 方法 https://github.com/force-net/DeepCloner/issues/17#issuecomment-678650032
                    // Fix: DeepClone's hashSet.Contains (ReferenceType) always returns false, Dictionary has no such problem
                    if (clonedObj.GetType().IsHashSet(out Type hashSetElementType) && !hashSetElementType.IsSimple()) {
                        IEnumerator enumerator = ((IEnumerable) clonedObj).GetEnumerator();

                        List<object> backup = new List<object>();
                        while (enumerator.MoveNext()) {
                            backup.Add(enumerator.Current);
                        }

                        if (backup.Count == 0) return clonedObj;

                        clonedObj.InvokeMethod("Clear");
                        backup.ForEach(obj => { clonedObj.InvokeMethod("Add", obj); });
                    }

                    // 同上
                    if (clonedObj.GetType().IsDictionary(out Type dictKeyType, out Type _)
                        && !dictKeyType.IsSimple() && clonedObj is IDictionary clonedDict && clonedDict.Count > 0
                    ) {
                        Dictionary<object, object> backupDict = new Dictionary<object, object>();
                        backupDict.AddRange(clonedDict);
                        clonedDict.Clear();
                        clonedDict.AddRange(backupDict);
                    }

                    // LightingRenderer 需要，不然不会发光
                    if (clonedObj is VertexLight vertexLight) {
                        vertexLight.Index = -1;
                    }

                    // Clone dynData.Data
                    Type objType = sourceObj.GetType();
                    if (objType.IsClass) {
                        do {
                            object dataMap = DynDataUtils.GetDataMap(objType);
                            if (dataMap == null) continue;

                            object[] parameters = { sourceObj, new object()};
                            if (false == (bool) dataMap.InvokeMethod("TryGetValue", parameters)) continue;

                            object sourceValue = parameters[1];
                            if (!(sourceValue.GetFieldValue("Data") is Dictionary<string, object> data) || data.Count == 0) continue;

                            dataMap.InvokeMethod("Add", clonedObj, sourceValue.DeepClone(deepCloneState));
                        } while ((objType = objType.BaseType) != null && objType.IsSameOrSubclassOf(typeof(object)));
                    }

                    if (clonedObj is VirtualTexture virtualTexture && virtualTexture.IsDisposed) {
                        virtualTexture.Reload();
                    }

                    if (clonedObj is VirtualRenderTarget virtualRenderTarget && virtualRenderTarget.IsDisposed) {
                        virtualRenderTarget.Reload();
                    }
                }

                return clonedObj;
            });
        }

        public static void Clear() {
            DeepCloner.ClearKnownTypesProcessors();
            DeepCloner.ClearPreCloneProcessors();
            DeepCloner.ClearPostCloneProcessors();
        }

        // 共用 DeepCloneState 可使多次 DeepClone 复用相同对象避免多次克隆同一对象
        private static DeepCloneState sharedDeepCloneState = new DeepCloneState();

        private static void InitSharedDeepCloneState() {
            if (sharedDeepCloneState == null) {
                sharedDeepCloneState = new DeepCloneState();
            }
        }

        public static void ClearSharedDeepCloneState() {
            sharedDeepCloneState = null;
        }

        public static void SetSharedDeepCloneState(DeepCloneState deepCloneState) {
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
}