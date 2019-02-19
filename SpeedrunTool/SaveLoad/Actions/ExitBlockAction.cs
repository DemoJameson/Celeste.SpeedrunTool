using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class ExitBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, ExitBlock> _savedExitBlocks = new Dictionary<EntityID, ExitBlock>();

        public override void OnQuickSave(Level level) {
            _savedExitBlocks = level.Tracker.GetDictionary<ExitBlock>();
        }

        private void PreventExitBlockLockPlayer(On.Celeste.ExitBlock.orig_ctor_EntityData_Vector2 orig,
            ExitBlock self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && _savedExitBlocks.ContainsKey(entityId)) {
                ExitBlock savedExitBlock = _savedExitBlocks[entityId];
                if (savedExitBlock.Collidable == false) {
                    self.Collidable = false;
                    self.Add(new Coroutine(SetState(self)));
                }
            }
        }

        private IEnumerator SetState(ExitBlock self) {
            (self.GetPrivateField("tiles") as TileGrid).Alpha = 0f;
            self.Get<EffectCutout>().Alpha = 0f;
            yield break;
        }

        private void OnExitBlockOnUpdate(On.Celeste.ExitBlock.orig_Update orig, ExitBlock self) {
            if (IsLoadStart)
                return;

            orig(self);
        }

        public override void OnClear() {
            _savedExitBlocks.Clear();
        }

        public override void OnLoad() {
            On.Celeste.ExitBlock.ctor_EntityData_Vector2 += PreventExitBlockLockPlayer;
            On.Celeste.ExitBlock.Update += OnExitBlockOnUpdate;
        }

        public override void OnUnload() {
            On.Celeste.ExitBlock.ctor_EntityData_Vector2 -= PreventExitBlockLockPlayer;
            On.Celeste.ExitBlock.Update -= OnExitBlockOnUpdate;
        }

        public override void OnInit() {
            typeof(ExitBlock).AddToTracker();
        }
    }
}