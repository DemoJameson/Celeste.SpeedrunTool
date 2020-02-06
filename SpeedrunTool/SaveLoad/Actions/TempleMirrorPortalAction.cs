using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class TempleMirrorPortalAction : AbstractEntityAction {
        private Dictionary<EntityID, TempleMirrorPortal> savedTempleMirrorPortal = new Dictionary<EntityID, TempleMirrorPortal>();

        public override void OnQuickSave(Level level) {
            savedTempleMirrorPortal = level.Entities.GetDictionary<TempleMirrorPortal>();
        }

        private void TempleMirrorPortalOnCtorEntityDataVector2(On.Celeste.TempleMirrorPortal.orig_ctor_EntityData_Vector2 orig, TempleMirrorPortal self, EntityData data, Vector2 offset) {
                EntityID entityId = data.ToEntityId();
                self.SetEntityId(entityId);
                orig(self, data, offset);

                if (IsLoadStart) {
                    if (savedTempleMirrorPortal.ContainsKey(entityId)) {
                        self.Add(new Coroutine(SetTorch(self, savedTempleMirrorPortal[entityId])));
                    }
                }
        }

        private static IEnumerator SetTorch(TempleMirrorPortal self, TempleMirrorPortal saved) {
            if ((int) saved.GetField("switchCounter") > 0) {
                if (saved.GetField("leftTorch").GetField("light") != null) {
                    self.OnSwitchHit(-1);
                }
                
                if (saved.GetField("rightTorch").GetField("light") != null) {
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