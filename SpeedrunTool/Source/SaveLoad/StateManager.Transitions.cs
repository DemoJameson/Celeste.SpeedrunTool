using Celeste.Mod.SpeedrunTool.ModInterop;
using Celeste.Mod.SpeedrunTool.SaveLoad.Profiling;
using Celeste.Mod.SpeedrunTool.Utils;
using Force.DeepCloner;
using Force.DeepCloner.Helpers;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EventInstance = FMOD.Studio.EventInstance;

namespace Celeste.Mod.SpeedrunTool.SaveLoad;

/// <summary>
/// Scene transitions, save-on-transition and death auto-load hooks.
/// </summary>
public sealed partial class StateManager {
    private static void IL_LevelTransitionRoutine(ILContext context) {
        ILCursor cursor = new(context);
        if (cursor.TryGotoNext(MoveType.After, ins => ins.OpCode == OpCodes.Newobj && ins.Operand.ToString().Contains("Level/<TransitionRoutine>"))) {
            cursor.EmitDelegate(SaveTransitionRoutine);
        }
    }

    private static IEnumerator SaveTransitionRoutine(IEnumerator enumerator) {
        Instance.transitionRoutine = new WeakReference<IEnumerator>(enumerator);
        return enumerator;
    }

    private static IEnumerator On_LevelTransitionRoutine(On.Celeste.Level.orig_TransitionRoutine orig, Level self, LevelData next, Vector2 direction) {
        IEnumerator enumerator = orig(self, next, direction);
        while (enumerator.MoveNext()) {
            yield return enumerator.Current;
        }

        Instance.transitionRoutine = null;
    }

    private static void LevelOnEnd(On.Celeste.Level.orig_End orig, Level self) {
        orig(self);
        Instance.transitionRoutine = null;
        DesyncRiskAnalyzer.CheckAll = true;
    }



    private static readonly Lazy<PropertyInfo> InGameOverworldHelperIsOpen = new(
        () => ModUtils.GetType("CollabUtils2", "Celeste.Mod.CollabUtils2.UI.InGameOverworldHelper")?.GetPropertyInfo("IsOpen")
    );

    internal void HandleSwitchScene(Scene self) {
        if (!IsSaved) {
            return;
        }

        // v3.28 新功能: 切换 Scene 时不再自动删除 SaveState.
        // 由于我们还保存了 Celeste.SaveData.Instance, 因此我们的多存档甚至可以存储 不同的原版存档文件上的不同章节
        // 读档时如果目标章节和当前场景不一致, StateManager.PendingLoad.cs 会先让游戏正常进入保存时的章节
        // (LevelLoader 加载 map bin / tilesets / audio 等 area 资源), 再从 Level.Begin 里恢复状态.
        // AutoClearStateOnSceneSwitch 保留为"旧行为/保守模式"开关, 默认关闭.

        if (ModSettings.AutoClearStateOnSceneSwitch) {
            if (self is Overworld && !SavedByTas && InGameOverworldHelperIsOpen.Value?.GetValue(null) as bool? != true
                || self.GetSession() is { } session && session.Area != savedLevel.Session.Area) {
                ClearStateImpl(hasGc: true);
            }
        }

        // 重启章节 Level 实例变更，所以之前预克隆的实体作废，需要重新克隆
        // 因为 preClone 期间若字段为 Level, 并且 Engine.Scene 也确实是 level, 则会直接把字段设为 level
        // pending load 也依赖这里: 新章节的 Level.Begin 会先刷新 preclone, 然后才执行恢复.
        if (self is Level) {
            State = State.None;
            PreCloneSavedEntities();
        }
    }

    private static void AutoLoadStateWhenDeath(On.Celeste.PlayerDeadBody.orig_End orig, PlayerDeadBody self) {
        if (ModSettings.Enabled
            && ModSettings.AutoLoadStateAfterDeath
            && Instance.IsSaved
            && !TasUtils.Running
            && !Instance.SavedByTas
            && !self.finished
            && Engine.Scene is Level level
            && level.Entities.FindFirst<PlayerSeeker>() == null
           ) {
            level.OnEndOfFrame += () => {
                if (Instance.IsSaved) {
                    Instance.LoadStateImpl(false, out _);
                }
                else {
                    level.DoScreenWipe(wipeIn: false, self.DeathAction ?? level.Reload);
                }
            };
            self.RemoveSelf();
        }
        else {
            orig(self);
        }
    }
}
