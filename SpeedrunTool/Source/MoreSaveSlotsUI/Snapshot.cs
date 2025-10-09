
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Other;

namespace Celeste.Mod.SpeedrunTool.MoreSaveSlotsUI;
internal class Snapshot {
    internal static void RequireCaptureSnapshot(string slot) {
        if (!SnapshotUI.Slots.Contains(slot)) {
            return;
        }
        DemandingSlot = slot;
        ScheduledCaptureSnapshots = true;
    }

    internal static void RemoveSnapshot(string slot) {
        SnapshotsDict.Remove(slot);
        ScheduledCaptureSnapshots = false; // avoid this field still somehow being true
    }

    internal static void ClearAll() {
        SnapshotsDict.Clear();
    }

    internal static Dictionary<string, Snapshot> SnapshotsDict = [];
    public Snapshot(string name) { snapshotTex = null; Name = name; }

    private Texture2D snapshotTex;

    internal string Name;

    internal float xPercent;

    internal int Index;

    internal static bool ScheduledCaptureSnapshots = false;

    internal static string DemandingSlot;

    private static void CaptureSnapshots(Scene scene, Snapshot instance) {
        // codes are modified from AssetReloadHelper
        GraphicsDevice graphicsDevice = Engine.Instance.GraphicsDevice;
        Viewport viewport = Engine.Viewport;
        Viewport viewport2 = graphicsDevice.Viewport;
        int width = viewport.Width;
        int height = viewport.Height;
        Color[] array = new Color[width * height];
        bool success = true;
        try {
            scene.BeforeRender();
            graphicsDevice.Viewport = viewport;
            graphicsDevice.SetRenderTarget(null);
            graphicsDevice.Clear(Engine.ClearColor);
            scene.Render();
            scene.AfterRender();
            graphicsDevice.GetBackBufferData(viewport.Bounds, array, 0, array.Length);
        }
        catch (Exception e) {
            Logger.Warn("SpeedrunTool", "Failed to render original scene for SpeedrunTool snapshot:");
            Logger.LogDetailed(e, "SpeedrunTool");
            success = false;
        }
        finally {
            graphicsDevice.SetRenderTarget(null);
            graphicsDevice.Viewport = viewport2;
            try {
                Draw.SpriteBatch.End();
            }
            catch {
            }
        }
        instance.snapshotTex?.Dispose();
        if (!success) {
            instance.snapshotTex = null;
            return;
        }
        instance.snapshotTex = new Texture2D(graphicsDevice, width, height, mipMap: false, SurfaceFormat.Color);
        instance.snapshotTex.SetData(array);
    }


    [Load]
    private static void Load() {
        On.Monocle.Scene.BeforeRender += CaptureSnapshotsBeforeRender;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Scene.BeforeRender -= CaptureSnapshotsBeforeRender;
    }
    private static void CaptureSnapshotsBeforeRender(On.Monocle.Scene.orig_BeforeRender orig, Scene self) {
        if (ScheduledCaptureSnapshots) {
            ScheduledCaptureSnapshots = false;
            // immediately set it so there won't be infinite loop
            Snapshot snapshot = new(DemandingSlot);
            CaptureSnapshots(self, snapshot);
            SnapshotsDict[DemandingSlot] = snapshot;
        }
        orig(self);
    }
    internal void RenderContent(float x1, float y1, float x2, float y2, float alpha) {
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

        if (!hasSnapshot) {
            Draw.Rect(new Rectangle((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1)), RectColor * alpha);
            DrawText("Snapshot   Not   Found", 0.4f);
        }
        else {
            Draw.SpriteBatch.Draw(snapshotTex, new Vector2(x1, y1), null, SnapshotColor * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            Draw.HollowRect(new Rectangle((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1)), HollowRectColor * alpha);
        }

        DrawText(Name, 0.85f);

        void DrawText(string text, float yLerp) {
            ActiveFont.DrawOutline(text, new Vector2((x1 + x2) / 2f, MathHelper.Lerp(y1, y2, yLerp)), new Vector2(0.5f, 0f), new Vector2(0.1f * (y2 - y1) / ActiveFont.Measure(text).Y), TextColor * alpha, 2f, StrokeColor * alpha);
        }
    }

    private static readonly Color SnapshotColor = new Color(1f, 1f, 0.8f);

    private static readonly Color TextColor = Color.White;

    private static readonly Color StrokeColor = Color.Black;

    private static readonly Color RectColor = new Color(0.5f, 0.5f, 0.5f) * 0.9f;

    private static readonly Color HollowRectColor = Color.White * 0.9f;
}

internal static class SnapshotUI {

