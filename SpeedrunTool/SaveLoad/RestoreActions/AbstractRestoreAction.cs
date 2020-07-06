using System;
using System.Collections.Generic;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public abstract class AbstractRestoreAction {
        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        
        public Type Type;
        public List<AbstractRestoreAction> SubclassRestoreActions;
        protected AbstractRestoreAction(Type type, List<AbstractRestoreAction> subclassRestoreActions = null) {
            Type = type;
            SubclassRestoreActions = subclassRestoreActions ?? new List<AbstractRestoreAction>();
            
        }

        public virtual void Load() { }
        public virtual void Unload() { }

        // 执行循序从上至下
        public virtual void Added(Entity loadedEntity, Entity savedEntity) { }

        public virtual void Awake(Entity loadedEntity, Entity savedEntity) { }
        
        // 用于处理保存了当是没有被重新创建的物体，一般是手动创建新的实例然后添加到 Level 中。
        // 例如草莓，红泡泡这种跨房间的物体就需要处理。
        public virtual void NotLoadedEntitiesButSaved(Level level, List<Entity> savedEntityList) { }
        
        // 此时恢复状态可以避免很多问题，例如刺的依附和第九章鸟的节点处理
        public virtual void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) { }
        
        // 与 AfterEntityCreateAndUpdate1Frame 是同样的时刻，用于处理不存在于保存数据中的 Entity，默认删除
        public virtual void NotSavedEntityButLoaded(Entity loadedEntity) {
            if (loadedEntity.TagCheck(Tags.Global)) return;
            loadedEntity.RemoveSelf();
        }

        // Madelin 复活完毕的时刻，主要用于恢复 Player
        public virtual void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) { }
    }
}