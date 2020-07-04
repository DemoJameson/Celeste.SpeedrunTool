using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {
    class CoroutineAction : AbstractEntityAction {
        private static readonly FieldInfo EnumeratorsFieldInfo =
            typeof(Coroutine).GetField("enumerators", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo FindFirstMethodInfo = typeof(EntityList).GetMethod("FindFirst");

        private static readonly Type DebrisListType =
            typeof(List<>).MakeGenericType(typeof(MoveBlock).GetNestedType("Debris", BindingFlags.NonPublic));

        private class Routine {
            public readonly EntityID ID;
            public readonly Type type;
            public readonly FieldInfo[] fields;
            public readonly object[] locals;
            public readonly float waitTimer;
            public readonly bool removeOnComplete;
            public Routine parent;
            public bool IsFromState { get; set; }
            public int State { get; set; }

            public Routine(EntityID ID, Type type, FieldInfo[] fields, object[] locals, float waitTimer,
                bool removeOnComplete, Routine parent) {
                this.ID = ID;
                this.type = type;
                this.fields = fields;
                this.locals = locals;
                this.waitTimer = waitTimer;
                this.removeOnComplete = removeOnComplete;
                this.parent = parent;
            }
        }

        private static List<Routine> loadedRoutines = new List<Routine>();

        public static bool HasRoutine(string name) {
            foreach (Routine routine in loadedRoutines) {
                if (routine.type.Name.Contains(name))
                    return true;
            }

            return false;
        }

        private static readonly List<Type> ExcludeTypes = new List<Type> {
            typeof(FlutterBird),
            typeof(ForsakenCitySatellite),
            typeof(Lightning),
            typeof(LightningBreakerBox),
            typeof(Lookout),
        };

        public override void OnQuickSave(Level level) {
            foreach (Entity e in level.Entities) {
                if (ExcludeTypes.Contains(e.GetType())) continue;
                if (e.NoEntityID()) continue;

                EntityID id = e.GetEntityId();
                foreach (Monocle.Component component in e.Components) {
                    if (component is Coroutine coroutine) {
                        SaveCoroutine(coroutine, id);
                    } else if (component is StateMachine state) {
                        coroutine = (Coroutine) state.GetField("currentCoroutine");
                        if (coroutine != null && SaveCoroutine(coroutine, id)) {
                            loadedRoutines[loadedRoutines.Count - 1].IsFromState = true;
                            loadedRoutines[loadedRoutines.Count - 1].State = state.State;
                        }
                    }
                }
            }

            loadedRoutines.Reverse();
        }

        private bool SaveCoroutine(Coroutine coroutine, EntityID id) {
            Stack<IEnumerator> enumerators = (Stack<IEnumerator>) EnumeratorsFieldInfo.GetValue(coroutine);
            
            // Dont Save Mod's Coroutine
            // Fixed NullReferenceException: Celeste.Mod.MaxHelpingHand.Entities.FlagTouchSwitch.<onSeekerRegenerateCoroutine>d__5.MoveNext()
            List<IEnumerator> enumeratorList = enumerators.Where(enumerator => {
                string fullName = enumerator.GetType().FullName ?? "";
                return fullName.StartsWith("Celeste.") && !fullName.StartsWith("Celeste.Mod");
            }).ToList();
            
            int initialCount = enumeratorList.Count;
            for (int j = 0; j < initialCount; j++) {
                IEnumerator enumerator = enumeratorList[j];

                if (enumerator.GetType().Assembly == Assembly.GetExecutingAssembly())
                    return false;

                GetCoroutineLocals(id, enumerator, out Type routineType, out FieldInfo[] routineFields,
                    out object[] routineLocals);

                // When a coroutine yields a float, Coroutine.Update() waits that long before calling it again.
                // Backup that as well.
                float routineTimer = (float) coroutine.GetField("waitTimer");

                Routine routine = new Routine(id, routineType, routineFields, routineLocals, routineTimer,
                    coroutine.RemoveOnComplete, null);
                
                // If the coroutine called another coroutine, record the calling coroutine.
                if (j > 0) {
                    loadedRoutines.Last().parent = routine;
                }
                
                loadedRoutines.Add(routine);
            }

            return initialCount > 0;
        }

        private void GetCoroutineLocals(EntityID id, object routine, out Type routineType, out FieldInfo[] routineFields,
            out object[] routineLocals) {
            // In Coroutine.ctor(IEnumerator):
            // The compiler creates a new class for the IEnumerator method call.
            // Calling the method itself just calls the constructor for that class.

            // Get the compiler-generated class.
            routineType = routine.GetType();
            // Locals in coroutine methods are stored as private fields in the generated class.
            // This includes a state variable, which is used in a switch statement at the beginning of the method.
            // It controls where in the method to start at.
            // Back up all the "locals" so we can restore them later.
            routineFields = routineType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            routineLocals = new object[routineFields.Length];
            for (int i = 0; i < routineFields.Length; i++) {
                FieldInfo local = routineFields[i];
                object value = routine.GetField(local.Name);
                if (local.FieldType.Name.StartsWith("<")) {
                    GetCoroutineLocals(id, value, out Type _routineType, out FieldInfo[] _routineFields,
                        out object[] _routineLocals);
                    routineLocals[i] = new Routine(id, _routineType, _routineFields, _routineLocals, 0f, false, null);
                } else {
                    routineLocals[i] = value;
                }
            }
        }
        
        private object LocalsSpecialCases(object value) {
            object foundValue = null;
            if (value is List<MTexture> mTextures) {
                foundValue = new List<MTexture>(mTextures);
            } else if (value?.GetType() == DebrisListType)
                foundValue = Convert.ChangeType(value, DebrisListType);
            else if (value is Image) {
                foundValue = new Image(new MTexture());
            } else if (value is SoundEmitter soundEmitter) {
                foundValue = SoundEmitter.Play(soundEmitter.Source.EventName, new Entity(soundEmitter.Position));
            } else if (value is Tween tween) {
                var foundTween = Tween.Create(tween.Mode, tween.Easer, tween.Duration);
                foundTween.CopyFrom(tween);
                foundValue = foundTween;
            }
            //doesn't actually work properly i think
            else if (value is Delegate @delegate) {
                foundValue = @delegate.Clone();
            }

            return foundValue;
        }

        public override void OnQuickLoading(Level level, Player player, Player savedPlayer) {
            var entities = level.Entities.GetDictionary<Entity>();
            Coroutine coroutine = null;
            foreach (Routine routine in loadedRoutines) {
                ConstructorInfo routineCtor = routine.type.GetConstructor(new Type[] {typeof(int)});
                IEnumerator functionCall = (IEnumerator) routineCtor.Invoke(new object[] {0});
                if (entities.TryGetValue(routine.ID, out Entity e)) {
                    if (routine.parent != null) {
                        var enumerators = (Stack<IEnumerator>) coroutine.GetField("enumerators");
                        enumerators.Push(functionCall);
                    } else if (routine.IsFromState) {
                        StateMachine state = e.Get<StateMachine>();
                        coroutine = new Coroutine(functionCall, routine.removeOnComplete);
                        coroutine.Active = true;
                        state.SetField("currentCoroutine", coroutine);
                        state.SetField("state", routine.State);
                    } else {
                        coroutine = new Coroutine(functionCall, routine.removeOnComplete);
                        e.Components.Add(coroutine);
                    }

                    if (e is CrushBlock) {
                        e.SetField(typeof(CrushBlock), "attackCoroutine", coroutine);
                    } else if (e is FinalBossMovingBlock) {
                        e.SetField(typeof(FinalBossMovingBlock), "moveCoroutine", coroutine);
                    }

                    coroutine.SetField("waitTimer", routine.waitTimer);
                }

                SetCoroutineLocals(level, entities, routine, functionCall);
            }
        }

        private void SetCoroutineLocals(Level level, Dictionary<EntityID, Entity> entities, Routine routine,
            object routineObj) {
            for (int i = 0; i < routine.fields.Length; i++) {
                FieldInfo field = routine.fields[i];
                object local = routine.locals[i];
                object foundValue = LocalsSpecialCases(local);
                
                if (foundValue != null) {
                    routineObj.SetField(routineObj.GetType(), field.Name, foundValue);
                    continue;
                }
                
                if (local is Entity entity) {
                    if (entities.TryGetValue(entity.GetEntityId(), out Entity e)) {
                        foundValue = e;
                    }
                    // If the entity doesn't store its ID, find the first entity of that type.
                    // This may break some sound related stuff but I think that's it.
                    else {
                        MethodInfo findFirst = FindFirstMethodInfo.MakeGenericMethod(field.FieldType);
                        object value = findFirst.Invoke(level.Entities, new object[0]);
                        foundValue = value;
                    }

                    if (foundValue == null) {
                        Logger.Log("SpeedrunTool",
                            $"\nCan't Restore Coroutine Locals:\nroutineType={routine.type}\nfield={field}\nlocal={local}");
                    }
                } else if (field.FieldType == typeof(Level))
                    foundValue = level;
                else if (local is ValueType)
                    foundValue = local;
                // It isn't actually a routine but a object of a compiler-generated type stored as a routine.
                else if (local is Routine _routine) {
                    ConstructorInfo routineCtor = _routine.type.GetConstructor(new Type[0]);
                    object obj = routineCtor.Invoke(new object[0]);
                    SetCoroutineLocals(level, entities, _routine, obj);
                    foundValue = obj;
                }

                if (foundValue != null) {
                    routineObj.SetField(routineObj.GetType(), field.Name, foundValue);
                }
            }
        }

        public override void OnClear() => loadedRoutines = new List<Routine>();
        public override void OnLoad() { }

        public override void OnUnload() { }
    }
}