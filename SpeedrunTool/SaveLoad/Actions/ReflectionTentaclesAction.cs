using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.SaveLoad.Component;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions
{
    public class ReflectionTentaclesAction : AbstractEntityAction
    {
        private Dictionary<string, ReflectionTentacles> _savedReflectionTentacles = new Dictionary<string, ReflectionTentacles>();

        public override void OnQuickSave(Level level)
        {
            _savedReflectionTentacles = level.Tracker.GetCastEntities<ReflectionTentacles>()
                .ToDictionary(entity => entity.GetExtendedDataValue<string>("nodesLayerKey"));
        }

        private void ReflectionTentaclesOnCreate(On.Celeste.ReflectionTentacles.orig_Create orig,
            ReflectionTentacles self, float fearDistance, int slideUntilIndex, int layer, List<Vector2> startNodes)
        {
            string nodesLayerKey = string.Join("", startNodes.Select(node => node.ToString())) + layer;
            self.SetExtendedDataValue("nodesLayerKey", nodesLayerKey);


            if (IsLoadStart && _savedReflectionTentacles.ContainsKey(nodesLayerKey))
            {
                ReflectionTentacles savedTentacle = _savedReflectionTentacles[nodesLayerKey];
                int index = savedTentacle.Index - savedTentacle.Nodes.Count + startNodes.Count;

                if (startNodes.Count - index <= 1)
                {
                    index--;
                    self.Visible = false;
                }

                slideUntilIndex -= index;
                startNodes = startNodes.Skip(index).ToList();
            }

            orig(self, fearDistance, slideUntilIndex, layer, startNodes);
        }

        public override void OnClear()
        {
            _savedReflectionTentacles = null;
        }

        public override void OnLoad()
        {
            On.Celeste.ReflectionTentacles.Create += ReflectionTentaclesOnCreate;
        }

        public override void OnUnload()
        {
            On.Celeste.ReflectionTentacles.Create -= ReflectionTentaclesOnCreate;
        }
    }
}