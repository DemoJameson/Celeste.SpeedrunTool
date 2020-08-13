using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using FMOD.Studio;
using Force.DeepCloner;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad {
    internal static class DeepCloneUtils {
        public static void Config() {
            // Clone 开始时，判断哪些类型是直接使用原对象而不 DeepClone 的
            DeepCloner.AddKnownTypesProcessor((type) => {
                if (
                    // Celeste Singleton
                    type == typeof(Celeste)
                    || type == typeof(Settings)

                    // Everest
                    || type == typeof(ModAsset)

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
                ) {
                    return true;
                }

                return null;
            });

            // Clone 对象的字段前，判断哪些类型是直接使用原对象而不 DeepClone 的
            DeepCloner.AddPreCloneProcessor((sourceObj, deepCloneState) => {
                if (sourceObj is Level) {
                    // 金草莓死亡或者 PageDown/Up 切换房间后等等改变 Level 实例的情况
                    if (Engine.Scene is Level level) return level;
                    return sourceObj;
                }

                if (sourceObj is Entity entity && entity.TagCheck(Tags.Global)
                                               && !(entity is SeekerBarrierRenderer)
                                               && !(entity is LightningRenderer)
                ) return sourceObj;

                // 稍后重新创建正在播放的 SoundSource 里的 EventInstance 实例
                if (sourceObj is SoundSource source && source.Playing && source.GetFieldValue("instance") is EventInstance instance) {
                    if (string.IsNullOrEmpty(source.EventName)) return null;
                    instance.SavePath(source.EventName);
                    instance.SaveTimelinePosition(instance.LoadTimelinePosition());
                }

                // 重新创建正在播放的 EventInstance 实例
                if (sourceObj is EventInstance eventInstance && eventInstance.Clone() is EventInstance clonedInstance) {
                    deepCloneState.AddKnownRef(sourceObj, clonedInstance);
                    return clonedInstance;
                }

                return null;
            });

            // Clone 对象的字段后，进行自定的处理
            DeepCloner.AddPostCloneProcessor((sourceObj, clonedObj) => {
                if (clonedObj == null) return null;

                // 修复：DeepClone 的 hashSet.Containes(里面存在的引用对象) 总是返回 False，Dictionary 无此问题
                if (clonedObj.GetType().IsHashSet(out Type type) && !type.IsSimple()) {
                    IEnumerator enumerator = ((IEnumerable) clonedObj).GetEnumerator();

                    List<object> backup = new List<object>();
                    while (enumerator.MoveNext()) {
                        backup.Add(enumerator.Current);
                    }

                    clonedObj.InvokeMethod("Clear");

                    backup.ForEach(obj => { clonedObj.InvokeMethod("Add", obj); });
                }

                return clonedObj;
            });
        }

        public static void Clear() {
            DeepCloner.ClearKnownTypesProcessors();
            DeepCloner.ClearPreCloneProcessors();
            DeepCloner.ClearPostCloneProcessors();
        }
    }
}