    [Load]
    private static void Load() {
        On.Monocle.Engine.Update += OnEngineUpdate;
        On.Celeste.Level.Render += RenderSnapshots;
        Hotkey.CallMoreSaveSlotsUI.RegisterPressedAction(_ => {
            if (Engine.Scene is Level) {
                ToggleTab();
            }
        });
    }


    [Unload]
    private static void Unload() {
        On.Monocle.Engine.Update -= OnEngineUpdate;
        On.Celeste.Level.Render -= RenderSnapshots;
    }

    [Initialize]
    private static void Initialize() {
        for (int i = 1; i <= 9; i++) {
            Slots[i] = SaveSlotsManager.GetSlotName(i);
        }

        SaveLoadAction.InternalSafeAdd(saveState: (_, _) => OnSaveLoadClear(), loadState: (_, _) => OnSaveLoadClear(), clearState: OnSaveLoadClear);
    }

    internal static Dictionary<string, Snapshot> Dict => Snapshot.SnapshotsDict;

    internal static string[] Slots = new string[10];

    #region Timer
    private static bool tabIn = false;

    private static float easeY = 0f;

    private static readonly float DeltaY = Engine.RawDeltaTime * 2f;

    private static float easeX = 0f;

    private static readonly float DeltaX = Engine.RawDeltaTime * 2f;

    private static int sgnX = 0;

    private static float bufferedXTimer = 1f;

    private static int bufferedSgnX = 0;

    #endregion

    #region Position
    private static float alpha = 1f;

    private static float yPercent = 1f;

    private static int itemCount = 0;

    private const float PaddingPercent = 0.2f;

    private const float InitialX = (1f - WidthPercent) / 2f;

    private const float WidthPercent = 0.8f;
    
    private const float HeightPercent = 0.8f;
    #endregion
    private static void OnSaveLoadClear() {
        easeX = 0f;
        easeY = 0f;
        sgnX = 0;
        bufferedXTimer = 0;
        tabIn = false;
        UpdateY();
    }
    private static void OnEngineUpdate(On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) {
        orig(self, gameTime);

        // todo: tab in 的时候阻止正常的按键
        // todo: 使得这玩意可以交互

        if (self.scene is not Level) {
            return;
        }

        if (Input.MoveX != 0) {
            WhenArrowKeyHeld(left: Input.MoveX > 0);
        }

        if (tabIn && easeY < 1f) {
            easeY += DeltaY;
            UpdateY();
        }
        else if (!tabIn && easeY > 0f) {
            easeY -= DeltaY;
            UpdateY();
        }

        if (itemCount > 1) {
            if (bufferedXTimer > 0f) {
                bufferedXTimer -= Engine.RawDeltaTime;
            }
            if (sgnX != 0) {
                easeX += DeltaX;
                UpdateX();
                if (easeX >= 1f) {
                    foreach (Snapshot snapshot in Dict.Values) {
                        snapshot.Index += sgnX > 0 ? 1 : -1;
                    }
                    easeX = 0f;
                    sgnX = bufferedXTimer > 0f ? bufferedSgnX : 0;
                    bufferedXTimer = 0f;
                }
            }
        }
    }
    private static void UpdateX() {
        foreach (Snapshot snapshot in Dict.Values) {
            snapshot.xPercent = InitialX + (WidthPercent + PaddingPercent) * snapshot.Index + (WidthPercent + PaddingPercent) * sgnX * Ease.SineInOut(easeX);
            if (sgnX > 0 && snapshot.xPercent > 1f) {
                snapshot.xPercent -= itemCount * (WidthPercent + PaddingPercent);
                snapshot.Index -= itemCount;
            }
            else if (sgnX < 0 && snapshot.xPercent < -WidthPercent) {
                snapshot.xPercent += itemCount * (WidthPercent + PaddingPercent);
                snapshot.Index += itemCount;
            }
        }
    }

