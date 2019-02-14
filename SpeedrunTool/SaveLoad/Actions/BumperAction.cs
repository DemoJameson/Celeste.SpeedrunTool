using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class BumperAction : AbstractEntityAction
    {
        private Dictionary<EntityID, Bumper> _savedBumpers = new Dictionary<EntityID, Bumper>();

        public override void OnQuickSave(Level level)
        {
            _savedBumpers = level.Tracker.GetDictionary<Bumper>();
        }

        private void RestoreBumperPosition(On.Celeste.Bumper.orig_ctor_EntityData_Vector2 orig,
            Bumper self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedBumpers.ContainsKey(entityId))
            {
                Bumper savedBumper = _savedBumpers[entityId];
                

                SineWave sineWave = self.Get<SineWave>();
                SineWave savedSineWave = savedBumper.Get<SineWave>();
                sineWave.SetPrivateProperty("Counter", savedSineWave.Counter);
                

                Tween savedTween = savedBumper.Get<Tween>();
                if (savedTween != null)
                {
                    self.CopyPrivateField("goBack", savedBumper);
                    self.CopyPrivateField("anchor", savedBumper);
                    self.Get<Tween>().CopyFrom(savedTween);
                }
            }
        }

        public override void OnClear()
        {
            _savedBumpers.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.Bumper.ctor_EntityData_Vector2 += RestoreBumperPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.Bumper.ctor_EntityData_Vector2 -= RestoreBumperPosition;
        }

        public override void OnInit()
        {
            typeof(Bumper).AddToTracker();
        }
    }
}