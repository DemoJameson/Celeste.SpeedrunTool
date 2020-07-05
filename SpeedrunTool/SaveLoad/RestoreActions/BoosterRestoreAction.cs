using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public class BoosterRestoreAction : AbstractRestoreAction {
        public BoosterRestoreAction() : base(typeof(Booster)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Booster loaded = (Booster) loadedEntity;
            Booster saved = (Booster) savedEntity;
            
            loaded.CopyEntity(saved);
            loaded.CopySprite(saved, "sprite");
            loaded.Ch9HubTransition = saved.Ch9HubTransition;
            loaded.SetProperty("BoostingPlayer", saved.BoostingPlayer);
            loaded.CopyFields(saved, "respawnTimer", "cannotUseTimer");
            
            var outline = loaded.GetField("outline") as Entity;
            var savedOutline = saved.GetField("outline") as Entity;
            outline.CopyEntity(savedOutline);
        }

        public override void CantFoundLoadedEntity(Level level, List<Entity> savedEntityList) {
            Entity saved = savedEntityList[0];
            Booster loaded = new Booster(saved.GetEntityData(), Vector2.Zero);
            loaded.CopyEntityId2(saved);
            // TODO 移除 CopyEntityId
            loaded.CopyEntityId(saved);
            loaded.CopyEntityData(saved);
            level.Add(loaded);
        }
    }
}