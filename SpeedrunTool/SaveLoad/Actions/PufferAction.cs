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
                self.CopyField(typeof(Puffer), "alertTimer", savedPuffer);
                self.CopyField(typeof(Puffer), "anchorPosition", savedPuffer);
                self.CopyField(typeof(Puffer), "cannotHitTimer", savedPuffer);
                self.CopyField(typeof(Puffer), "cantExplodeTimer", savedPuffer);
                self.CopyField(typeof(Puffer), "eyeSpin", savedPuffer);
                self.CopyField(typeof(Puffer), "goneTimer", savedPuffer);
                self.CopyField(typeof(Puffer), "hitSpeed", savedPuffer);
                self.CopyField(typeof(Puffer), "lastPlayerPos", savedPuffer);
                self.CopyField(typeof(Puffer), "lastSinePosition", savedPuffer);
                self.CopyField(typeof(Puffer), "lastSpeedPosition", savedPuffer);
                self.CopyField(typeof(Puffer), "playerAliveFade", savedPuffer);
                self.CopyField(typeof(Puffer), "scale", savedPuffer);
                self.CopyField(typeof(Puffer), "state", savedPuffer);
                
                SineWave sineWave = (SineWave) self.GetField(typeof(Puffer), "idleSine");
                SineWave savedSineWave = (SineWave) savedPuffer.GetField(typeof(Puffer), "idleSine");
                sineWave.Counter = savedSineWave.Counter;

                Sprite sprite = (Sprite) self.GetField(typeof(Puffer), "sprite");
                Sprite savedSprite = (Sprite) savedPuffer.GetField(typeof(Puffer), "sprite");
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