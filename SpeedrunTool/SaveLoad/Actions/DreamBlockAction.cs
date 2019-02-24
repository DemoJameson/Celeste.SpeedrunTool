using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class DreamBlockAction : AbstractEntityAction {
        private Dictionary<EntityID, DreamBlock> savedDreamBlocks = new Dictionary<EntityID, DreamBlock>();

        public override void OnQuickSave(Level level) {
            savedDreamBlocks = level.Tracker.GetDictionary<DreamBlock>();
        }

        private void ResotreDreamBlockPosition(On.Celeste.DreamBlock.orig_ctor_EntityData_Vector2 orig,
            DreamBlock self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedDreamBlocks.ContainsKey(entityId)) {
                DreamBlock savedDreamBlock = savedDreamBlocks[entityId];
                self.Position = savedDreamBlock.Position;
                Tween savedTween = savedDreamBlock.Get<Tween>();
                if (savedTween != null)
                    self.Add(new Coroutine(RestorePosition(self, savedTween, data.Position + offset,
                        data.FirstNodeNullable(offset).Value)));
            }
        }

        private IEnumerator RestorePosition(DreamBlock self, Tween savedTween, Vector2 start, Vector2 end) {
            Tween tween = self.Get<Tween>();
            self.Remove(tween);

            float duration = Vector2.Distance(start, end) / 12f;
            if ((bool) self.GetPrivateField("fastMoving"))
                duration /= 3f;
            Tween newTween = Tween.Create(Tween.TweenMode.YoyoLooping, Ease.SineInOut, duration, true);
            newTween.OnUpdate = t => {
                if (self.Collidable)
                    self.MoveTo(Vector2.Lerp(start, end, t.Eased));
                else
                    self.MoveToNaive(Vector2.Lerp(start, end, t.Eased));
            };

            newTween.CopyFrom(savedTween);
            self.Add(newTween);
            yield break;
        }

        public override void OnClear() {
            savedDreamBlocks.Clear();
        }


        public override void OnLoad() {
            On.Celeste.DreamBlock.ctor_EntityData_Vector2 += ResotreDreamBlockPosition;
        }

        public override void OnUnload() {
            On.Celeste.DreamBlock.ctor_EntityData_Vector2 -= ResotreDreamBlockPosition;
        }

        public override void OnInit() {
            typeof(DreamBlock).AddToTracker();
        }
    }
}