    private static void UpdateY() {
        alpha = Math.Clamp(easeY, 0f, 1f);
        yPercent = MathHelper.Lerp(1.1f, 0.1f, Ease.QuadInOut(alpha));
    }

    internal static void ToggleTab() {
        tabIn = !tabIn;
        if (tabIn && easeY <= 0.05f) {
            easeY = 0f;
            bool b = InitializeSnapshotX();
            if (!b) {
                tabIn = false;
                UpdateY();
            }
        }
        else if (!tabIn && easeY > 1f) {
            easeY = 1f;
        }
    }

    internal static void WhenArrowKeyHeld(bool left) {
        if (sgnX == 0) {
            easeX = 0f;
            sgnX = left ? -1 : 1;
            bufferedXTimer = 0f;
        }
        else {
            bufferedXTimer = 0.1f;
            bufferedSgnX = left ? -1 : 1;
        }
    }

    internal static bool InitializeSnapshotX() {
        // return false if fail to init
        for (int i = 1; i <= 9; i++) {
            string name = SaveSlotsManager.GetSlotName(i);
            if (!Dict.ContainsKey(name) && SaveSlotsManager.IsSaved(name)) {
                Dict[name] = new Snapshot(name);
                // for some reason Snapshot is not created, but save slot is saved
                // so we create an empty snapshot for emergency use
            }
        }

        itemCount = Dict.Count;
        if (itemCount == 0) {
            return false;
        }
        if (itemCount == 1) {
            Dict.Values.First().xPercent = InitialX;
            return true;
        }
        string currentSlot = SaveSlotsManager.SlotName;
        if (!Slots.Contains(currentSlot)) {
            currentSlot = Dict.Values.First().Name;
        }
        int validCount = 0;
        for (int i = 1; i <= 9; i++) {
            if (Slots[i] == currentSlot && Dict.TryGetValue(currentSlot, out Snapshot firstSnapshot) && firstSnapshot is not null) {
                firstSnapshot.xPercent = InitialX;
                firstSnapshot.Index = 0;
                validCount++;
                for (int j = i+1; j <= 9; j++) {
                    if (Dict.TryGetValue(Slots[j], out Snapshot shot) && shot is not null) {
                        shot.xPercent = InitialX + validCount * (WidthPercent + PaddingPercent);
                        shot.Index = validCount;
                        validCount++;
                    }
                }
                for (int k = 1; k<= i-1; k++) {
                    if (Dict.TryGetValue(Slots[k], out Snapshot shot2) && shot2 is not null) {
                        shot2.xPercent = InitialX + validCount * (WidthPercent + PaddingPercent);
                        shot2.Index = validCount;
                        validCount++;
                    }
                }
            }
        }
        itemCount = validCount;
        return itemCount > 0;
    }

    private static void RenderSnapshots(On.Celeste.Level.orig_Render orig, Level self) {
        orig(self);
        if (alpha < 0.1f) {
            return;
        }

        if (Dict.IsNullOrEmpty()) {
            RenderTellingNotSavedYet();
            return;
        }
        Draw.SpriteBatch.Begin();

        int viewWidth = Engine.ViewWidth;
        int viewHeight = Engine.ViewHeight;
        float minX, minY, maxX, maxY;
        minX = 0;
        minY = 0;
        maxX = viewWidth;
        maxY = viewHeight;

        foreach (Snapshot snap in Dict.Values) {
            PercentRender(snap, snap.xPercent, yPercent);
        }

        void PercentRender(Snapshot snapshot, float xPercent, float yPercent) {
            if (xPercent >= 1f || xPercent <= -WidthPercent || yPercent >= 1f || yPercent <= -HeightPercent) {
                return;
            }
            snapshot.RenderContent(
                MathHelper.Lerp(minX, maxX, xPercent),
                MathHelper.Lerp(minY, maxY, yPercent),
                MathHelper.Lerp(minX, maxX, xPercent + WidthPercent),
                MathHelper.Lerp(minY, maxY, yPercent + HeightPercent),
                alpha);
        }

        ShowButtonBinding();

        Draw.SpriteBatch.End();
    }

    private static void RenderTellingNotSavedYet() {
        // todo
    }

    private static void ShowButtonBinding() {
        // todo
        // include: bottom right buttons, left-right arrows on both sides (don't show if only one item)
    }
}
