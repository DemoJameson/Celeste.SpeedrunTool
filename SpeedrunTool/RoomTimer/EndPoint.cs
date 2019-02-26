using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.RoomTimer {
    [Tracked]
    public class EndPoint : Entity {
        public enum SpriteStyle {
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
        public readonly string LevelName;
        private readonly SpriteStyle spriteStyle;
        private readonly Player player;

        public EndPoint(Player player, SpriteStyle spriteStyle) {
            this.player = player;
            this.spriteStyle = spriteStyle;
            LevelName = player.SceneAs<Level>().Session.Level;

            Collidable = false;
            Collider = new Hitbox(8f, 11f, -4f, -11f);
            Position = player.Position;
            Depth = player.Depth + 1;
            Add(new PlayerCollider(OnCollidePlayer));

            if (spriteStyle == SpriteStyle.Random) {
                this.spriteStyle = (SpriteStyle) new Random().Next(Enum.GetNames(typeof(SpriteStyle)).Length - 1);
            }

            switch (this.spriteStyle) {
                case SpriteStyle.Madeline:
                    CreateMadelineSprite();
                    break;
                case SpriteStyle.Badeline:
                    CreateMadelineSprite(true);
                    break;
                default:
                    CreateSprite();
                    break;
            }
        }

        private void CreateMadelineSprite(bool badeline = false) {
            Active = false;
            PlayerSpriteMode mode;
            if (badeline) {
                mode = PlayerSpriteMode.Badeline;
            }
            else {
                bool backpack = player.SceneAs<Level>()?.Session.Inventory.Backpack ?? true;
                mode = backpack ? PlayerSpriteMode.Madeline : PlayerSpriteMode.MadelineNoBackpack;
            }

            PlayerSprite playerSprite = new PlayerSprite(mode) {
                Position = player.Sprite.Position,
                Rotation = player.Sprite.Rotation,
                HairCount = player.Sprite.HairCount,
                Scale = player.Sprite.Scale,
                Rate = player.Sprite.Rate,
                Justify = player.Sprite.Justify
            };
            playerSprite.Scale.X = playerSprite.Scale.X * (float) player.Facing;
            if (player.StateMachine.State == Player.StStarFly) {
                playerSprite.Color = StarFlyColor;
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
            }
            else {
                hairColor = StarFlyColor;
            }

            PlayerHair playerHair = new PlayerHair(playerSprite) {
                Color = hairColor,
                Alpha = player.Hair.Alpha,
                Facing = player.Facing,
                SimulateMotion = player.Hair.SimulateMotion,
                StepYSinePerSegment = player.Hair.StepYSinePerSegment,
                Border = player.Hair.Border,
                DrawPlayerSpriteOutline = player.Hair.DrawPlayerSpriteOutline
            };
            Vector2[] nodes = new Vector2[player.Hair.Nodes.Count];
            player.Hair.Nodes.CopyTo(nodes);
            playerHair.Nodes = nodes.ToList();

            Add(playerHair);
            Add(playerSprite);

            try {
                playerSprite.Play(player.Sprite.CurrentAnimationID);
                playerSprite.SetAnimationFrame(player.Sprite.CurrentAnimationFrame);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception) { }
        }

        private void CreateSprite() {
            Sprite sprite;
            if (spriteStyle <= SpriteStyle.Bird) {
                sprite = GFX.SpriteBank.Create(Enum.GetNames(typeof(SpriteStyle))[(int) spriteStyle].ToLower());
                if (spriteStyle == SpriteStyle.Oshiro) {
                    sprite.Position += Vector2.UnitY * 7;
                }
            }
            else {
                string name = "secret_" + Enum.GetNames(typeof(SpriteStyle))[(int) spriteStyle].ToLower();
                sprite = new Sprite(GFX.Game, "decals/6-reflection/" + name);
                sprite.AddLoop(name, "", 0.1f);
                sprite.Play(name);
                sprite.CenterOrigin();
                
                Vector2 offset = Vector2.UnitY * -8;
                if (spriteStyle != SpriteStyle.EyeBat) {
                    offset = Vector2.UnitY * -16;
                }

                sprite.RenderPosition += offset;
            }

            sprite.Scale.X = sprite.Scale.X * (int) player.Facing;
            if (spriteStyle == SpriteStyle.Towerfall) {
                sprite.Scale.X = -sprite.Scale.X;
            }

            Add(sprite);
        }

        private static void OnCollidePlayer(Player player) {
            RoomTimerManager.Instance.UpdateTimerState(true);
        }
    }
}