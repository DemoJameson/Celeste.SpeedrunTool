using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class FinalBossMovingBlockAction : AbstractEntityAction
    {
        private Dictionary<EntityID, FinalBossMovingBlock> _savedFinalBossMovingBlocks = new Dictionary<EntityID, FinalBossMovingBlock>();

        public override void OnQuickSave(Level level)
        {
            _savedFinalBossMovingBlocks = level.Tracker.GetDictionary<FinalBossMovingBlock>();
        }

        private void RestoreFinalBossMovingBlockPosition(On.Celeste.FinalBossMovingBlock.orig_ctor_EntityData_Vector2 orig, FinalBossMovingBlock self, EntityData data,
            Vector2 offset)
        {
            orig(self, data, offset);

            EntityID entityId = data.ToEntityId();
            self.SetEntityId(entityId);

            if (IsLoadStart)
            {
                if (_savedFinalBossMovingBlocks.ContainsKey(entityId))
                {
                    self.Position = _savedFinalBossMovingBlocks[entityId].Position;
                    self.Add(new UpdateComponent());
                }
                else
                {
                    self.Add(new RemoveSelfComponent());
                }
            }
        }

        public override void OnClear()
        {
            _savedFinalBossMovingBlocks.Clear();
        }

        public override void OnLoad()
        {
            On.Celeste.FinalBossMovingBlock.ctor_EntityData_Vector2 += RestoreFinalBossMovingBlockPosition;
        }

        public override void OnUnload()
        {
            On.Celeste.FinalBossMovingBlock.ctor_EntityData_Vector2 -= RestoreFinalBossMovingBlockPosition;
        }

        public override void OnUpdateEntitiesWhenFreeze(Level level)
        {
            level.UpdateEntities<FinalBossMovingBlock>();
        }

        private class UpdateComponent: Monocle.Component
        {
            public UpdateComponent() : base(true, false) { }

            public override void Update()
            {
                FinalBoss finalBoss = Scene.Tracker.GetEntity<FinalBoss>();
                int nodeIndex = (int) finalBoss.GetPrivateField("nodeIndex");
                FinalBossMovingBlock finalBossMovingBlock = EntityAs<FinalBossMovingBlock>();
                if (finalBossMovingBlock.BossNodeIndex == nodeIndex)
                {
                    finalBossMovingBlock.StartMoving(0);
                }
                RemoveSelf();
            }
        }
    }
}