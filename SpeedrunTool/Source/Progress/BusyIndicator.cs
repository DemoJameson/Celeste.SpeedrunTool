using Celeste.Mod.Helpers;
using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Celeste.Mod.SpeedrunTool.Progress;

// Render-only checkpoints for synchronous work. Never tick the engine, pump
// input, or render a partially unloaded/cloned scene to update the indicator.
internal static class BusyIndicator {
    private const double RefreshInterval = 0.1;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double lastPresentation;
    private static bool hasDrawn;
    private static bool drawing;
    private static bool failed;
    private static Texture2D background;
    private static SpriteBatch batch;

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
        // Snapshot capture is itself blocking, and happens inside BeforeRender.
        // Present its hint BEFORE entering the render pipeline (not in the saved image).
        using OperationProgress capture = Snapshot.ScheduledCaptureSnapshots ? Begin("SNAPSHOT") : null;
        drawing = true;
        try {
            orig(self, gameTime);
            hasDrawn = true;
        }
        finally {
            drawing = false;
        }
    }

    internal static OperationProgress Begin(string stage) {
        // Loaders and hot reload already have their own UI. Never touch graphics
        // on their worker threads, during rendering, or while TAS is recording.
        if (!MainThreadHelper.IsMainThread || !hasDrawn || drawing || AssetReloadHelper.IsReloading
            || TasUtils.Running || TasUtils.HideGamePlay) {
            return null;
        }
        if (OperationProgress.Current == null) {
            failed = false;
        }
        return new OperationProgress(stage, TryPresent, ReleaseBackground);
    }

    internal static void Wait(Task task) {
        if (task == null) {
            return;
        }
        if (task.IsCompleted) {
            task.Wait(); // Preserve fault/cancellation propagation, even without a UI.
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

    private static void TryPresent(OperationProgress progress, bool force) {
        try {
            Present(progress, force);
        }
        catch (Exception exception) {
            // Also cover unavailable/disposed device state and state restoration.
            failed = true;
            Logger.Warn("SpeedrunTool/Progress", $"Cannot present busy indicator: {exception.Message}");
        }
    }

    private static void Present(OperationProgress progress, bool force) {
        if (failed || drawing || !MainThreadHelper.IsMainThread
            || (!force && Clock.Elapsed.TotalSeconds - lastPresentation < RefreshInterval)) {
            return;
        }
        GraphicsDevice device = Engine.Instance.GraphicsDevice;
        // A mod may invoke a callback from its own render pass. Don't disturb it.
        if (device.GetRenderTargets().Length != 0) {
            return;
        }
        Viewport viewport = device.Viewport;
        Rectangle scissor = device.ScissorRectangle;
        BlendState blend = device.BlendState;
        DepthStencilState depth = device.DepthStencilState;
        RasterizerState rasterizer = device.RasterizerState;
        SamplerState sampler = device.SamplerStates[0];
        Texture texture = device.Textures[0];
        SpriteBatch previousBatch = Draw.SpriteBatch;
        bool begun = false;
        try {
            int width = device.PresentationParameters.BackBufferWidth;
            int height = device.PresentationParameters.BackBufferHeight;
            if (width <= 0 || height <= 0) {
                return;
            }
            if (background == null) {
                // Read once per operation, not every gameplay frame. Unlike
                // Scene.Render this has no gameplay, snapshot or clone side effects.
                Color[] pixels = new Color[width * height];
                device.GetBackBufferData(pixels);
                background = new Texture2D(device, width, height);
                background.SetData(pixels);
            }
            device.Viewport = new Viewport(0, 0, width, height);
            Draw.SpriteBatch = batch ??= new SpriteBatch(device);
            // Backbuffer alpha need not be opaque. Copy its RGB without blending
            // it over the previous frame, which would brighten each presentation.
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            begun = true;
            batch.Draw(background, new Rectangle(0, 0, width, height), Color.White);
            batch.End();
            begun = false;

            // Fit the HUD in both letterboxed and non-16:9 windows.
            float scale = Math.Min(width / 1920f, height / 1080f);
            Matrix matrix = Matrix.CreateScale(scale) * Matrix.CreateTranslation(
                (width - 1920f * scale) / 2f, (height - 1080f * scale) / 2f, 0f);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, matrix);
            begun = true;
            Draw.Rect(360f, 782f, 1200f, 238f, Color.Black * 0.9f);
            Text(Clean(progress.Title) + new string('.', 1 + (int)(progress.ElapsedSeconds * 3) % 3), 822f, 0.8f);
            string stage = Clean(progress.Stage);
            if (!string.IsNullOrWhiteSpace(progress.Detail)) {
                stage += "  —  " + progress.Detail.Replace('\r', ' ').Replace('\n', ' ');
            }
            Text(stage, 874f, 0.48f);
            if (progress.Fraction is { } fraction) {
                Draw.Rect(420f, 918f, 1080f, 8f, Color.White * 0.2f);
                Draw.Rect(420f, 918f, 1080f * fraction, 8f, Color.CornflowerBlue);
                Text($"{progress.Completed} / {progress.Total}  ({fraction:P0})", 958f, 0.55f);
            }
            else {
                // Indeterminate: elapsed time is not an estimate of completion.
                Text($"{Clean("WAIT")}  {progress.ElapsedSeconds:F1}s", 958f, 0.55f);
            }
            batch.End();
            begun = false;
            device.Present();
            lastPresentation = Clock.Elapsed.TotalSeconds;
        }
        finally {
            if (begun) {
                try {
                    batch.End();
                }
                catch {
                    // Preserve the original draw failure and always restore the shared batch.
                }
            }
            Draw.SpriteBatch = previousBatch;
            device.Viewport = viewport;
            device.ScissorRectangle = scissor;
            device.BlendState = blend;
            device.DepthStencilState = depth;
            device.RasterizerState = rasterizer;
            device.SamplerStates[0] = sampler;
            device.Textures[0] = texture;
        }
    }

    private static string Clean(string stage) => Dialog.Clean("SPEEDRUN_TOOL_BUSY_" + stage);

    private static void Text(string text, float y, float scale) {
        float width = ActiveFont.Measure(text).X;
        scale = Math.Min(scale, 1100f / Math.Max(1f, width));
        ActiveFont.DrawOutline(text, new Vector2(960f, y), new Vector2(0.5f), new Vector2(scale), Color.White, 2f, Color.Black);
    }

    private static void ReleaseBackground() {
        Texture2D texture = background;
        background = null;
        texture?.Dispose();
    }
}
