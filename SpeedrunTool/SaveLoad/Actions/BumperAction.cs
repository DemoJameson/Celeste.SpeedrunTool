using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BumperAction : AbstractEntityAction {
        private Dictionary<EntityId2, Bumper> savedBumpers = new Dictionary<EntityId2, Bumper>();

        public override void OnSaveSate(Level level) {
            savedBumpers = level.Entities.FindAllToDict<Bumper>();
        }

        private void RestoreBumperPosition(On.Celeste.Bumper.orig_ctor_EntityData_Vector2 orig,
            Bumper self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedBumpers.ContainsKey(entityId)) {
                Bumper savedBumper = savedBumpers[entityId];

                self.CopyFields(savedBumper,
                    "anchor",
                    "fireMode",
                    "goBack",
                    "respawnTimer"
                );
                
                self.CopySprite(savedBumper, "sprite");
                self.CopySprite(savedBumper, "spriteEvil");

                SineWave sineWave = self.Get<SineWave>();
                SineWave savedSineWave = savedBumper.Get<SineWave>();
                sineWave.Counter = savedSineWave.Counter;

                Tween savedTween = savedBumper.Get<Tween>();
                if (savedTween != null) {
                    self.Get<Tween>().CopyFrom(savedTween);
                }
            }
        }

        public override void OnClear() {
            savedBumpers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Bumper.ctor_EntityData_Vector2 += RestoreBumperPosition;
        }

        public override void OnUnload() {
            On.Celeste.Bumper.ctor_EntityData_Vector2 -= RestoreBumperPosition;
        }
    }
}