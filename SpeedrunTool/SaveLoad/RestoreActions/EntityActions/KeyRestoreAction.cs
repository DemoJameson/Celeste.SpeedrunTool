using System;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.EntityActions {
    public class KeyRestoreAction : RestoreAction {
        public KeyRestoreAction() : base(typeof(Key)) { }
        public override void OnLoad() {
            IL.Celeste.Key.ctor_Player_EntityID += KeyOnCtor_Player_EntityID;
        }

        // 解决吃下钥匙马上保存后出现两把钥匙的问题

        public override void OnUnload() {
            IL.Celeste.Key.ctor_Player_EntityID -= KeyOnCtor_Player_EntityID;
        }

        private void KeyOnCtor_Player_EntityID(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            if (cursor.TryGotoNext(MoveType.After, i => i.MatchCallvirt<Leader>("GainFollower"))) {
                Instruction skipInstruction = cursor.Next;

                if (cursor.TryGotoPrev(i => i.OpCode == OpCodes.Ldarg_1)) {
                    cursor.Emit(OpCodes.Ldarg_0).Emit(OpCodes.Ldarg_0).EmitDelegate<Action<Key, EntityID>>(
                        (key, id) => {
                            key.SetEntityId2(id);
                        });
                    cursor.EmitDelegate<Func<bool>>(() => IsLoadStart);
                    cursor.Emit(OpCodes.Brtrue, skipInstruction);
                    
                }
            }
        }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Key loaded = (Key) loadedEntity;
            Key saved = (Key) savedEntity;
            
            // TODO Restore Tween and Alarm
            // RestoreTween(loaded, saved);
        }

        private void RestoreTween(Key loaded, Key saved) {
            Tween loadedTween = loaded.GetTween("tween");
            Tween savedTween = saved.GetTween("tween");
            if (savedTween == null) return;
            loadedTween?.RemoveSelf();

            // TODO 还原 Key Tween
            if (Math.Abs(savedTween.Duration - 1f) < 0.01f) {
                // Vector2 position = Position;
                // SimpleCurve curve = new SimpleCurve(position, target, (target + position) / 2f + new Vector2(0f, -48f));
                // tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeOut, 1f, start: true);
                // tween.OnUpdate = delegate(Tween t)
                // {
                //     key.Position = curve.GetPoint(t.Eased);
                //     key.sprite.Rate = 1f + t.Eased * 2f;
                // };
            } else if (savedTween.Duration - 0.3f < 0.01f) {
                Tween tween = Tween.Create(savedTween.Mode, Ease.CubeIn, savedTween.Duration, start: true);
                tween.OnUpdate = delegate(Tween t)
                {
                    loaded.GetSprite("sprite").Rotation = t.Eased * ((float)Math.PI / 2f);
                };
                tween.TryCopyFrom(savedTween);
                loaded.Add(tween);
                loaded.SetField("tween", tween);
            }
        }
    }
}