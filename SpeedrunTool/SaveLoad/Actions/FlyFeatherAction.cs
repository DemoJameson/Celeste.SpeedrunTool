using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class FlyFeatherAction : AbstractEntityAction {
        private Dictionary<EntityID, FlyFeather> savedFlyFeathers = new Dictionary<EntityID, FlyFeather>();

        public override void OnQuickSave(Level level) {
            savedFlyFeathers = level.Entities.GetDictionary<FlyFeather>();
        }

        private void RestoreFlyFeatherState(On.Celeste.FlyFeather.orig_ctor_EntityData_Vector2 orig,
            FlyFeather self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            RestoreState(self, entityId);
        }

        private void RestoreFlyFeatherState(On.Celeste.FlyFeather.orig_ctor_Vector2_bool_bool orig, FlyFeather self,
            Vector2 position, bool shielded, bool singleUse) {
            orig(self, position, shielded, singleUse);
            if (self.GetEntityId().Equals(default(EntityID)) && Engine.Scene is Level level) {
                EntityID entityId = new EntityID(level.Session.Level,
                    position.GetHashCode() + shielded.GetHashCode() + singleUse.GetHashCode());
                self.SetEntityId(entityId);

                RestoreState(self, entityId);
            }
        }

        private void RestoreState(FlyFeather self, EntityID entityId) {
            if (IsLoadStart && savedFlyFeathers.ContainsKey(entityId)) {
                FlyFeather savedFlyFeather = savedFlyFeathers[entityId];
                if (!savedFlyFeather.Collidable) {
                    self.Collidable = false;
                    float respawnTimer = (float) savedFlyFeather.GetField("respawnTimer") + 0.4f;
                    (self.GetField("sprite") as Sprite).Visible = false;

                    if ((bool) self.GetField("singleUse")) {
                        return;
                    }

                    self.SetField("respawnTimer", respawnTimer);
                    (self.GetField("outline") as Image).Visible = true;
                }
            }
        }

        private static void FlyFeatherOnOnPlayer(On.Celeste.FlyFeather.orig_OnPlayer orig, FlyFeather self, Player player) {
            if (IsFrozen || IsLoadStart) {
                return;
            }
            
            orig(self, player);
        }

        public override void OnClear() {
            savedFlyFeathers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.FlyFeather.ctor_EntityData_Vector2 += RestoreFlyFeatherState;
            On.Celeste.FlyFeather.ctor_Vector2_bool_bool += RestoreFlyFeatherState;
            On.Celeste.FlyFeather.OnPlayer += FlyFeatherOnOnPlayer;
        }

        public override void OnUnload() {
            On.Celeste.FlyFeather.ctor_EntityData_Vector2 -= RestoreFlyFeatherState;
            On.Celeste.FlyFeather.ctor_Vector2_bool_bool -= RestoreFlyFeatherState;
            On.Celeste.FlyFeather.OnPlayer -= FlyFeatherOnOnPlayer;
        }
    }
}