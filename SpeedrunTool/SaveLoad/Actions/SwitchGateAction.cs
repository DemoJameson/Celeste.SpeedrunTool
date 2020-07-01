using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SwitchGateAction : AbstractEntityAction {
        private Dictionary<EntityID, SwitchGate> savedSwitchGates = new Dictionary<EntityID, SwitchGate>();

        public override void OnQuickSave(Level level) {
            savedSwitchGates = level.Entities.GetDictionary<SwitchGate>();
        }

        private void RestoreSwitchGatePosition(On.Celeste.SwitchGate.orig_ctor_EntityData_Vector2 orig, SwitchGate self,
            EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart && savedSwitchGates.ContainsKey(entityId)) {
                var savedSwitchGate = savedSwitchGates[entityId];
                self.Position = savedSwitchGate.Position;
                self.CopySprite(savedSwitchGate, "icon");
                self.CopyFields(savedSwitchGate, "iconOffset");

                Tween savedTween = savedSwitchGate.Get<Tween>();
                if (savedTween == null) {
                    return;
                }

                var start = data.Position + offset;
                var end = data.FirstNodeNullable(offset).Value;
                
                Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeOut, 2f, true);
                tween.OnUpdate = t => { self.MoveTo(Vector2.Lerp(start, end, t.Eased)); };
                tween.CopyFrom(savedTween);
                self.Add(tween);
            }
        }
        
        private void BlockCoroutineStart(ILContext il) {
            ILCursor cursor = new ILCursor(il);
            cursor.GotoNext(i => i.MatchRet());
            cursor.GotoNext(i => i.MatchRet());
            Instruction skipCoroutine = cursor.Next;
            cursor.GotoPrev(i => i.MatchRet());
            cursor.GotoNext();
            ILLabel label = cursor.MarkLabel();
            cursor.EmitDelegate<Func<bool>>(() => IsLoadStart);
            cursor.Emit(OpCodes.Brtrue, skipCoroutine);
            if (cursor.TryGotoPrev(MoveType.After, i => i.MatchCall<Switch>("CheckLevelFlag"),
                i => i.OpCode == OpCodes.Brfalse_S)) {
                cursor.Prev.Operand = label;
            } 
        }

        public override void OnClear() {
            savedSwitchGates.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SwitchGate.ctor_EntityData_Vector2 += RestoreSwitchGatePosition;
            IL.Celeste.SwitchGate.Awake += BlockCoroutineStart;
        }

        public override void OnUnload() {
            On.Celeste.SwitchGate.ctor_EntityData_Vector2 -= RestoreSwitchGatePosition;
            IL.Celeste.SwitchGate.Awake -= BlockCoroutineStart;
        }
    }
}