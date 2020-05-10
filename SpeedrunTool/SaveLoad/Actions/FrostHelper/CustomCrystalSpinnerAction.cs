using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.FrostHelper {
    public class CustomCrystalSpinnerAction : AbstractEntityAction {
        private const string FullName = "FrostHelper.CrystalStaticSpinner";
        private Dictionary<EntityID, Entity> savedSpinners = new Dictionary<EntityID, Entity>();

        public override void OnQuickSave(Level level) {
            savedSpinners = level.Entities.FindAll<Entity>()
                .Where(entity => entity.GetType().FullName == FullName).GetDictionary();
        }

        private void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self, Vector2 position) {
            orig(self, position);
            if (self.GetType().FullName != FullName) {
                return;
            }

            Level level = CelesteExtensions.GetLevel();
            if (level?.Session?.Level == null) {
                return;
            }
            EntityID entityId = new EntityID(level.Session.Level, position.GetRealHashCode());
            self.SetEntityId(entityId);

            if (IsLoadStart) {
                if (savedSpinners.ContainsKey(entityId)) {
                    var savedSpinner = savedSpinners[entityId];
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
            savedSpinners.Clear();
        }

        public override void OnLoad() {
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
        }

        public override void OnUnload() {
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
        }
    }
}