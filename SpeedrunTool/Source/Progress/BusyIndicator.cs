using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Celeste.Mod.SpeedrunTool.Progress;

// Small, unobtrusive bottom-right overlay for SRT operations that are actually
// blocking the main thread for long enough to be noticed. It never runs gameplay
// logic, never pumps input and never captures a full-screen snapshot.
internal static class BusyIndicator {
    private const double ShowAfterSeconds = 0.25;
    // 5 Hz, not 10: every presentation calls GraphicsDevice.Present() from the blocked main thread,
    // so each extra frame is added latency on the operation being reported.
    private const double RefreshInterval = 0.2;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double lastPresentation;
    private static bool hasDrawn;
    private static bool drawing;
    private static bool failed;
    private static SpriteBatch batch;
    private static Texture2D frozenFrame;
    // Snapshot of the frozen frame plus the buffer it is read into. Both are kept between operations:
    // a full-screen Color[] is 8-15 MB, and letting it become garbage on every save/load puts an LOH
    // collection right behind the operation, on the first frames the player actually sees.
    private static Color[] frozenFramePixels;
    private static bool frozenFrameCaptured;

    [Load]
    private static void Load() {
        On.Monocle.Engine.Draw += EngineOnDraw;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Draw -= EngineOnDraw;
        ReleaseFrozenFrameBuffers();
        batch?.Dispose();
        batch = null;
        hasDrawn = false;
    }

    private static void EngineOnDraw(On.Monocle.Engine.orig_Draw orig, Engine self, GameTime gameTime) {
        drawing = true;
        try {
            orig(self, gameTime);
            hasDrawn = true;
        }
        finally {
            drawing = false;
        }
    }

    [Conditional("DEBUG")]
    private static void Diag(string message) => Logger.Info("SpeedrunTool/Progress/Diag", message);

    internal static OperationProgress Begin(string title) {
        bool root = OperationProgress.Current == null;
        if (!ModSettings.ShowProgress || !MainThreadHelper.IsMainThread || !hasDrawn || drawing || AssetReloadHelper.IsReloading
            || TasUtils.Running || TasUtils.HideGamePlay) {
            if (root) {
                Diag($"suppress {title}: enabled={ModSettings.ShowProgress} main={MainThreadHelper.IsMainThread} drawn={hasDrawn} " +
                     $"drawing={drawing} reload={AssetReloadHelper.IsReloading} " +
                     $"tas={TasUtils.Running} hideTas={TasUtils.HideGamePlay}");
            }
            return null;
        }
        OperationProgress progress = null;
        progress = new OperationProgress(title, TryPresent, () => {
            if (root) {
                Diag($"end {title} total={progress.ElapsedSeconds * 1000.0:F0}ms shown={progress.Presented}");
                // Keep the snapshot buffers for the next operation; only the content is stale.
                frozenFrameCaptured = false;
            }
        });
        if (root) {
            Level level = Engine.Scene as Level;
            Diag($"begin {title} scene={Engine.Scene?.GetType().Name ?? "null"} " +
                 $"room={level?.Session?.Level ?? "?"} slot={SaveSlotsManager.SlotName}");
        }
        return progress;
    }

    internal static void Wait(Task task) {
        if (task == null) {
            return;
        }

        // A pre-clone task that faulted (a third-party clone processor throwing) used to escape here as an
        // AggregateException on whatever called us. The result is only a head start for the next load, so
        // log it and move on -- but never continue while the task is still running: cloning concurrently
        // with it is not safe, which is why the wait has no timeout.
        try {
            if (task.IsCompleted) {
                task.Wait();
                return;
            }

            using OperationProgress progress = Begin("PRECLONE");
            if (progress is null) {
                // The overlay is off (the default) or cannot be drawn right now: just wait.
                task.Wait();
                return;
            }

            while (!task.Wait(100)) {
                progress.Refresh();
            }
        }
        catch (Exception exception) {
            Logger.LogDetailed(exception, "SpeedrunTool");
        }
    }

    private static void TryPresent(OperationProgress progress) {
        try {
            Present(progress);
        }
        catch (Exception exception) {
            failed = true;
            Logger.Warn("SpeedrunTool/Progress", $"Cannot present busy indicator: {exception.Message}");
        }
    }

