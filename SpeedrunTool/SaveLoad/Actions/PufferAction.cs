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
                self.CopyField("alertTimer", savedPuffer);
                self.CopyField("anchorPosition", savedPuffer);
                self.CopyField("cannotHitTimer", savedPuffer);
                self.CopyField("cantExplodeTimer", savedPuffer);
                self.CopyField("eyeSpin", savedPuffer);
                self.CopyField("goneTimer", savedPuffer);
                self.CopyField("hitSpeed", savedPuffer);
                self.CopyField("lastPlayerPos", savedPuffer);
                self.CopyField("lastSinePosition", savedPuffer);
                self.CopyField("lastSpeedPosition", savedPuffer);
                self.CopyField("playerAliveFade", savedPuffer);
                self.CopyField("scale", savedPuffer);
                self.CopyField("state", savedPuffer);
                
                SineWave sineWave = (SineWave) self.GetField("idleSine");
                SineWave savedSineWave = (SineWave) savedPuffer.GetField("idleSine");
                sineWave.Counter = savedSineWave.Counter;

                Sprite sprite = (Sprite) self.GetField("sprite");
                Sprite savedSprite = (Sprite) savedPuffer.GetField("sprite");
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
    }
}