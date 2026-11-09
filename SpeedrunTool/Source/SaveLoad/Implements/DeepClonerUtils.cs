using Celeste.Mod.SpeedrunTool.ModInterop;
using FMOD.Studio;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using NLua;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

public static class DeepClonerUtils {
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FastReflectionHelper.FastInvoker> hashSetAddInvokers = new();

    [ThreadStatic] private static Stack<Component> backupComponents;
    [ThreadStatic] private static Stack<object> backupHashSet;
    [ThreadStatic] private static Dictionary<object, object> backupDict;

    // 共用 DeepCloneState 可使多次 DeepClone 复用相同对象避免多次克隆同一对象
    private static DeepCloneState sharedDeepCloneState = new();

    // Clone 开始时，判断哪些类型是直接使用原对象而不 DeepClone 的
    // Before cloning, determine which types use the original object directly
    private static bool? KnownTypeProcessor(Type type) {
        if (
            // Celeste Singleton
            type == typeof(Celeste)
            || type == typeof(Settings)

            // Everest
            || type.IsSubclassOf(typeof(ModAsset))
            || type.IsSubclassOf(typeof(EverestModule))
            || type.IsSubclassOf(typeof(EverestModuleSettings))
            || type == typeof(EverestModuleMetadata)

            // Monocle
            || type == typeof(GraphicsDevice)
            || type == typeof(GraphicsDeviceManager)
            || type == typeof(Monocle.Commands)
            || type == typeof(BitTag)
            || type == typeof(Atlas)

            // 应该是可以直接使用原对象的. 但还有待测试
            // TileGrid.Tiles
            || type == typeof(VirtualMap<MTexture>)
            // Level.SolidData / BgData
            || type == typeof(VirtualMap<char>)

            // XNA GraphicsResource
            || type.IsSubclassOf(typeof(GraphicsResource))

            // NLua
            || type == typeof(Lua)
            || type == typeof(KeraLua.Lua)
            || type.IsSubclassOf(typeof(LuaBase))

            // MonoMod
            || type == typeof(ILHook)
            || type == typeof(Helpers.LegacyMonoMod.LegacyILHook)
            || typeof(IDetourBase).IsAssignableFrom(type)
            || type.GetInterfaces().Any(t => t.FullName is "Celeste.Mod.Helpers.LegacyMonoMod.ILegacyDetour")

            // CelesteNet
            || (type.Namespace?.StartsWith("Celeste.Mod.CelesteNet.") ?? false) && !type.IsSubclassOf(typeof(Entity))
        ) {
            return true;
        }

        if (SaveLoadInterop.CanReturnSameObject(type)) {
            return true;
        }


#if DEBUG
        if (Log_IDisposable) {
            if (typeof(IDisposable).IsAssignableFrom(Nullable.GetUnderlyingType(type) ?? type)) {
                if (type.IsCompilerGenerated()) {
                    Logger.Debug("SpeedrunTool/DeepClonerUtils", $"{type} is CompilerGenerated IDisposable, check if it could be cloned safely!");
                    // 比如由返回值 IEnumerator 的方法生成的类, 就是 IDisposable.
                    // 如果方法内部没有 using / try-finally, 那么就不持有资源, 不 Dispose 也没事
                }
                else {
                    Logger.Warn("SpeedrunTool/DeepClonerUtils", $"{type} is IDisposable and should not be cloned!");
                }
            }
        }
#endif

        return null;
    }

    // The processors below run for every cloned object. The original implementation re-evaluated
    // a long chain of `is` checks per object, called Type.IsHashSet/IsIDictionary (both allocate
    // via GetGenericArguments/GetInterfaces) and took a lock on every object. On a room-sized
    // object graph that is a large part of the clone time (measured: 2.7-3.5x slower than no
    // processors at all). Everything below caches the type-only decisions once per type, which
    // leaves at most one dictionary lookup (usually a [ThreadStatic] hit) per object.
    [Flags]
    private enum ProcFlags {
        None = 0,
        VirtualAsset = 1 << 0,
        Scene = 1 << 1,
        SoundSource = 1 << 2,
        CassetteBlockManager = 1 << 3,
        EventInstance = 1 << 4,
        WeakReference = 1 << 5,
        WeakReferenceT = 1 << 6,
        ConditionalWeakTable = 1 << 7,
        Entity = 1 << 8,
        HashSetComponent = 1 << 9,
        HashSetComplex = 1 << 10,
        DictionaryComplex = 1 << 11,
        Class = 1 << 12
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ProcFlags> procFlagsCache = new();

    // Direct-mapped thread-local cache in front of the concurrent dictionary. Four slots are
    // enough to keep the common "alternating a few types while walking a list" pattern a hit.
    private const int ProcFlagsCacheSlots = 4;
    [ThreadStatic] private static Type[] lastProcFlagsTypes;
    [ThreadStatic] private static ProcFlags[] lastProcFlagsValues;

