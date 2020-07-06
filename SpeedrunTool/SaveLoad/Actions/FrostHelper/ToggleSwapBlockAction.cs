using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.FrostHelper {
    public class ToggleSwapBlockAction : AbstractEntityAction {
        private const string FullName = "FrostHelper.ToggleSwapBlock";
        private Dictionary<EntityId2, Solid> savedSolids = new Dictionary<EntityId2, Solid>();

        public override void OnQuickSave(Level level) {
            savedSolids = level.Entities.FindAll<Solid>()
                .Where(entity => entity.GetType().FullName == FullName).GetDictionary();
        }

        private void SolidOnCtor(On.Celeste.Solid.orig_ctor orig, Solid self, Vector2 position, float width,
            float height, bool safe) {
            orig(self, position, width, height, safe);
            self.TrySetEntityId2(position.ToString(), width.ToString(), height.ToString(), safe.ToString());

            if (self.GetType().FullName == FullName) {
                EntityId2 entityId = self.GetEntityId2();
                if (IsLoadStart && savedSolids.ContainsKey(entityId)) {
                    Solid savedSolid = savedSolids[entityId];
                    Vector2 start = (Vector2) savedSolid.GetField("start");
                    if (Vector2.Distance(savedSolid.Position, start) > 0.1) {
                        self.Add(new Coroutine(RestoreState(self, savedSolid)));
                    }
                }
            }
        }

        private static IEnumerator RestoreState(Solid self, Solid saved) {
            self.Position = saved.Position;
            self.CopyFields(saved, "lerp");
            self.CopyFields(saved, "target");
            self.CopyFields(saved, "returnTimer");
            self.CopyFields(saved, "speed");
            self.CopyFields(saved, "Swapping");
            // 重新使刺依附上去
            self.Awake(self.Scene);
            yield break;
        }

        public override void OnClear() {
            savedSolids.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Solid.ctor += SolidOnCtor;
        }

        public override void OnUnload() {
            On.Celeste.Solid.ctor -= SolidOnCtor;
        }
    }
}