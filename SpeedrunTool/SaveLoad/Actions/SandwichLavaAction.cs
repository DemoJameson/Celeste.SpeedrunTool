using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class SandwichLavaAction : AbstractEntityAction {
        private Dictionary<EntityID, SandwichLava> savedSandwichLavas = new Dictionary<EntityID, SandwichLava>();

        public override void OnQuickSave(Level level) {
            savedSandwichLavas = level.Tracker.GetDictionary<SandwichLava>();
        }

        private void RestoreSandwichLavaState(On.Celeste.SandwichLava.orig_ctor_EntityData_Vector2 orig,
            SandwichLava self, EntityData data,
            Vector2 offset) {
            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);
            orig(self, data, offset);

            if (IsLoadStart) {
                if (savedSandwichLavas.ContainsKey(entityId)) {
                    SandwichLava savedSandwichLava = savedSandwichLavas[entityId];
                    self.Collidable = savedSandwichLava.Collidable;
                    self.Waiting = savedSandwichLava.Waiting;
                    self.CopyPrivateField("leaving", savedSandwichLava);
                    self.CopyPrivateField("delay", savedSandwichLava);
                    self.Add(new RestorePositionComponent(self, savedSandwichLava));
                }
                else {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear() {
            savedSandwichLavas.Clear();
        }

        public override void OnLoad() {
            On.Celeste.SandwichLava.ctor_EntityData_Vector2 += RestoreSandwichLavaState;
        }

        public override void OnUnload() {
            On.Celeste.SandwichLava.ctor_EntityData_Vector2 -= RestoreSandwichLavaState;
        }

        public override void OnInit() {
            typeof(SandwichLava).AddToTracker();
        }

        private class SandwichLavaComponent : Monocle.Component {
            private readonly SandwichLava savedSandwichLava;

            public SandwichLavaComponent(SandwichLava savedSandwichLava) : base(true, false) {
                this.savedSandwichLava = savedSandwichLava;
            }

            public override void Update() {
                Entity.Position = savedSandwichLava.Position;

                RemoveSelf();
            }
        }
    }
}