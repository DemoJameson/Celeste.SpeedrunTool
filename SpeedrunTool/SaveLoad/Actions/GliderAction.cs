using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class GliderAction : AbstractEntityAction {
        private List<Glider> savedGliders = new List<Glider>();
        private List<Glider> savedGlidersCopy = new List<Glider>();

        public override void OnQuickSave(Level level) {
            savedGliders = level.Tracker.GetCastEntities<Glider>().ToList();
            savedGlidersCopy = level.Tracker.GetCastEntities<Glider>().ToList();
        }

        private void RestoreGliderPosition(On.Celeste.Glider.orig_ctor_EntityData_Vector2 orig,
            Glider self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedGliders.Exists(glider => glider.GetEntityId().Equals(entityId))) {
                    Glider savedGlider = savedGliders.Find(glider => glider.GetEntityId().Equals(entityId));
                    savedGlidersCopy.Remove(savedGlider);
                
                    RestoreState(self, savedGlider);
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnQuickLoadStart(Level level) {
            if (savedGlidersCopy.Count == 0) {
                return;
            }

            foreach (var savedGlider in savedGlidersCopy) {
                var createdGlider = new Glider(savedGlider.Position, (bool) savedGlider.GetPrivateField("bubble"), (bool) savedGlider.GetPrivateField("tutorial"));
                createdGlider.SetEntityId(savedGlider.GetEntityId());
                level.Add(createdGlider);
                RestoreState(createdGlider, savedGlider);
            }
        }

        private static void RestoreState(Glider self, Glider savedGlider) {
            if (!savedGlider.Collidable) {
                self.Add(new RemoveSelfComponent());
            }
            self.Position = savedGlider.Position;
            self.Speed = savedGlider.Speed;
            self.CopyPrivateField("prevLiftSpeed", savedGlider);
            self.CopyPrivateField("noGravityTimer", savedGlider);
            self.CopyPrivateField("highFrictionTimer", savedGlider);
            self.CopyPrivateField("bubble", savedGlider);
            self.CopyPrivateField("destroyed", savedGlider);
        }

        public override void OnClear() {
            savedGliders.Clear();
            savedGlidersCopy.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Glider.ctor_EntityData_Vector2 += RestoreGliderPosition;
        }

        public override void OnUnload() {
            On.Celeste.Glider.ctor_EntityData_Vector2 -= RestoreGliderPosition;
        }

        public override void OnInit() {
            typeof(Glider).AddToTracker();
        }
    }
}