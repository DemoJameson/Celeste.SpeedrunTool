using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SinkingPlatformAction : AbstractEntityAction {
        private readonly Dictionary<EntityId2, SinkingPlatform> sinkingPlatforms = new Dictionary<EntityId2, SinkingPlatform>();

        public override void OnSaveSate(Level level) {
            sinkingPlatforms.AddRange(level.Entities.FindAll<SinkingPlatform>());
        }

        private void RestoreSinkingPlatformPosition(On.Celeste.SinkingPlatform.orig_ctor_EntityData_Vector2 orig,
            SinkingPlatform self, EntityData data, Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && sinkingPlatforms.ContainsKey(entityId)) {
                self.Add(new Coroutine(SetPosition(self, sinkingPlatforms[entityId])));
            }
        }

        private IEnumerator SetPosition(SinkingPlatform platform, SinkingPlatform savedPlatform) {
            platform.Position = savedPlatform.Position;
            yield break;
        }


        public override void OnClear() {
            sinkingPlatforms.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SinkingPlatform.ctor_EntityData_Vector2 += RestoreSinkingPlatformPosition;
        }

        public override void OnUnload() {
            On.Celeste.SinkingPlatform.ctor_EntityData_Vector2 -= RestoreSinkingPlatformPosition;
        }
    }
}