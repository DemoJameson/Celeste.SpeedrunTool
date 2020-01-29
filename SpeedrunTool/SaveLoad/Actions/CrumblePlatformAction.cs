using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrumblePlatformAction : AbstractEntityAction {
        private const string IsFade = "IsFade";

        private Dictionary<EntityID, CrumblePlatform> savedCrumblePlatforms =
            new Dictionary<EntityID, CrumblePlatform>();

        public override void OnQuickSave(Level level) {
            savedCrumblePlatforms = level.Entities.FindAll<CrumblePlatform>()
                .Where(platform => !platform.Collidable).ToDictionary(platform => platform.GetEntityId());
        }

        private void RestoreCrumblePlatformPosition(On.Celeste.CrumblePlatform.orig_ctor_EntityData_Vector2 orig,
            CrumblePlatform self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedCrumblePlatforms.ContainsKey(entityId)) {
                self.SetExtendedBoolean(IsFade, true);
                self.Add(new FastForwardComponent<CrumblePlatform>(savedCrumblePlatforms[entityId], OnFastForward));
            }
        }

        private void OnFastForward(CrumblePlatform entity, CrumblePlatform savedEntity) {
            int breakTimeFrames = savedEntity.GetExtendedDataValue<int>(nameof(breakTimeFrames));
            for (int i = 0; i < 36 + breakTimeFrames; i++) {
                entity.Update();
            }
        }

        private static Player SolidOnGetPlayerOnTop(On.Celeste.Solid.orig_GetPlayerOnTop orig, Solid self) {
            if (self is CrumblePlatform && self.GetExtendedBoolean(IsFade)) {
                self.SetExtendedBoolean(IsFade, false);

                AudioAction.MuteAudioPathVector2("event:/game/general/platform_disintegrate");
                return self.Scene.Entities.FindFirst<Player>();
            }
            
            // 适用于所有 Solid
            if (IsLoadStart || IsLoading || IsFrozen) {
                return null;
            }

            return orig(self);
        }

        private IEnumerator CrumblePlatformOnSequence(On.Celeste.CrumblePlatform.orig_Sequence orig, CrumblePlatform self) {
            IEnumerator enumerator = orig(self);
            while (enumerator.MoveNext()) {
                object result = enumerator.Current;
                if (result is float restoreTime && Math.Abs(restoreTime - 2f) < 0.01) {
                    restoreTime += 0.016f;
                    int breakTimeFrames = 0;
                    while (restoreTime > 0f) {
                        restoreTime -= Engine.DeltaTime;
                        breakTimeFrames++;
                        self.SetExtendedDataValue(nameof(breakTimeFrames), breakTimeFrames);
                        yield return null;
                    }
                    continue;
                }

                yield return result;
            }
        }

        public override void OnClear() {
            savedCrumblePlatforms.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 += RestoreCrumblePlatformPosition;
            On.Celeste.Solid.GetPlayerOnTop += SolidOnGetPlayerOnTop;
            On.Celeste.CrumblePlatform.Sequence += CrumblePlatformOnSequence;
        }

        public override void OnUnload() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 -= RestoreCrumblePlatformPosition;
            On.Celeste.Solid.GetPlayerOnTop -= SolidOnGetPlayerOnTop;
            On.Celeste.CrumblePlatform.Sequence -= CrumblePlatformOnSequence;
        }
    }
}