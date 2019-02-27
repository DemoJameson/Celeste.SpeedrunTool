using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Celeste.Pico8;
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
            Flag,
            EyeBat,
            Ogmo,
            Skytorn,
            Towerfall,
            Yuri,
            Random
        }

        private static readonly Color StarFlyColor = Calc.HexToColor("ffd65c");
        public readonly string LevelName;
        private readonly Player player;
        private readonly SpriteStyle spriteStyle;
        private FlagComponent flagComponent;


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
                case SpriteStyle.Granny:
                case SpriteStyle.Theo:
                case SpriteStyle.Oshiro:
                case SpriteStyle.Bird:
                    CreateNpcSprite();
                    break;
                case SpriteStyle.Flag:
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
                    throw new ArgumentOutOfRangeException(nameof(spriteStyle), this.spriteStyle, null);
            }
            
            AddFlag();
        }

        private void AddFlag() {
            int flagNumber = player.SceneAs<Level>().Session.Area.ID;
            if (SaveData.Instance.LevelSet == "Celeste") {
                if (flagNumber == 8) {
                    flagNumber = 0;
                }
                else if (flagNumber > 8) {
                    flagNumber--;
                }
            }
            
            Add(flagComponent = new FlagComponent(flagNumber, spriteStyle == SpriteStyle.Flag));
        }

        public void ReAdded(Level level) {
            Collidable = true;
            flagComponent.Active = true;
            flagComponent.Activated = false;
            level.Add(this);
        }

        private void OnCollidePlayer(Player _) {
            RoomTimerManager.Instance.UpdateTimerState(true);
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

        private void CreateNpcSprite() {
            Sprite sprite = GFX.SpriteBank.Create(Enum.GetNames(typeof(SpriteStyle))[(int) spriteStyle].ToLower());
            if (spriteStyle == SpriteStyle.Oshiro) {
                sprite.Position += Vector2.UnitY * 7;
            }

            sprite.Scale.X *= (int) player.Facing;
            Add(sprite);
        }

        private void CreateSecretSprite() {
            string id = "secret_" + Enum.GetNames(typeof(SpriteStyle))[(int) spriteStyle].ToLower();
            Sprite sprite = new Sprite(GFX.Game, "decals/6-reflection/" + id);
            sprite.AddLoop(id, "", 0.1f);
            sprite.Play(id);
            sprite.CenterOrigin();

            Vector2 offset = Vector2.UnitY * -8;
            if (spriteStyle != SpriteStyle.EyeBat) {
                offset = Vector2.UnitY * -16;
            }

            sprite.RenderPosition += offset;

            sprite.Scale.X *= (int) player.Facing;
            if (spriteStyle == SpriteStyle.Towerfall) {
                sprite.Scale.X = -sprite.Scale.X;
            }

            Add(sprite);
        }
    }

    internal class ConfettiRenderer : Entity {
        private static readonly Color[] ConfettiColors = {
            Calc.HexToColor("fe2074"),
            Calc.HexToColor("205efe"),
            Calc.HexToColor("cefe20")
        };

        private readonly Particle[] particles = new Particle[30];

        public ConfettiRenderer(Vector2 position)
            : base(position) {
            Depth = -10010;
            for (int index = 0; index < particles.Length; ++index) {
                particles[index].Position = Position + new Vector2(Calc.Random.Range(-3, 3), Calc.Random.Range(-3, 3));
                particles[index].Color = Calc.Random.Choose(ConfettiColors);
                particles[index].Timer = Calc.Random.NextFloat();
                particles[index].Duration = Calc.Random.Range(2, 4);
                particles[index].Alpha = 1f;
                float angleRadians = Calc.Random.Range(-0.5f, 0.5f) - 1.570796f;
                int num = Calc.Random.Range(140, 220);
                particles[index].Speed = Calc.AngleToVector(angleRadians, num);
            }
        }

        public override void Update() {
            for (int index = 0; index < particles.Length; ++index) {
                particles[index].Position += particles[index].Speed * Engine.DeltaTime;
                particles[index].Speed.X = Calc.Approach(particles[index].Speed.X, 0.0f, 80f * Engine.DeltaTime);
                particles[index].Speed.Y = Calc.Approach(particles[index].Speed.Y, 20f, 500f * Engine.DeltaTime);
                particles[index].Timer += Engine.DeltaTime;
                particles[index].Percent += Engine.DeltaTime / particles[index].Duration;
                particles[index].Alpha = Calc.ClampedMap(particles[index].Percent, 0.9f, 1f, 1f, 0.0f);
                if (particles[index].Speed.Y > 0.0) {
                    particles[index].Approach = Calc.Approach(particles[index].Approach, 5f, Engine.DeltaTime * 16f);
                }
            }
        }

        public override void Render() {
            for (int index = 0; index < particles.Length; ++index) {
                Vector2 position = particles[index].Position;
                float rotation;
                if (particles[index].Speed.Y < 0.0) {
                    rotation = particles[index].Speed.Angle();
                }
                else {
                    rotation = (float) Math.Sin(particles[index].Timer * 4.0) * 1f;
                    position += Calc.AngleToVector(1.570796f + rotation, particles[index].Approach);
                }

                GFX.Game["particles/confetti"].DrawCentered(position + Vector2.UnitY, Color.Black * (particles[index].Alpha * 0.5f), 1f, rotation);
                GFX.Game["particles/confetti"].DrawCentered(position, particles[index].Color * particles[index].Alpha, 1f, rotation);
            }
        }

        private struct Particle {
            public Vector2 Position;
            public Color Color;
            public Vector2 Speed;
            public float Timer;
            public float Percent;
            public float Duration;
            public float Alpha;
            public float Approach;
        }
    }
}