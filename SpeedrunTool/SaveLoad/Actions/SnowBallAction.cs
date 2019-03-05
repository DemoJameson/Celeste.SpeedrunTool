using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SnowballAction : AbstractEntityAction {
        private Snowball savedSnowball;

        public override void OnQuickSave(Level level) {
            savedSnowball = level.Entities.FindFirst<Snowball>();
        }

        public override void OnQuickLoadStart(Level level) {
            if (savedSnowball != null) {
                Snowball snowball = new Snowball();
                On.Celeste.Snowball.Added += SnowballOnAdded;
                level.Add(snowball);
            }
        }

        private void SnowballOnAdded(On.Celeste.Snowball.orig_Added orig, Snowball self, Scene scene) {
            On.Celeste.Snowball.Added -= SnowballOnAdded;

            orig(self, scene);

            if (savedSnowball == null) {
                return;
            }

            self.Position = savedSnowball.Position;
            self.Collidable = self.Visible = savedSnowball.Collidable;
            self.CopyPrivateField("atY", savedSnowball);
            self.CopyPrivateField("resetTimer", savedSnowball);
            SineWave sine = self.Get<SineWave>();
            SineWave savedSine = savedSnowball.Get<SineWave>();
            sine.SetPrivateProperty("Counter", savedSine.Counter);
        }

        private void WindAttackTriggerOnOnEnter(On.Celeste.WindAttackTrigger.orig_OnEnter orig, WindAttackTrigger self,
            Player player) {
            if (IsFrozen && savedSnowball != null) {
                return;
            }

            orig(self, player);
        }

        public override void OnClear() {
            savedSnowball = null;
        }

        public override void OnLoad() {
            On.Celeste.WindAttackTrigger.OnEnter += WindAttackTriggerOnOnEnter;
        }

        public override void OnUnload() {
            On.Celeste.WindAttackTrigger.OnEnter -= WindAttackTriggerOnOnEnter;
        }

        public override void OnInit() { }
    }
}