    /// <summary>
    /// Drops the memoized per-type decisions. Called after a mod hot reload, because the cache
    /// keys (and the flags) belong to the unloaded mod assemblies and would otherwise pin them.
    /// </summary>
    internal static void ClearProcFlagsCache() {
        procFlagsCache.Clear();
        lastProcFlagsTypes = null;
        lastProcFlagsValues = null;
    }

    private static ProcFlags GetProcFlags(Type type) {
        int slot = RuntimeHelpers.GetHashCode(type) & (ProcFlagsCacheSlots - 1);
        Type[] cachedTypes = lastProcFlagsTypes ??= new Type[ProcFlagsCacheSlots];
        ProcFlags[] cachedValues = lastProcFlagsValues ??= new ProcFlags[ProcFlagsCacheSlots];

        if (cachedTypes[slot] == type) {
            return cachedValues[slot];
        }

        if (!procFlagsCache.TryGetValue(type, out ProcFlags flags)) {
            flags = ComputeProcFlags(type);
            procFlagsCache.TryAdd(type, flags);
        }

        cachedTypes[slot] = type;
        cachedValues[slot] = flags;
        return flags;
    }

    private static ProcFlags ComputeProcFlags(Type type) {
        ProcFlags flags = type.IsClass ? ProcFlags.Class : ProcFlags.None;

        if (typeof(VirtualAsset).IsAssignableFrom(type)) {
            flags |= ProcFlags.VirtualAsset;
        }
        if (typeof(Scene).IsAssignableFrom(type)) {
            flags |= ProcFlags.Scene;
        }
        if (typeof(SoundSource).IsAssignableFrom(type)) {
            flags |= ProcFlags.SoundSource;
        }
        if (typeof(CassetteBlockManager).IsAssignableFrom(type)) {
            flags |= ProcFlags.CassetteBlockManager;
        }
        if (type == typeof(EventInstance)) {
            flags |= ProcFlags.EventInstance;
        }
        if (typeof(WeakReference).IsAssignableFrom(type)) {
            flags |= ProcFlags.WeakReference;
        }
        if (type.IsWeakReference(out _)) {
            flags |= ProcFlags.WeakReferenceT;
        }
        if (type.IsConditionalWeakTable(out _, out _)) {
            flags |= ProcFlags.ConditionalWeakTable;
        }
        if (typeof(Entity).IsAssignableFrom(type)) {
            flags |= ProcFlags.Entity;
        }

        if (type.IsClass) {
            if (typeof(HashSet<Component>).IsAssignableFrom(type)) {
                flags |= ProcFlags.HashSetComponent;
            }
            else if (type.IsHashSet(out Type hashSetElementType) && !hashSetElementType.IsSimple()) {
                flags |= ProcFlags.HashSetComplex;
            }
            else if (type.IsIDictionary(out Type dictionaryKeyType, out _) && !dictionaryKeyType.IsSimple()) {
                flags |= ProcFlags.DictionaryComplex;
            }
        }

        return flags;
    }

    /// <summary>
    /// True while the throw-away warm-up clone runs (see <see cref="Utils.CloneWarmUp"/>).
    /// The processors below have side effects on the live game (virtual assets, audio, DynData,
    /// ...), so they are skipped for the disposable warm-up graph.
    /// </summary>
    internal static bool WarmingUp;

    // Clone 对象的字段前，判断哪些类型是直接使用原对象或者自行通过其它方法 clone
    // Before cloning object's field, determine which types are directly used by the original object
    private static object PreCloneProcessor(object sourceObj, DeepCloneState deepCloneState) {
        if (sourceObj == null || WarmingUp) {
            return null;
        }

        Type type = sourceObj.GetType();
        ProcFlags flags = GetProcFlags(type);

        // NOTE: the original implementation locked on every object here. Cloning is serialized by
        // design (all entry points wait for a running pre-clone task, and the game is frozen while
        // saving/loading), so the lock only cost time. The shared collections touched below are
        // guarded by their own locks instead.

        if ((flags & ProcFlags.VirtualAsset) != 0) {
            VirtualAsset virtualAsset = (VirtualAsset)sourceObj;
            if (StateManager.Instance.State == State.Loading || !Thread.CurrentThread.IsMainThread()) {
                // 预克隆的资源需要等待 LoadState 中移除实体之后才能判断是否需要 Reload，必须等待主线程中再操作
                GraphicResourcesHandler.Add(virtualAsset);
            }
            return virtualAsset;
        }

