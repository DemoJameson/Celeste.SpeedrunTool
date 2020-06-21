using Celeste.Mod.SpeedrunTool.Extensions;
using Monocle;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.SpeedrunTool.SaveLoad.Actions {

    class CoroutineAction {

        private class Routine {
            public EntityID ID;
            public Type routine;
            public FieldInfo[] fields;
            public object[] locals;
            public float waitTimer;
            public bool removeOnComplete;
            public Routine parent;
            public bool IsFromState { get; set; } = false;
            public int State { get; set; } = 0;
            public Routine(EntityID ID, Type routine, FieldInfo[] fields, object[] locals, float waitTimer, bool removeOnComplete, Routine parent) {
                this.ID = ID;
                this.routine = routine;
                this.fields = fields;
                this.locals = locals;
                this.waitTimer = waitTimer;
                this.removeOnComplete = removeOnComplete;
                this.parent = parent;
            }
        }

        List<Routine> loadedRoutines = new List<Routine>();

        bool IsLoading => StateManager.Instance.IsLoading;

        public void OnQuickSave(Level level) {
            foreach (Entity e in level.Entities) {
                EntityID id = e.GetEntityId();
                foreach (Monocle.Component component in e.Components) {
                    if (component is Coroutine coroutine) {
                        SaveCoroutine(coroutine, id);
                    }
                    else if (component is StateMachine state) {
                        coroutine = (Coroutine)state.GetField("currentCoroutine");
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
            object enumerators = typeof(Coroutine).GetField("enumerators", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(coroutine);
            int initialCount = ((Stack<IEnumerator>)enumerators).Count;
            for (int j = 0; j < initialCount; j++) {
                IEnumerator routine = ((Stack<IEnumerator>)enumerators).Pop();

				if (routine.GetType().Assembly == Assembly.GetExecutingAssembly())
					return false;

				GetCoroutineLocals(id, routine, out Type routineType, out FieldInfo[] routineFields, out object[] routineLocals);

                // When a coroutine yields a float, Coroutine.Update() waits that long before calling it again.
                // Backup that as well.
                float routineTimer = (float)coroutine.GetField("waitTimer");

                Routine toAdd = new Routine(id, routineType, routineFields, routineLocals, routineTimer, coroutine.RemoveOnComplete, null);
                loadedRoutines.Add(toAdd);
                // If the coroutine called another coroutine, record the calling coroutine.
                if (j > 0)
                    loadedRoutines[j - 1].parent = toAdd;
            }
            return initialCount > 0;
        }

        public void GetCoroutineLocals(EntityID id, object routine, out Type routineType, out FieldInfo[] routineFields, out object[] routineLocals) {
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
				// Store the value of value types.
				if (value is ValueType)
					routineLocals[i] = value;
				// Store the EntityID of entities.
				else if (value is Entity entity)
					routineLocals[i] = entity.GetEntityId();
				// If it's a compiler-generated type (ex: BadelineBoost.BoostRoutine) back that up too.
				else if (local.FieldType.Name.StartsWith("<")) {
					GetCoroutineLocals(id, value, out Type _routineType, out FieldInfo[] _routineFields, out object[] _routineLocals);
					routineLocals[i] = new Routine(id, _routineType, _routineFields, _routineLocals, 0f, false, null);
				}
				else
					LocalsSpecialCases(value, out routineLocals[i], false);

            }
        }

		public void LocalsSpecialCases(object value, out object foundValue, bool loading) {
			foundValue = null;

			if (value is List<MTexture>) {
				foundValue = new List<MTexture>(value as List<MTexture>);
			}
			else if (value is Image) {
				foundValue = new Image(new MTexture());
			}
			else if (value is SoundEmitter) {
				foundValue = SoundEmitter.Play((value as SoundEmitter).Source.EventName);
			}
			else if (value is Tween) {
				foundValue = Tween.Create((value as Tween).Mode);
			}
			else if (value is Delegate) {
				foundValue = (value as Delegate).Clone();
			}
		}

		public void OnQuickLoad(Level level) {
            var entities = level.Entities.GetDictionary<Entity>();
            Coroutine coroutine = null;
            foreach (Routine routine in loadedRoutines) {
                ConstructorInfo routineCtor = routine.routine.GetConstructor(new Type[] { typeof(int) });
                IEnumerator functionCall = (IEnumerator)routineCtor.Invoke(new object[] { 0 });
                if (entities.TryGetValue(routine.ID, out Entity e)) {
                    if (routine.parent != null) {
                        var enumerators = (Stack<IEnumerator>)coroutine.GetField("enumerators");
                        enumerators.Push(functionCall);
                    }
                    else if (routine.IsFromState) {
                        StateMachine state = e.Components.Get<StateMachine>();
                        coroutine = new Coroutine(functionCall, routine.removeOnComplete);
                        coroutine.Active = true;
                        state.SetField("currentCoroutine", coroutine);
                        state.SetField("state", routine.State);
                    }
                    else {
                        coroutine = new Coroutine(functionCall, routine.removeOnComplete);
                        e.Components.Add(coroutine);
                    }
                    coroutine.SetField("waitTimer", routine.waitTimer);
                }
                SetCoroutineLocals(level, entities, routine, functionCall);
            }
        }

        private void SetCoroutineLocals(Level level, Dictionary<EntityID, Entity> entities, Routine routine, object routineObj) {
            for (int i = 0; i < routine.fields.Length; i++) {
                FieldInfo field = routine.fields[i];
                object foundValue = null;
				if (routine.locals[i] is EntityID storedID)
					if (entities.TryGetValue(storedID, out Entity e))
						foundValue = e;
					// If the entity doesn't store its ID, find the first entity of that type.
					// This may break some sound related stuff but I think that's it.
					else {
						MethodInfo findFirst = typeof(EntityList).GetMethod("FindFirst").MakeGenericMethod(field.FieldType);
						object value = findFirst.Invoke(level.Entities, new object[0]);
						foundValue = value;
					}
				else if (field.FieldType == typeof(Level))
					foundValue = level;
				else if (routine.locals[i] is ValueType)
					foundValue = routine.locals[i];
				// It isn't actually a routine but a object of a compiler-generated type stored as a routine.
				else if (routine.locals[i] is Routine _routine) {
					ConstructorInfo routineCtor = _routine.routine.GetConstructor(new Type[0]);
					object obj = routineCtor.Invoke(new object[0]);
					SetCoroutineLocals(level, entities, _routine, obj);
					foundValue = obj;
				}
				else LocalsSpecialCases(routine.locals[i], out foundValue, true);
				if (foundValue != null)
	                routineObj.SetField(routineObj.GetType(), field.Name, foundValue);
            }
        }



        public void OnClear() => loadedRoutines = new List<Routine>();
    }
}
