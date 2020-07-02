using System;
using System.Collections;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class BadelineOldsiteAction : AbstractEntityAction {
        private Dictionary<EntityID, BadelineOldsite> savedBadelineOldsites =
            new Dictionary<EntityID, BadelineOldsite>();

        private ILHook addedHook;

        public override void OnQuickSave(Level level) {
            savedBadelineOldsites = level.Entities.GetDictionary<BadelineOldsite>();
        }

        private void RestoreBadelineOldsitePosition(On.Celeste.BadelineOldsite.orig_ctor_EntityData_Vector2_int orig,
            BadelineOldsite self, EntityData data,
            Vector2 offset, int index) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset, index);

            if (!IsLoadStart) return;

            if (savedBadelineOldsites.ContainsKey(entityId)) {
                var saved = savedBadelineOldsites[entityId];
                self.CopyFrom(saved);
                self.Hovering = saved.Hovering;
                self.CopyFields(saved, "following", "hoveringTimer");

                self.Add(new Coroutine(RestorePlayer(self, saved)));
            } else {
                self.Add(new RemoveSelfComponent());
            }
        }

        private IEnumerator RestorePlayer(BadelineOldsite self, BadelineOldsite saved) {
            if (self.Scene.GetPlayer() is Player player && saved.GetField("player") != null) {
                self.SetField("player", player);
            }

            yield break;
        }

        private void BadelineOldsiteOnAdded(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            if (!cursor.TryGotoNextAddCoroutine<BadelineOldsite>("StartChasingRoutine", out var skipCoroutine, 9)) {
                return;
            }


            Instruction start = cursor.Next;
            start.GetHashCode().ToString().Log();
            ILLabel startLabel = cursor.MarkLabel();

            cursor.EmitDelegate<Func<bool>>(() => IsLoadStart);
            cursor.Emit(OpCodes.Brtrue, skipCoroutine);

            // if (cursor.TryGotoPrev(MoveType.After,
            //     i => i.MatchCallvirt<Session>("GetFlag"),
            //     i => i.OpCode == OpCodes.Brtrue)) {
            //     cursor.Prev.Operand = label;
            // }
            
            if (cursor.TryFindPrev(out var cursors,
                i => {
                    if (i.Operand is ILLabel a) {
                        ("jumpLabelTarget = " + a.Target.GetHashCode()).Log();
                        ("start =" + start.GetHashCode()).Log();
                        (a.Target == start).ToString().Log();
                    }

                    return false;
                    return i.Operand is ILLabel jumpLabel && jumpLabel.Target == start;
                })) {
                foreach (var ilCursor in cursors) {
                    // ilCursor.Next.Operand = startLabel;
                    "sjdfjsdjsdjf".Log();
                }
            }
            
        }

        public override void OnClear() {
            savedBadelineOldsites.Clear();
        }

        public override void OnLoad() {
            On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int += RestoreBadelineOldsitePosition;
            addedHook = new ILHook(typeof(BadelineOldsite).GetMethod("orig_Added"), BadelineOldsiteOnAdded);
        }

        public override void OnUnload() {
            On.Celeste.BadelineOldsite.ctor_EntityData_Vector2_int -= RestoreBadelineOldsitePosition;
            addedHook.Dispose();
        }
    }
}