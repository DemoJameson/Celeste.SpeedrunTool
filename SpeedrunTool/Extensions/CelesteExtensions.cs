using System.Collections.Generic;
using Celeste.Mod.SpeedrunTool.SaveLoad.EntityIdPlus;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.Extensions {
    public static class CelesteExtensions {
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

        public static bool TrySetEntityId2(this Entity entity, params string[] id) {
            if (entity.NoEntityId2()) {
                Session session = GetSession();
                if (session?.Level == null) {
                    return false;
                }

                EntityId2 entityId = entity.CreateEntityId2(id);
                entity.SetEntityId2(entityId);
                return true;
            }

            return false;
        }

        public static EntityId2 CreateEntityId2(this Entity entity, params string[] id) {
            Session session = GetSession();
            if (session?.Level == null) {
                return default;
            }

            return new EntityID(session.Level, (string.Join("-", id)).GetHashCode()).ToEntityId2(entity);
        }

        // TODO 重写，或许不需要这个方法
        public static Dictionary<EntityId2, T> GetDictionary<T>(this IEnumerable<T> enumerable) where T : Entity {
            Dictionary<EntityId2, T> result = new Dictionary<EntityId2, T>();
            foreach (T entity in enumerable) {
                if (entity.NoEntityId2()) {
                    continue;
                }

                EntityId2 entityId2 = entity.GetEntityId2();
                if (result.ContainsKey(entityId2)) {
                    Logger.Log("Speedrun Tool", $"EntityId2 Duplication: {entityId2}");
                    continue;
                }

                result[entityId2] = entity;
            }

            return result;
        }

        public static void CopyFrom(this Tween tween, Tween otherTween) {
            tween.SetProperty("TimeLeft", otherTween.TimeLeft);
            tween.SetProperty("Reverse", otherTween.Reverse);
        }

        public static void AddRange<T>(this Dictionary<EntityId2, T> dict, IEnumerable<T> entities) where T : Entity {
            foreach (T entity in entities) {
                EntityId2 entityId = entity.GetEntityId2();
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

        public static Tween GetTween<T>(this T entity, string fieldName) where T : Entity {
            return entity.GetField(typeof(T), fieldName) as Tween;
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