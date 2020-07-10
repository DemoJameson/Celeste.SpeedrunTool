using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public abstract class RestoreAction {
        protected static Dictionary<EntityId2, Entity> SavedEntitiesDict => StateManager.Instance.SavedEntitiesDict;
        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;

        public Type Type;
        public List<RestoreAction> SubclassRestoreActions;

        protected RestoreAction(Type type, List<RestoreAction> subclassRestoreActions = null) {
            Type = type;
            SubclassRestoreActions = subclassRestoreActions ?? new List<RestoreAction>();
        }

        public virtual void OnLoad() { }
        public virtual void OnUnload() { }

        // 执行循序从上至下
        public virtual void Added(Entity loadedEntity, Entity savedEntity) { }

        public virtual void Awake(Entity loadedEntity, Entity savedEntity) { }

        // 用于处理保存了当是没有被重新创建的物体，一般是手动创建新的实例然后添加到 Level 中。
        // 例如草莓，红泡泡，Theo，水母等跨房间的物体就需要处理，也就是附加了 Tags.Persistent 的物体。
        // 还有一些是游戏过程代码New出来的，没有 EntityData 的也需要处理，例如 BadelinDummy 和 SlashFx
        public static void EntitiesSavedButNotLoaded(Level level, Dictionary<EntityId2, Entity> savedEntities) {
            foreach (var pair in savedEntities) {
                Entity savedEntity = pair.Value;

                if (savedEntity.GetEntityData() != null) {
                    Type type = savedEntity.GetType();
                    ConstructorInfo constructorInfo = type.GetConstructor(new[] {typeof(EntityData), typeof(Vector2)});
                    if (constructorInfo == null) {
                        constructorInfo =
                            type.GetConstructor(new[] {typeof(EntityData), typeof(Vector2), typeof(EntityID)});
                    }

                    if (constructorInfo == null) {
                        continue;
                    }

                    var parameters = new object[] {savedEntity.GetEntityData(), Vector2.Zero};
                    if (constructorInfo.GetParameters().Length == 3) {
                        parameters = new object[]
                            {savedEntity.GetEntityData(), Vector2.Zero, savedEntity.GetEntityId2().EntityId};
                    }

                    object loaded = constructorInfo.Invoke(parameters);
                    if (loaded is Entity loadedEntity) {
                        loadedEntity.Position = savedEntity.Position;
                        loadedEntity.CopyEntityData(savedEntity);
                        loadedEntity.CopyEntityId2(savedEntity);
                        level.Add(loadedEntity);
                    }
                } else if (savedEntity.GetType().IsSubclassOf(typeof(Entity))) {
                    Entity loadedEntity = null;
                    Player player = Engine.Scene.GetPlayer();
                    switch (savedEntity) {
                        // 先将范围限定在 Entity 的子类，如果出现问题再说
                        case BadelineDummy dummy:
                            loadedEntity = new BadelineDummy(dummy.GetStartPosition());
                            break;
                        case AngryOshiro oshiro:
                            loadedEntity = new AngryOshiro(oshiro.GetStartPosition(),
                                (bool) oshiro.GetField("fromCutscene"));
                            break;
                        case Snowball _:
                            loadedEntity = new Snowball();
                            break;
                        case SlashFx slashFx:
                            loadedEntity = slashFx.Clone();
                            break;
                        case SpeedRing speedRing:
                            loadedEntity = speedRing.Clone();
                            break;
                        case FinalBossShot finalBossShot:
                            loadedEntity = finalBossShot.Clone();
                            break;
                        case FinalBossBeam finalBossBeam:
                            loadedEntity = finalBossBeam.Clone();
                            break;
                        // BUG: 钥匙开门多重声音
                        case SoundEmitter soundEmitter:
                            loadedEntity = SoundEmitter.Play(soundEmitter.Source.EventName,
                                new Entity(soundEmitter.Position), Vector2.Zero);
                            break;
                        default:
                            if (savedEntity.GetType().ForceCreateInstance("EntitiesSavedButNotLoaded") is Entity
                                newEntity) {
                                loadedEntity = newEntity;
                            }

                            break;
                    }

                    if (loadedEntity == null) continue;

                    loadedEntity.Position = savedEntity.Position;
                    loadedEntity.CopyEntityId2(savedEntity);
                    loadedEntity.CopyStartPosition(savedEntity);

                    level.Add(loadedEntity);
                }
            }
        }

        // 此时恢复状态可以避免很多问题，例如刺的依附和第九章鸟的节点处理
        public virtual void AfterEntityCreateAndUpdate1Frame(Entity loadedEntity, Entity savedEntity) { }

        // 与 AfterEntityCreateAndUpdate1Frame 是同样的时刻，用于处理不存在于保存数据中的 Entity，删除就好
        public static void EntitiesLoadedButNotSaved(Dictionary<EntityId2, Entity> notSavedEntities) {
            foreach (Entity loadedEntity in notSavedEntities.Select(pair => pair.Value)) {
                if (loadedEntity.TagCheck(Tags.Global)) return;
                loadedEntity.RemoveSelf();
            }
        }

        // Madelin 复活完毕的时刻，主要用于恢复 Player 的状态
        public virtual void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) { }
    }
}