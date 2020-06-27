using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class CrumblePlatformAction : AbstractEntityAction {
        private const string IsFade = "IsFade";

        private Dictionary<EntityID, CrumblePlatform> savedCrumblePlatforms =
            new Dictionary<EntityID, CrumblePlatform>();

		private ILHook addedHook;


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

		private void BlockCoroutineStart(ILContext il) {
			ILCursor c = new ILCursor(il);
			for (int i = 0; i < 6; i++)
				c.GotoNext((inst) => inst.MatchCall(typeof(Entity).GetMethod("Add", new Type[] { typeof(Monocle.Component) })));
			Instruction skipCoroutine = c.Next.Next;
			c.GotoPrev((i) => i.MatchCall(typeof(Entity), "get_Width"));
			c.GotoNext();
			c.GotoNext();
			c.EmitDelegate<Func<bool>>(() => IsLoadStart);
			c.Emit(OpCodes.Brtrue, skipCoroutine);
		}

		public override void OnClear() {
            savedCrumblePlatforms.Clear();
        }

        public override void OnLoad() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 += RestoreCrumblePlatformPosition;
            On.Celeste.Solid.GetPlayerOnTop += SolidOnGetPlayerOnTop;
			addedHook = new ILHook(typeof(CrumblePlatform).GetMethod("orig_Added"), BlockCoroutineStart);
        }

        public override void OnUnload() {
            On.Celeste.CrumblePlatform.ctor_EntityData_Vector2 -= RestoreCrumblePlatformPosition;
            On.Celeste.Solid.GetPlayerOnTop -= SolidOnGetPlayerOnTop;
			addedHook.Dispose();
		}
    }
}