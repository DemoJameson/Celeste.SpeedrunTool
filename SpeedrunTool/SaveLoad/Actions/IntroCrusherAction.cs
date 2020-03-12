using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class IntroCrusherAction : AbstractEntityAction {
        private Dictionary<EntityID, IntroCrusher> savedIntroCrushers = new Dictionary<EntityID, IntroCrusher>();

        public override void OnQuickSave(Level level) {
            savedIntroCrushers = level.Entities.GetDictionary<IntroCrusher>();
        }

        private void RestoreIntroCrusherPosition(On.Celeste.IntroCrusher.orig_ctor_EntityData_Vector2 orig,
            IntroCrusher self, EntityData data, Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedIntroCrushers.ContainsKey(entityId)) {
                IntroCrusher savedIntroCrusher = savedIntroCrushers[entityId];
                
                self.SetField(typeof(IntroCrusher), "start", savedIntroCrusher.Position);
                if (savedIntroCrusher.Position == (Vector2) savedIntroCrusher.GetField(typeof(IntroCrusher), "end")) {
                    self.Position = savedIntroCrusher.Position;
                    self.Add(new FastForwardComponent<IntroCrusher>(savedIntroCrusher, RemoveShake));
                } else if (self.Position != savedIntroCrusher.Position) {
                    self.Add(new FastForwardComponent<IntroCrusher>(savedIntroCrusher, TriggerAndSkipShake));
                }
            }
        }

        private static void RemoveShake(IntroCrusher entity, IntroCrusher savedEntity) {
            Coroutine coroutine = entity.Get<Coroutine>();
            if (coroutine != null) {
                entity.Remove(coroutine);
            }
        }

        private static void TriggerAndSkipShake(IntroCrusher entity, IntroCrusher savedEntity) {
            Player player = entity.SceneAs<Level>().GetPlayer();
            if (player == null) {
                return;
            }

            Vector2 origPosition = player.Position;
            player.Position = entity.Position + Vector2.UnitX * 30;
            player.Collidable = false;
            entity.Update();
            entity.Update();
            player.Collidable = true;
            player.Position = origPosition;
            while (Vector2.Distance(entity.Position, savedEntity.Position) > 0.5) {
                entity.Update();
            }
        }

        public override void OnClear() {
            savedIntroCrushers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.IntroCrusher.ctor_EntityData_Vector2 += RestoreIntroCrusherPosition;
        }

        public override void OnUnload() {
            On.Celeste.IntroCrusher.ctor_EntityData_Vector2 -= RestoreIntroCrusherPosition;
        }
    }
}