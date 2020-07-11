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
        public virtual void AfterEntityAwake(Entity loadedEntity, Entity savedEntity) { }


        // Madelin 复活完毕的时刻，主要用于恢复 Player 的状态
        public virtual void AfterPlayerRespawn(Entity loadedEntity, Entity savedEntity) { }

        // 用于处理保存了当是没有被重新创建的物体，一般是手动创建新的实例然后添加到 Level 中。
        // 例如草莓，红泡泡，Theo，水母等跨房间的物体就需要处理，也就是附加了 Tags.Persistent 的物体。
        // 还有一些是游戏过程代码New出来的，没有 EntityData 的也需要处理，例如 BadelinDummy 和 SlashFx
        public static void EntitiesSavedButNotLoaded(Level level, Dictionary<EntityId2, Entity> savedEntities) {
            foreach (var pair in savedEntities) {
                Entity savedEntity = pair.Value;

                if (savedEntity.GetEntityData() != null) {
                    Type type = savedEntity.GetType();

                    object loaded = type.GetConstructor(new[] {typeof(EntityData), typeof(Vector2)})
                        ?.Invoke(new object[] {savedEntity.GetEntityData(), Vector2.Zero}) ?? type
                        .GetConstructor(new[] {typeof(EntityData), typeof(Vector2), typeof(EntityID)})
                        ?.Invoke(new object[] {
                            savedEntity.GetEntityData(), Vector2.Zero,
                            savedEntity.GetEntityId2().EntityId
                        });

                    if (loaded is Entity loadedEntity) {
                        loadedEntity.Position = savedEntity.Position;
                        loadedEntity.CopyEntityData(savedEntity);
                        loadedEntity.CopyEntityId2(savedEntity);
                        level.Add(loadedEntity);
                    }
                } else if (savedEntity.GetType().IsSubclassOf(typeof(Entity))) {
                    if (CreateEntityCopy(savedEntity) is Entity entity) {
                        level.Add(entity);
                    }
                }
            }
        }

        public static Entity CreateEntityCopy(Entity savedEntity) {
            Entity loadedEntity = null;
            switch (savedEntity) {
                // 先将范围限定在 Entity 的子类，如果出现问题再说
                case AbsorbOrb absorbOrb:
                    // TODO AbsorbOrb
                    break;
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
                case BirdTutorialGui birdTutorialGui:
                    loadedEntity = birdTutorialGui.Clone();
                    break;
                // BUG: SoundEmitter 不知道为何创建后也不能从 Level 里查找到，所以找出重复的只还原第一个
                case SoundEmitter soundEmitter:
                    loadedEntity = SoundEmitter.Play(soundEmitter.Source.EventName,
                        new Entity(soundEmitter.Position));
                    if (SoundSourceAction.PlayingSoundSources.FirstOrDefault(source =>
                        source.EventName == soundEmitter.Source.EventName) == null) {
                        (loadedEntity as SoundEmitter)?.Source.CopySpecifiedType(soundEmitter.Source);
                    } else {
                        (loadedEntity as SoundEmitter)?.Source.Stop();
                    }

                    break;
                case Debris debris:
                    loadedEntity = Engine.Pooler.Create<Debris>()
                        .Init(debris.GetStartPosition(), (char) debris.GetField("tileset"),
                            (bool) debris.GetField("playSound"));
                    break;
                case Key _:
                    // let's level create the key.
                    // Level.orig_LoadLevel: foreach (EntityID key in Session.Keys) Add(new Key(player, key)); 
                    break;
                case TalkComponent.TalkComponentUI _:
                    // ignore
                    break;
                default:
                    if (savedEntity.GetType().FullName == "Celeste.MoveBlock+Debris") {
                        loadedEntity = (savedEntity as Actor).CloneMoveBlockDebris();
                    } else if (savedEntity.ForceCreateInstance("EntitiesSavedButNotLoaded") is Entity newEntity) {
                        loadedEntity = newEntity;
                    }

                    break;
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