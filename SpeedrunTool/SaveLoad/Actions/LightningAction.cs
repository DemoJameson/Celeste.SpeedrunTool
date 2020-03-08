using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class LightningAction : AbstractEntityAction {
        private const string BACK = "back";
        private const string END = "end";

        private Dictionary<EntityID, Lightning> savedLightnings = new Dictionary<EntityID, Lightning>();

        public override void OnQuickSave(Level level) {
            savedLightnings = level.Entities.GetDictionary<Lightning>();
        }

        private void RestoreLightningState(
            On.Celeste.Lightning.orig_ctor_EntityData_Vector2 orig, Lightning self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedLightnings.ContainsKey(entityId)) {
                    Lightning saved = savedLightnings[entityId];
                    self.Collidable = saved.Collidable;
                    self.Visible = saved.Visible;
                    self.Add(new FastForwardComponent<Lightning>(saved, OnFastForward));
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        private void OnFastForward(Lightning self, Lightning saved) {
            LightningRenderer lightningRenderer = self.Scene.Entities.FindFirst<LightningRenderer>();
            
            if (saved.GetExtendedDataValue<bool>(BACK)) {
                Vector2 end = saved.GetExtendedDataValue<Vector2>(END);
                while (self.Position != end) {
                    lightningRenderer?.Update();
                    self.Update();
                }
            }

            while (Vector2.Distance(self.Position, saved.Position) > 0.5) {
                lightningRenderer?.Update();
                self.Update();
            }
        }

        private static IEnumerator LightningOnMoveRoutine(On.Celeste.Lightning.orig_MoveRoutine orig, Lightning self, Vector2 start, Vector2 end, float moveTime) {
            self.SetExtendedDataValue(END, end);

            IEnumerator enumerator = orig(self, start, end, moveTime);
            while (enumerator.MoveNext()) {
                yield return enumerator.Current;
                self.SetExtendedDataValue(BACK, !self.GetExtendedDataValue<bool>(BACK));
            }
        }

        public override void OnClear() {
            savedLightnings.Clear();
        }

        public override void OnLoad() {
            On.Celeste.Lightning.ctor_EntityData_Vector2 += RestoreLightningState;
            On.Celeste.Lightning.MoveRoutine += LightningOnMoveRoutine;
        }

        public override void OnUnload() {
            On.Celeste.Lightning.ctor_EntityData_Vector2 -= RestoreLightningState;
            On.Celeste.Lightning.MoveRoutine -= LightningOnMoveRoutine;
        }
    }
}