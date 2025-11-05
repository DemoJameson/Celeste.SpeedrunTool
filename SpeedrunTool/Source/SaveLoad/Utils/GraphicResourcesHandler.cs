using Force.DeepCloner.Helpers;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Utils;
internal static class GraphicResourcesHandler {

    // VirtualAssets: if unloaded, then reload

    // Atlas: let us decide when it should dispose (and Atlas is a collection of VirtualTexture). There is no reload function

    // MTexture: holding a VirtualAsset, so no need for special handle?

    // Image: holds MTexture

    // Sprite: extending image, and gets resources from an Atlas


    private static int count = 0;

    private static int countInternal = 0;
    private static void Log(this object obj) {
        // Logger.Warn($"SpeedrunTool/{nameof(GraphicResourcesHandler)}", obj.ToString());
    }

    private static readonly List<VirtualAsset> VirtualAssets = new(); // added and cleared when loading, so no need to be individual

    private static readonly HashSet<Atlas> SavedAtlas = new();

    private static readonly HashSet<Atlas> ShouldBeDisposedAtlas = new();
    // todo: each save slot should have a different HoldingAtlas

    [Load]
    private static void Load() {
        On.Monocle.Atlas.Dispose += Atlas_Dispose;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Atlas.Dispose -= Atlas_Dispose;
    }

    public static void ExternalLog(string str) {
        // str.Log();
    }

    private static void Atlas_Dispose(On.Monocle.Atlas.orig_Dispose orig, Atlas self) {
        if (SavedAtlas.Contains(self)) {
            ShouldBeDisposedAtlas.Add(self);
            $"{self} should be disposed but stopped by us".Log();
        }
        else {
            $"Dispose {self}".Log();
            orig(self);
        }
    }

    public static void Add(Atlas atlas) {
        SavedAtlas.Add(atlas);
    }

    public static void Add(VirtualAsset asset) {
        count++;
        $"add #{count} {asset}".Log();
        VirtualAssets.Add(asset);
    }

    public static void AssetsClear() {
        VirtualAssets.Clear();
        count = 0;
    }

    public static void DelayedDispose() {
        SavedAtlas.Clear();
        foreach (Atlas atlas in ShouldBeDisposedAtlas) {
            atlas.Dispose();
        }
        ShouldBeDisposedAtlas.Clear();
    }

    public static void DeepClonerInternalLog(object obj) {
        if (obj is VirtualAsset) {
            countInternal++;
            $"#{countInternal} {obj}".Log();
        }
    }

    internal static void ReloadVirtualAssets() {
        SaveLoadAction.InternalSafeAdd(
            saveState: (_, _) => {
                countInternal = 0;
                LogVirtualAssetDeepCloneProcedure();
            },
            loadState: (_, _) => {
                countInternal = 0;
                List<VirtualAsset> list = new List<VirtualAsset>(VirtualAssets);
                // if load too frequently and switching between different slots, then collection might be modified? idk
                // so we avoid the crash in this way

                foreach (VirtualAsset virtualAsset in list) {
                    switch (virtualAsset) {
                        case VirtualTexture { IsDisposed: true } virtualTexture:
                            // Fix: 全屏切换然后读档煤球红边消失
                            if (!virtualTexture.Name.StartsWith("dust-noise-")) {
                                virtualTexture.Reload();
                                "reload".Log();
                            }

                            break;
                        case VirtualRenderTarget { IsDisposed: true } virtualRenderTarget:
                            virtualRenderTarget.Reload();
                            "virtual render target reload".Log();
                            break;
                    }
                }

                AssetsClear();

                LogVirtualAssetDeepCloneProcedure();
            },
            clearState: () => {
                AssetsClear();
                DelayedDispose();
            },
            preCloneEntities: () => AssetsClear()
        );
    }

    private static void LogVirtualAssetDeepCloneProcedure() {
        StringBuilder sb = new();
        AppendInstanceField(typeof(VirtualTexture), null);
        AppendInstanceField(typeof(VirtualRenderTarget), null);
        AppendInstanceField(typeof(MTexture), null);
        AppendInstanceField(typeof(PlayerHair), null);
        AppendInstanceField(typeof(Sprite), null);
        sb.ToString().Log();

        void AppendInstanceField(Type type, object obj) {
            // modified from DeepClonerMsilGenerator.GenerateClonerInternal
            List<FieldInfo> list = new List<FieldInfo>();
            Type tp = type;
            do {
                // don't do anything with this dark magic!
                if (tp == typeof(ContextBoundObject)) {
                    break;
                }

                list.AddRange(tp.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly));
                tp = tp.BaseType;
            } while (tp != null);
            sb.Append($"\n--- {type.FullName} ---\n");
            if (obj is not null) {
                foreach (FieldInfo fi in list) {
                    sb.Append($" ├  {PadToMultiple(fi.Name, 32)} | {PadToMultiple(fi.GetValue(obj),32 )} | {(DeepClonerSafeTypes.CanReturnSameObject(fi.FieldType) ? "" : "Complex")}\n");
                }
            }
            else {
                foreach (FieldInfo fi in list) {
                    sb.Append($" ├  {PadToMultiple(fi.Name, 32)} | {PadToMultiple(fi.FieldType.Name, 32)} | {(DeepClonerSafeTypes.CanReturnSameObject(fi.FieldType) ? "" : "Complex")}\n");
                }
            }
        }

        string PadToMultiple(object obj, int b) {
            string str = obj?.ToString() ?? " ";
            int length = str.Length;
            int targetLength = ((length - 1) / b + 1) * b;
            return str.PadRight(targetLength);
        }
    }
}

internal static class SkinModHelperPlusInject {

    [Initialize]
    private static void Init() {
        /*
        ModInterop.SaveLoadInterop.SaveLoadExports.RegisterStaticTypes(typeof(SkinsSystem), new string[1] { "SpriteDataCache" });
        ModInterop.SaveLoadInterop.SaveLoadExports.RegisterStaticTypes(typeof(HairConfig), new string[1] { "_Instance" });



        ModInterop.SaveLoadInterop.SaveLoadExports.AddCustomDeepCloneProcessor(delegate (object sourceObj) {
            if (sourceObj == SkinsSystem.SpriteDataCache && sourceObj is ConditionalWeakTable<Sprite, SpriteData> conditionalWeakTable) {
                ConditionalWeakTable<Sprite, SpriteData> dictionary = new ConditionalWeakTable<Sprite, SpriteData>();
                foreach (KeyValuePair<Sprite, SpriteData> item in (IEnumerable<KeyValuePair<Sprite, SpriteData>>)conditionalWeakTable) {
                    dictionary.AddOrUpdate((Sprite)item.Key.DeepCloneShared(), (SpriteData)item.Value.DeepCloneShared());
                }
                return dictionary;
            }

            if (sourceObj == HairConfig._Instance && sourceObj is ConditionalWeakTable<PlayerHair, HairConfig> conditionalWeakTable2) {
                ConditionalWeakTable<PlayerHair, HairConfig> dictionary2 = new ConditionalWeakTable<PlayerHair, HairConfig>();
                foreach (KeyValuePair<PlayerHair, HairConfig> item in (IEnumerable<KeyValuePair<PlayerHair, HairConfig>>)conditionalWeakTable2) {
                    dictionary2.AddOrUpdate((PlayerHair)item.Key.DeepCloneShared(), (HairConfig)item.Value.DeepCloneShared());
                }
                return dictionary2;
            }

            return (object)null;
        });
        */
    }

}
