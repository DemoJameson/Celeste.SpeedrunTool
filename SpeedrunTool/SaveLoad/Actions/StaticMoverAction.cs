using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class StaticMoverAction : AbstractEntityAction {
        private const string CheckStaticMover = "CheckStaticMover";
        private readonly Dictionary<EntityID, StaticMover> savedStaticMovers = new Dictionary<EntityID, StaticMover>();

        public override void OnQuickSave(Level level) {
            var staticMovers = level.Tracker.GetComponents<StaticMover>();
            foreach (StaticMover staticMover in staticMovers) {
                var entityId = staticMover.Entity.GetEntityId();
                if (staticMover.Entity != null && !entityId.IsDefault() && !savedStaticMovers.ContainsKey(entityId)) {
                    savedStaticMovers.Add(staticMover.Entity.GetEntityId(), staticMover);
                }
            }
        }

        private bool StaticMoverOnIsRiding_Solid(On.Celeste.StaticMover.orig_IsRiding_Solid orig, StaticMover self, Solid solid) {
            bool result = orig(self, solid);
            if (solid.GetExtendedDataValue<bool>(CheckStaticMover)) {
                result = result && StaticMoverOnIsRiding(self, solid);
            }

            return result;
        }
        
        private bool StaticMoverOnIsRiding_JumpThru(On.Celeste.StaticMover.orig_IsRiding_JumpThru orig, StaticMover self, JumpThru jumpthru) {
            bool result = orig(self, jumpthru);
            if (jumpthru.GetExtendedDataValue<bool>(CheckStaticMover)) {
                result = result && StaticMoverOnIsRiding(self, jumpthru);
            }

            return result;
        }

        private bool StaticMoverOnIsRiding(StaticMover staticMover, Platform platform) {
            EntityID entityId = staticMover.Entity.GetEntityId();
            EntityID platformEntityId = platform.GetEntityId();
            if (entityId.IsDefault() || platformEntityId.IsDefault()) {
                return true;
            }

            if (savedStaticMovers.ContainsKey(entityId)) {
                var savedStaticMover = savedStaticMovers[entityId];
                // 之前依附的 Platform 与本次查找的 Platform 非同一个则不依附
                if (savedStaticMover.Platform == null || !savedStaticMover.Platform.GetEntityId().Equals(platformEntityId)) {
                    return false;
                }
            }

            return true;
        }

        private void SolidOnAwake(On.Celeste.Solid.orig_Awake orig, Solid self, Scene scene) {
            self.SetExtendedDataValue(CheckStaticMover, IsLoadStart && self.AllowStaticMovers);
            orig(self, scene);
            self.SetExtendedDataValue(CheckStaticMover, false);
        }

        private void JumpThruOnAwake(On.Celeste.JumpThru.orig_Awake orig, JumpThru self, Scene scene) {
            self.SetExtendedDataValue(CheckStaticMover, IsLoadStart);
            orig(self, scene);
            self.SetExtendedDataValue(CheckStaticMover, false);
        }

        public override void OnClear() {
            savedStaticMovers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.StaticMover.IsRiding_Solid += StaticMoverOnIsRiding_Solid;
            On.Celeste.StaticMover.IsRiding_JumpThru += StaticMoverOnIsRiding_JumpThru;
            On.Celeste.Solid.Awake += SolidOnAwake;
            On.Celeste.JumpThru.Awake += JumpThruOnAwake;
        }

        public override void OnUnload() {
            On.Celeste.StaticMover.IsRiding_Solid -= StaticMoverOnIsRiding_Solid;
            On.Celeste.StaticMover.IsRiding_JumpThru -= StaticMoverOnIsRiding_JumpThru;
            On.Celeste.Solid.Awake -= SolidOnAwake;
            On.Celeste.JumpThru.Awake -= JumpThruOnAwake;
        }
    }
}