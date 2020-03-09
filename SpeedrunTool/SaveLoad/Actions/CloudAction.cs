using System.Collections;
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

        // TODO 在云蓄力到最顶端时保存会被弹起来
        private void RestoreCloudPosition(On.Celeste.Cloud.orig_ctor_EntityData_Vector2 orig,
            Cloud self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedClouds.ContainsKey(entityId)) {
                Cloud savedCloud = savedClouds[entityId];
                self.Position = savedCloud.Position;
                self.CopyField(typeof(Cloud), "waiting", savedCloud);
                self.CopyField(typeof(Cloud), "returning", savedCloud);
                self.CopyField(typeof(Cloud), "timer", savedCloud);
                self.CopyField(typeof(Cloud), "scale", savedCloud);
                self.CopyField(typeof(Cloud), "canRumble", savedCloud);

                self.Add(new Coroutine(RestoreRespawnState(self, savedCloud)));
            }
        }

        private IEnumerator RestoreRespawnState(Cloud self, Cloud savedCloud) {
            self.CopyField(typeof(Cloud), "respawnTimer", savedCloud);
            self.Collidable = savedCloud.Collidable;
            self.CopyField(typeof(Cloud), "speed", savedCloud);
            yield break;
        }

        public override void OnClear() {
            savedClouds.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Cloud.ctor_EntityData_Vector2 += RestoreCloudPosition;
        }

        public override void OnUnload() {
            On.Celeste.Cloud.ctor_EntityData_Vector2 -= RestoreCloudPosition;
        }
    }
}