using Celeste.Mod.Helpers;
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
    private const double RefreshInterval = 0.1;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double lastPresentation;
    private static bool hasDrawn;
    private static bool drawing;
    private static bool failed;
    private static SpriteBatch batch;
    private static Texture2D background;

    [Load]
    private static void Load() {
        On.Monocle.Engine.Draw += EngineOnDraw;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Draw -= EngineOnDraw;
        ReleaseBackground();
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
        if (!MainThreadHelper.IsMainThread || !hasDrawn || drawing || AssetReloadHelper.IsReloading
            || TasUtils.Running || TasUtils.HideGamePlay) {
            if (root) {
                Diag($"suppress {title}: main={MainThreadHelper.IsMainThread} drawn={hasDrawn} " +
                     $"drawing={drawing} reload={AssetReloadHelper.IsReloading} " +
                     $"tas={TasUtils.Running} hideTas={TasUtils.HideGamePlay}");
            }
            return null;
        }
        OperationProgress progress = null;
        progress = new OperationProgress(title, TryPresent, () => {
            if (root) {
                Diag($"end {title} total={progress.ElapsedSeconds * 1000.0:F0}ms shown={progress.Presented}");
                ReleaseBackground();
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
        if (task.IsCompleted) {
            task.Wait();
            return;
        }
        using OperationProgress progress = Begin("PRECLONE");
        if (progress == null) {
            task.Wait();
            return;
        }
        while (!task.Wait(100)) {
            progress.Refresh();
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

            if (background == null) {
                // The main thread is blocked in a synchronous operation: no new game
                // frame is drawn between stages. Without restoring this base frame,
                // each translucent Present would composite over itself and get darker.
                Color[] pixels = new Color[width * height];
                device.GetBackBufferData(pixels);
                background = new Texture2D(device, width, height);
                background.SetData(pixels);
            }

            device.Viewport = new Viewport(0, 0, width, height);
            Draw.SpriteBatch = batch ??= new SpriteBatch(device);
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            begun = true;
            batch.Draw(background, new Rectangle(0, 0, width, height), Color.White);
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

    private static void ReleaseBackground() {
        Texture2D texture = background;
        background = null;
        texture?.Dispose();
    }

    private static string Clean(string title) => Dialog.Clean("SPEEDRUN_TOOL_BUSY_" + title);
}