    private static void Present(OperationProgress progress) {
        if (failed || drawing || !MainThreadHelper.IsMainThread) {
            return;
        }
        if (progress.ElapsedSeconds < ShowAfterSeconds) {
            return;
        }
        if (Clock.Elapsed.TotalSeconds - lastPresentation < RefreshInterval) {
            return;
        }
        GraphicsDevice device = Engine.Instance.GraphicsDevice;
        if (device.GetRenderTargets().Length != 0) {
            return;
        }

        Viewport viewport = device.Viewport;
        Rectangle scissor = device.ScissorRectangle;
        BlendState blend = device.BlendState;
        DepthStencilState depth = device.DepthStencilState;
        RasterizerState rasterizer = device.RasterizerState;
        SpriteBatch previousBatch = Draw.SpriteBatch;
        bool begun = false;
        try {
            int width = device.PresentationParameters.BackBufferWidth;
            int height = device.PresentationParameters.BackBufferHeight;
            if (width <= 0 || height <= 0) {
                return;
            }

            string title = Clean(progress.Title);
            if (progress.Total > 0) {
                title += "  " + progress.Completed + "/" + progress.Total;
            }
            string stage = progress.Stage != progress.Title ? Clean(progress.Stage) : "";
            if (!string.IsNullOrWhiteSpace(progress.Detail)) {
                string detail = progress.Detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (detail.Length > 30) {
                    detail = detail[..29] + "…";
                }
                stage = string.IsNullOrWhiteSpace(stage) ? detail : stage + "  —  " + detail;
            }
            bool hasStageLine = !string.IsNullOrWhiteSpace(stage);

            if (!frozenFrameCaptured) {
                // The main thread is blocked in a synchronous operation: no new game
                // frame is drawn between stages. Without restoring this base frame,
                // each translucent Present would composite over itself and get darker.
                //
                // Captured once per operation (the back buffer still holds the last presented frame,
                // because nothing has drawn since), into buffers that are kept and reused.
                if (frozenFramePixels is null || frozenFramePixels.Length != width * height) {
                    frozenFramePixels = new Color[width * height];
                    frozenFrame?.Dispose();
                    frozenFrame = null;
                }

                device.GetBackBufferData(frozenFramePixels);
                frozenFrame ??= new Texture2D(device, width, height);
                frozenFrame.SetData(frozenFramePixels);
                frozenFrameCaptured = true;
            }

            device.Viewport = new Viewport(0, 0, width, height);
            Draw.SpriteBatch = batch ??= new SpriteBatch(device);
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            begun = true;
            batch.Draw(frozenFrame, new Rectangle(0, 0, width, height), Color.White);
            batch.End();
            begun = false;

            // Virtual 1920x1080 canvas with a scaling matrix: the readable,
            // fractional-scale rendering that was in use before the integer
            // experiment. It scales with the game window instead of using a
            // fixed number of backbuffer pixels.
            float uiScale = Math.Min(width / 1920f, height / 1080f);
            Matrix matrix = Matrix.CreateScale(uiScale) * Matrix.CreateTranslation(
                (width - 1920f * uiScale) / 2f, (height - 1080f * uiScale) / 2f, 0f);

            const float pillWidth = 760f;
            float pillHeight = hasStageLine ? 112f : 78f;
            float x = 1920f - pillWidth - 24f;
            float y = 1080f - pillHeight - 24f;

            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, matrix);
            begun = true;
            Draw.Rect(x, y, pillWidth, pillHeight, Color.Black * 0.62f);
            Draw.Rect(x, y, pillWidth, 3f, Color.CornflowerBlue * 0.9f);

            float centerX = x + pillWidth / 2f;
            DrawCenteredText(title, centerX, hasStageLine ? y + 28f : y + pillHeight / 2f,
                0.8f, pillWidth - 48f);
            if (hasStageLine) {
                DrawCenteredText(stage, centerX, y + 70f, 0.48f, pillWidth - 48f);
            }

            const float barInset = 24f;
            float barW = pillWidth - barInset * 2f;
            float barY = y + pillHeight - 24f;
            Draw.Rect(x + barInset, barY, barW, 8f, Color.White * 0.18f);
            float fraction = progress.Fraction ?? 0f;
            if (progress.Fraction is null) {
                fraction = (float)(progress.ElapsedSeconds % 1.0);
            }
            Draw.Rect(x + barInset, barY, barW * Math.Clamp(fraction, 0f, 1f), 8f,
                Color.CornflowerBlue);
            batch.End();
            begun = false;

            // Present from this thread is the only way to show anything while the operation holds the
            // main thread; the engine presents normally again once it returns. The sync interval
            // cannot be relaxed here: FNA3D_SwapBuffers() takes no interval, and
            // PresentationParameters.PresentationInterval only reaches the device through
            // FNA3D_ResetBackbuffer (ApplyChanges/Reset), which recreates the swapchain. Presenting
            // less often (RefreshInterval) is the only lever, which is why it is 5 Hz.
            device.Present();
            if (!progress.Presented) {
                progress.Presented = true;
                Diag($"show {progress.Title} at {progress.ElapsedSeconds * 1000.0:F0}ms");
            }
            lastPresentation = Clock.Elapsed.TotalSeconds;
        }
        finally {
            if (begun) {
                try {
                    batch.End();
                }
                catch {
                }
            }
            Draw.SpriteBatch = previousBatch;
            device.Viewport = viewport;
            device.ScissorRectangle = scissor;
            device.BlendState = blend;
            device.DepthStencilState = depth;
            device.RasterizerState = rasterizer;
        }
    }

    private static void DrawCenteredText(string text, float centerX, float y, float scale, float maxWidth) {
        float measured = ActiveFont.Measure(text).X;
        if (measured > 0) {
            scale = Math.Min(scale, maxWidth / measured);
        }
        ActiveFont.DrawOutline(text, new Vector2(centerX, y), new Vector2(0.5f),
            new Vector2(scale), Color.White, 2f, Color.Black);
    }

    /// <summary>
    /// Drops the pooled snapshot buffers. The setting calls this when the overlay is turned off: the
    /// buffers are only worth keeping while the feature is in use, and they are 8-15 MB.
    /// </summary>
    internal static void ReleaseFrozenFrame() => ReleaseFrozenFrameBuffers();

    private static void ReleaseFrozenFrameBuffers() {
        Texture2D texture = frozenFrame;
        frozenFrame = null;
        texture?.Dispose();
        frozenFramePixels = null;
        frozenFrameCaptured = false;
    }

    private static string Clean(string title) => Dialog.Clean("SPEEDRUN_TOOL_BUSY_" + title);
}
