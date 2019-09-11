using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class PufferAction : AbstractEntityAction {
        private Dictionary<EntityID, Puffer> savedPuffers = new Dictionary<EntityID, Puffer>();

        public override void OnQuickSave(Level level) {
            savedPuffers = level.Tracker.GetDictionary<Puffer>();
        }

        private void RestorePufferPosition(On.Celeste.Puffer.orig_ctor_EntityData_Vector2 orig,
            Puffer self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedPuffers.ContainsKey(entityId)) {
                Puffer savedPuffer = savedPuffers[entityId];
                self.Position = savedPuffer.Position;
                self.CopyPrivateField("state", savedPuffer);
                self.CopyPrivateField("goneTimer", savedPuffer);
                self.CopyPrivateField("cannotHitTimer", savedPuffer);
                self.CopyPrivateField("alertTimer", savedPuffer);
                self.CopyPrivateField("eyeSpin", savedPuffer);
                self.CopyPrivateField("hitSpeed", savedPuffer);
                self.CopyPrivateField("lastPlayerPos", savedPuffer);
                self.CopyPrivateField("scale", savedPuffer);
                self.CopyPrivateField("playerAliveFade", savedPuffer);

                Sprite sprite = (Sprite) self.GetPrivateField("sprite");
                Sprite savedSprite = (Sprite) savedPuffer.GetPrivateField("sprite");
                sprite.Play(savedSprite.CurrentAnimationID);
            }
        }

        public override void OnClear() {
            savedPuffers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Puffer.ctor_EntityData_Vector2 += RestorePufferPosition;
        }

        public override void OnUnload() {
            On.Celeste.Puffer.ctor_EntityData_Vector2 -= RestorePufferPosition;
        }

        public override void OnInit() {
            typeof(Puffer).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<Puffer>();
        }
    }
}