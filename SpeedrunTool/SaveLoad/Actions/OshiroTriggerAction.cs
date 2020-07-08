using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class OshiroTriggerAction : AbstractEntityAction {
        private Dictionary<EntityId2, OshiroTrigger> savedOshiroTriggers = new Dictionary<EntityId2, OshiroTrigger>();

        public override void OnSaveSate(Level level) {
            savedOshiroTriggers = level.Entities.FindAllToDict<OshiroTrigger>();
        }

        private void RestoreOshiroTrigger(On.Celeste.OshiroTrigger.orig_ctor orig, OshiroTrigger self,
            EntityData data,
            Vector2 offset) {
            EntityId2 entityId = data.ToEntityId2(self.GetType());
            self.SetEntityId2(entityId);
            orig(self, data, offset);

            if (IsLoadStart && !savedOshiroTriggers.ContainsKey(entityId)) {
                self.Add(new Coroutine(OnEnter(self)));
            }
        }

        private IEnumerator OnEnter(OshiroTrigger self) {
            Player player = self.SceneAs<Level>().GetPlayer();
            if (player != null) {
                self.OnEnter(player);
            }
            yield break;
        }

        public override void OnClear() {
            savedOshiroTriggers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.OshiroTrigger.ctor += RestoreOshiroTrigger;
        }

        public override void OnUnload() {
            On.Celeste.OshiroTrigger.ctor -= RestoreOshiroTrigger;
        }
    }
}