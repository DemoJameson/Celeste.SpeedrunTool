using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class ReflectionTentaclesAction : AbstractEntityAction {
        private EntityId2 mainEntityId2;

        private Dictionary<EntityId2, ReflectionTentacles> savedReflectionTentacles =
            new Dictionary<EntityId2, ReflectionTentacles>();

        public override void OnQuickSave(Level level) {
            savedReflectionTentacles = level.Entities.FindAllToDict<ReflectionTentacles>();
        }

        private void ReflectionTentaclesOnCreate(On.Celeste.ReflectionTentacles.orig_Create orig,
            ReflectionTentacles self, float fearDistance, int slideUntilIndex, int layer, List<Vector2> startNodes) {
            if (mainEntityId2 != default && layer > 0) {
                self.SetEntityId2(new EntityID(mainEntityId2.EntityId.Level, (mainEntityId2 + "-" + layer).GetHashCode()));
            }

            EntityId2 entityId = self.GetEntityId2();

            if (self.HasEntityId2() && IsLoadStart && savedReflectionTentacles.ContainsKey(entityId)) {
                ReflectionTentacles savedTentacle = savedReflectionTentacles[entityId];
                int index = savedTentacle.Index - savedTentacle.Nodes.Count + startNodes.Count;

                if (startNodes.Count - index <= 1) {
                    index--;
                    self.Visible = false;
                }

                slideUntilIndex -= index;
                startNodes = startNodes.Skip(index).ToList();
            }

            orig(self, fearDistance, slideUntilIndex, layer, startNodes);
        }

        private void AttachEntityId(On.Celeste.ReflectionTentacles.orig_ctor_EntityData_Vector2 orig,
            ReflectionTentacles self, EntityData data, Vector2 offset) {
            mainEntityId2 = data.ToEntityId2(self);
            self.SetEntityId2(mainEntityId2);
            orig(self, data, offset);
        }

        public override void OnClear() {
            savedReflectionTentacles = null;
        }

        public override void OnLoad() {
            On.Celeste.ReflectionTentacles.Create += ReflectionTentaclesOnCreate;
            On.Celeste.ReflectionTentacles.ctor_EntityData_Vector2 += AttachEntityId;
        }

        public override void OnUnload() {
            On.Celeste.ReflectionTentacles.Create -= ReflectionTentaclesOnCreate;
            On.Celeste.ReflectionTentacles.ctor_EntityData_Vector2 -= AttachEntityId;
        }
    }
}