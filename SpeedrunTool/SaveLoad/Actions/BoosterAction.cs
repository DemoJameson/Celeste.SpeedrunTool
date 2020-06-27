using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BoosterAction : AbstractEntityAction {
        private Dictionary<EntityID, Booster> savedBoosters = new Dictionary<EntityID, Booster>();
        private readonly Vector2 hidden = new Vector2(-999999);

        public override void OnQuickSave(Level level) {
            savedBoosters = level.Entities.GetDictionary<Booster>();
        }

        public override void OnQuickLoadStart(Level level) {
            Player savedPlayer = StateManager.Instance.SavedPlayer;
            Booster lastBooster = savedPlayer.LastBooster;

            if (savedPlayer.StateMachine.State != Player.StRedDash || lastBooster == null) {
                return;
            }

            if (lastBooster.GetEntityId().Level == level.Session.Level) {
                return;
            }

            Booster booster = new Booster(savedPlayer.Position - Vector2.UnitY * 6, true);
            booster.SetEntityId(lastBooster.GetEntityId());
            booster.Add(new Coroutine(BoostPlayerNewRoom(booster)));
            level.Add(booster);
        }

        private IEnumerator BoostPlayerNewRoom(Booster booster) {
            Player player = booster.SceneAs<Level>().Entities.FindFirst<Player>();
            if (player == null) {
                yield break;
            }

            while (player.StateMachine.State != Player.StRedDash) {
                yield return null;
            }

            yield return null;

            Entity outline = booster.GetField(typeof(Booster), "outline") as Entity;
            outline?.RemoveSelf();
            booster.Position = hidden;
        }

        private void RestoreBoosterPosition(On.Celeste.Booster.orig_ctor_EntityData_Vector2 orig,
            Booster self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedBoosters.ContainsKey(entityId)) {
                Booster savedBooster = savedBoosters[entityId];

                if (self.CollideCheck(StateManager.Instance.SavedPlayer)) {
                    self.Collidable = false;
                    self.Add(new Coroutine(WaitPlayerRespawn(self)));
                }

                if (savedBooster.BoostingPlayer) {
                    if (data.Bool("red")) {
                        self.Add(new Coroutine(BoostPlayer(self, savedBooster)));
                    } else {
                        self.SetField(typeof(Booster), "respawnTimer", 1f);
                        self.Add(new Coroutine(WaitToRespawn(self, savedBooster)));
                    }
                } else if ((float) savedBooster.GetField(typeof(Booster), "respawnTimer") > 0f) {
                    self.Add(new Coroutine(WaitToRespawn(self, savedBooster)));
                }
            }
        }

        private static IEnumerator WaitPlayerRespawn(Booster self) {
            while (!IsLoadComplete) {
                yield return null;
            }

            self.Collidable = true;
        }

        private IEnumerator BoostPlayer(Booster self, Booster savedBooster) {
            Vector2 origCenter = self.Center;
            self.Center = StateManager.Instance.SavedPlayer.Center;
            self.Collidable = false;

            while (!IsLoadComplete) {
                yield return null;
            }

            self.Collidable = true;

            Player player = self.SceneAs<Level>().Entities.FindFirst<Player>();
            if (player == null) {
                yield break;
            }

            player.Collidable = true;

            while (player.StateMachine.State != Player.StRedDash) {
                yield return null;
            }

            while (player.StateMachine.State == Player.StRedDash) {
                yield return null;
            }

            yield return 0.7f;
            self.Center = origCenter;
        }

        private IEnumerator WaitToRespawn(Booster self, Booster savedBooster) {
            self.CopyFields(typeof(Booster), savedBooster, "respawnTimer");
            Sprite sprite = self.GetField(typeof(Booster), "sprite") as Sprite;
            sprite.Visible = false;
            Entity outline = self.GetField(typeof(Booster), "outline") as Entity;
            outline.Visible = true;
            if ((bool) self.GetField(typeof(Booster), "red") == false) {
                AudioAction.MuteAudioPathVector2("event:/game/04_cliffside/greenbooster_end");
                self.PlayerReleased();
            }
            yield break;
        }

        private void BoosterOnOnPlayer(On.Celeste.Booster.orig_OnPlayer orig, Booster self, Player player) {
            if (IsLoadStart || self.SceneAs<Level>().Frozen) {
                return;
            }

            orig(self, player);
        }

        public override void OnClear() {
            savedBoosters.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Booster.ctor_EntityData_Vector2 += RestoreBoosterPosition;
            On.Celeste.Booster.OnPlayer += BoosterOnOnPlayer;
        }

        public override void OnUnload() {
            On.Celeste.Booster.ctor_EntityData_Vector2 -= RestoreBoosterPosition;
            On.Celeste.Booster.OnPlayer -= BoosterOnOnPlayer;
        }
    }
}