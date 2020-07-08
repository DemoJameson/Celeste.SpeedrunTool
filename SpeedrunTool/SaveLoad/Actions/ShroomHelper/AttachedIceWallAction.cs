using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Components;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.ShroomHelper {
    public class AttachedIceWallAction : AbstractEntityAction {
        private const string FullName = "Celeste.Mod.ShroomHelper.Entities.AttachedIceWall";
        private Dictionary<EntityId2, Entity> savedIceWalls = new Dictionary<EntityId2, Entity>();

        public override void OnSaveSate(Level level) {
            savedIceWalls = level.Entities.FindAll<Entity>()
                .Where(entity => entity.GetType().FullName == FullName).GetDictionary();
        }

        private void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self, Vector2 position) {
            orig(self, position);
            if (self.GetType().FullName != FullName) {
                return;
            }

            EntityId2 entityId = self.CreateEntityId2(position.ToString(), self.GetField("Facing").ToString());
            if (entityId == default) {
                return;
            }
            self.SetEntityId2(entityId);

            if (IsLoadStart) {
                if (savedIceWalls.ContainsKey(entityId)) {
                    var savedSpinner = savedIceWalls[entityId];
                    var platform = savedSpinner.Get<StaticMover>()?.Platform;
                    if (platform is CassetteBlock) {
                        return;
                    }

                    if (platform is FloatySpaceBlock) {
                        self.Add(new RestorePositionComponent(self, savedSpinner));
                    }
                    else {
                        self.Position = savedSpinner.Position;
                    }

                    self.Collidable = savedSpinner.Collidable;
                    self.Visible = savedSpinner.Visible;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            } 
        }

        public override void OnClear() {
            savedIceWalls.Clear();
        }

        public override void OnLoad() {
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
        }

        public override void OnUnload() {
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
        }
    }
}