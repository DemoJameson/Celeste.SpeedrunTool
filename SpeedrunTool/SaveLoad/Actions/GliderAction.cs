using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class GliderAction : AbstractEntityAction {
        private Dictionary<EntityID, Glider> savedGliders = new Dictionary<EntityID, Glider>();

        public override void OnQuickSave(Level level) {
            savedGliders = level.Tracker.GetDictionary<Glider>();
        }

        private void RestoreGliderPosition(On.Celeste.Glider.orig_ctor_EntityData_Vector2 orig,
            Glider self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedGliders.ContainsKey(entityId)) {
                Glider savedGlider = savedGliders[entityId];
                self.Position = savedGlider.Position;
                self.Speed = savedGlider.Speed;
                self.CopyPrivateField("prevLiftSpeed", savedGlider);
                self.CopyPrivateField("noGravityTimer", savedGlider);
                self.CopyPrivateField("highFrictionTimer", savedGlider);
                self.CopyPrivateField("bubble", savedGlider);
                self.CopyPrivateField("destroyed", savedGlider);
                self.CopyPrivateField("destroyed", savedGlider);
            }
        }

        public override void OnClear() {
            savedGliders.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Glider.ctor_EntityData_Vector2 += RestoreGliderPosition;
        }

        public override void OnUnload() {
            On.Celeste.Glider.ctor_EntityData_Vector2 -= RestoreGliderPosition;
        }

        public override void OnInit() {
            typeof(Glider).AddToTracker();
        }
    }
}