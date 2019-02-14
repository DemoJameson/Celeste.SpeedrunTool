using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class FlyFeatherAction : AbstractEntityAction
    {
        private Dictionary<EntityID, FlyFeather> _savedFlyFeathers = new Dictionary<EntityID, FlyFeather>();

        public override void OnQuickSave(Level level)
        {
            _savedFlyFeathers = level.Tracker.GetDictionary<FlyFeather>();
        }

        private void RestoreFlyFeatherState(On.Celeste.FlyFeather.orig_ctor_EntityData_Vector2 orig,
            FlyFeather self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            RestoreState(self, entityId);
        }

        private void RestoreFlyFeatherState(On.Celeste.FlyFeather.orig_ctor_Vector2_bool_bool orig, FlyFeather self,
            Vector2 position, bool shielded, bool singleUse)
        {
            orig(self, position, shielded, singleUse);
            if (self.GetEntityId().Equals(default(EntityID)))
            {
                EntityID entityId = new EntityID((Engine.Scene as Level).Session.Level,
                    position.GetHashCode() + shielded.GetHashCode() + singleUse.GetHashCode());
                self.SetEntityId(entityId);

                RestoreState(self, entityId);
            }
        }

        private void RestoreState(FlyFeather self, EntityID entityId)
        {
            if (IsLoadStart && _savedFlyFeathers.ContainsKey(entityId))
            {
                FlyFeather savedFlyFeather = _savedFlyFeathers[entityId];
                if (!savedFlyFeather.Collidable)
                {
                    self.Collidable = false;
                    float respawnTimer = (float) savedFlyFeather.GetPrivateField("respawnTimer") + 0.4f;
                    (self.GetPrivateField("sprite") as Sprite).Visible = false;

                    if ((bool) self.GetPrivateField("singleUse"))
                        return;
                    
                    self.SetPrivateField("respawnTimer", respawnTimer);
                    (self.GetPrivateField("outline") as Image).Visible = true;
                }
            }
        }

        public override void OnClear()
        {
            _savedFlyFeathers.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.FlyFeather.ctor_EntityData_Vector2 += RestoreFlyFeatherState;
            On.Celeste.FlyFeather.ctor_Vector2_bool_bool += RestoreFlyFeatherState;
        }

        public override void OnUnload()
        {
            On.Celeste.FlyFeather.ctor_EntityData_Vector2 -= RestoreFlyFeatherState;
        }

        public override void OnInit()
        {
            typeof(FlyFeather).AddToTracker();
        }
    }
}