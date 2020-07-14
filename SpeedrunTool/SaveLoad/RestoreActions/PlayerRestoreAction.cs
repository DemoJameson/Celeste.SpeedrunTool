using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class PlayerRestoreAction : RestoreAction {
        public PlayerRestoreAction() : base(typeof(Player)) { }

        public override void AfterEntityAwake(Entity loadedEntity, Entity savedEntity, List<Entity> toList) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;

            // 只还原父类字段，其他等到复活完恢复
            loaded.CopyAllFrom(saved, true);

                        
            // 不恢复速度，原地等待复活
            loaded.Speed = Vector2.Zero;
            
            // 避免复活时的光圈被背景遮住
            loaded.Depth = Depths.Top;
        }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;

            loaded.CopyAllFrom(saved);
        }

        public override void OnHook() {
            On.Celeste.Player.ctor += PlayerOnCtor;
            On.Celeste.Level.LoadNewPlayer += LevelOnLoadNewPlayer;
        }

        public override void OnUnhook() {
            On.Celeste.Player.ctor -= PlayerOnCtor;
            On.Celeste.Level.LoadNewPlayer -= LevelOnLoadNewPlayer;
        }

        private static void PlayerOnCtor(On.Celeste.Player.orig_ctor orig, Player self, Vector2 position,
            PlayerSpriteMode spriteMode) {
            self.SetEntityId2(EntityId2.PlayerFixedEntityId2);
            orig(self, position, spriteMode);
        }

        private Player LevelOnLoadNewPlayer(On.Celeste.Level.orig_LoadNewPlayer orig, Vector2 position,
            PlayerSpriteMode spriteMode) {
            Player player = orig(position, spriteMode);
            player.SetEntityId2(EntityId2.PlayerFixedEntityId2);
            return player;
        }
    }
}