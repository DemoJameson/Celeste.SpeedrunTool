﻿using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
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
                    || type == typeof(VirtualTexture)

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
                    // Fixes: Glyph Teleport Area Effect
                    && entity.GetType().FullName != "Celeste.Mod.AcidHelper.Entities.InstantTeleporterRenderer"
                ) return sourceObj;

                // 稍后重新创建正在播放的 SoundSource 里的 EventInstance 实例
                if (sourceObj is SoundSource source
                    // TODO SoundEmitter 的声音会存留在关卡中，切换房间后保存依然会播放
                    && !(source.Entity is SoundEmitter)
                    && source.Playing
                    && source.GetFieldValue("instance") is EventInstance instance) {
                    if (string.IsNullOrEmpty(source.EventName)) return null;
                    instance.NeedManualClone(true);
                    instance.SaveTimelinePosition(instance.LoadTimelinePosition());

                    return null;
                }

                if (sourceObj is CassetteBlockManager manager) {
                    // isLevelMusic = true 时 sfx 自动等于 Audio.CurrentMusicEventInstance，无需重建
                    if (manager.GetFieldValue("sfx") is EventInstance sfx &&
                        !(bool) manager.GetFieldValue("isLevelMusic")) {
                        sfx.NeedManualClone(true);
                    }

                    if (manager.GetFieldValue("snapshot") is EventInstance snapshot) {
                        snapshot.NeedManualClone(true);
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

                return null;
            });

            // Clone 对象的字段后，进行自定的处理
            // After cloning, perform custom processing
            DeepCloner.AddPostCloneProcessor((sourceObj, clonedObj, deepCloneState) => {
                if (clonedObj == null) return null;

                // 修复：DeepClone 的 hashSet.Containes(里面存在的引用对象) 总是返回 False，Dictionary 无此问题
                // Fix: DeepClone's hashSet.Contains (ReferenceType) always returns false, Dictionary has no such problem
                if (clonedObj.GetType().IsHashSet(out Type hashSetElementType) && !hashSetElementType.IsSimple()) {
                    IEnumerator enumerator = ((IEnumerable) clonedObj).GetEnumerator();

                    List<object> backup = new List<object>();
                    while (enumerator.MoveNext()) {
                        backup.Add(enumerator.Current);
                    }

                    clonedObj.InvokeMethod("Clear");

                    backup.ForEach(obj => { clonedObj.InvokeMethod("Add", obj); });
                }

                // LightingRenderer 需要，不然不会发光
                if (clonedObj is VertexLight vertexLight) {
                    vertexLight.Index = -1;
                }

                // Clone dynData.Data
                Type objType = sourceObj.GetType();
                if (objType.IsClass) {
                    do {
                        IDictionary dataMap = DynDataUtils.GetDataMap(objType);
                        if (dataMap == null || dataMap.Count == 0) continue;
                        WeakReference sourceWeak = new WeakReference(sourceObj);

                        if (!dataMap.Contains(sourceWeak)) continue;
                        if (!(dataMap[sourceWeak].GetFieldValue("Data") is Dictionary<string, object> data) || data.Count == 0) continue;

                        WeakReference clonedWeak = new WeakReference(clonedObj);
                        dataMap[clonedWeak] = dataMap[sourceWeak].DeepClone(deepCloneState);
                    } while ((objType = objType.BaseType) != null && objType.IsSameOrSubclassOf(typeof(object)));
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