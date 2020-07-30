using System;
using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class PlayerRestoreAction : RestoreAction {
        private bool ignoreHairException = true;

        public PlayerRestoreAction() : base(typeof(Player)) { }

        public override void AfterEntityAwake(Entity loadedEntity, Entity savedEntity, List<Entity> toList) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;

            // 只恢复简单字段，其他等到复活完恢复
            CopyCore.DeepCopyMembers(loaded, saved, true);

            // 不恢复速度，原地等待复活
            loaded.Speed = Vector2.Zero;

            // 避免复活时的光圈被背景遮住
            loaded.Depth = Depths.Top;

            // 避免复活期间与其他物体发生碰撞
            loaded.Collidable = false;

            loaded.Hair.Color = GetRespawnHairColor(saved);
        }

        private static Color GetRespawnHairColor(Player player) {
            bool madelineMode = player.Sprite.Mode == PlayerSpriteMode.Madeline || player.Sprite.Mode == PlayerSpriteMode.MadelineNoBackpack;
            if (player.Dashes > 1) {
                return madelineMode
                    ? Player.TwoDashesHairColor
                    : Player.TwoDashesBadelineHairColor;
            }

            if (player.Dashes == 1) {
                return madelineMode
                    ? Player.NormalHairColor
                    : Player.NormalBadelineHairColor;
            }

            return madelineMode
                ? Player.UsedHairColor
                : Player.UsedBadelineHairColor;
        }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;

            CopyCore.DeepCopyMembers(loaded, saved);
        }

        public override void OnLoadStart(Level level) {
            ignoreHairException = true;
        }

        public override void OnHook() {
            On.Celeste.Player.ctor += PlayerOnCtor;
            On.Celeste.Level.LoadNewPlayer += LevelOnLoadNewPlayer;
            On.Celeste.PlayerHair.Render += PlayerHairOnRender;
        }

        public override void OnUnhook() {
            On.Celeste.Player.ctor -= PlayerOnCtor;
            On.Celeste.Level.LoadNewPlayer -= LevelOnLoadNewPlayer;
            On.Celeste.PlayerHair.Render -= PlayerHairOnRender;
        }

        // 修复：红发状态下保存然后辅助模式修改冲刺次数为无限后读档游戏崩溃
        private void PlayerHairOnRender(On.Celeste.PlayerHair.orig_Render orig, PlayerHair self) {
            if (StateManager.Instance.IsLoadComplete && ignoreHairException) {
                ignoreHairException = false;
                try {
                    orig(self);
                } catch (ArgumentOutOfRangeException) {
                    // ignore.
                }
            } else {
                orig(self);
            }
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