using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class EntityRestoreAction : RestoreAction {
        public EntityRestoreAction() : base(typeof(Entity)) { }

        public override void AfterEntityAwake(Entity loadedEntity, Entity savedEntity) {
            // Player 需要特殊处理，由 PlayerRestoreAction 负责
            if (loadedEntity is Player) return;

            // CrystalStaticSpinner 看不见的地方等于不存在，ch9 g-06 保存恢复后屏幕外的刺无法恢复显示，所以只恢复位置就好
            if (loadedEntity is CrystalStaticSpinner) {
                loadedEntity.Position = savedEntity.Position;
                return;
            }
            
            loadedEntity.CopyAllFrom(savedEntity);
        }

        // 解决第九章 g-06 石块砸在望远镜上后存档游戏崩溃的问题，是不是应该交给 Everest 解决
        private void TalkComponentUIOnAwake(On.Celeste.TalkComponent.TalkComponentUI.orig_Awake orig, TalkComponent.TalkComponentUI self, Scene scene) {
            if (StateManager.Instance.IsLoadFrozen && self.Handler?.Entity == null) {
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