
using Celeste.Mod.SpeedrunTool.Message;
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
internal class Snapshot {

    internal static void RequireCaptureSnapshot(string slot) {
        if (!LegalNames.Contains(slot)) {
            return;
        }
        DemandingSlot = slot;
        ScheduledCaptureSnapshots = true;
        captureWaitFrames = 0;
    }

    internal static void RemoveSnapshot(string slot) {
        if (SnapshotsDict.TryGetValue(slot, out Snapshot snap)) {
            snap.IsSaved = false;
            snap.snapshotTex?.Dispose();
            snap.snapshotTex = null;
        }
        ScheduledCaptureSnapshots = false; // avoid this field still somehow being true
    }

    internal static void ClearAll() {
        DisposeAllTextures();
        SnapshotsDict.Clear();
        SnapshotUI.Close();
    }

    // The snapshot textures are unmanaged GPU resources: dropping the dictionary is not
    // enough, every texture has to be disposed explicitly or it leaks VRAM on clear/reload.
    internal static void DisposeAllTextures() {
        foreach (Snapshot snapshot in SnapshotsDict.Values) {
            snapshot.snapshotTex?.Dispose();
            snapshot.snapshotTex = null;
        }
    }

    internal static Dictionary<string, Snapshot> SnapshotsDict = [];

    internal static string[] LegalNames = new string[10];

    public Snapshot(string name) { snapshotTex = null; Name = name; IsSaved = true; }

    private Texture2D snapshotTex;

    internal string Name;

    internal float xPercent;

    internal float yPercent;

    internal bool IsSaved;

    internal static bool ScheduledCaptureSnapshots = false;

    internal static string DemandingSlot;

    // Thumbnails are only ever drawn inside ~1/3 of the screen (SnapshotUI), so storing a
    // full-resolution texture per slot wastes a lot of VRAM: 1920x1080 = 8.3 MB per slot,
    // ~75 MB for all 9 slots. Downscaling to at most this long edge keeps the UI crisp
    // while using ~0.5 MB per slot.
    private const int MaxSnapshotLongEdge = 480;

    // The full-screen readback and the downscaled thumbnail are reused between captures.
    // A fresh Color[1920 * 1080] (~8.3 MB) per save lands on the LOH and is a classic
    // source of late-generation GC pauses, so keep one buffer and overwrite it.
    private static Color[] readbackBuffer;
    private static Color[] thumbnailBuffer;

    private static void EnsureBufferSize(ref Color[] buffer, int length) {
        if (buffer is null || buffer.Length < length) {
            buffer = new Color[length];
        }
    }

