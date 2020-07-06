using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.ActorActions {
    public class TheoCrystalRestoreAction : AbstractRestoreAction {
        public TheoCrystalRestoreAction() : base(typeof(TheoCrystal), new List<AbstractRestoreAction> {
            new PlayerRestoreAction()
        }) { }

        public override void NotLoadedEntitiesButSaved(Level level, List<Entity> savedEntityList) {
            foreach (TheoCrystal saved in savedEntityList.Cast<TheoCrystal>()) {
                TheoCrystal loaded = new TheoCrystal(saved.GetEntityData(), Vector2.Zero);
                loaded.CopyEntityId2(saved);
                loaded.CopyEntityData(saved);
                level.Add(loaded);
            }
        }
    }
}