using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions.Base {
    public static class RestoreEntityUtils {
        private static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        private static Dictionary<EntityId2, Entity> SavedEntitiesDict => StateManager.Instance.SavedEntitiesDict;
        private static List<Entity> SavedDuplicateIdList => StateManager.Instance.SavedDuplicateIdList;

        public static void OnLoad() {
            On.Celeste.Level.Begin += LevelOnBegin;
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnHook());
        }

        public static void Unload() {
            On.Celeste.Level.Begin -= LevelOnBegin;
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnUnhook());
        }
        
        public static void OnSaveState(Level level) {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnSaveState(level));
        }

        public static void OnLoadStart(Level level) {
            EntityCopyCore.ClearCachedObjects();
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnLoadStart(level));
        }

        public static void OnLoadComplete(Level level) {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnLoadComplete(level));
        }

        public static void OnClearState() {
            RestoreAction.All.ForEach(restoreAction => restoreAction.OnClearState());
        }

        private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level level) {
            orig(level);
            if (!IsLoadStart) return;

            var loadedDict = level.FindAllToDict<Entity>();

            var notLoadedEntities = SavedEntitiesDict.Where(pair => !loadedDict.ContainsKey(pair.Key))
                .ToDictionary(p => p.Key, p => p.Value);
            if (notLoadedEntities.Count > 0) {
                EntitiesSavedButNotLoaded(level, notLoadedEntities);
            }
        }

        public static void AfterEntityAwake(Level level) {
            var loadedEntitiesDict = level.FindAllToDict<Entity>();

            var notSavedEntities = loadedEntitiesDict.Where(pair => !SavedEntitiesDict.ContainsKey(pair.Key))
                .ToDictionary(p => p.Key, p => p.Value);

            EntitiesLoadedButNotSaved(notSavedEntities);

            foreach (KeyValuePair<EntityId2, Entity> pair in loadedEntitiesDict.Where(loaded => SavedEntitiesDict.ContainsKey(loaded.Key))) {
                RestoreAction.All.ForEach(restoreAction => {
                    if (restoreAction.EntityType != null && pair.Value.GetType().IsSameOrSubclassOf(restoreAction.EntityType)) {
                        restoreAction.AfterEntityAwake(pair.Value, SavedEntitiesDict[pair.Key],
                            SavedDuplicateIdList);
                    }
                });
            }
        }

        public static void AfterPlayerRespawn(Level level) {
            Dictionary<EntityId2, Entity> loadedEntitiesDict = level.FindAllToDict<Entity>();

            foreach (KeyValuePair<EntityId2, Entity> pair in loadedEntitiesDict.Where(loaded => SavedEntitiesDict.ContainsKey(loaded.Key))) {
                RestoreAction.All.ForEach(restoreAction => {
                    if (restoreAction.EntityType != null && pair.Value.GetType().IsSameOrSubclassOf(restoreAction.EntityType)) {
                        restoreAction.AfterPlayerRespawn(pair.Value, SavedEntitiesDict[pair.Key]);
                    }
                });
            }
        }
        
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
            } else if (savedType.IsType<Solid>() && savedEntity is Solid solid) {
                loadedEntity = solid.Clone();
            } else if (savedType.IsType<SpeedRing>() && savedEntity is SpeedRing speedRing) {
                loadedEntity = speedRing.Clone();
            } else if (savedType.IsType<StrawberryPoints>() && savedEntity is StrawberryPoints points) {
                loadedEntity = points.Clone();
            } else if (savedType.IsType<FinalBossShot>() && savedEntity is FinalBossShot finalBossShot) {
                loadedEntity = finalBossShot.Clone();
            } else if (savedType.IsType<FinalBossBeam>() && savedEntity is FinalBossBeam finalBossBeam) {
                loadedEntity = finalBossBeam.Clone();
            } else if (savedType.IsType<BirdTutorialGui>() && savedEntity is BirdTutorialGui birdTutorialGui) {
                loadedEntity = birdTutorialGui.Clone();
            } else if (savedType.IsType<SoundEmitter>() && savedEntity is SoundEmitter soundEmitter) {
                loadedEntity = SoundEmitter.Play(soundEmitter.Source.EventName, new Entity(soundEmitter.Position));
                (loadedEntity as SoundEmitter)?.Source.Stop();
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
                if (loadedEntity.IsGlobalButExcludeSomeTypes()) return;
                loadedEntity.RemoveSelf();
            }
        }
    }
}