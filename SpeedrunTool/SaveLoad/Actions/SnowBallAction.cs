using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class SnowballAction : AbstractEntityAction
    {
        private Snowball _savedSnowball;

        public override void OnQuickSave(Level level)
        {
            _savedSnowball = level.Entities.FindFirst<Snowball>();
        }

        public override void OnQuickLoadStart(Level level)
        {
            if (_savedSnowball != null)
            {
                Snowball snowball = new Snowball();
                On.Celeste.Snowball.Added += SnowballOnAdded;
                Engine.Scene.Add(snowball);
            }
        }

        private void SnowballOnAdded(On.Celeste.Snowball.orig_Added orig, Snowball self, Scene scene)
        {
            On.Celeste.Snowball.Added -= SnowballOnAdded;
            
            orig(self, scene);

            if (_savedSnowball == null) return;
            
            self.Position = _savedSnowball.Position;
            self.Collidable = self.Visible = _savedSnowball.Collidable;
            self.CopyPrivateField("atY", _savedSnowball);
            self.CopyPrivateField("resetTimer", _savedSnowball);
            SineWave sine = self.Get<SineWave>();
            SineWave savedSine = _savedSnowball.Get<SineWave>();
            sine.SetPrivateProperty("Counter", savedSine.Counter);
        }

        public override void OnClear()
        {
            _savedSnowball = null;
        }

        public override void OnLoad()
        {
        }

        public override void OnUnload()
        {
        }
    }
}