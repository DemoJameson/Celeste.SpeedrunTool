using System;
using System.Collections.Generic;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public abstract class AbstractRestoreAction {
        public Type Type;
        protected AbstractRestoreAction(Type type) {
            Type = type;
        }

        public virtual void Load() { }
        public virtual void Unload() { }
        public virtual void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) { }

        public virtual void Added(Entity loadedEntity, Entity savedEntity) { }
        
        public virtual void Awake(Entity loadedEntity, Entity savedEntity) { }
        
        public virtual void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) { }
        
        public virtual void CantFoundSavedEntity(Entity loadedEntity) {
            loadedEntity.RemoveSelf();
        }
        
        public virtual void CantFoundLoadedEntity(Level level, List<Entity> savedEntityList) {
            
        }
    }
}