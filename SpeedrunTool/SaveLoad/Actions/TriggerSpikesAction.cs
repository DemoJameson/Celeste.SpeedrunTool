using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TriggerSpikesAction : AbstractEntityAction {
        private Dictionary<EntityID, TriggerSpikes> savedTriggerSpikes = new Dictionary<EntityID, TriggerSpikes>();

        public override void OnQuickSave(Level level) {
            savedTriggerSpikes = level.Tracker.GetDictionary<TriggerSpikes>();
        }

        private void TriggerSpikesOnCtorEntityDataVector2Directions(
            On.Celeste.TriggerSpikes.orig_ctor_EntityData_Vector2_Directions orig, TriggerSpikes self, EntityData data,
            Vector2 offset, TriggerSpikes.Directions dir) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, dir);

            if (IsLoadStart) {
                if (savedTriggerSpikes.ContainsKey(entityId)) {
                    TriggerSpikes savedTriggerSpike = savedTriggerSpikes[entityId];
                    if (savedTriggerSpike.Get<StaticMover>()?.Platform is CassetteBlock) {
                        return;
                    }
                    self.Position = savedTriggerSpikes[entityId].Position;
                    self.Collidable = savedTriggerSpikes[entityId].Collidable;
                    self.Visible = savedTriggerSpikes[entityId].Visible;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedTriggerSpikes.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TriggerSpikes.ctor_EntityData_Vector2_Directions +=
                TriggerSpikesOnCtorEntityDataVector2Directions;
        }

        public override void OnUnload() {
            On.Celeste.TriggerSpikes.ctor_EntityData_Vector2_Directions -=
                TriggerSpikesOnCtorEntityDataVector2Directions;
        }

        public override void OnInit() {
            typeof(TriggerSpikes).AddToTracker();
        }
    }
}