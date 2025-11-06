using System.Collections.Generic;

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

        private static AtlasHolder CurrentSlot => new AtlasHolder(SaveSlotsManager.SlotName, isSlot: true);

        private static readonly AtlasHolder CurrentGame = new AtlasHolder($"SpeedrunTool/{nameof(AtlasHandler)}/CurrentGame", isSlot: false);

        [Load]
        private static void Load() {
            On.Monocle.Atlas.Dispose += Atlas_Dispose;
        }

        [Unload]
        private static void Unload() {
            On.Monocle.Atlas.Dispose -= Atlas_Dispose;
        }


        private static void Atlas_Dispose(On.Monocle.Atlas.orig_Dispose orig, Atlas self) {
            BipartiteGraph.RemoveEdge(self, CurrentGame);
            if (BipartiteGraph.HasDisposeBarrier(self)) {
                Logger.Debug($"SpeedrunTool/{nameof(AtlasHandler)}", $"{self.DataPath} was to be disposed but stopped by us.");
            }
            else {
                Logger.Debug($"SpeedrunTool/{nameof(AtlasHandler)}", $"Dispose {self.DataPath}");
                orig(self);
            }
        }

        internal static void Add(Atlas atlas) {
            BipartiteGraph.AddEdge(atlas, CurrentSlot);
        }

        internal static void AddAction() {
            SaveLoadAction.InternalSafeAdd(
                saveState: (_, _) => {
                    SyncSlotToGame();
                },
                loadState: (_, _) => {
                    SyncSlotToGame();
                },
                clearState: () => {
                    BipartiteGraph.Remove(CurrentSlot);
                }
            );
        }

        private static void SyncSlotToGame() {
            BipartiteGraph.Remove(CurrentGame);
            BipartiteGraph.CloneVertex(CurrentSlot, CurrentGame);
        }

        internal static class BipartiteGraph {
            private static readonly HashSet<Atlas> Atlases = new();

            private static readonly HashSet<AtlasHolder> Holders = new();

            private static readonly HashSet<(Atlas, AtlasHolder)> Edges = new();

            private static readonly Dictionary<Atlas, List<AtlasHolder>> AdjacencyList_1 = new();

            private static readonly Dictionary<AtlasHolder, List<Atlas>> AdjacencyList_2 = new();

            internal static bool TryAdd(Atlas atlas) {
                if (Atlases.Add(atlas)) {
                    AdjacencyList_1[atlas] = [];
                    return true;
                }
                return false;
            }

            internal static bool TryAdd(AtlasHolder holder) {
                if (Holders.Add(holder)) {
                    AdjacencyList_2[holder] = [];
                    return true;
                }
                return false;
            }

            internal static void AddEdge(Atlas atlas, AtlasHolder holder) {
                TryAdd(atlas);
                TryAdd(holder);
                if (Edges.Add((atlas, holder))) {
                    AdjacencyList_1[atlas].Add(holder);
                    AdjacencyList_2[holder].Add(atlas);
                }
            }

            internal static void CloneVertex(AtlasHolder from, AtlasHolder to) {
                TryAdd(from);
                if (!TryAdd(to)) {
                    throw new Exception($"{to} is already in this graph.");
                }
                foreach (Atlas atlas in AdjacencyList_2[from]) {
                    AddEdge(atlas, to);
                }
            }

            internal static void RemoveEdge(Atlas atlas, AtlasHolder holder) {
                if (Edges.Remove((atlas, holder))) {
                    AdjacencyList_2[holder].Remove(atlas);
                    if (AdjacencyList_1[atlas].Remove(holder)) {
                        if (AdjacencyList_1[atlas].IsNullOrEmpty()) {
                            OnLosingAllNeighbour(atlas);
                        }
                    }
                }
            }

            internal static void Remove(AtlasHolder holder) {
                if (!AdjacencyList_2.TryGetValue(holder, out List<Atlas> list)) {
                    return;
                }
                foreach (Atlas atlas in list) {
                    Edges.Remove((atlas, holder));
                    if (AdjacencyList_1[atlas].Remove(holder)) {
                        if (AdjacencyList_1[atlas].IsNullOrEmpty()) {
                            OnLosingAllNeighbour(atlas);
                        }
                    }
                }
                AdjacencyList_2.Remove(holder);
                Holders.Remove(holder);
            }

            internal static void OnLosingAllNeighbour(Atlas atlas) {
                AdjacencyList_1.Remove(atlas);
                Atlases.Remove(atlas);
                atlas?.Dispose();
            }

            internal static bool HasDisposeBarrier(Atlas atlas) {
                return Atlases.Contains(atlas);
            }

            internal static void Log() {
                System.Text.StringBuilder sb = new();
                sb.Append("BipartiteGraph Structure:");
                foreach (Atlas atlas in Atlases) {
                    sb.Append($"\n{atlas.DataPath} -> ");
                    foreach (AtlasHolder holder in AdjacencyList_1[atlas]) {
                        sb.Append($" {holder.Name}, ");
                    }
                }
                Logger.Debug($"SpeedrunTool/{nameof(AtlasHolder)}", sb.ToString());
            }
        }

        internal struct AtlasHolder(string name, bool isSlot) {
            public enum HolderType { Slot, CurrentRunningGame }

            public string Name = name;

            public HolderType Type = isSlot ? HolderType.Slot : HolderType.CurrentRunningGame;

            public override readonly string ToString() {
                return $"{nameof(AtlasHolder)}[{(Type == HolderType.Slot ? Name : "CurrentGame")}]";
            }
        }
    }
}
