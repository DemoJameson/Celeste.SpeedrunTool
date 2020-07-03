using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    public class EntityAction : AbstractEntityAction {
        private static readonly List<Type> ExcludeTypes = new List<Type> {
            typeof(ParticleSystem),
            typeof(Wire),
            typeof(Cobweb),
            typeof(Decal),
        };
        
        private static readonly List<string> ExcludeTypeNames = new List<string> {
            "Celeste.CrystalStaticSpinner+Border",
        };
        
        public override void OnQuickSave(Level level) { }
        public override void OnClear() { }

        private static void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self,
            Vector2 position) {
            orig(self, position);

            Type type = self.GetType();
            
            if (type.Namespace != "Celeste") return;
            if (ExcludeTypes.Contains(type)) return;
            if (ExcludeTypeNames.Contains(type.FullName)) return;
            
            self.TrySetEntityId(position.ToString());
        }

        public override void OnLoad() {
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
        }

        public override void OnUnload() {
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
        }
    }
}