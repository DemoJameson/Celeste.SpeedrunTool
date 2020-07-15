using System;
using System.Collections.Generic;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base {
    public abstract class RestoreAction {
        public static readonly List<RestoreAction> All = new List<RestoreAction> {
            // Entity
            new EntityRestoreAction(),
            new PlayerRestoreAction(),
            new KeyRestoreAction(),
            new TriggerSpikesRestoreAction(),
            
            // Component
            new SoundSourceAction(),
            new ComponentRestoreAction(),
            
            // Non Entity
            new EventInstanceRestoreAction(),
            new BurstRestoreAction(),
        };

        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;

        public readonly Type EntityType;

        protected RestoreAction(Type entityType = null) {
            EntityType = entityType;
        }

        public virtual void OnHook() { }

        public virtual void OnUnhook() { }

        public virtual void OnSaveState(Level level) { }
        public virtual void OnLoadStart(Level level) { }

        public virtual void OnLoadComplete(Level level) { }

        public virtual void OnClearState() { }

        // 此时恢复 Entity 的状态可以避免很多问题，例如刺的依附和第九章鸟的节点处理
        public virtual void AfterEntityAwake(Entity loadedEntity, Entity savedEntity,
            List<Entity> savedDuplicateIdList) { }


        // Madelin 复活完毕的时刻，主要用于恢复 Player 的状态
        public virtual void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) { }
    }
}