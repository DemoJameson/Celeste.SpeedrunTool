using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CloudAction : AbstractEntityAction {
        private Dictionary<EntityID, Cloud> savedClouds = new Dictionary<EntityID, Cloud>();

        public override void OnQuickSave(Level level) {
            savedClouds = level.Entities.GetDictionary<Cloud>();
        }

        private void RestoreCloudPosition(On.Celeste.Cloud.orig_ctor_EntityData_Vector2 orig,
            Cloud self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedClouds.ContainsKey(entityId)) {
                Cloud savedCloud = savedClouds[entityId];
                self.Position = savedCloud.Position;
                self.Visible = savedCloud.Visible;
                self.Collidable = savedCloud.Collidable;
                self.CopyFields(savedCloud, "waiting", "returning", "timer", "scale", "canRumble", "respawnTimer",
                    "speed");
            }
        }

        private void CloudOnAdded(On.Celeste.Cloud.orig_Added orig, Cloud self, Scene scene) {
            orig(self, scene);
            EntityID entityId = self.GetEntityId();
            if (IsLoadStart && savedClouds.ContainsKey(entityId)) {
                self.CopySprite(savedClouds[entityId]);
            }
        }

        private void CloudOnUpdate(On.Celeste.Cloud.orig_Update orig, Cloud self) {
            // 避免站在云上保存读档后被弹飞到天花板
            if (IsLoadStart && self.SceneAs<Level>().GetPlayer() is Player player &&
                player.StateMachine.State == Player.StIntroRespawn) {
                return;
            }

            orig(self);
        }

        public override void OnClear() {
            savedClouds.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Cloud.ctor_EntityData_Vector2 += RestoreCloudPosition;
            On.Celeste.Cloud.Added += CloudOnAdded;
            On.Celeste.Cloud.Update += CloudOnUpdate;
        }

        public override void OnUnload() {
            On.Celeste.Cloud.ctor_EntityData_Vector2 -= RestoreCloudPosition;
            On.Celeste.Cloud.Added -= CloudOnAdded;
            On.Celeste.Cloud.Update -= CloudOnUpdate;
        }
    }
}