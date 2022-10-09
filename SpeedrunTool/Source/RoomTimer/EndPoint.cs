using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Editor;
using Celeste.Mod.SpeedrunTool.SaveLoad;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.RoomTimer;

[Tracked]
public class EndPoint : Entity {
    private static string roomIdEndPoint;
    private static readonly List<EndPoint> CachedEndPoints = new();
    private static AreaKey cachedAreaKey;

    [Load]
    private static void Load() {
        On.Celeste.Level.End += LevelOnEnd;
        On.Celeste.Level.Begin += LevelOnBegin;
        On.Celeste.Editor.MapEditor.Render += MapEditorOnRender;
        IL.Celeste.Editor.LevelTemplate.RenderContents += LevelTemplateOnRenderContents;
        On.Celeste.Editor.LevelTemplate.RenderHighlight += LevelTemplateOnRenderHighlight;
        IL.Celeste.Editor.MapEditor.RenderManualText += MapEditorOnRenderManualText;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.End -= LevelOnEnd;
        On.Celeste.Level.Begin -= LevelOnBegin;
        On.Celeste.Editor.MapEditor.Render -= MapEditorOnRender;
        IL.Celeste.Editor.LevelTemplate.RenderContents -= LevelTemplateOnRenderContents;
        On.Celeste.Editor.LevelTemplate.RenderHighlight -= LevelTemplateOnRenderHighlight;
        IL.Celeste.Editor.MapEditor.RenderManualText -= MapEditorOnRenderManualText;
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        CachedEndPoints.Clear();
        CachedEndPoints.AddRange(All);
        cachedAreaKey = self.Session.Area;
        orig(self);
    }

