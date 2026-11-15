#if DEBUG
namespace Celeste.Mod.SpeedrunTool.DebugTool;

internal static class MemoryTracker {

    public struct GcSample(long allocated, int gc0, int gc1, int gc2) {
        public long Allocated = allocated;
        public int Gc0 = gc0;
        public int Gc1 = gc1;
        public int Gc2 = gc2;
    }

    public static GcSample TakeGcSample() => new(GC.GetAllocatedBytesForCurrentThread(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    public static string FormatDiff(GcSample before, GcSample after, bool simplified) {
        long allocatedDiff = after.Allocated - before.Allocated;
        int gc0Diff = after.Gc0 - before.Gc0;
        int gc1Diff = after.Gc1 - before.Gc1;
        int gc2Diff = after.Gc2 - before.Gc2;
        return simplified ? $"{allocatedDiff / 1024.0 / 1024.0,7:F2} MB, GC ({gc0Diff}/{gc1Diff}/{gc2Diff})"
                          : $"{allocatedDiff / 1024.0 / 1024.0,7:F2} MB,   GC collections: {gc0Diff}/{gc1Diff}/{gc2Diff}";
    }
    public static void CheckFragmentation(string tag) {
        if (!MemoryTracker_CheckFragmentation) {
            return;
        }

        GCMemoryInfo info = GC.GetGCMemoryInfo();

        // 获取关键指标
        long totalHeapSize = info.HeapSizeBytes;        // GC 堆总大小
        long fragmentedBytes = info.FragmentedBytes;    // 碎片化总字节数

        // 计算碎片率（百分比）
        double fragmentationRatio = 0;
        if (totalHeapSize > 0) {
            fragmentationRatio = (double)fragmentedBytes / totalHeapSize * 100;
        }

        // 输出结果（转换为 MB）
        Logger.Debug("SpeedrunTool/MemoryTracker", $"[{tag}] Total GC Heap Size: {totalHeapSize / 1024.0 / 1024.0:F2} MB, Fragmented Ratio: {fragmentationRatio:F2}%");
    }
}
#endif