
using Celeste.Mod.SpeedrunTool.Other;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
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
        SnapshotsDict.Clear();
        SnapshotUI.Close();
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

    private static void CaptureSnapshots(Level level, Snapshot instance) {
        // codes are modified from AssetReloadHelper
        GraphicsDevice graphicsDevice = Engine.Instance.GraphicsDevice;
        Viewport viewport = Engine.Viewport;
        Viewport viewport2 = graphicsDevice.Viewport;
        int width = viewport.Width;
        int height = viewport.Height;
        Color[] array = new Color[width * height];
        bool success = true;
        try {
            level.BeforeRender();
            graphicsDevice.Viewport = viewport;
            graphicsDevice.SetRenderTarget(null);
            graphicsDevice.Clear(Engine.ClearColor);
            level.Render();
            level.AfterRender();
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
        
        On.Celeste.Level.BeforeRender += CaptureSnapshotsBeforeRender;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.BeforeRender -= CaptureSnapshotsBeforeRender;
    }

    [Initialize]
    private static void Initialize() {
        for (int i = 1; i <= 9; i++) {
            LegalNames[i] = SaveSlotsManager.GetSlotName(i);
        }
        LegalNames[0] = "DONT_USE_IT";
    }
    private static void CaptureSnapshotsBeforeRender(On.Celeste.Level.orig_BeforeRender orig, Level self) {
        if (ScheduledCaptureSnapshots) {
            ScheduledCaptureSnapshots = false;
            // immediately set it so there won't be infinite loop
            Snapshot snapshot = new(DemandingSlot);
            CaptureSnapshots(self, snapshot);
            SnapshotsDict[DemandingSlot] = snapshot;
        }
        orig(self);
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
        IL.Monocle.Engine.Update += IL_Engine_Update;
        On.Celeste.Level.Render += RenderSnapshots;
        Hotkey.ToggleSaveLoadUI.RegisterPressedAction(_ => {
            if (Engine.Scene is Level { Paused: false }) {
                ToggleTab();
            }
        });
    }


    [Unload]
    private static void Unload() {
        IL.Monocle.Engine.Update -= IL_Engine_Update;
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

    private static void IL_Engine_Update(ILContext il) {
        ILCursor cursor = new(il);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.MatchCall(typeof(MInput), nameof(MInput.Update)))) {
            // Prevent further execution
            ILLabel label = cursor.DefineLabel();
            cursor.EmitDelegate(Update);
            cursor.EmitDelegate(IsPaused);
            cursor.Emit(OpCodes.Brfalse, label);
            cursor.Emit(OpCodes.Ret);
            cursor.MarkLabel(label);
        }
    }

    private static bool IsPaused() {
        if (Engine.Scene is Level && easeY > 0f) {
            if (ModInterop.TasUtils.Running) {
                Close();
                return false;
            }
            return true;
        }
        return false;
    }
    private static void Update() {
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
        maxY = viewHeight;

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
