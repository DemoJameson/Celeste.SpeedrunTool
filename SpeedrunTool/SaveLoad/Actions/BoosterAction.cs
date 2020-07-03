using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BoosterAction : AbstractEntityAction {
        private Dictionary<EntityID, Booster> savedBoosters = new Dictionary<EntityID, Booster>();

        public override void OnQuickSave(Level level) {
            savedBoosters = level.Entities.GetDictionary<Booster>();
        }

        public override void OnQuickLoadStart(Level level, Player player, Player savedPlayer) {
            Booster lastBooster = savedPlayer.LastBooster;

            if (savedPlayer.StateMachine.State != Player.StRedDash || lastBooster == null) {
                return;
            }

            if (lastBooster.GetEntityId().Level == level.Session.Level) {
                return;
            }

            Booster booster = new Booster(lastBooster.GetEntityData(), Vector2.Zero);
            level.Add(booster);
        }

        private void RestoreBoosterPosition(On.Celeste.Booster.orig_ctor_EntityData_Vector2 orig,
            Booster self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            self.SetEntityData(data);
            orig(self, data, offset);

            if (IsLoadStart && savedBoosters.ContainsKey(entityId)) {
                Booster savedBooster = savedBoosters[entityId];

                self.CopyFrom(savedBooster);
                self.CopySprite(savedBooster, "sprite");
                self.Ch9HubTransition = savedBooster.Ch9HubTransition;
                self.SetProperty("BoostingPlayer", savedBooster.BoostingPlayer);
                self.CopyFields(savedBooster, "respawnTimer",
                    "cannotUseTimer"
                );
                
                self.Add(new Coroutine(RestoreOutline(self, savedBooster)));
            }
        }

        private IEnumerator RestoreOutline(Booster self, Booster booster) {
            var outline = self.GetField("outline") as Entity;
            var savedOutline = booster.GetField("outline") as Entity;
            outline.CopyFrom(savedOutline);
            yield break;
        }

        public override void OnClear() {
            savedBoosters.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Booster.ctor_EntityData_Vector2 += RestoreBoosterPosition;
        }

        public override void OnUnload() {
            On.Celeste.Booster.ctor_EntityData_Vector2 -= RestoreBoosterPosition;
        }
    }
}