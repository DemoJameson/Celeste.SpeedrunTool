using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class ReflectionTentaclesAction : AbstractEntityAction {
        private EntityID mainEntityId = default(EntityID);

        private Dictionary<EntityID, ReflectionTentacles> savedReflectionTentacles =
            new Dictionary<EntityID, ReflectionTentacles>();

        public override void OnQuickSave(Level level) {
            savedReflectionTentacles = level.Tracker.GetDictionary<ReflectionTentacles>();
        }

        private void ReflectionTentaclesOnCreate(On.Celeste.ReflectionTentacles.orig_Create orig,
            ReflectionTentacles self, float fearDistance, int slideUntilIndex, int layer, List<Vector2> startNodes) {
            if (!mainEntityId.Equals(default(EntityID)) && layer > 0) {
                self.SetEntityId(new EntityID(mainEntityId.Level + "ReflectionTentacles", mainEntityId.ID + layer));
            }

            EntityID entityId = self.GetEntityId();

            if (!entityId.Equals(default(EntityID)) && IsLoadStart && savedReflectionTentacles.ContainsKey(entityId)) {
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
            mainEntityId = data.ToEntityId();
            self.SetEntityId(data);
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