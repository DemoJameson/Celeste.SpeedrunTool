using System;
using System.Diagnostics;

namespace Celeste.Mod.SpeedrunTool.Progress;

// Deliberately independent of the scene, entity tracker and clone state.
// A progress scope must never become part of a savestate.
internal sealed class OperationProgress : IDisposable {
    [ThreadStatic] private static OperationProgress current;
    internal static OperationProgress Current => current;

    private readonly OperationProgress parent;
    private readonly Action<OperationProgress, bool> changed;
    private readonly Action finished;
    private readonly Stopwatch clock;
    private bool disposed;

    internal string Title { get; }
    internal string Stage { get; private set; }
    internal string Detail { get; private set; } = "";
    internal int Completed { get; private set; }
    internal int Total { get; private set; }
    internal double ElapsedSeconds => clock.Elapsed.TotalSeconds;
    internal float? Fraction => Total > 0 ? (float)Completed / Total : null;

    internal OperationProgress(string stage, Action<OperationProgress, bool> changed, Action finished) {
        parent = current;
        Title = parent?.Title ?? stage;
        Stage = stage;
        clock = parent?.clock ?? Stopwatch.StartNew();
        this.changed = changed;
        this.finished = finished;
        current = this;
        Refresh(parent == null);
    }

    internal void Report(string stage, string detail = "", int completed = 0, int total = 0) {
        Stage = stage;
        Detail = detail ?? "";
        Total = Math.Max(0, total);
        Completed = Math.Clamp(completed, 0, Total);
        Refresh();
    }

    internal void Refresh(bool force = false) {
        if (!disposed && current == this) {
            changed?.Invoke(this, force);
        }
    }

    public void Dispose() {
        if (disposed) {
            return;
        }
        disposed = true;
        current = parent;
        if (parent == null) {
            finished?.Invoke();
        }
        else {
            parent.Refresh();
        }
    }
}
