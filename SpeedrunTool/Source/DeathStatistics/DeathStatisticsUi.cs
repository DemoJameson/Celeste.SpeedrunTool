using System.Collections.Generic;

namespace Celeste.Mod.SpeedrunTool.DeathStatistics; 

[Tracked]
public class DeathStatisticsUi : TextMenu {
    private static SpeedrunToolSaveData ModSaveData => SpeedrunToolModule.SaveData;

    private static readonly Dictionary<string, int> ColumnHeaders = new() {
        {Dialog.Clean(DialogIds.Chapter), 400},
        {Dialog.Clean(DialogIds.Room), 400},
        {Dialog.Clean(DialogIds.LostTime), 400},
        {Dialog.Clean(DialogIds.CauseOfDeath), 400},
    };

    private bool closing;
    private float inputDelay;

    public DeathStatisticsUi() {
        Reload(ModSaveData.Selection);
        OnESC = OnCancel = () => {
            Focused = false;
            closing = true;
            ModSaveData.SetSelection(Selection);
        };
        MinWidth = 600f;
        Position.Y = ScrollTargetY;
        Alpha = 0.0f;
    }

    private void Reload(int index = -1) {
        Clear();

        string title;
        if (SaveData.Instance is { } saveData) {
            if (saveData.FileSlot == -1) {
                title = Dialog.Clean(DialogIds.DeathStatisticsHeaderDebug);
            } else {
                title = string.Format(Dialog.Get(DialogIds.DeathStatisticsHeader), saveData.FileSlot);
            }
        } else {
            title = Dialog.Clean(DialogIds.DeathStatistics);
        }

        Add(new Header(title));

        if (ModSaveData.DeathInfos.Count == 0) {
            AddEmptyInfo();
        } else {
            AddTotalInfo();
            AddListHeader();
            AddListItems();
            AddClearButton();
        }

        if (index < 0) {
            return;
        }

        Selection = index;
    }

    private void AddEmptyInfo() {
        Add(new Button(DialogIds.NoData.DialogClean()) {
            Disabled = true,
            Selectable = false
        });
    }

    private void AddTotalInfo() {
        Add(new TotalItem(new Dictionary<string, string> {
            {$"{Dialog.Clean(DialogIds.TotalDeathCount)}: ", ModSaveData.GetTotalDeathCount().ToString()},
            {$"{Dialog.Clean(DialogIds.TotalLostTime)}: ", ModSaveData.GetTotalLostTime()},
        }));
        Add(new SubHeader(""));
    }

    private void AddListHeader() {
        Add(new ListItem(ColumnHeaders, false));
    }

    private void AddListItems() {
        ModSaveData.DeathInfos.ForEach(deathInfo => {
            Dictionary<string, int> labels = new() {
                {deathInfo.Chapter, 400},
                {deathInfo.Room, 400},
                {deathInfo.FormattedLostTime, 400},
                {deathInfo.CauseOfDeath, 400},
            };
            ListItem item = new(labels);
            item.Pressed(() => {
                ModSaveData.Selection = Selection;
                DeathStatisticsManager.TeleportToDeathPosition(deathInfo);
            });
            Add(item);
        });
    }

    private void AddClearButton() {
        Add(new SubHeader(""));
        Button clearButton = new(Dialog.Clean(DialogIds.ClearDeathStatistics)) {
            IncludeWidthInMeasurement = false,
            AlwaysCenter = true,
            OnPressed = () => {
                ModSaveData.Clear();
                Reload(0);
            }
        };
        Add(clearButton);
    }

    public override void Update() {
        base.Update();

        if (inputDelay > 0.0) {
            inputDelay -= Engine.DeltaTime;
            if (inputDelay <= 0.0) {
                Focused = true;
            }
        }

        Alpha = Calc.Approach(Alpha, closing ? 0.0f : 1f, Engine.DeltaTime * 8f);
        if (!closing || Alpha > 0.0) {
            return;
        }

        Close();
    }

    public override void Render() {
        Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * Ease.CubeOut(Alpha));
        base.Render();
    }
}

public class ListItem : TextMenu.Item {
    private const string ConfirmSfx = "event:/ui/main/button_select";
    private const int Divider = 20;
    private const float FixedWidth = 1600f;
    private readonly Dictionary<string, int> labels;

    public ListItem(Dictionary<string, int> labels, bool selectable = true) {
        this.labels = labels;
        Selectable = selectable;
        Disabled = !selectable;
    }

    public override void ConfirmPressed() {
        Audio.Play(ConfirmSfx);
        base.ConfirmPressed();
    }

    public override float LeftWidth() {
        return FixedWidth;
    }

    public override float Height() {
        return ActiveFont.LineHeight;
    }

    public override void Render(Vector2 position, bool highlighted) {
        float alpha = Container.Alpha;
        Color color = Disabled
            ? Color.Gray * alpha
            : (highlighted ? Container.HighlightColor : Color.White) * alpha;
        Color strokeColor = Color.Black * (alpha * alpha * alpha);

        Vector2 offset = Vector2.Zero;
        foreach (KeyValuePair<string, int> label in labels) {
            float scale = 1f;
            float measureWidth = ActiveFont.Measure(label.Key).X;
            if (measureWidth > label.Value - Divider) {
                scale = (label.Value - Divider) / measureWidth;
            }

            ActiveFont.DrawOutline(label.Key, position + offset, new Vector2(0.0f, 0.5f), Vector2.One * scale,
                color, 2f,
                strokeColor);
            offset += new Vector2(label.Value, 0);
        }
    }
}

public class TotalItem : TextMenu.Item {
    private const float FixedWidth = 1600f;
    private readonly Dictionary<string, string> labels;

    public TotalItem(Dictionary<string, string> labels) {
        this.labels = labels;
        Selectable = false;
    }

    public override float LeftWidth() {
        return FixedWidth;
    }

    public override float Height() {
        return ActiveFont.LineHeight;
    }

    public override void Render(Vector2 position, bool highlighted) {
        float alpha = Container.Alpha;
        Color color = Color.Gray * alpha;
        Color strokeColor = Color.Black * (alpha * alpha * alpha);

        Vector2 offset = Vector2.Zero;
        foreach (KeyValuePair<string, string> label in labels) {
            ActiveFont.DrawOutline(label.Key, position + offset, new Vector2(0.0f, 0.5f), Vector2.One, color, 2f,
                strokeColor);
            ActiveFont.DrawOutline(label.Value, position + offset + ActiveFont.Measure(label.Key).XComp(),
                new Vector2(0.0f, 0.5f), Vector2.One, Color.White, 2f, strokeColor);
            offset += new Vector2(FixedWidth / labels.Count, 0);
        }
    }
}