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
                self.CopyFields(typeof(Puffer), savedPuffer, "alertTimer");
                self.CopyFields(typeof(Puffer), savedPuffer, "anchorPosition");
                self.CopyFields(typeof(Puffer), savedPuffer, "cannotHitTimer");
                self.CopyFields(typeof(Puffer), savedPuffer, "cantExplodeTimer");
                self.CopyFields(typeof(Puffer), savedPuffer, "eyeSpin");
                self.CopyFields(typeof(Puffer), savedPuffer, "goneTimer");
                self.CopyFields(typeof(Puffer), savedPuffer, "hitSpeed");
                self.CopyFields(typeof(Puffer), savedPuffer, "lastPlayerPos");
                self.CopyFields(typeof(Puffer), savedPuffer, "lastSinePosition");
                self.CopyFields(typeof(Puffer), savedPuffer, "lastSpeedPosition");
                self.CopyFields(typeof(Puffer), savedPuffer, "playerAliveFade");
                self.CopyFields(typeof(Puffer), savedPuffer, "scale");
                self.CopyFields(typeof(Puffer), savedPuffer, "state");
                
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