    private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
        orig(self);
        if (self.Session.Area == cachedAreaKey) {
            foreach (EndPoint endPoint in CachedEndPoints) {
                self.Add(endPoint);
            }
        } else {
            CachedEndPoints.Clear();
            cachedAreaKey = default;
            roomIdEndPoint = "";
        }
    }

    private static void MapEditorOnRender(On.Celeste.Editor.MapEditor.orig_Render orig, MapEditor self) {
        orig(self);

        if (roomIdEndPoint.IsNotNullAndEmpty()) {
            string text = string.Format(Dialog.Get(DialogIds.RoomIdEndPoint), EndPoint.roomIdEndPoint);
            Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullNone, null, Engine.ScreenMatrix);
            Draw.Rect(0f, 1020f, ActiveFont.WidthToNextLine(text, 0) + 20f, ActiveFont.HeightOf(text), Color.Black * 0.8f);
            ActiveFont.Draw(text, new Vector2(10f, 1020f), Color.AliceBlue);
            Draw.SpriteBatch.End();
        }
    }

    private static void LevelTemplateOnRenderContents(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After,
                ins => ins.MatchLdfld<LevelTemplate>("EditorColorIndex"),
                ins => ins.OpCode == OpCodes.Ldelem_Any
            )) {
            ilCursor.Emit(OpCodes.Ldarg_0)
                .EmitDelegate<Func<Color, LevelTemplate, Color>>((color, template) => template.Name == roomIdEndPoint ? Color.Yellow : color);
        }
    }

    private static void LevelTemplateOnRenderHighlight(On.Celeste.Editor.LevelTemplate.orig_RenderHighlight orig, LevelTemplate self, Camera camera,
        bool hovered, bool selected) {
        orig(self, camera, hovered, selected);

        if (!hovered && !selected && self.Name == roomIdEndPoint) {
            float thickness = 1f / camera.Zoom * 2f;
            self.Outline(self.X, self.Y, self.Width, self.Height, thickness, Color.Yellow);
        }
    }

    private static void MapEditorOnRenderManualText(ILContext il) {
        string GetKeyText(params IList[] lists) {
            foreach (IList list in lists) {
                if (list.Count == 0) {
                    return null;
                }

                switch (list) {
                    case List<Keys> keys:
                        return string.Join("+", keys) + ": ";
                    case List<Buttons> buttons:
                        return string.Join("+", buttons) + ": ";
                }
            }

            return null;
        }

        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(
                ins => ins.MatchLdstr(typeof(MapEditor).GetFieldValue<string>("ManualText")),
                ins => ins.OpCode == OpCodes.Stloc_0
            )) {
            ilCursor.Index++;
            ilCursor.EmitDelegate<Func<string, string>>(manualText => {
                string keyText = GetKeyText(
                    ModSettings.KeyboardSetEndPoint,
                    ModSettings.KeyboardSetAdditionalEndPoint,
                    ModSettings.ControllerSetEndPoint,
                    ModSettings.ControllerSetEndPoint
                );

                return keyText == null ? manualText : $"{keyText,-14}Set room as timer endpoint\n\n{manualText}";
            });
        }
    }

    public enum SpriteStyle {
        Flag,
        GoldBerry,
        Madeline,
        Badeline,
        Granny,
        Theo,
        Oshiro,
        Bird,
        EyeBat,
        Ogmo,
        Skytorn,
        Towerfall,
        Yuri,
        Random
    }

    private static readonly Color StarFlyColor = Calc.HexToColor("ffd65c");
    private readonly Facings facing;
    public bool Activated;
    private PlayerHair playerHair;
    private PlayerSprite playerSprite;
    private SpriteStyle spriteStyle;
    private readonly string roomName;

    private EndPoint(Player player) {
        Tag = Tags.Global;

        facing = player.Facing;

        Collidable = false;
        Collider = new Hitbox(8f, 11f, -4f, -11f);
        Position = player.Position;
        Depth = player.Depth + 1;
        Add(new PlayerCollider(OnCollidePlayer));
        Add(new BloomPoint(Vector2.UnitY * -8, 0.5f, 18f));
        Add(new VertexLight(Vector2.UnitY * -8, Color.White, 1f, 24, 48));
        Add(new IgnoreSaveLoadComponent());

        // saved madeline sprite
        CreateMadelineSprite(player);
        SetSprite(player);
        roomName = player.SceneAs<Level>().Session.Level;
    }

    private void SetSprite(Player player) {
        spriteStyle = ModSettings.EndPointStyle;
        if (spriteStyle == SpriteStyle.Random) {
            spriteStyle = (SpriteStyle)new Random().Next(Enum.GetNames(typeof(SpriteStyle)).Length - 1);
        }

        switch (spriteStyle) {
            case SpriteStyle.Flag:
                CreateFlag();
                break;
            case SpriteStyle.Madeline:
                CreateMadelineSprite(player);
                AddMadelineSprite();
                break;
            case SpriteStyle.Badeline:
                CreateMadelineSprite(player, true);
                AddMadelineSprite();
                break;
            case SpriteStyle.GoldBerry:
            case SpriteStyle.Granny:
            case SpriteStyle.Theo:
            case SpriteStyle.Oshiro:
            case SpriteStyle.Bird:
                CreateSpriteFromBank();
                break;
            case SpriteStyle.EyeBat:
            case SpriteStyle.Ogmo:
            case SpriteStyle.Skytorn:
            case SpriteStyle.Towerfall:
            case SpriteStyle.Yuri:
                CreateSecretSprite();
                break;
            // ReSharper disable once RedundantCaseLabel
            case SpriteStyle.Random:
            default:
                throw new ArgumentOutOfRangeException(nameof(spriteStyle), spriteStyle, null);
        }
    }

    private void CreateFlag() {
        Add(new FlagComponent(spriteStyle == SpriteStyle.Flag));
    }

    private void ResetSprite() {
        if (Engine.Scene.GetPlayer() is { } player) {
            Get<Sprite>()?.RemoveSelf();
            Get<PlayerHair>()?.RemoveSelf();
            Get<PlayerSprite>()?.RemoveSelf();
            Get<FlagComponent>()?.RemoveSelf();
            SetSprite(player);
        }
    }

    public void ReadyForTime() {
        Collidable = true;
        Activated = false;
    }

    private static void OnCollidePlayer(Player player) {
        if (player.Scene is Level {TimerStarted: true}) {
            RoomTimerManager.UpdateTimerState(true);
        }
    }

    private void StopTime() {
        if (!Activated) {
            Activated = true;
            SceneAs<Level>().Displacement.AddBurst(TopCenter, 0.5f, 4f, 24f, 0.5f);
            Scene.Add(new ConfettiRenderer(TopCenter));
            if (All.FirstOrDefault(point => point.roomName == roomName) == this) {
                Audio.Play("event:/game/07_summit/checkpoint_confetti", TopCenter + Vector2.UnitX);
            }
        }
    }

    private void CreateMadelineSprite(Player player, bool badeline = false) {
        PlayerSpriteMode mode;
        if (badeline) {
            mode = PlayerSpriteMode.Badeline;
        } else {
            bool backpack = player.SceneAs<Level>()?.Session.Inventory.Backpack ?? true;
            mode = backpack ? PlayerSpriteMode.Madeline : PlayerSpriteMode.MadelineNoBackpack;
        }

        PlayerSprite origSprite = player.Sprite;
        if (playerSprite != null) {
            origSprite = playerSprite;
        }

        playerSprite = new PlayerSprite(mode) {
            Position = origSprite.Position,
            Rotation = origSprite.Rotation,
            HairCount = origSprite.HairCount,
            Scale = origSprite.Scale,
            Rate = origSprite.Rate,
            Justify = origSprite.Justify
        };
        if (player.StateMachine.State == Player.StStarFly) {
            playerSprite.Color = StarFlyColor;
        }

        playerSprite.Scale.X = playerSprite.Scale.Abs().X * (int)facing;

        playerSprite.Active = false;
        try {
            if (!origSprite.CurrentAnimationID.IsNullOrEmpty()) {
                playerSprite.Play(origSprite.CurrentAnimationID);
                playerSprite.SetAnimationFrame(origSprite.CurrentAnimationFrame);
            }
        }
        // ReSharper disable once EmptyGeneralCatchClause
        catch (Exception) { }

        if (playerHair == null) {
            playerHair = new PlayerHair(playerSprite) {
                Alpha = player.Hair.Alpha,
                Facing = facing,
                Border = player.Hair.Border,
                DrawPlayerSpriteOutline = player.Hair.DrawPlayerSpriteOutline
            };
            Vector2[] nodes = new Vector2[player.Hair.Nodes.Count];
            player.Hair.Nodes.CopyTo(nodes);
            playerHair.Nodes = nodes.ToList();
            playerHair.Active = false;
        }

        Color hairColor = Player.NormalHairColor;
        if (player.StateMachine.State != Player.StStarFly) {
            switch (player.Dashes) {
                case 0:
                    hairColor = badeline ? Player.UsedBadelineHairColor : Player.UsedHairColor;
                    break;
                case 1:
                    hairColor = badeline ? Player.NormalBadelineHairColor : Player.NormalHairColor;
                    break;
                case 2:
                    hairColor = badeline ? Player.TwoDashesBadelineHairColor : Player.TwoDashesHairColor;
                    break;
            }
        } else {
            hairColor = StarFlyColor;
        }

        playerHair.Color = hairColor;
    }

    private void AddMadelineSprite() {
        Add(playerHair);
        Add(playerSprite);
    }

    private void CreateSpriteFromBank() {
        Sprite sprite = GFX.SpriteBank.Create(Enum.GetNames(typeof(SpriteStyle))[(int)spriteStyle].ToLower());
        sprite.Scale.X *= -(int)facing;

        if (spriteStyle == SpriteStyle.Oshiro) {
            sprite.Position += Vector2.UnitY * 7;
        }

        if (spriteStyle == SpriteStyle.GoldBerry) {
            sprite.Position -= Vector2.UnitY * 10;
        }

        Add(sprite);
    }

    private void CreateSecretSprite() {
        string id = "secret_" + Enum.GetNames(typeof(SpriteStyle))[(int)spriteStyle].ToLower();
        Sprite sprite = new(GFX.Game, "decals/6-reflection/" + id);
        sprite.AddLoop(id, "", 0.1f);
        sprite.Play(id);
        sprite.CenterOrigin();

        Vector2 offset = Vector2.UnitY * -8;
        if (spriteStyle != SpriteStyle.EyeBat) {
            offset = Vector2.UnitY * -16;
        }

        sprite.RenderPosition += offset;

        sprite.Scale.X *= -(int)facing;
        if (spriteStyle == SpriteStyle.Towerfall) {
            sprite.Scale.X = -sprite.Scale.X;
        }

        Add(sprite);
    }

    public static bool IsExist => Engine.Scene is Level level && level.Tracker.GetEntity<EndPoint>() != null || !roomIdEndPoint.IsNullOrEmpty();

    public static bool IsReachedRoomIdEndPoint =>
        !roomIdEndPoint.IsNullOrEmpty() && Engine.Scene is Level level && level.Session.Level == roomIdEndPoint;

    private static readonly List<EndPoint> EmptyList = new();

    private static List<EndPoint> All {
        get {
            if (Engine.Scene is Level level && level.Tracker.Entities.ContainsKey(typeof(EndPoint))) {
                return level.Tracker.GetEntities<EndPoint>().Cast<EndPoint>().ToList();
            } else {
                return EmptyList;
            }
        }
    }

    public static void AllReadyForTime() {
        All.ForEach(point => point.ReadyForTime());
    }

    public static void AllStopTime() {
        All.ForEach(point => point.StopTime());
    }

    public static void AllResetSprite() {
        All.ForEach(point => point.ResetSprite());
    }

    public static void ClearAll() {
        CachedEndPoints.Clear();
        All.ForEach(point => point.RemoveSelf());
        roomIdEndPoint = "";
    }

    public static void SetEndPoint(Scene scene, bool additional) {
        if (scene is Level {Paused: false} level) {
            if (!additional || All.Count == 0) {
                RoomTimerManager.ClearPbTimes();
            }

            CreateEndPoint(level, additional);
        } else if (scene is MapEditor mapEditor) {
            LevelTemplate levelTemplate = mapEditor.TestCheck(mapEditor.mousePosition);
            if (levelTemplate is not null && levelTemplate.Type is not LevelTemplateType.Filler) {
                string lastRoomIdEndPoint = roomIdEndPoint;
                RoomTimerManager.ClearPbTimes();
                if (lastRoomIdEndPoint != levelTemplate.Name) {
                    roomIdEndPoint = levelTemplate.Name;
                }
            }
        }
    }

    private static void CreateEndPoint(Level level, bool additional) {
        if (level.GetPlayer() is {Dead: false} player) {
            if (!additional) {
                All.ForEach(point => point.RemoveSelf());
            }

            level.Add(new EndPoint(player));
        }
    }
}