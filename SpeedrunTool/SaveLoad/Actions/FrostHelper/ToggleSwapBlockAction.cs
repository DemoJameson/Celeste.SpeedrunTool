using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.FrostHelper {
    public class ToggleSwapBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, Solid> savedSolids = new Dictionary<EntityID, Solid>();

        public override void OnQuickSave(Level level) {
            savedSolids = level.Entities.FindAll<Solid>()
                .Where(entity => entity.GetType().FullName == "FrostHelper.ToggleSwapBlock").GetDictionary();
        }

        private void SolidOnCtor(On.Celeste.Solid.orig_ctor orig, Solid self, Vector2 position, float width,
            float height, bool safe) {
            orig(self, position, width, height, safe);
            self.SetEntityId(position.GetRealHashCode() + width.GetHashCode() + height.GetHashCode() + safe.GetHashCode());

            if (self.GetType().FullName == "FrostHelper.ToggleSwapBlock") {
                EntityID entityId = self.GetEntityId();
                if (IsLoadStart && savedSolids.ContainsKey(entityId)) {
                    Solid savedSolid = savedSolids[entityId];
                    Vector2 start = (Vector2) savedSolid.GetField("start");
                    if (Vector2.Distance(savedSolid.Position, start) > 0.5) {
                        self.Add(new Coroutine(RestoreState(self, savedSolid)));
                    }
                }
            }
        }

        private static IEnumerator RestoreState(Solid self, Solid saved) {
            self.Position = saved.Position;
            self.CopyField("lerp", saved);
            self.CopyField("target", saved);
            self.CopyField("returnTimer", saved);
            self.CopyField("speed", saved);
            self.CopyField("Swapping", saved);
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