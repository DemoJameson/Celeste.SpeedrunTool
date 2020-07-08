using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class GliderAction : AbstractEntityAction {
        private List<Glider> savedGliders = new List<Glider>();
        private List<Glider> savedGlidersCopy = new List<Glider>();

        public override void OnSaveSate(Level level) {
            savedGliders = level.Entities.FindAll<Glider>().ToList();
            savedGlidersCopy = level.Entities.FindAll<Glider>().ToList();
        }

        private void RestoreGliderPosition(On.Celeste.Glider.orig_ctor_EntityData_Vector2 orig,
            Glider self, EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedGliders.Exists(glider => glider.GetEntityId2().Equals(entityId))) {
                    Glider savedGlider = savedGliders.FirstOrDefault(glider => glider.GetEntityId2().Equals(entityId));
                    savedGlidersCopy.Remove(savedGlider);
                
                    RestoreState(self, savedGlider);
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnLoadStart(Level level, Player player, Player savedPlayer) {
            if (savedGlidersCopy.Count == 0) {
                return;
            }

            foreach (var savedGlider in savedGlidersCopy) {
                var createdGlider = new Glider(savedGlider.Position, (bool) savedGlider.GetField(typeof(Glider), "bubble"), (bool) savedGlider.GetField(typeof(Glider), "tutorial"));
                createdGlider.SetEntityId2(savedGlider.GetEntityId2());
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
            self.CopyFields(typeof(Glider), savedGlider, "prevLiftSpeed");
            self.CopyFields(typeof(Glider), savedGlider, "noGravityTimer");
            self.CopyFields(typeof(Glider), savedGlider, "highFrictionTimer");
            self.CopyFields(typeof(Glider), savedGlider, "bubble");
            self.CopyFields(typeof(Glider), savedGlider, "destroyed");
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
    }
}