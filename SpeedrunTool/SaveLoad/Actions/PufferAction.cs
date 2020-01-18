using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class PufferAction : AbstractEntityAction {
        private Dictionary<EntityID, Puffer> savedPuffers = new Dictionary<EntityID, Puffer>();

        public override void OnQuickSave(Level level) {
            savedPuffers = level.Entities.GetDictionary<Puffer>();
        }

        private void RestorePufferPosition(On.Celeste.Puffer.orig_ctor_EntityData_Vector2 orig,
            Puffer self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedPuffers.ContainsKey(entityId)) {
                Puffer savedPuffer = savedPuffers[entityId];
                self.CopyPrivateField("alertTimer", savedPuffer);
                self.CopyPrivateField("anchorPosition", savedPuffer);
                self.CopyPrivateField("cannotHitTimer", savedPuffer);
                self.CopyPrivateField("cantExplodeTimer", savedPuffer);
                self.CopyPrivateField("eyeSpin", savedPuffer);
                self.CopyPrivateField("goneTimer", savedPuffer);
                self.CopyPrivateField("hitSpeed", savedPuffer);
                self.CopyPrivateField("lastPlayerPos", savedPuffer);
                self.CopyPrivateField("lastSinePosition", savedPuffer);
                self.CopyPrivateField("lastSpeedPosition", savedPuffer);
                self.CopyPrivateField("playerAliveFade", savedPuffer);
                self.CopyPrivateField("scale", savedPuffer);
                self.CopyPrivateField("state", savedPuffer);
                
                SineWave sineWave = (SineWave) self.GetPrivateField("idleSine");
                SineWave savedSineWave = (SineWave) savedPuffer.GetPrivateField("idleSine");
                sineWave.Counter = savedSineWave.Counter;

                Sprite sprite = (Sprite) self.GetPrivateField("sprite");
                Sprite savedSprite = (Sprite) savedPuffer.GetPrivateField("sprite");
                sprite.Play(savedSprite.CurrentAnimationID);
                
                self.Add(new RestorePositionComponent(self, savedPuffer));
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

        public override void OnUpdateEntitiesWhenFreeze(Level level) {
            level.UpdateEntities<Puffer>();
        }
    }
}