        if ((flags & ProcFlags.Scene) != 0) {
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
        if ((flags & ProcFlags.SoundSource) != 0 && sourceObj is SoundSource { Playing: true, instance: { } instance } source) {
            if (source.EventName.IsNullOrEmpty()) {
                return null;
            }

            instance.NeedManualClone();
            instance.SaveTimelinePosition(instance.LoadTimelinePosition());

            return null;
        }

        if ((flags & ProcFlags.CassetteBlockManager) != 0 && sourceObj is CassetteBlockManager manager) {
            // isLevelMusic = true 时 sfx 自动等于 Audio.CurrentMusicEventInstance，无需重建
            if (manager.sfx is { } sfx && !manager.isLevelMusic) {
                sfx.NeedManualClone();
            }

            if (manager.snapshot is { } snapshot) {
                snapshot.NeedManualClone();
            }

            return null;
        }

        // 重新创建正在播放的 EventInstance 实例
        if ((flags & ProcFlags.EventInstance) != 0 && sourceObj is EventInstance eventInstance && eventInstance.IsNeedManualClone()) {
            EventInstance clonedEventInstance = eventInstance.Clone();

            bool isMainThread = Thread.CurrentThread.IsMainThread();
            if (StateManager.Instance.State == State.Saving && isMainThread) {
                SaveLoadAction.ClonedEventInstancesWhenSave.Add(clonedEventInstance);
            }
            else if (!isMainThread) {
                SaveLoadAction.ClonedEventInstancesWhenPreClone.Add(clonedEventInstance);
            }

            return clonedEventInstance;
        }

        // Fixes: 克隆 WeakReference 后 Target 没有一起被克隆的问题，修复 dynData.Weak 克隆不完整导致的一些报错
        // System.Reflection.TargetException: 非静态字段需要一个目标。
        // 在 System.Reflection.RtFieldInfo.CheckConsistency(Object target)
        // 在 System.Reflection.RtFieldInfo.InternalGetValue(Object obj, StackCrawlMark& stackMark)
        // 在 System.Reflection.RtFieldInfo.GetValue(Object obj)
        // 在 MonoMod.Utils.DynData`1.<>c__DisplayClass19_0.<.cctor>b__1(TTarget obj)
        // 在 MonoMod.Utils.DynData`1.get_Item(String name)
        // 在 Celeste.Mod.MaxHelpingHand.Entities.CustomizableRefill.<>c__DisplayClass0_0.<.ctor>b__0(Player player)
        if ((flags & ProcFlags.WeakReference) != 0 && sourceObj is WeakReference sourceWeak) {
            return new WeakReference(sourceWeak.Target.DeepClone(deepCloneState), sourceWeak.TrackResurrection);
        }

        // 手动克隆 WeakReference<T>
        if ((flags & ProcFlags.WeakReferenceT) != 0) {
            Type genericType = type.GetGenericArguments()[0];
            object[] parameters = { null };
            sourceObj.InvokeMethod("TryGetTarget", parameters);
            return type.GetConstructorInfo(genericType).Invoke(parameters.DeepClone(deepCloneState));
        }

        // 手动克隆 ConditionalWeakTable<TKey, TValue>
        // 之前好像不能正确支持 SMH+ v1.7.2 (尽管 SMH+ 自己内部写的也有问题)
        if ((flags & ProcFlags.ConditionalWeakTable) != 0) {
            type.IsConditionalWeakTable(out Type weakTableKeyType, out Type weakTableValueType);

            // 目前已知需要克隆 ConditionalWeakTable 的有:
            // SkinModHelperPlus:   ConditionalWeakTable<Sprite, List<Object>>
            // Aqua:                ConditionalWeakTable<Entity, DataContainer>   . 其中 Key 包含了 **所有** 实体
            //
            // 当 key 是 Level 时, 由于 SRT 克隆不同的 Level 对象时都会返回 Engine.Scene, 所以可能会出问题
            // 导致只能 TryAdd 而非 Add
            // https://discord.com/channels/403698615446536203/1520883580933374124/1521136713005662258
            FastReflectionHelper.FastInvoker addMethod = type.GetMethodDelegate("TryAdd");
            object weakTable = Activator.CreateInstance(type);

            // Entity-keyed CWTs are commonly used as per-entity dynamic
            // state. Only clone entries whose keys are part of the saved
            // object graph; stale entries from previous rooms otherwise
            // make a single save traverse and clone the entire history.
            bool onlyKnownEntityKeys = weakTableKeyType.IsSameOrSubclassOf(typeof(Entity));

            Type kvpType = typeof(KeyValuePair<,>).MakeGenericType(weakTableKeyType, weakTableValueType);
            FastReflectionHelper.FastInvoker keyGetter = kvpType.GetFieldInfo("key").GetFastInvoker();
            FastReflectionHelper.FastInvoker valueGetter = kvpType.GetFieldInfo("value").GetFastInvoker();
            object[] args = new object[2];
            foreach (object kvp in (IEnumerable)sourceObj) {
                object sourceKey = keyGetter.Invoke(kvp, null);
                object clonedKey = onlyKnownEntityKeys
                    ? deepCloneState.GetKnownRef(sourceKey)
                    : sourceKey.DeepClone(deepCloneState);
                if (clonedKey == null) {
                    continue;
                }
                object clonedValue = valueGetter.Invoke(kvp, null).DeepClone(deepCloneState);
                if (clonedKey != null && clonedValue != null) {
                    args[0] = clonedKey;
                    args[1] = clonedValue;
                    addMethod.Invoke(weakTable, args);
                }
            }
            return weakTable;
        }

        if ((flags & ProcFlags.Entity) != 0) {
            DesyncRiskAnalyzer.Check((Entity)sourceObj);
        }

        return SaveLoadInterop.CustomDeepCloneObject(sourceObj);
    }

