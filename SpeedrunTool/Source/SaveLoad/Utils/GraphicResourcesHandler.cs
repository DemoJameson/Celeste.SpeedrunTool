using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
internal static class GraphicResourcesHandler {

    public static void Add(VirtualAsset asset) {
        VirtualAssetsHandler.Add(asset);
    }

    public static void Add(Atlas atlas) {
        AtlasHandler.Add(atlas);
    }

    internal static void AddSaveLoadAction() {
        AtlasHandler.AddAction();
        VirtualAssetsHandler.ReloadVirtualAssets();
    }

    private static class MemoryLeakHandler {
        /* 
         * 如果某个 mod 加载了某个 Disposable 的资源, 并且它还没有 Finalizer
         * 并且这个资源在某个实例上
         * 并且这个资源并不是 (在某个实体上的, 会随着实体 Removed 而 Dispose) 的
         * 那么在加载此资源前存档
         * 加载资源后, 读档回到加载前, 再加载, 再读档...
         * 就会造成内存泄漏
         * 
         * GraphicResource 类当然是实现了 Finalizer 的... 那么 Celeste 里的情况应该不会很严重吧...
         */

        private static void Handle() {
            throw new NotImplementedException("Don't know if there is memory leak");
        }
    }

    private static class VirtualAssetsHandler {

        private static readonly List<VirtualAsset> VirtualAssets = new(); // added and cleared when loading, so no need to be individual

        internal static void Add(VirtualAsset asset) {
            VirtualAssets.Add(asset);
        }
        internal static void ReloadVirtualAssets() {
            SaveLoadAction.InternalSafeAdd(
                loadState: (_, _) => {
                    List<VirtualAsset> list = new List<VirtualAsset>(VirtualAssets);
                    // if load too frequently and switching between different slots, then collection might be modified? idk
                    // so we avoid the crash in this way

                    foreach (VirtualAsset virtualAsset in list) {
                        switch (virtualAsset) {
                            case VirtualTexture { IsDisposed: true } virtualTexture:
                                // Fix: 全屏切换然后读档煤球红边消失
                                if (!virtualTexture.Name.StartsWith("dust-noise-")) {
                                    virtualTexture.Reload();
                                }

                                break;
                            case VirtualRenderTarget { IsDisposed: true } virtualRenderTarget:
                                virtualRenderTarget.Reload();
                                break;
                        }
                    }

                    VirtualAssets.Clear();
                },
                clearState: () => {
                    VirtualAssets.Clear();
                },
                preCloneEntities: () => {
                    VirtualAssets.Clear();
                }
            );
        }
    }

    private static class AtlasHandler {
        
        // This Fixes: WaveDashPresentation 中存档, 结束, 再读档, 会崩溃

        /* 每个存档带有信息: 它存档时持有的 atlas, 以及截止到存档时所有在它的历史上本应释放但没释放的 atlas
         * 外加 CurrentGame 带有: 读取的档上应释放的, 再加上读档后直到当前时刻为止, 所有本应释放的资源. 它不持有 atlas
         *
         * 每次清除存档时, 尝试释放这个存档持有的且在其他某个存档/CurrentGame 上本应释放的 atlas
         * 每次读档时, 将 CurrentGame 的历史回退到读取的档上
         * 每次存档时, 将 CurrentGame 的历史存入, 持有 atlas
         * 每次 Dispose 但被阻碍时, 存入 CurrentGame
         */

        private static Holder CurrentSlot => new Holder(SaveSlotsManager.SlotName, isSlot: true);

        private static readonly Holder CurrentGame = new Holder($"SpeedrunTool/{nameof(AtlasHandler)}/CurrentGame", isSlot: false);

        [Load]
        private static void Load() {
            On.Monocle.Atlas.Dispose += Atlas_Dispose;
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Atlas.Dispose -= Atlas_Dispose;
        }

        private static void Atlas_Dispose(On.Monocle.Atlas.orig_Dispose orig, Atlas self) {
            if (BipartiteGraph.HasDisposeBarrier(self)) {
                BipartiteGraph.AddDisposeEdge(self, CurrentGame);
                Logger.Info($"SpeedrunTool/{nameof(AtlasHandler)}", $"{self.DataPath} was to be disposed but stopped by us.");
            }
            else {
                Logger.Info($"SpeedrunTool/{nameof(AtlasHandler)}", $"Dispose {self.DataPath}");
                orig(self);
            }
        }

        internal static void SafeDispose(Atlas atlas) {
            atlas?.Dispose();
        }

        internal static void Add(Atlas atlas) {
            BipartiteGraph.AddHoldEdge(atlas, CurrentSlot);
        }

        internal static void AddAction() {
            SaveLoadAction.InternalSafeAdd(
                saveState: (_, _) => {
                    BipartiteGraph.CloneDisposeEdge(CurrentGame, CurrentSlot);
                },
                loadState: (_, _) => {
                    BipartiteGraph.CloneDisposeEdge(CurrentSlot, CurrentGame);
                },
                clearState: () => {
                    BipartiteGraph.ClearAndDispose(CurrentSlot);
                }
            );
        }


        internal static class BipartiteGraph {
            internal enum State { Hold, Dispose } // Hold: don't dispose; Dispose: wanna dispose but failed to

            private static readonly Dictionary<Atlas, HashSet<Holder>> Atlas2Hold = new(); // only contains Hold edges

            private static readonly Dictionary<Atlas, HashSet<Holder>> Atlas2Dispose = new(); // only contains Dispose edges

            private static readonly Dictionary<Holder, HashSet<Atlas>> Hold2Atlas = new(); // only contains Hold edges

