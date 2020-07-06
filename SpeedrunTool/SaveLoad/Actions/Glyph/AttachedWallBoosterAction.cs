using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Glyph {
    public class AttachedWallBoosterAction : AbstractEntityAction {
        private const string FullName = "Celeste.Mod.AcidHelper.Entities.AttachedWallBooster";
        private Dictionary<EntityId2, WallBooster> savedWallBoosters = new Dictionary<EntityId2, WallBooster>();

        public override void OnQuickSave(Level level) {
            savedWallBoosters = level.Entities.FindAllToDict<WallBooster>();
        }

        private void WallBoosterOnCtor_Vector2_float_bool_bool(On.Celeste.WallBooster.orig_ctor_Vector2_float_bool_bool orig, WallBooster self, Vector2 position, float height, bool left, bool notCoreMode) {
            orig(self, position, height, left, notCoreMode);
            if (self.GetType().FullName != FullName) {
                return;
            }
            
            // 移除 WallBooster 本身无用的 StaticMover 避免对 StaticMoverAction 造成干扰导致无法依附
            self.Remove(self.Get<StaticMover>());

            EntityId2 entityId = self.CreateEntityId2(position.ToString(), height.ToString(), left.ToString(), notCoreMode.ToString());
            if (entityId == default) {
                return;
            }
            self.SetEntityId2(entityId);

            if (IsLoadStart) {
                if (savedWallBoosters.ContainsKey(entityId)) {
                    WallBooster savedSpike = savedWallBoosters[entityId];
                    var platform = savedSpike.Get<StaticMover>()?.Platform;
                    if (platform is CassetteBlock) {
                        return;
                    }

                    if (platform is FloatySpaceBlock) {
                        self.Add(new RestorePositionComponent(self, savedSpike));
                    }
                    else {
                        self.Position = savedSpike.Position;
                    }
                    self.Collidable = savedSpike.Collidable;
                    self.Visible = savedSpike.Visible;
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedWallBoosters.Clear();
        }

        public override void OnLoad() {
            On.Celeste.WallBooster.ctor_Vector2_float_bool_bool += WallBoosterOnCtor_Vector2_float_bool_bool;
        }


        public override void OnUnload() {
            On.Celeste.WallBooster.ctor_Vector2_float_bool_bool -= WallBoosterOnCtor_Vector2_float_bool_bool;
        }
    }
}