    // Clone 对象的字段后，进行自定的处理
    // After cloning, perform custom processing
    private static object PostCloneProcessor(object sourceObj, object clonedObj, DeepCloneState deepCloneState) {
        if (sourceObj == null) {
            return null;
        }

        if (WarmingUp) {
            // The return value replaces the clone: null would null out every cloned object in the
            // throw-away warm-up graph and make it blow up with a NullReferenceException.
            return clonedObj;
        }

        Type type = clonedObj.GetType();
        ProcFlags flags = GetProcFlags(type);

        // 修复：DeepClone 后的 HashSet.Containes/Dictonary.ContainsKey(未重新 GetHashCode 的对象) 总是返回 False
        // 原因：没有重写 GetHashCode 方法 https://github.com/force-net/DeepCloner/issues/17#issuecomment-678650032
        // Fix: DeepClone's hashSet.Contains (ReferenceType) always returns false

        // 手动处理最常见的 HashSet<Component> 类型，避免使用反射以及判断类型
        if ((flags & ProcFlags.HashSetComponent) != 0) {
            HashSet<Component> hashSet = (HashSet<Component>)clonedObj;
            backupComponents ??= new Stack<Component>();
            foreach (Component component in hashSet) {
                if (component != null) {
                    backupComponents.Push(component);
                }
            }

            hashSet.Clear();
            while (backupComponents.Count > 0) {
                hashSet.Add(backupComponents.Pop());
            }
        }
        else if ((flags & ProcFlags.HashSetComplex) != 0) {
            IEnumerator enumerator = ((IEnumerable)clonedObj).GetEnumerator();
            backupHashSet ??= new Stack<object>();
            while (enumerator.MoveNext()) {
                if (enumerator.Current is { } element) {
                    backupHashSet.Push(element);
                }
            }

            if (backupHashSet.Count > 0) {
                clonedObj.InvokeMethod("Clear");
                FastReflectionHelper.FastInvoker addDelegate = hashSetAddInvokers.GetOrAdd(type, static t => t.GetMethodDelegate("Add"));
                while (backupHashSet.Count > 0) {
                    addDelegate.Invoke(clonedObj, backupHashSet.Pop());
                }
            }
        }
        else if ((flags & ProcFlags.DictionaryComplex) != 0 && clonedObj is IDictionary { Count: > 0 } clonedDict) {
            backupDict ??= [];
            backupDict.SetRange(clonedDict);
            clonedDict.Clear();
            clonedDict.SetRange(backupDict);
            backupDict.Clear();
        }

        // Clone dynData.Data
        if ((flags & ProcFlags.Class) != 0 && !DynDataUtils.IgnoreObjects.ContainsKey(sourceObj)) {
            bool cloned = false;
            Type objType = type;

            // 对其类型及其逐层展开的根类型 T 检验 DynData<T> 里是否涉及到这个对象
            do {
                if (DynDataUtils.NotExistDynData(objType, out object dataMap)) {
                    continue;
                }

                object[] parameters = [sourceObj, null];
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
        // 注意这个和 DynData 不是同一个东西
        if (DynamicData._DataMap.TryGetValue(sourceObj, out DynamicData._Data_ value) && value.Data.Count > 0) {
            DynamicData._DataMap.TryAdd(clonedObj, value.DeepClone(deepCloneState));
        }

        return clonedObj;
    }

    [Load]
    private static void Config() {
        DeepCloner.SetKnownTypesProcessor(KnownTypeProcessor);
        DeepCloner.SetPreCloneProcessor(PreCloneProcessor);
        DeepCloner.SetPostCloneProcessor(PostCloneProcessor);
    }

    [Unload]
    private static void Clear() {
        DeepCloner.ClearKnownTypesProcessor();
        DeepCloner.ClearPreCloneProcessor();
        DeepCloner.ClearPostCloneProcessor();
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
