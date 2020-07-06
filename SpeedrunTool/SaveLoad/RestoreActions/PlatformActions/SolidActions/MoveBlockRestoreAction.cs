using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.PlatformActions.SolidActions {
    public class MoveBlockRestoreAction : AbstractRestoreAction {
        public MoveBlockRestoreAction() : base(typeof(MoveBlock), new List<AbstractRestoreAction>()) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            MoveBlock loaded = (MoveBlock) loadedEntity;
            MoveBlock saved = (MoveBlock) savedEntity;

            loaded.CopyFields(saved,
                "flash",
                "state",
                "triggered",
                "targetSpeed",
                "speed",
                "angle",
                "targetAngle",
                "leftPressed",
                "rightPressed",
                "topPressed",
                "particleRemainder"
                );
            if (saved.GetField("noSquish") != null && loaded.Scene.GetPlayer() is Player player) {
                loaded.SetField("noSquish", player);
            }
            
            // TODO moveSfx 我觉得需要一整套恢复 Component 的方法
        }
        
        private static void BlockCoroutineStart(ILContext il) {
            il.SkipAddCoroutine<MoveBlock>("Controller", () => IsLoadStart);
        }

        public override void Load() {
            IL.Celeste.MoveBlock.ctor_Vector2_int_int_Directions_bool_bool += BlockCoroutineStart;
        }

        public override void Unload() {
            IL.Celeste.MoveBlock.ctor_Vector2_int_int_Directions_bool_bool -= BlockCoroutineStart;
        }
    }
}