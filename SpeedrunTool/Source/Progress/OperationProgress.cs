using System;
using System.Diagnostics;

namespace Celeste.Mod.SpeedrunTool.Progress;

// Deliberately independent of the scene, entity tracker and clone state.
// A progress scope must never become part of a savestate.
internal sealed class OperationProgress : IDisposable {
    [ThreadStatic] private static OperationProgress current;
    internal static OperationProgress Current => current;

    private readonly OperationProgress parent;
    private readonly Action<OperationProgress> changed;
    private readonly Action finished;
    private readonly Stopwatch clock;
    private bool disposed;

    internal string Title { get; }
    internal string Stage { get; private set; } = "";
    internal string Detail { get; private set; } = "";
    internal bool Presented { get; set; }
    internal bool SkipLogged { get; set; }
    internal int Completed { get; private set; }
    internal int Total { get; private set; }
    internal float? Fraction => Total > 0 ? Math.Clamp((float)Completed / Total, 0f, 1f) : null;
    internal double ElapsedSeconds => clock.Elapsed.TotalSeconds;

    internal OperationProgress(string title, Action<OperationProgress> changed, Action finished) {
        parent = current;
        Title = title;
        clock = parent?.clock ?? Stopwatch.StartNew();
        this.changed = changed;
        this.finished = finished;
        current = this;
        // Do not present here: quick operations must stay completely silent.
    }

    internal void Report(string stage, string detail = "", int completed = 0, int total = 0) {
        Stage = stage ?? "";
        Detail = detail ?? "";
        Total = Math.Max(0, total);
        Completed = Math.Clamp(completed, 0, Total);
        Refresh();
    }

    internal void Refresh() {
        if (!disposed && current == this) {
            changed?.Invoke(this);
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
    }
}
