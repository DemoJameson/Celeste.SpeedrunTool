using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class BoosterAction : AbstractEntityAction
    {
        private Dictionary<EntityID, Booster> _savedBoosters = new Dictionary<EntityID, Booster>();

        public override void OnQuickSave(Level level)
        {
            _savedBoosters = level.Tracker.GetDictionary<Booster>();
        }

        private void RestoreBoosterPosition(On.Celeste.Booster.orig_ctor_EntityData_Vector2 orig,
            Booster self, EntityData data,
            Vector2 offset)
        {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedBoosters.ContainsKey(entityId))
            {
                Booster savedBooster = _savedBoosters[entityId];

                if ((bool) savedBooster.GetPrivateField("boostingPlayer"))
                {
                    self.Add(new Coroutine(BoostPlayer(self, savedBooster)));
                }
                else if((float)savedBooster.GetPrivateField("respawnTimer")> 0f)
                {
                    self.Add(new Coroutine(WaitToRespawn(self, savedBooster)));
                }
            }
        }

        private IEnumerator BoostPlayer(Booster self, Booster savedBooster)
        {
            self.Center = StateManager.Instance.SavedPlayer.Center;

            while (!IsLoadComplete)
                yield return null;

            Player player = self.SceneAs<Level>().Tracker.GetEntity<Player>();
            self.InvokePrivateMethod("OnPlayer", player);
            self.Center = savedBooster.Center;
        }

        private IEnumerator WaitToRespawn(Booster self, Booster savedBooster)
        {
            self.CopyPrivateField("respawnTimer", savedBooster);
            Sprite sprite = self.GetPrivateField("sprite") as Sprite;
            sprite.Visible = false;
            Entity outline = self.GetPrivateField("outline") as Entity;
            outline.Visible = true;
            yield break;
        }

        private void BoosterOnOnPlayer(On.Celeste.Booster.orig_OnPlayer orig, Booster self, Player player)
        {
            if (self.SceneAs<Level>().Frozen)
                return;

            orig(self, player);
        }

        public override void OnClear()
        {
            _savedBoosters.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.Booster.ctor_EntityData_Vector2 += RestoreBoosterPosition;
            On.Celeste.Booster.OnPlayer += BoosterOnOnPlayer;
        }

        public override void OnUnload()
        {
            On.Celeste.Booster.ctor_EntityData_Vector2 -= RestoreBoosterPosition;
        }

        public override void OnInit()
        {
            typeof(Booster).AddToTracker();
        }
    }
}