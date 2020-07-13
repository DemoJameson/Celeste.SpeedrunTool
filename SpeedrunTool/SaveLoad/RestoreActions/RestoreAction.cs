using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public abstract class RestoreAction {
        public static readonly List<RestoreAction> All = new List<RestoreAction> {
            new EntityRestoreAction(),
            new PlayerRestoreAction(),
            new KeyRestoreAction(),
            new TriggerSpikesRestoreAction(),
            new ComponentRestoreAction(),
            new SoundSourceAction(),
            new EventInstanceRestoreAction(),
        };

        protected static bool IsLoadStart => StateManager.Instance.IsLoadStart;

        public readonly Type EntityType;

        protected RestoreAction(Type entityType) {
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

        // 用于处理保存了当是没有被重新创建的物体，一般是手动创建新的实例然后添加到 Level 中。
        // 例如草莓，红泡泡，Theo，水母等跨房间的物体就需要处理，也就是附加了 Tags.Persistent 的物体。
        // 还有一些是游戏过程代码New出来的，没有 EntityData 的也需要处理，例如 BadelinDummy 和 SlashFx
        public static void EntitiesSavedButNotLoaded(Level level, Dictionary<EntityId2, Entity> savedEntities) {
            foreach (var pair in savedEntities) {
                Entity savedEntity = pair.Value;
                if (CreateEntityCopy(savedEntity) is Entity entity) {
                    level.Add(entity);
                }
            }
        }

        public static Entity CreateEntityCopy(Entity savedEntity, string tag = "EntitiesSavedButNotLoaded") {
            Entity loadedEntity = null;
            Type savedType = savedEntity.GetType();

            if (savedEntity.GetEntityData() != null) {
                // 一般 Entity 都是 EntityData + Vector2
                loadedEntity = (savedType.GetConstructor(new[] {typeof(EntityData), typeof(Vector2)})
                    ?.Invoke(new object[] {savedEntity.GetEntityData(), Vector2.Zero})) as Entity;

                if (loadedEntity == null) {
                    // 部分例如草莓则是 EntityData + Vector2 + EntityID
                    loadedEntity = savedType
                        .GetConstructor(new[] {typeof(EntityData), typeof(Vector2), typeof(EntityID)})
                        ?.Invoke(new object[] {
                            savedEntity.GetEntityData(), Vector2.Zero, savedEntity.GetEntityId2().EntityId
                        }) as Entity;
                }

                if (loadedEntity == null && savedType.IsType<CrystalStaticSpinner>()) {
                    loadedEntity = new CrystalStaticSpinner(savedEntity.GetEntityData(), Vector2.Zero,
                        (CrystalColor) savedEntity.GetField(typeof(CrystalStaticSpinner), "color"));
                }

                if (loadedEntity == null && savedType.IsType<TriggerSpikes>()) {
                    loadedEntity = new TriggerSpikes(savedEntity.GetEntityData(), Vector2.Zero,
                        (TriggerSpikes.Directions) savedEntity.GetField(typeof(TriggerSpikes), "direction"));
                }
                
                if (loadedEntity == null && savedType.IsType<Spikes>()) {
                    loadedEntity = new Spikes(savedEntity.GetEntityData(), Vector2.Zero,
                        ((Spikes)savedEntity).Direction);
                }
                
                if (loadedEntity == null && savedType.IsType<TriggerSpikes>()) {
                    loadedEntity = new Spring(savedEntity.GetEntityData(), Vector2.Zero, ((Spring)savedEntity).Orientation);
                }

                if (loadedEntity != null) {
                    loadedEntity.Position = savedEntity.Position;
                    loadedEntity.CopyEntityData(savedEntity);
                    loadedEntity.CopyEntityId2(savedEntity);
                    return loadedEntity;
                }
            }

            // TODO 如果是他们的子类该怎么办……
            if (savedType.IsType<BadelineDummy>()) {
                loadedEntity = new BadelineDummy(savedEntity.GetStartPosition());
            } else if (savedType.IsType<AngryOshiro>()) {
                loadedEntity = new AngryOshiro(savedEntity.GetStartPosition(),
                    (bool) savedEntity.GetField(savedType, "fromCutscene"));
            } else if (savedType.IsType<Snowball>()) {
                loadedEntity = new Snowball();
            } else if (savedType.IsType<SlashFx>() && savedEntity is SlashFx slashFx) {
                loadedEntity = slashFx.Clone();
            } else if (savedType.IsType<SpeedRing>() && savedEntity is SpeedRing speedRing) {
                loadedEntity = speedRing.Clone();
            } else if (savedType.IsType<FinalBossShot>() && savedEntity is FinalBossShot finalBossShot) {
                loadedEntity = finalBossShot.Clone();
            } else if (savedType.IsType<FinalBossBeam>() && savedEntity is FinalBossBeam finalBossBeam) {
                loadedEntity = finalBossBeam.Clone();
            } else if (savedType.IsType<BirdTutorialGui>() && savedEntity is BirdTutorialGui birdTutorialGui) {
                loadedEntity = birdTutorialGui.Clone();
            } else if (savedType.IsType<SoundEmitter>() && savedEntity is SoundEmitter soundEmitter) {
                loadedEntity = SoundEmitter.Play(soundEmitter.Source.EventName,
                    new Entity(soundEmitter.Position));
                if (SoundSourceAction.PlayingSoundSources.FirstOrDefault(source =>
                    source.EventName == soundEmitter.Source.EventName) == null) {
                    (loadedEntity as SoundEmitter)?.Source.TryCopyObject(soundEmitter.Source);
                } else {
                    (loadedEntity as SoundEmitter)?.Source.Stop();
                }
            } else if (savedType.IsType<Debris>() && savedEntity is Debris debris) {
                loadedEntity = Engine.Pooler.Create<Debris>()
                    .Init(debris.GetStartPosition(), (char) debris.GetField("tileset"),
                        (bool) debris.GetField("playSound"));
            } else if (savedType == typeof(TalkComponent.TalkComponentUI)) {
                // ignore
            } else if (savedType.IsType<Entity>()) {
                loadedEntity = new Entity(savedEntity.GetStartPosition());
            } else {
                if (savedEntity.GetType().FullName == "Celeste.MoveBlock+Debris") {
                    loadedEntity = (savedEntity as Actor).CloneMoveBlockDebris();
                } else if (savedEntity.ForceCreateInstance(tag) is Entity newEntity) {
                    loadedEntity = newEntity;
                }
            }


            if (loadedEntity == null) return null;

            loadedEntity.Position = savedEntity.Position;
            loadedEntity.CopyEntityId2(savedEntity);
            loadedEntity.CopyStartPosition(savedEntity);

            return loadedEntity;
        }

        // 与 AfterEntityCreateAndUpdate1Frame 是同样的时刻，用于处理不存在于保存数据中的 Entity，删除就好
        public static void EntitiesLoadedButNotSaved(Dictionary<EntityId2, Entity> notSavedEntities) {
            foreach (Entity loadedEntity in notSavedEntities.Select(pair => pair.Value)) {
                if (loadedEntity.IsGlobalButNotCassetteManager()) return;
                loadedEntity.RemoveSelf();
            }
        }
    }
}