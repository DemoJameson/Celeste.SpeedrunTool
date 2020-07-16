using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class EntityRestoreAction : RestoreAction {
        public EntityRestoreAction() : base(typeof(Entity)) { }

        public override void AfterEntityAwake(Entity loadedEntity, Entity savedEntity,
            List<Entity> savedDuplicateIdList) {
            // Player 需要特殊处理，由 PlayerRestoreAction 负责
            if (loadedEntity is Player) return;

            if (TryRestoreCrystalStaticSpinner(loadedEntity, savedEntity)) return;

            loadedEntity.CopyAllFrom(savedEntity);
            
            // 避免复活期间与 Player 发生碰撞，例如保存时与 Spring 过近，恢复时会被弹起。
            loadedEntity.Collidable = loadedEntity.GetType().FullName == "FrostHelper.CustomFireBarrier";;

            RecreateDuplicateGlider(loadedEntity, savedDuplicateIdList);
        }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            loadedEntity.Collidable = savedEntity.Collidable;
        }

        // CrystalStaticSpinner 看不见的地方等于不存在，ch9 g-06 保存恢复后屏幕外的刺无法恢复显示，所以只恢复位置就好
        private static bool TryRestoreCrystalStaticSpinner(Entity loadedEntity, Entity savedEntity) {
            if (loadedEntity is CrystalStaticSpinner ||
                loadedEntity.GetType().FullName == "FrostHelper.CustomSpinner") {
                loadedEntity.Position = savedEntity.Position;
                return true;
            }

            return false;
        }

        // 通过切换房间复制的水母需要重建
        private static void RecreateDuplicateGlider(Entity loadedEntity, List<Entity> savedDuplicateIdList) {
            if (loadedEntity.IsType<Glider>() &&
                savedDuplicateIdList.FirstOrDefault(entity => entity.IsType<Glider>()) is Glider savedGlider) {
                if (savedGlider.GetEntityData() != null) {
                    Glider newGlider = new Glider(savedGlider.GetEntityData(), Vector2.Zero);
                    newGlider.CopyEntityData(savedGlider);
                    newGlider.CopyEntityId2(savedGlider);
                    newGlider.CopyAllFrom(savedGlider);
                    loadedEntity.SceneAs<Level>().Add(newGlider);
                }
            }
        }

        // 解决第九章 g-06 石块砸在望远镜上后存档游戏崩溃的问题，是不是应该交给 Everest 解决
        private void TalkComponentUIOnAwake(On.Celeste.TalkComponent.TalkComponentUI.orig_Awake orig,
            TalkComponent.TalkComponentUI self, Scene scene) {
            if (StateManager.Instance.IsLoadFrozen && self.Handler.Entity == null) {
                return;
            }

            orig(self, scene);
        }

        public override void OnHook() {
            On.Celeste.TalkComponent.TalkComponentUI.Awake += TalkComponentUIOnAwake;
        }

        public override void OnUnhook() {
            On.Celeste.TalkComponent.TalkComponentUI.Awake -= TalkComponentUIOnAwake;
        }
    }
}