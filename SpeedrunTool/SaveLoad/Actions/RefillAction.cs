using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class RefillAction : AbstractEntityAction
    {
        private Dictionary<EntityID, Refill> _savedRefills = new Dictionary<EntityID, Refill>();

        public override void OnQuickSave(Level level)
        {
            _savedRefills = level.Tracker.GetDictionary<Refill>();
        }

        private void RefillOnCtorEntityDataVector2(
            On.Celeste.Refill.orig_ctor_EntityData_Vector2 orig, Refill self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart)
            {
                if (_savedRefills.ContainsKey(entityId))
                {
                    Refill savedRefill = _savedRefills[entityId];
                    if (!savedRefill.Collidable)
                    {
                        self.Collidable = false;
                        float respawnTimer = (float) savedRefill.GetPrivateField("respawnTimer") + 0.35f;
                        self.SetPrivateField("respawnTimer", respawnTimer);
                        self.Add(new Coroutine(ConsumeRefill(self)));
                    }
                }
                else
                    self.Add(new RemoveSelfComponent());
            }
        }

        private static IEnumerator ConsumeRefill(Refill self)
        {
            Player player = self.Scene.Tracker.GetEntity<Player>();
            self.Add(new Coroutine((IEnumerator) self.GetPrivateMethod("RefillRoutine")
                .Invoke(self, new object[] {player})));
            yield return null;
        }

        public override void OnClear()
        {
            _savedRefills.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.Refill.ctor_EntityData_Vector2 += RefillOnCtorEntityDataVector2;
        }


        public override void OnUnload()
        {
            On.Celeste.Refill.ctor_EntityData_Vector2 -= RefillOnCtorEntityDataVector2;
        }

        public override void OnInit()
        {
            typeof(Refill).AddToTracker();
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level)
        {
            level.UpdateEntities<Refill>();
        }
    }
}