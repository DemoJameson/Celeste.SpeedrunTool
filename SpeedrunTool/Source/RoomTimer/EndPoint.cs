using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad;

namespace Celeste.Mod.SpeedrunTool.RoomTimer;

[Tracked]
public class EndPoint: Entity {
    private static readonly List<EndPoint> CachedEndPoints = new();
    private static AreaKey cachedAreaKey;

    [Load]
    private static void Load() {
        On.Celeste.Level.End += LevelOnEnd;
        On.Celeste.Level.Begin += LevelOnBegin;
    }

    [Unload]
    private static void Unload() {
        On.Celeste.Level.End -= LevelOnEnd;
        On.Celeste.Level.Begin -= LevelOnBegin;
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

    public EndPoint(Player player) {
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

    public void ResetSprite() {
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

    public void StopTime() {
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
            if (!string.IsNullOrEmpty(origSprite.CurrentAnimationID)) {
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

    public static bool IsExist => Engine.Scene is Level level && level.Tracker.GetEntity<EndPoint>() != null;

    private static readonly List<EndPoint> EmptyList = new();

    public static List<EndPoint> All {
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
}