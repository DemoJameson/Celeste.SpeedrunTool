using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TempleMirrorPortalAction : AbstractEntityAction {
        private Dictionary<EntityId2, TempleMirrorPortal> savedTempleMirrorPortal = new Dictionary<EntityId2, TempleMirrorPortal>();

        public override void OnQuickSave(Level level) {
            savedTempleMirrorPortal = level.Entities.FindAllToDict<TempleMirrorPortal>();
        }

        private void TempleMirrorPortalOnCtorEntityDataVector2(On.Celeste.TempleMirrorPortal.orig_ctor_EntityData_Vector2 orig, TempleMirrorPortal self, EntityData data, Vector2 offset) {
                EntityId2 entityId = data.ToEntityId2(self.GetType());
                self.SetEntityId2(entityId);
                orig(self, data, offset);

                if (IsLoadStart) {
                    if (savedTempleMirrorPortal.ContainsKey(entityId)) {
                        self.Add(new Coroutine(SetTorch(self, savedTempleMirrorPortal[entityId])));
                    }
                }
        }

        private static IEnumerator SetTorch(TempleMirrorPortal self, TempleMirrorPortal saved) {
            if ((int) saved.GetField(typeof(TempleMirrorPortal), "switchCounter") > 0) {
                if (saved.GetField(typeof(TempleMirrorPortal), "leftTorch").GetField("light") != null) {
                    self.OnSwitchHit(-1);
                }
                
                if (saved.GetField(typeof(TempleMirrorPortal), "rightTorch").GetField("light") != null) {
                    self.OnSwitchHit(1);
                }
            }
            
            yield break;
        }
        
        public override void OnClear() {
            savedTempleMirrorPortal.Clear();
        }

        public override void OnLoad() {
            On.Celeste.TempleMirrorPortal.ctor_EntityData_Vector2 += TempleMirrorPortalOnCtorEntityDataVector2;
        }


        public override void OnUnload() {
            On.Celeste.TempleMirrorPortal.ctor_EntityData_Vector2 -= TempleMirrorPortalOnCtorEntityDataVector2;
        }
    }
}