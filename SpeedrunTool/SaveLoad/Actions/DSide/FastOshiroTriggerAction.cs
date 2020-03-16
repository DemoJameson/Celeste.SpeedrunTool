using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions.DSide {
    public class FastOshiroTriggerAction : AbstractEntityAction {
        private static string TypeFullName = "Celeste.Mod.RubysEntities.FastOshiroTrigger";
        private Dictionary<EntityID, Trigger> savedTriggers = new Dictionary<EntityID, Trigger>();

        public override void OnQuickSave(Level level) {
            savedTriggers = level.Entities.FindAll<Trigger>()
                .Where(entity => entity.GetType().FullName == TypeFullName).GetDictionary();
        }

        private void TriggerOnCtor(On.Celeste.Trigger.orig_ctor orig, Trigger self, EntityData data, Vector2 offset) {
            orig(self, data, offset);
            
            if (self.GetType().FullName != TypeFullName) {
                return;
            }

            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            if (IsLoadStart & !savedTriggers.ContainsKey(entityId)) {
                self.Add(new Coroutine(OnEnter(self)));               
            }
        }
        
        private static IEnumerator OnEnter(Trigger self) {
            Player player = self.SceneAs<Level>().GetPlayer();
            if (player != null) {
                self.OnEnter(player);
            }
            yield break;
        }

        public override void OnClear() {
            savedTriggers.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Trigger.ctor += TriggerOnCtor;
        }

        public override void OnUnload() {
            On.Celeste.Trigger.ctor -= TriggerOnCtor;
        }
    }
}