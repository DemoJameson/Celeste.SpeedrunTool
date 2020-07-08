using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SnowballAction : AbstractEntityAction {
        private bool snowballOnAdded;
        private Snowball savedSnowball;

        public override void OnSaveSate(Level level) {
            savedSnowball = level.Entities.FindFirst<Snowball>();
        }

        public override void OnLoadStart(Level level, Player player, Player savedPlayer) {
            if (savedSnowball != null) {
                Snowball snowball = new Snowball();
                snowballOnAdded = true;
                level.Add(snowball);
            }
        }

        private void SnowballOnAdded(On.Celeste.Snowball.orig_Added orig, Snowball self, Scene scene) {
            orig(self, scene);

            if (!snowballOnAdded || savedSnowball == null) {
                return;
            }

            snowballOnAdded = false;

            self.Position = savedSnowball.Position;
            self.Collidable = self.Visible = savedSnowball.Collidable;
            self.CopyFields(typeof(Snowball), savedSnowball, "atY");
            self.CopyFields(typeof(Snowball), savedSnowball, "resetTimer");
            SineWave sine = self.Get<SineWave>();
            SineWave savedSine = savedSnowball.Get<SineWave>();
            sine.Counter = savedSine.Counter;
        }

        private void WindAttackTriggerOnOnEnter(On.Celeste.WindAttackTrigger.orig_OnEnter orig, WindAttackTrigger self,
            Player player) {
            if (IsLoadStart && savedSnowball != null) {
                return;
            }

            orig(self, player);
        }

        public override void OnClear() {
            snowballOnAdded = false;
            savedSnowball = null;
        }

        public override void OnLoad() {
            On.Celeste.WindAttackTrigger.OnEnter += WindAttackTriggerOnOnEnter;
            On.Celeste.Snowball.Added += SnowballOnAdded;
        }

        public override void OnUnload() {
            On.Celeste.WindAttackTrigger.OnEnter -= WindAttackTriggerOnOnEnter;
            On.Celeste.Snowball.Added -= SnowballOnAdded;
        }
    }
}