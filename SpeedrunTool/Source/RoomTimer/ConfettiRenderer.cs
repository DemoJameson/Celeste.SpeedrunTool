namespace Celeste.Mod.SpeedrunTool.RoomTimer; 

[Tracked]
internal class ConfettiRenderer : Entity {
    private static readonly Color[] ConfettiColors = {
        Calc.HexToColor("fe2074"),
        Calc.HexToColor("205efe"),
        Calc.HexToColor("cefe20")
    };

    private readonly Particle[] particles = new Particle[30];

    public ConfettiRenderer(Vector2 position) : base(position) {
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
            } else {
                rotation = (float)Math.Sin(particles[index].Timer * 4.0) * 1f;
                position += Calc.AngleToVector(1.570796f + rotation, particles[index].Approach);
            }

            GFX.Game["particles/confetti"].DrawCentered(position + Vector2.UnitY,
                Color.Black * (particles[index].Alpha * 0.5f), 1f, rotation);
            GFX.Game["particles/confetti"].DrawCentered(position, particles[index].Color * particles[index].Alpha,
                1f, rotation);
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