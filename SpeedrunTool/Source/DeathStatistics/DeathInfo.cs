using System.Collections.Generic;
using Force.DeepCloner;
using YamlDotNet.Serialization;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics; 

public class DeathInfo {
    public string Chapter { get; set; }
    public string Room { get; set; }
    public long LostTime { get; set; }

    public string FormattedLostTime {
        get {
            TimeSpan lostTimeSpan = TimeSpan.FromTicks(LostTime);
            return (int)lostTimeSpan.TotalSeconds + lostTimeSpan.ToString("\\.fff");
        }
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

    [YamlIgnore]
    public Session Session => new(Area) {
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

    public void CopyFromSession(Session session) {
        Session clonedSession = session.DeepClone();
        Chapter = GetChapterName(session);
        Room = clonedSession.Level;
        Area = clonedSession.Area;
        RespawnPoint = clonedSession.RespawnPoint;
        Inventory = clonedSession.Inventory;
        Flags = clonedSession.Flags;
        LevelFlags = clonedSession.LevelFlags;
        Strawberries = clonedSession.Strawberries;
        DoNotLoad = clonedSession.DoNotLoad;
        Keys = clonedSession.Keys;
        SummitGems = clonedSession.SummitGems;
        FurthestSeenLevel = clonedSession.FurthestSeenLevel;
        Time = clonedSession.Time;
        StartedFromBeginning = clonedSession.StartedFromBeginning;
        Deaths = clonedSession.Deaths;
        Dashes = clonedSession.Dashes;
        DashesAtLevelStart = clonedSession.DashesAtLevelStart;
        DeathsInCurrentLevel = clonedSession.DeathsInCurrentLevel;
        InArea = clonedSession.InArea;
        StartCheckpoint = clonedSession.StartCheckpoint;
        FirstLevel = clonedSession.FirstLevel;
        Cassette = clonedSession.Cassette;
        HeartGem = clonedSession.HeartGem;
        Dreaming = clonedSession.Dreaming;
        ColorGrade = clonedSession.ColorGrade;
        LightingAlphaAdd = clonedSession.LightingAlphaAdd;
        BloomBaseAdd = clonedSession.BloomBaseAdd;
        DarkRoomAlpha = clonedSession.DarkRoomAlpha;
        CoreMode = clonedSession.CoreMode;
        GrabbedGolden = clonedSession.GrabbedGolden;
        HitCheckpoint = clonedSession.HitCheckpoint;
    }

    private string GetChapterName(Session session) {
        string levelName = Dialog.Get(AreaData.Get(session).Name, Dialog.Languages["english"]);
        string levelMode = ((char) (session.Area.Mode + 'A')).ToString();

        switch (levelName) {
            case "Forsaken City":
                levelName = "1";
                break;
            case "Old Site":
                levelName = "2";
                break;
            case "Celestial Resort":
                levelName = "3";
                break;
            case "Golden Ridge":
                levelName = "4";
                break;
            case "Mirror Temple":
                levelName = "5";
                break;
            case "Reflection":
                levelName = "6";
                break;
            case "The Summit":
                levelName = "7";
                break;
            case "Core":
                levelName = "8";
                break;
        }

        if (levelName.Length == 1) {
            return levelName + levelMode;
        }

        if (AreaData.Get(session).Interlude) {
            return levelName;
        }

        if (levelName == "Farewell") {
            return levelName;
        }

        return levelName + " " + levelMode;
    }
}