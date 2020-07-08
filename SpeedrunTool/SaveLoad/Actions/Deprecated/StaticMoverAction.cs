using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.Deprecated {
    public class StaticMoverAction : ComponentAction {
        private const string CheckStaticMover = "CheckStaticMover";
        private readonly Dictionary<EntityId2, StaticMover> savedStaticMovers = new Dictionary<EntityId2, StaticMover>();

        public override void OnSaveSate(Level level) {
            var staticMovers = level.Tracker.GetComponents<StaticMover>();
            foreach (StaticMover staticMover in staticMovers) {
                var entityId = staticMover.Entity.GetEntityId2();
                if (staticMover.Entity != null && entityId != default && !savedStaticMovers.ContainsKey(entityId)) {
                    savedStaticMovers.Add(staticMover.Entity.GetEntityId2(), staticMover);
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
            EntityId2 entityId = staticMover.Entity.GetEntityId2();
            EntityId2 platformEntityId = platform.GetEntityId2();
            if (entityId == default || platformEntityId == default) {
                return true;
            }

            if (savedStaticMovers.ContainsKey(entityId)) {
                var savedStaticMover = savedStaticMovers[entityId];
                // 之前依附的 Platform 与本次查找的 Platform 非同一个则不依附
                if (savedStaticMover.Platform == null || !savedStaticMover.Platform.GetEntityId2().Equals(platformEntityId)) {
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