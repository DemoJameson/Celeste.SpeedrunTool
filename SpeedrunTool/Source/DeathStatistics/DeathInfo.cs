using System;
using System.Collections.Generic;
using Force.DeepCloner;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics {
    public class DeathInfo {
        public string Chapter { get; set; }
        public string Room { get; set; }
        public long LostTime { get; set; }

        public string GetLostTime() {
            TimeSpan lostTimeSpan = TimeSpan.FromTicks(LostTime);
            return (int) lostTimeSpan.TotalSeconds + lostTimeSpan.ToString("\\.fff");
        }

        public string CauseOfDeath { get; set; }

        public Vector2 DeathPosition { get; set; }

        public Vector2 PlaybackStartPosition { get; set; }
        public string PlaybackFilePath { get; set; }

        // Session
        public AreaKey Area { get; set; }
        public Vector2? RespawnPoint { get; set; }
        public PlayerInventory Inventory { get; set; } = PlayerInventory.Default;
        public HashSet<string> Flags { get; set; } = new();
        public HashSet<string> LevelFlags { get; set; } = new();
        public HashSet<EntityID> Strawberries { get; set; } = new();
        public HashSet<EntityID> DoNotLoad { get; set; } = new();
        public HashSet<EntityID> Keys { get; set; } = new();
        public bool[] SummitGems { get; set; } = new bool[6];
        public string FurthestSeenLevel { get; set; }
        public long Time { get; set; }
        public bool StartedFromBeginning { get; set; }
        public int Deaths { get; set; }
        public int Dashes { get; set; }
        public int DashesAtLevelStart { get; set; }
        public int DeathsInCurrentLevel { get; set; }
        public bool InArea { get; set; }
        public string StartCheckpoint { get; set; }
        public bool FirstLevel { get; set; } = true;
        public bool Cassette { get; set; }
        public bool HeartGem { get; set; }
        public bool Dreaming { get; set; }
        public string ColorGrade { get; set; }
        public float LightingAlphaAdd { get; set; }
        public float BloomBaseAdd { get; set; }
        public float DarkRoomAlpha { get; set; } = 0.75f;
        public Session.CoreModes CoreMode { get; set; }
        public bool GrabbedGolden { get; set; }
        public bool HitCheckpoint { get; set; }

        public void TeleportToDeathPosition() {
            Session session = new(Area) {
                RespawnPoint = RespawnPoint,
                Inventory = Inventory,
                Level = Room,
                Flags = Flags,
                LevelFlags = LevelFlags,
                Strawberries = Strawberries,
                DoNotLoad = DoNotLoad,
                Keys = Keys,
                SummitGems = SummitGems,
                FurthestSeenLevel = FurthestSeenLevel,
                Time = Time,
                StartedFromBeginning = StartedFromBeginning,
                Deaths = Deaths,
                Dashes = Dashes,
                DashesAtLevelStart = DashesAtLevelStart,
                DeathsInCurrentLevel = DeathsInCurrentLevel,
                InArea = InArea,
                StartCheckpoint = StartCheckpoint,
                FirstLevel = FirstLevel,
                Cassette = Cassette,
                HeartGem = HeartGem,
                Dreaming = Dreaming,
                LightingAlphaAdd = LightingAlphaAdd,
                BloomBaseAdd = BloomBaseAdd,
                DarkRoomAlpha = DarkRoomAlpha,
                CoreMode = CoreMode,
                GrabbedGolden = GrabbedGolden,
                HitCheckpoint = HitCheckpoint,
            };
            DeathStatisticsManager.Instance.SetTeleportDeathInfo(this);
            Engine.Scene = new LevelLoader(session.DeepClone());
        }
    }
}