using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.ActorActions {
    public class PlayerRestoreAction : RestoreAction {
        public PlayerRestoreAction() : base(typeof(Player)) { }

        public override void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;

            // 只还原父类字段，其他等到复活完恢复
            loaded.CopyAllFrom(saved, typeof(Entity), typeof(Actor));

            // 避免复活时的光圈被背景遮住
            loaded.Depth = Depths.Top;

            loaded.JustRespawned = saved.JustRespawned;
            loaded.CameraAnchor = saved.CameraAnchor;
            loaded.CameraAnchorLerp = saved.CameraAnchorLerp;
            loaded.CameraAnchorIgnoreX = saved.CameraAnchorIgnoreX;
            loaded.CameraAnchorIgnoreY = saved.CameraAnchorIgnoreY;
            loaded.ForceCameraUpdate = saved.ForceCameraUpdate;
            loaded.EnforceLevelBounds = saved.EnforceLevelBounds;
            loaded.Dashes = saved.Dashes;
        }

        public override void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) {
            Player loaded = (Player) loadedEntity;
            Player saved = (Player) savedEntity;

            loaded.CopyAllFrom(saved, typeof(Entity));

            // too lazy to restore private List<ChaserStateSound> activeSounds
            // too lazy to restore this field, hope its ok.
            // private HashSet<Trigger> triggersInside;

            RestoreLeader(loaded, saved);
        }

        private void RestoreLeader(Player loaded, Player saved) {
            Leader loadedLeader = loaded.Leader;
            Leader savedLeader = saved.Leader;

            loadedLeader.Position = savedLeader.Position;
            loadedLeader.PastPoints.Clear();
            loadedLeader.PastPoints.AddRange(savedLeader.PastPoints);
            savedLeader.Followers.ForEach(savedFollower => {
                Entity entity = loaded.Scene.FindFirst(savedFollower.Entity.GetEntityId2());
                if (entity == null) return;
                FieldInfo followerFieldInfo = entity.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(fieldInfo => fieldInfo.FieldType == typeof(Follower));
                if (followerFieldInfo?.GetValue(entity) is Follower follower) {
                    loadedLeader.Followers.Add(follower);
                }
            });
        }

        public override void OnLoad() {
            On.Celeste.Player.ctor += PlayerOnCtor;
            On.Celeste.Level.LoadNewPlayer += LevelOnLoadNewPlayer;
        }

        public override void OnUnload() {
            On.Celeste.Player.ctor -= PlayerOnCtor;
            On.Celeste.Level.LoadNewPlayer -= LevelOnLoadNewPlayer;
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