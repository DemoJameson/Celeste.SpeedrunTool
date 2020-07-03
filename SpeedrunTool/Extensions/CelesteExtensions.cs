using System.Collections.Generic;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class CelesteExtensions {
        private const string EntityIdKey = "SpeedrunToolEntityId";
        private const string EntityDataKey = "SpeedrunToolEntityDataKey";

        // public static void AddToTracker(this Type type) {
        //     if (!Tracker.StoredEntityTypes.Contains(type)) {
        //         Tracker.StoredEntityTypes.Add(type);
        //     }
        //
        //     if (!Tracker.TrackedEntityTypes.ContainsKey(type)) {
        //         Tracker.TrackedEntityTypes[type] = new List<Type> {type};
        //     }
        //     else if (!Tracker.TrackedEntityTypes[type].Contains(type)) {
        //         Tracker.TrackedEntityTypes[type].Add(type);
        //     }
        // }

        public static void SetEntityId(this Entity entity, EntityID entityId) {
            entity.SetExtendedDataValue(EntityIdKey, entityId);
        }

        public static void SetEntityId(this Entity entity, EntityData entityData) {
            entity.SetExtendedDataValue(EntityIdKey, entityData.ToEntityId());
        }

        public static EntityID GetEntityId(this Entity entity) {
            return entity.GetExtendedDataValue<EntityID>(EntityIdKey);
        }

        public static void SetEntityData(this Entity entity, EntityData entityData) {
            entity.SetExtendedDataValue(EntityDataKey, entityData);
        }

        public static void TrySetEntityId(this Entity entity, params string[] id) {
            EntityID entityId = entity.GetEntityId();
            if (entityId.IsDefault()) {
                Session session = GetSession();
                if (session?.Level == null) {
                    return;
                }

                entityId = entity.CreateEntityId(id);
                entity.SetEntityId(entityId);
            }
        }

        public static bool NoEntityID(this Entity entity) {
            return entity.GetEntityId().IsDefault();
        }

        public static bool HasEntityID(this Entity entity) {
            return !entity.NoEntityID();
        }

        public static EntityID CreateEntityId(this Entity entity, params string[] id) {
            Session session = GetSession();
            if (session?.Level == null) {
                return default;
            }

            return new EntityID(session.Level, (entity.GetType().FullName + "-" + string.Join("-", id)).GetHashCode());
        }

        public static EntityData GetEntityData(this Entity entity) {
            return entity.GetExtendedDataValue<EntityData>(EntityDataKey);
        }

        public static bool IsDefault(this EntityID entityId) {
            return entityId.Equals(default(EntityID));
        }

        public static EntityID ToEntityId(this EntityData entityData) {
            return new EntityID(entityData.Level.Name, entityData.ID);
        }

        public static T FindFirst<T>(this EntityList entityList, EntityID entityId) where T : Entity {
            if (entityId.IsDefault()) return null;
            
            var dictionary = entityList.GetDictionary<T>();
            return dictionary.ContainsKey(entityId) ? dictionary[entityId] : null;
        }
        
        public static T FindFirst<T>(this EntityList entityList, T entity) where T : Entity {
            return entity == null ? null : entityList.FindFirst<T>(entity.GetEntityId());
        }
        
        public static Dictionary<EntityID, T> GetDictionary<T>(this EntityList entityList) where T : Entity {
            Dictionary<EntityID, T> result = new Dictionary<EntityID, T>();
            foreach (T entity in entityList.FindAll<T>()) {
                EntityID entityId = entity.GetEntityId();
                if (entity.NoEntityID()) {
                    continue;
                }

                if (result.ContainsKey(entityId)) {
                    Logger.Log("Speedrun Tool",
                        $"EntityID Duplication: ID={entityId.ID} Level Name={entityId.Level}, Entity Name={entity.GetType().FullName}, Position={entity.Position}");
                    continue;
                }

                result[entityId] = entity;
            }

            return result;
        }
        
        public static Dictionary<EntityID, T> GetDictionary<T>(this IEnumerable<T> enumerable) where T : Entity {
            Dictionary<EntityID, T> result = new Dictionary<EntityID, T>();
            foreach (T entity in enumerable) {
                EntityID entityId = entity.GetEntityId();
                if (entity.NoEntityID()) {
                    continue;
                }

                if (result.ContainsKey(entityId)) {
                    Logger.Log("Speedrun Tool",
                        $"EntityID Duplication: ID={entityId.ID} Level Name={entityId.Level}, Entity Name={entity.GetType().FullName}, Position={entity.Position}");
                    continue;
                }

                result[entityId] = entity;
            }

            return result;
        }

        public static void SetTime(this SoundSource soundSource, int time) {
            object eventInstance = soundSource.GetField("instance");
            eventInstance.GetType().GetMethod("setTimelinePosition")?.Invoke(eventInstance, new object[] {time});
        }

        public static void CopyEntity<T>(this Player player, Player savedPlayer, string fieldName) where T: Entity{
            if (player.SceneAs<Level>().Entities.FindFirst(savedPlayer.GetField(fieldName) as T) is T entity) {
                player.SetField(fieldName, entity);
            }
        }

        public static void CopyFrom(this Tween tween, Tween otherTween) {
            tween.SetProperty("TimeLeft", otherTween.TimeLeft);
            tween.SetProperty("Reverse", otherTween.Reverse);
        }

        public static void AddRange<T>(this Dictionary<EntityID, T> dict, IEnumerable<T> entities) where T : Entity {
            foreach (T entity in entities) {
                EntityID entityId = entity.GetEntityId();
                if (!dict.ContainsKey(entityId)) {
                    dict[entityId] = entity;
                }
            }
        }

        public static Player GetPlayer(this Scene scene) {
            if (scene is Level level && level.Entities.FindFirst<Player>() is Player player) {
                return player;
            }

            return null;
        }

        public static Sprite GetSprite<T>(this T entity, string fieldName) where T : Entity {
            return entity.GetField(typeof(T), fieldName) as Sprite;
        }

        public static void CopySprite<T>(this T entity, T otherEntity, string fieldName) where T : Entity {
            var sprite = entity.GetSprite(fieldName);
            if (sprite == null) {
                return;
            }

            var otherSprite = otherEntity.GetSprite(fieldName);
            if (otherSprite == null) {
                return;
            }

            sprite._CopySprite(otherSprite);
        }

        private static void _CopySprite(this Sprite sprite, Sprite otherSprite) {
            sprite.CopyGraphicsComponent(otherSprite);
            otherSprite.InvokeMethod("CloneInto", sprite);
            sprite.Rate = otherSprite.Rate;
            sprite.UseRawDeltaTime = otherSprite.UseRawDeltaTime;
        }
        
        private static void CopyPlayerSprite(this PlayerSprite sprite, PlayerSprite otherSprite) {
            sprite.HairCount = otherSprite.HairCount;
            sprite._CopySprite(otherSprite);
        }
        
        public static void CopyPlayerHairAndSprite(this PlayerHair hair, PlayerHair otherHair) {
            hair.CopyComponent(otherHair);
            hair.Alpha = otherHair.Alpha;
            hair.Facing = otherHair.Facing;
            hair.DrawPlayerSpriteOutline = otherHair.DrawPlayerSpriteOutline;
            hair.SimulateMotion = otherHair.SimulateMotion;
            hair.StepPerSegment = otherHair.StepPerSegment;
            hair.StepInFacingPerSegment = otherHair.StepInFacingPerSegment;
            hair.StepApproach = otherHair.StepApproach;
            hair.StepYSinePerSegment = otherHair.StepYSinePerSegment;
            hair.Nodes.Clear();
            hair.Nodes.AddRange(otherHair.Nodes);
            hair.CopyFields(otherHair, "wave");
            hair.Sprite.CopyPlayerSprite(otherHair.Sprite);
        }
        
        public static TileGrid GetTileGrid<T>(this T entity, string fieldName) where T : Entity {
            return entity.GetField(fieldName) as TileGrid;
        }
        
        public static void CopyTileGrid<T>(this T entity, T otherEntity, string fieldName) where T : Entity {
            var tileGrid = entity.GetTileGrid(fieldName);
            if (tileGrid == null) {
                return;
            }

            var otherTileGrid = otherEntity.GetTileGrid(fieldName);
            if (otherTileGrid == null) {
                return;
            }

            tileGrid.CopyComponent(otherTileGrid);
            tileGrid.Position = otherTileGrid.Position;
            tileGrid.Color = otherTileGrid.Color;
            tileGrid.VisualExtend = otherTileGrid.VisualExtend;
            tileGrid.ClipCamera = otherTileGrid.ClipCamera;
            tileGrid.Alpha = otherTileGrid.Alpha;
            // tileGrid.Tiles = otherTileGrid.Tiles;
        }

        public static Image GetImage<T>(this T entity, string fieldName) where T : Entity {
            return entity.GetField(fieldName) as Image;
        }

        public static void CopyImage<T>(this T entity, T otherEntity, string fieldName) where T : Entity {
            var image = entity.GetImage(fieldName);
            if (image == null) {
                return;
            }

            var otherImage = otherEntity.GetImage(fieldName);
            if (otherImage == null) {
                return;
            }

            image.CopyGraphicsComponent(otherImage);
        }

        private static void CopyComponent(this Component component, Component otherComponent) {
            component.Active = otherComponent.Active;
            component.Visible = otherComponent.Visible;
        }

        public static void CopyGraphicsComponent(this GraphicsComponent graphicsComponent,
            GraphicsComponent otherGraphicsComponent) {
            graphicsComponent.Scale = otherGraphicsComponent.Scale;
            graphicsComponent.Color = otherGraphicsComponent.Color;
            graphicsComponent.Position = otherGraphicsComponent.Position;
            graphicsComponent.Origin = otherGraphicsComponent.Origin;
            graphicsComponent.Rotation = otherGraphicsComponent.Rotation;
            graphicsComponent.Effects = otherGraphicsComponent.Effects;
            CopyComponent(graphicsComponent, otherGraphicsComponent);
        }

        public static void CopyImageList<T>(this T entity, T otherEntity, string fieldName) where T : Entity {
            var imageList = entity.GetField(fieldName) as List<Image>;
            var otherImageList = otherEntity.GetField(fieldName) as List<Image>;
            if (imageList == null || otherImageList == null || imageList.Count != otherImageList.Count) {
                return;
            }

            for (var i = 0; i < imageList.Count; i++) {
                imageList[i].CopyGraphicsComponent(otherImageList[i]);
            }
        }

        public static Level GetLevel() {
            if (Engine.Scene is Level level) {
                return level;
            }

            if (Engine.Scene is LevelLoader levelLoader) {
                return levelLoader.Level;
            }

            return null;
        }

        public static Session GetSession() {
            return GetLevel()?.Session;
        }
    }
}