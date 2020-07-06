using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.SpeedrunTool.Extensions;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.RestoreActions {
    public static class RestoreEntityUtils {
        private static bool IsLoadStart => StateManager.Instance.IsLoadStart;
        private static bool IsLoadComplete => StateManager.Instance.IsLoadComplete;
        private static Level SavedLevel => StateManager.Instance.SavedLevel;

        private static readonly List<AbstractRestoreAction> RestoreActions = new List<AbstractRestoreAction> {
            new BoosterRestoreAction(),
            new CloudRestoreAction(),
            new FlyFeatherRestoreAction(),
            new KeyRestoreAction(),
            new PlayerRestoreAction(),
            new StrawberryRestoreAction(),
            // TODO CoroutineAction 需要放在最后
        };

        private static ILHook origLoadLevelHook;
        private static ILHook LoadCustomEntityHook;

        private static readonly List<Type> ExcludeTypes = new List<Type> {
            typeof(ParticleSystem),
            typeof(Wire),
            typeof(Cobweb),
            typeof(Decal),
            typeof(Lamp),
            typeof(HangingLamp),
        };

        private static readonly List<string> ExcludeTypeNames = new List<string> {
            "Celeste.CrystalStaticSpinner+Border",
            "Celeste.DustGraphic+Eyeballs",
            "Celeste.TalkComponent+TalkComponentUI",
            "Celeste.ZipMover+ZipMoverPathRenderer",
        };

        private static void EntityOnCtor_Vector2(On.Monocle.Entity.orig_ctor_Vector2 orig, Entity self,
            Vector2 position) {
            orig(self, position);

            if (!(Engine.Scene is LevelLoader) && !(Engine.Scene is Level)) return;

            Type type = self.GetType();
            if (type.Namespace != "Celeste") return;
            if (ExcludeTypes.Contains(type)) return;
            if (ExcludeTypeNames.Contains(type.FullName)) return;

            self.TrySetEntityId2(position.ToString());
        }

        // 将 EntityData 与 EntityID 附加到 Entity 的实例上
        private static void ModOrigLoadLevel(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.HasParameters &&
                     m.Parameters.Any(parameter => parameter.ParameterType.Name == "EntityData"))) {
                if (cursor.TryFindPrev(out var results,
                    i => i.OpCode == OpCodes.Ldloc_S && i.Operand is VariableDefinition v &&
                         v.VariableType.Name == "EntityData")) {
                    // cursor.Previous.Log();
                    cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldloc_S, results[0].Next.Operand);
                    cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityId);
                }
            }
        }

        // 将 EntityData 与 EntityID 附加到 Entity 的实例上
        private static void ModLoadCustomEntity(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            while (cursor.TryGotoNext(MoveType.After,
                i => i.OpCode == OpCodes.Newobj && i.Operand.ToString().Contains("::.ctor(Celeste.EntityData"))) {
                // cursor.Previous.Log();
                cursor.Emit(OpCodes.Dup).Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Action<Entity, EntityData>>(AttachEntityId);
            }
        }

        private static void AttachEntityId(Entity entity, EntityData data) {
            entity.SetEntityId2(data.ToEntityId2(entity));
            entity.SetEntityData(data);
            
            entity.Log("IL Set EntityId2: ", entity.GetEntityId2().ToString());
        }
        
        private delegate void Found(AbstractRestoreAction restoreAction, Entity loaded, Entity saved);

        private delegate void NotFound(AbstractRestoreAction restoreAction, Entity loaded);

        private static void InvokeAction(Entity loaded, Found found, NotFound notFound = null) {
            RestoreActions.ForEach(restoreAction => {
                if (restoreAction.Type != loaded.GetType()) return;
                if (loaded.NoEntityId2()) return;

                if (SavedLevel.FindFirst(loaded.GetEntityId2()) is Entity saved) {
                    found(restoreAction, loaded, saved);
                } else {
                    notFound?.Invoke(restoreAction, loaded);
                }
            });
        }

        private static void EntityOnAdded(On.Monocle.Entity.orig_Added orig, Entity self, Scene scene) {
            orig(self, scene);


            if (IsLoadStart) {
                InvokeAction(self, (action, loaded, saved) => action.Added(loaded, saved));
            }
        }

        private static void EntityOnAwake(On.Monocle.Entity.orig_Awake orig, Entity self, Scene scene) {
            orig(self, scene);

            if (IsLoadStart) {
                InvokeAction(self, (action, loaded, saved) => action.Awake(loaded, saved));
            }
        }

        private static void LevelOnBegin(On.Celeste.Level.orig_Begin orig, Level self) {
            orig(self);
            if (!IsLoadStart) return;

            RestoreActions.ForEach(restoreAction => {
                List<Entity> loadedEntityList = self.Entities.FindAll<Entity>()
                    .Where(entity => entity.GetType() == restoreAction.Type).ToList();
                List<Entity> savedEntityList = SavedLevel.Entities.FindAll<Entity>()
                    .Where(entity => entity.GetType() == restoreAction.Type).ToList();

                List<Entity> entityNotExistInLevel = savedEntityList.Where(saved =>
                    !loadedEntityList.Any(loaded => loaded.GetEntityId2().Equals(saved.GetEntityId2()))).ToList();
                if (entityNotExistInLevel.Count > 0) {
                    restoreAction.CantFoundLoadedEntity(self, entityNotExistInLevel);
                }
            });
        }

        public static void AfterEntityCreateAndUpdate1Frame(Level level) {
            RestoreActions.ForEach(restoreAction => {
                var loadedDict = level.FindAllToDict(restoreAction.Type);
                var savedDict = SavedLevel.FindAllToDict(restoreAction.Type);
                
                foreach (var loaded in loadedDict) {
                    if (savedDict.ContainsKey(loaded.Key)) {
                        restoreAction.AfterEntityCreateAndUpdate1Frame(loaded.Value, savedDict[loaded.Key]);
                    } else {
                        restoreAction.CantFoundSavedEntity(loaded.Value);
                    }

                }
            });
        }
        
        public static void AfterPlayerRespawn(Level level) {
            RestoreActions.ForEach(restoreAction => {
                var loadedDict = level.FindAllToDict(restoreAction.Type);
                var savedDict = SavedLevel.FindAllToDict(restoreAction.Type);
                
                foreach (var loaded in loadedDict) {
                    if (savedDict.ContainsKey(loaded.Key)) {
                        restoreAction.AfterPlayerRespawn(loaded.Value, savedDict[loaded.Key]);
                    }

                }
            });
        } 

        public static void Load() {
            On.Monocle.Entity.ctor_Vector2 += EntityOnCtor_Vector2;
            origLoadLevelHook = new ILHook(typeof(Level).GetMethod("orig_LoadLevel"), ModOrigLoadLevel);
            LoadCustomEntityHook = new ILHook(typeof(Level).GetMethod("LoadCustomEntity"), ModLoadCustomEntity);
            
            On.Monocle.Entity.Added += EntityOnAdded;
            On.Monocle.Entity.Awake += EntityOnAwake;
            On.Celeste.Level.Begin += LevelOnBegin;
            RestoreActions.ForEach(action => action.Load());
        }

        public static void Unload() {
            On.Monocle.Entity.ctor_Vector2 -= EntityOnCtor_Vector2;
            origLoadLevelHook.Dispose();
            On.Monocle.Entity.Added -= EntityOnAdded;
            // On.Celeste.LevelData.ctor -= LevelDataOnCtor;
            
            On.Monocle.Entity.Awake -= EntityOnAwake;
            On.Celeste.Level.Begin -= LevelOnBegin;
            RestoreActions.ForEach(action => action.Unload());
        }
    }
}