using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class CloudAction : AbstractEntityAction
    {
        private Dictionary<EntityID, Cloud> _savedClouds = new Dictionary<EntityID, Cloud>();

        public override void OnQuickSave(Level level)
        {
            _savedClouds = level.Tracker.GetDictionary<Cloud>();
        }

        private void RestoreCloudPosition(On.Celeste.Cloud.orig_ctor_EntityData_Vector2 orig,
            Cloud self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedClouds.ContainsKey(entityId))
            {
                Cloud savedCloud = _savedClouds[entityId];
                self.Position = savedCloud.Position;
                self.CopyPrivateField("waiting", savedCloud);
                self.CopyPrivateField("returning", savedCloud);
                self.CopyPrivateField("timer", savedCloud);
                self.CopyPrivateField("scale", savedCloud);
                self.CopyPrivateField("canRumble", savedCloud);

                self.Add(new Coroutine(RestoreRespawnState(self, savedCloud)));
            }
        }

        private IEnumerator RestoreRespawnState(Cloud self, Cloud savedCloud)
        {
            yield return null;
            self.CopyPrivateField("respawnTimer", savedCloud);
            self.Collidable = savedCloud.Collidable;
            self.CopyPrivateField("speed", savedCloud);
        }

        public override void OnClear()
        {
            _savedClouds.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.Cloud.ctor_EntityData_Vector2 += RestoreCloudPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.Cloud.ctor_EntityData_Vector2 -= RestoreCloudPosition;
        }

        public override void OnInit()
        {
            typeof(Cloud).AddToTracker();
        }
    }
}