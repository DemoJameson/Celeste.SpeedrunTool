using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class TestPlayerRestoreAction : AbstractRestoreAction {
        public TestPlayerRestoreAction() : base(typeof(Player)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;
            
            AutoMapperUtils.GetMapper(typeof(Actor)).Map(saved, loaded);
            
            // 避免复活时的光圈被背景遮住
            loaded.Depth = Depths.Top; 
        }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;
            
            AutoMapperUtils.GetMapper(typeof(Player)).Map(saved, loaded);
        }

        public override void Load() {
            On.Celeste.Player.ctor += PlayerOnCtor;
        }

        public override void Unload() {
            On.Celeste.Player.ctor -= PlayerOnCtor;
        }

        private static void PlayerOnCtor(On.Celeste.Player.orig_ctor orig, Player self, Vector2 position,
            PlayerSpriteMode spriteMode) {
            // Give Player a fixed EntityId2.
            self.SetEntityId2(new EntityID("You can do it. —— 《Celeste》", 20180125));
            orig(self, position, spriteMode);
        }
    }
}