            private static readonly Dictionary<Holder, HashSet<Atlas>> Dispose2Atlas = new(); // only contains Dispose edges

            private static void SafeAdd<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> dict, TKey key, TValue value) {
                if (dict.TryGetValue(key, out HashSet<TValue> list)) {
                    list.Add(value);
                }
                else {
                    dict[key] = [value];
                }
            }

            private static void SafeRemove<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> dict, TKey key, TValue value) {
                if (dict.TryGetValue(key, out HashSet<TValue> list)) {
                    list.Remove(value);
                    if (list.IsNullOrEmpty()) {
                        dict.Remove(key);
                    }
                }
            }

            private static void SafeAdd<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> dict1, Dictionary<TValue, HashSet<TKey>> dict2, TKey key, TValue value) {
                SafeAdd(dict1, key, value);
                SafeAdd(dict2, value, key);
            }

            private static void SafeRemove<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> dict1, Dictionary<TValue, HashSet<TKey>> dict2, TKey key, TValue value) {
                SafeRemove(dict1, key, value);
                SafeRemove(dict2, value, key);
            }

            private static void SafeRemove<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> dict1, Dictionary<TValue, HashSet<TKey>> dict2, TKey key) {
                if (dict1.TryGetValue(key, out HashSet<TValue> list)) {
                    foreach (TValue value in list) {
                        SafeRemove(dict2, value, key);
                    }
                    dict1.Remove(key);
                }
            }

            internal static void AddHoldEdge(Atlas atlas, Holder holder) {
                SafeAdd(Atlas2Hold, Hold2Atlas, atlas, holder);
                SafeRemove(Atlas2Dispose, Dispose2Atlas, atlas, holder);
            }

            internal static void AddDisposeEdge(Atlas atlas, Holder holder) {
                SafeRemove(Atlas2Hold, Hold2Atlas, atlas, holder);
                SafeAdd(Atlas2Dispose, Dispose2Atlas, atlas, holder);
            }

            internal static void Clear(Atlas atlas) {
                SafeRemove(Atlas2Hold, Hold2Atlas, atlas);
                SafeRemove(Atlas2Dispose, Dispose2Atlas, atlas);
            }

            internal static void Clear(Holder holder) {
                SafeRemove(Hold2Atlas, Atlas2Hold, holder);
                SafeRemove(Dispose2Atlas, Atlas2Dispose, holder);
            }

            internal static void ClearAndDispose(Holder holder) {
                if (!Hold2Atlas.TryGetValue(holder, out HashSet<Atlas> list)) {
                    return;
                }
                HashSet<Atlas> maybeUnholding = [.. list];
                Clear(holder);
                HashSet<Atlas> unholding = [.. maybeUnholding.Where(x => !Atlas2Hold.ContainsKey(x))];
                foreach (Atlas atlas in unholding.Where(Atlas2Dispose.ContainsKey)) {
                    SafeDispose(atlas);
                }
                foreach (Atlas atlas2 in unholding) {
                    Clear(atlas2);
                }
            }

            internal static void CloneDisposeEdge(Holder from, Holder to) {
                if (Dispose2Atlas.TryGetValue(from, out HashSet<Atlas> list)) {
                    foreach (Atlas atlas in list) {
                        AddDisposeEdge(atlas, to);
                    }
                }
            }


            internal static bool HasDisposeBarrier(Atlas atlas) {
                return Atlas2Hold.ContainsKey(atlas);
            }

            internal static void Log() {
#if DEBUG
                System.Text.StringBuilder sb = new();
                sb.Append("BipartiteGraph Structure:");
                foreach (Atlas atlas in Atlas2Hold.Keys.Union(Atlas2Dispose.Keys)) {
                    sb.Append($"\n{atlas.DataPath}");
                    if (Atlas2Hold.TryGetValue(atlas, out HashSet<Holder> list1)) {
                        sb.Append($" | hold by -> ");
                        foreach (Holder holder in list1) {
                            sb.Append($" {holder.Name}, ");
                        }
                    }
                    if (Atlas2Dispose.TryGetValue(atlas, out HashSet<Holder> list2)) {
                        sb.Append($" | dispose by -> ");
                        foreach (Holder holder in list2) {
                            sb.Append($" {holder.Name}, ");
                        }
                    }
                }
                sb.Append('\n');
                foreach (Holder holder in Hold2Atlas.Keys.Union(Dispose2Atlas.Keys)) {
                    sb.Append($"\n{holder.Name}");
                    if (Hold2Atlas.TryGetValue(holder, out HashSet<Atlas> list1)) {
                        sb.Append($" | hold -> ");
                        foreach (Atlas atlas in list1) {
                            sb.Append($" {atlas.DataPath}, ");
                        }
                    }
                    if (Dispose2Atlas.TryGetValue(holder, out HashSet<Atlas> list2)) {
                        sb.Append($" | dispose -> ");
                        foreach (Atlas atlas in list2) {
                            sb.Append($" {atlas.DataPath}, ");
                        }
                    }
                }
                Logger.Debug($"SpeedrunTool/{nameof(Holder)}", sb.ToString());
#endif
            }
        }

        internal struct Holder(string name, bool isSlot) {
            public enum HolderType { Slot, CurrentRunningGame }

            public string Name = name;

            public HolderType Type = isSlot ? HolderType.Slot : HolderType.CurrentRunningGame;

            public override readonly string ToString() {
                return $"{nameof(Holder)}[{(Type == HolderType.Slot ? Name : "CurrentGame")}]";
            }
        }
    }
}
