using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class ClutterSwitchAction : AbstractEntityAction {
        private Dictionary<EntityId2, ClutterSwitch> savedClutterSwitches = new Dictionary<EntityId2, ClutterSwitch>();
        private ClutterAbsorbEffect savedClutterAbsorbEffect = null;

        public override void OnQuickSave(Level level) {
            savedClutterSwitches = level.Entities.FindAllToDict<ClutterSwitch>();
            savedClutterAbsorbEffect = level.Entities.FindFirst<ClutterAbsorbEffect>();
        }

        public override void OnQuickLoadStart(Level level, Player player, Player savedPlayer) {
            if (savedClutterAbsorbEffect != null && level.Entities.FindFirst<ClutterAbsorbEffect>() == null) {
                level.Add(new ClutterAbsorbEffect());
            }
        }

        private void RestoreClutterSwitchPosition(On.Celeste.ClutterSwitch.orig_ctor_EntityData_Vector2 orig,
            ClutterSwitch self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedClutterSwitches.ContainsKey(entityId)) {
                ClutterSwitch saved = savedClutterSwitches[entityId];
                self.Add(new Coroutine(Restore(self, saved)));
            }
        }

        private IEnumerator Restore(ClutterSwitch self, ClutterSwitch saved) {
            self.Position = saved.Position;
            self.CopySprite(saved, "sprite");
            yield break;
        }

        public override void OnClear() {
            savedClutterSwitches.Clear();
            savedClutterAbsorbEffect = null;
        }

        public override void OnLoad() {
            On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 += RestoreClutterSwitchPosition;
        }

        public override void OnUnload() {
            On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 -= RestoreClutterSwitchPosition;
        }
    }
}