    private static void CaptureSnapshots(Level level, Snapshot instance) {
        // codes are modified from AssetReloadHelper
        GraphicsDevice graphicsDevice = Engine.Instance.GraphicsDevice;
        Viewport viewport = Engine.Viewport;
        Viewport viewport2 = graphicsDevice.Viewport;
        int width = viewport.Width;
        int height = viewport.Height;
        bool success = true;
        bool renderedHere = false;
        try {
            graphicsDevice.Viewport = viewport;
            if (graphicsDevice.GetRenderTargets().Length > 0) {
                // Unusual frame (rendering into a render target): fall back to rendering the
                // room once into the back buffer.
                level.BeforeRender();
                graphicsDevice.SetRenderTarget(null);
                graphicsDevice.Clear(Engine.ClearColor);
                level.Render();
                level.AfterRender();
                renderedHere = true;
            }

            // Prefer GameplayBuffers.Level: it holds the composed level before Level.Render
            // applies the color grade and the final letterboxed draw to the back buffer.
            // Reading the back buffer stored the graded frame, making previews change color and
            // darkness with the room's color grade/lights.
            RenderTarget2D levelBuffer = GameplayBuffers.Level?.Target;
            if (levelBuffer is not null && !levelBuffer.IsDisposed) {
                width = levelBuffer.Width;
                height = levelBuffer.Height;
                EnsureBufferSize(ref readbackBuffer, width * height);
                levelBuffer.GetData(readbackBuffer, 0, width * height);
            }
            else {
                // Unusual frame (level buffer unavailable): fall back to the frame the engine
                // just drew into the back buffer.
                EnsureBufferSize(ref readbackBuffer, width * height);
                graphicsDevice.GetBackBufferData(viewport.Bounds, readbackBuffer, 0, width * height);
            }
        }
        catch (Exception e) {
            Logger.Warn("SpeedrunTool", "Failed to render original scene for SpeedrunTool snapshot:");
            Logger.LogDetailed(e, "SpeedrunTool");
            success = false;
        }
        finally {
            graphicsDevice.SetRenderTarget(null);
            graphicsDevice.Viewport = viewport2;
            if (renderedHere) {
                try {
                    Draw.SpriteBatch.End();
                }
                catch {
                }
            }
        }

        instance.snapshotTex?.Dispose();
        if (!success || width <= 0 || height <= 0) {
            instance.snapshotTex = null;
            return;
        }

        int scale = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (float)MaxSnapshotLongEdge));
        int thumbnailWidth = Math.Max(1, width / scale);
        int thumbnailHeight = Math.Max(1, height / scale);
        EnsureBufferSize(ref thumbnailBuffer, thumbnailWidth * thumbnailHeight);

        // Nearest-neighbour downscale. The source rows are read sequentially, so this is
        // cache friendly despite the stride, and it is only ~130k pixels at 1080p.
        int halfScale = scale / 2;
        for (int y = 0; y < thumbnailHeight; y++) {
            int sourceY = Math.Min(height - 1, y * scale + halfScale);
            int sourceRow = sourceY * width;
            int thumbnailRow = y * thumbnailWidth;
            for (int x = 0; x < thumbnailWidth; x++) {
                int sourceX = Math.Min(width - 1, x * scale + halfScale);
                thumbnailBuffer[thumbnailRow + x] = readbackBuffer[sourceRow + sourceX];
            }
        }

        instance.snapshotTex = new Texture2D(graphicsDevice, thumbnailWidth, thumbnailHeight, mipMap: false, SurfaceFormat.Color);
        instance.snapshotTex.SetData(thumbnailBuffer, 0, thumbnailWidth * thumbnailHeight);
    }


    [Load]
    private static void Load() {
        On.Celeste.Level.Render += CaptureSnapshotsAfterRender;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.Render -= CaptureSnapshotsAfterRender;
        DisposeAllTextures();
        SnapshotsDict.Clear();
        // Do not keep the full-screen readback buffer (8.3 MB at 1080p) alive across reloads.
        readbackBuffer = null;
        thumbnailBuffer = null;
        captureWaitFrames = 0;
    }

    [Initialize]
    private static void Initialize() {
        for (int i = 1; i <= 9; i++) {
            LegalNames[i] = SaveSlotsManager.GetSlotName(i);
        }
        LegalNames[0] = "DONT_USE_IT";
    }
    private static int captureWaitFrames;

    private static void CaptureSnapshotsAfterRender(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);

        if (!ScheduledCaptureSnapshots) {
            return;
        }

        // Saving starts a screen wipe right after the state is taken. Reading the frame back
        // while it runs would store a white/black thumbnail, so wait until the wipe is gone.
        // The game is frozen during this time, so the captured scene does not change.
        if (self.Wipe is not null && captureWaitFrames < 180) {
            captureWaitFrames++;
            return;
        }
        captureWaitFrames = 0;

        ScheduledCaptureSnapshots = false; // immediately clear it so there won't be an infinite loop
        Snapshot snapshot = new(DemandingSlot);
        CaptureSnapshots(self, snapshot);
        // Re-saving into the same slot replaces the dictionary entry: dispose the texture of
        // the snapshot being replaced, otherwise every save leaks one texture.
        if (SnapshotsDict.TryGetValue(DemandingSlot, out Snapshot previous) && previous != snapshot) {
            previous.snapshotTex?.Dispose();
            previous.snapshotTex = null;
        }
        SnapshotsDict[DemandingSlot] = snapshot;
    }
    internal void RenderContent(float x1, float y1, float x2, float y2, float alpha, bool highlight) {
        bool hasSnapshot = true;
        Vector2 scale = Vector2.One;
        if (snapshotTex is null) {
            hasSnapshot = false;
        }
        else {
            int w = snapshotTex.Width;
            int h = snapshotTex.Height;
            if (w <= 0 || h <= 0) {
                hasSnapshot = false;
            }
            else {
                scale = new Vector2((x2 - x1) / w, (y2 - y1) / h);
            }
        }

        if (!IsSaved) {
            Draw.Rect(new Rectangle((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1)), NoSaveColor * alpha);
            DrawText("Not   Saved   Yet", 0.4f);
        }
        else if (!hasSnapshot) {
            Draw.Rect(new Rectangle((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1)), RectColor * alpha);
            DrawText("Snapshot   Not   Found", 0.4f);
        }
        else {
            Draw.SpriteBatch.Draw(snapshotTex, new Vector2(x1, y1), null, SnapshotColor * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        Draw.HollowRect(new Rectangle((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1)), (highlight ? HighlightRectColor : HollowRectColor) * alpha);
        DrawText(Name, 0.95f);

        void DrawText(string text, float yLerp) {
            ActiveFont.DrawOutline(text, new Vector2((x1 + x2) / 2f, MathHelper.Lerp(y1, y2, yLerp)), new Vector2(0.5f, 1f), new Vector2(0.2f * (y2 - y1) / ActiveFont.Measure(text).Y), (highlight ? HighlightTextColor : TextColor) * alpha, Stroke, StrokeColor * alpha);
        }
    }

    private const float Stroke = 1f;

    private static readonly Color SnapshotColor = new Color(1f, 1f, 0.8f);

    private static readonly Color TextColor = Color.White;

    private static readonly Color HighlightTextColor = Color.Goldenrod;

    private static readonly Color StrokeColor = Color.Black;

    private static readonly Color NoSaveColor = new Color(0.3f, 0.3f, 0.3f) * 0.9f;

    private static readonly Color RectColor = new Color(0.5f, 0.5f, 0.5f) * 0.9f;

    private static readonly Color HollowRectColor = Color.White * 0.9f;

    private static readonly Color HighlightRectColor = Color.Gold;
}

internal static class SnapshotUI {

    [Load]
    private static void Load() {
        On.Celeste.Level.Render += RenderSnapshots;
        Hotkey.ToggleSaveLoadUI.RegisterPressedAction(_ => {
            if (Engine.Scene is Level { Paused: false }) {
                ToggleTab();
            }
        });
    }


    [Unload]
    private static void Unload() {
        On.Celeste.Level.Render -= RenderSnapshots;
    }

    [Initialize]
    private static void Initialize() {
        SaveLoadAction.InternalSafeAdd(saveState: (_, _) => Close(), loadState: (_, _) => Close());
    }

    internal static Dictionary<string, Snapshot> Dict => Snapshot.SnapshotsDict;

    internal static string[] Slots => Snapshot.LegalNames;

    #region AnimConfig
    private static bool tabIn = false;

    internal static bool OnScreen => tabIn || easeY > 0f;

    private static float easeY = 0f;

    private static float DeltaY => Engine.RawDeltaTime * 2f;

    private static float alpha = 1f;

    private static float yAnim = 1f;

    private const float WidthPercent = 0.97f / 3f;

    private const float HeightPercent = 0.93f / 3f;

    private const float WidthPaddingPercent = (1f - 3f * WidthPercent) / 4f;

    private const float HeightPaddingPercent = (1f - 3f * HeightPercent) / 4f;

    #endregion

    private static int selectionX;

    private static int selectionY;

    private static int selectionIndex => 1 + selectionX + selectionY * 3;

    private static string highlight;

    private static float yWiggleTimer;

    private static float WiggleSpeed => Engine.RawDeltaTime * 10f;

    private static bool focusOnItem;

    private static int itemOptionIndex;
    internal static void Close() {
        easeY = 0f;
        tabIn = false;
        UpdateY();
        // force close UI
    }

    private static void ConfigAllSnapshots() {
        for (int i = 1; i <= 9; i++) {
            string name = Slots[i];
            Snapshot snap;
            if (Dict.TryGetValue(name, out Snapshot s)) {
                snap = s;
            }
            else {
                snap = new Snapshot(name);
                snap.IsSaved = SaveSlotsManager.IsSaved(name);
                Dict[name] = snap;
            }
            snap.xPercent = ((i - 1) % 3) * (WidthPercent + WidthPaddingPercent) + WidthPaddingPercent;
            snap.yPercent = ((i - 1) / 3) * (HeightPercent + HeightPaddingPercent) + HeightPaddingPercent;
        }
        int num = Math.Clamp(PeriodicTableOfSlots.CurrentSlotIndex, 1, 9);
        highlight = Slots[num];
        selectionX = (num - 1) % 3;
        selectionY = (num - 1) / 3;
        yWiggleTimer = 0f;
        focusOnItem = false;
    }

    internal static void Update() {
        // 懒得找合适的现成工具了, 干脆手搓动画效果
        if (tabIn && easeY < 1f) {
            easeY += DeltaY;
            UpdateY();
        }
        else if (!tabIn && easeY > 0f) {
            easeY -= DeltaY;
            UpdateY();
        }

        if (easeY >= 1f) {
            UpdateFullOpen();
        }
    }

    private const string PauseSfx = "event:/ui/game/pause";

    private const string UnpauseSfx = "event:/ui/game/unpause";

    private const string ConfirmSfx = "event:/ui/main/button_select";

    private const string CancelSfx = "event:/ui/game/hotspot_note_out";

    private const string FailureSfx = "event:/ui/game/lookout_off";

    private const string ArrowKeySfx = "event:/ui/main/rollover_down";

    private const string DeleteSfx = "event:/ui/main/savefile_delete";

    private static void UpdateFullOpen() {
        if (!focusOnItem) {
            yWiggleTimer += WiggleSpeed;
            if (Input.MenuCancel.Pressed || Input.ESC.Pressed) {
                ToggleTab();
                return;
            }
            if (Input.MenuConfirm.Pressed) {
                focusOnItem = true;
                yWiggleTimer = 0f;
                Audio.Play(ConfirmSfx);
                return;
            }

            bool hasArrowKey = true;
            if (Input.MenuDown.Pressed) {
                selectionY++;
            }
            else if (Input.MenuUp.Pressed) {
                selectionY--;
            }
            else if (Input.MenuLeft.Pressed) {
                selectionX--;
            }
            else if (Input.MenuRight.Pressed) {
                selectionX++;
            }
            else {
                hasArrowKey = false;
            }
            if (!hasArrowKey) {
                return;
            }
            yWiggleTimer = 0f;
            selectionX = (selectionX + 3) % 3;
            selectionY = (selectionY + 3) % 3;
            highlight = Slots[selectionIndex];
            Audio.Play(ArrowKeySfx);
        }
        else {
            if (Input.MenuCancel.Pressed || Input.ESC.Pressed) {
                focusOnItem = false;
                Audio.Play(CancelSfx);
                return;
            }

            if (Input.MenuConfirm.Pressed) {
                bool success = false;
                bool allow = StateManager.AllowSaveLoadWhenWaiting;
                StateManager.AllowSaveLoadWhenWaiting = true;
                // The snapshot UI freezes Engine.Update before Scene.AfterUpdate runs. A pending
                // (cross-chapter) load defers LevelEnter.Go to Scene.OnEndOfFrame, so close the UI
                // first or that deferred switch can never fire.
                Close();
                if (itemOptionIndex == 0) {
                    success = SaveSlotsManager.SwitchSlot(selectionIndex) && SaveLoadHotkeys.SaveStateAndMessage();
                }
                else if (itemOptionIndex == 1) {
                    success = SaveSlotsManager.IsSaved(selectionIndex) && SaveSlotsManager.SwitchSlot(selectionIndex) && SaveLoadHotkeys.LoadStateAndMessage();
                }
                else {
                    success = SaveSlotsManager.IsSaved(selectionIndex) && SaveSlotsManager.SwitchSlot(selectionIndex);
                    if (success) {
                        SaveSlotsManager.ClearState();
                    }
                }
                StateManager.AllowSaveLoadWhenWaiting = allow;
                if (success) {
                    focusOnItem = false;
                    Audio.Play(itemOptionIndex > 1 ? DeleteSfx : ConfirmSfx);
                }
                else {
                    Audio.Play(FailureSfx);
                }
                return;
            }

            if (Input.MenuRight.Pressed) {
                itemOptionIndex++;
            }
            else if (Input.MenuLeft.Pressed) {
                itemOptionIndex--;
            }
            else {
                return;
            }
            itemOptionIndex = (itemOptionIndex + 3) % 3;
            Audio.Play(ArrowKeySfx);
        }

    }

    private static void UpdateY() {
        alpha = Math.Clamp(easeY, 0f, 1f);
        yAnim = MathHelper.Lerp(1f, 0f, Ease.QuadInOut(alpha));
    }

    private static void ToggleTab() {
        if (tabIn) {
            focusOnItem = false;
        }
        tabIn = !tabIn;
        Audio.Play(tabIn ? PauseSfx : UnpauseSfx);
        if (tabIn && easeY < 0.05f) {
            easeY = 0f;
            ConfigAllSnapshots();
        }
        else if (!tabIn && easeY > 0.95f) {
            easeY = 1f;
        }
    }


    private static void RenderSnapshots(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);
        if (alpha < 0.1f || Dict.IsNullOrEmpty()) {
            return;
        }

        Draw.SpriteBatch.Begin();

        int viewWidth = Engine.ViewWidth;
        int viewHeight = Engine.ViewHeight;
        float minX, minY, maxX, maxY;
        minX = 0;
        minY = 0;
        maxX = viewWidth;
        if (SaveLoadHotkeys.SaveLoadStateShowMessage) {
            float spaceForTooltip = (ActiveFont.Measure("T").Y + Tooltip.Padding / 2f) / Engine.Height;
            maxY = viewHeight * (1f + HeightPaddingPercent - spaceForTooltip);
        }
        else {
            maxY = viewHeight;
        }

        TitleRender("SPEEDRUN TOOL", MathHelper.Lerp(TitleY1, TitleY2, yAnim));

        foreach (Snapshot snap in Dict.Values) {
            if (snap.Name == highlight) {
                PercentRender(snap, snap.xPercent, snap.yPercent + yAnim + MathF.Sin(yWiggleTimer) * 0.003f, true);
            }
            else {
                PercentRender(snap, snap.xPercent, snap.yPercent + yAnim, false);
            }
        }

        void TitleRender(string text, float yPercent) {
            if (yPercent >= 1f) {
                return;
            }
            float x = (minX + maxX) / 2f;
            float y = MathHelper.Lerp(minY, maxY, yPercent);
            float height = TitleHeight * (maxY - minY);
            float titleAlpha = MathHelper.Lerp(0.5f, 1f, alpha);
            ActiveFont.DrawOutline(text, new Vector2(x, y), new Vector2(0.5f, 0f), new Vector2(height / ActiveFont.Measure(text).Y), TextColor * titleAlpha, Stroke, StrokeColor * titleAlpha);
        }

        void PercentRender(Snapshot snapshot, float xPercent, float yPercent, bool highlight) {
            if (xPercent >= 1f || xPercent <= -WidthPercent || yPercent >= 1f || yPercent <= -HeightPercent) {
                return;
            }
            snapshot.RenderContent(
                MathHelper.Lerp(minX, maxX, xPercent),
                MathHelper.Lerp(minY, maxY, yPercent),
                MathHelper.Lerp(minX, maxX, xPercent + WidthPercent),
                MathHelper.Lerp(minY, maxY, yPercent + HeightPercent),
                alpha, highlight);
        }

        if (focusOnItem) {
            float xPercent = WidthPaddingPercent * (selectionX + 1) + WidthPercent * selectionX + OptionMarginX;
            float yPercent = HeightPaddingPercent * (selectionY + 1) + HeightPercent * selectionY + OptionMarginY;
            float width = OptionWidth * (maxX - minX);
            float padding = OptionPadding * (maxX - minX);
            float height = OptionHeight * (maxY - minY);
            float x = MathHelper.Lerp(minX, maxX, xPercent);
            float y = MathHelper.Lerp(minY, maxY, yPercent);
            for (int i = 0; i <= 2; i++) {
                float x1 = x + i * (width + padding);
                bool highlight = i == itemOptionIndex;
                Draw.Rect(x1, y, width, height, highlight ? HighlightRectColor : RectColor);
                string text = i switch { 0 => "Save", 1 => "Load", 2 => "Clear", _ => "?" };
                ActiveFont.Draw(text, new Vector2(x1 + width / 2f, y + height / 2f), new Vector2(0.5f, 0.5f), new Vector2(height / ActiveFont.Measure(text).Y), TextColor);
            }
        }

        Draw.SpriteBatch.End();
    }

    private const float OptionMarginX = 0.03f;

    private const float OptionMarginY = 0.15f;

    private const float OptionWidth = (WidthPercent - 2 * (OptionMarginX + OptionPadding)) / 3f;

    private const float OptionHeight = 0.06f;

    private const float OptionPadding = 0.01f;

    private const float TitleY1 = 0.425f;

    private const float TitleY2 = 1.01f;

    private const float TitleHeight = 0.2f;

    private const float Stroke = 1f;

    private static readonly Color StrokeColor = Color.Black;

    private static readonly Color RectColor = new Color(0.5f, 0.5f, 0.5f);

    private static readonly Color HighlightRectColor = new Color(0.8f, 0.7f, 0.2f);

    private static readonly Color TextColor = new Color(0.9f, 1f, 0.9f);
}
