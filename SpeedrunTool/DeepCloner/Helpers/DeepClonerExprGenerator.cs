using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Force.DeepCloner.Helpers {
    internal static class DeepClonerExprGenerator {
        private static readonly ConcurrentDictionary<FieldInfo, bool> _readonlyFields = new ConcurrentDictionary<FieldInfo, bool>();

        internal static object GenerateClonerInternal(Type realType, bool asObject) {
            return GenerateProcessMethod(realType, asObject && realType.IsValueType());
        }

        private static FieldInfo _attributesFieldInfo = typeof(FieldInfo).GetPrivateField("m_fieldAttributes");

        // slow, but hardcore method to set readonly field
        internal static void ForceSetField(FieldInfo field, object obj, object value) {
            FieldInfo fieldInfo = field.GetType().GetPrivateField("m_fieldAttributes");

            // TODO: think about it
            // nothing to do :( we should a throw an exception, but it is no good for user
            if (fieldInfo == null) {
                return;
            }

            object ov = fieldInfo.GetValue(field);
            if (!(ov is FieldAttributes)) {
                return;
            }

            FieldAttributes v = (FieldAttributes)ov;

            // protect from parallel execution, when first thread set field readonly back, and second set it to write value
            lock (fieldInfo) {
                fieldInfo.SetValue(field, v & ~FieldAttributes.InitOnly);
                field.SetValue(obj, value);
                fieldInfo.SetValue(field, v | FieldAttributes.InitOnly);
            }
        }

        private static object GenerateProcessMethod(Type type, bool unboxStruct) {
            if (type.IsArray) {
                return GenerateProcessArrayMethod(type);
            }

            if (type.FullName != null && type.FullName.StartsWith("System.Tuple`")) {
                // if not safe type it is no guarantee that some type will contain reference to
                // this tuple. In usual way, we're creating new object, setting reference for it
                // and filling data. For tuple, we will fill data before creating object
                // (in constructor arguments)
                Type[] genericArguments = type.GenericArguments();
                // current tuples contain only 8 arguments, but may be in future...
                // we'll write code that works with it
                if (genericArguments.Length < 10 && genericArguments.All(DeepClonerSafeTypes.CanReturnSameObject)) {
                    return GenerateProcessTupleMethod(type);
                }
            }

            Type methodType = unboxStruct || type.IsClass() ? typeof(object) : type;

            List<Expression> expressionList = new List<Expression>();

            ParameterExpression from = Expression.Parameter(methodType);
            ParameterExpression fromLocal = from;
            ParameterExpression toLocal = Expression.Variable(type);
            ParameterExpression state = Expression.Parameter(typeof(DeepCloneState));

            if (!type.IsValueType()) {
                MethodInfo methodInfo = typeof(object).GetPrivateMethod("MemberwiseClone");

                // to = (T)from.MemberwiseClone()
                expressionList.Add(Expression.Assign(toLocal, Expression.Convert(Expression.Call(from, methodInfo), type)));

                fromLocal = Expression.Variable(type);
                // fromLocal = (T)from
                expressionList.Add(Expression.Assign(fromLocal, Expression.Convert(from, type)));

                // added from -> to binding to ensure reference loop handling
                // structs cannot loop here
                // state.AddKnownRef(from, to)
                expressionList.Add(Expression.Call(state, typeof(DeepCloneState).GetMethod("AddKnownRef"), from, toLocal));
            }
            else {
                if (unboxStruct) {
                    // toLocal = (T)from;
                    expressionList.Add(Expression.Assign(toLocal, Expression.Unbox(from, type)));
                    fromLocal = Expression.Variable(type);
                    // fromLocal = toLocal; // structs, it is ok to copy
                    expressionList.Add(Expression.Assign(fromLocal, toLocal));
                }
                else {
                    // toLocal = from
                    expressionList.Add(Expression.Assign(toLocal, from));
                }
            }

            List<FieldInfo> fi = new List<FieldInfo>();
            Type tp = type;
            do {
#if !NETCORE
                // don't do anything with this dark magic!
                if (tp == typeof(ContextBoundObject)) {
                    break;
                }
#else
				if (tp.Name == "ContextBoundObject") break;
#endif

                fi.AddRange(tp.GetDeclaredFields());
                tp = tp.BaseType();
            } while (tp != null);

            foreach (FieldInfo fieldInfo in fi) {
                if (!DeepClonerSafeTypes.CanReturnSameObject(fieldInfo.FieldType)) {
                    MethodInfo methodInfo = fieldInfo.FieldType.IsValueType()
                        ? typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneStructInternal")
                            .MakeGenericMethod(fieldInfo.FieldType)
                        : typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneClassInternal");

                    MemberExpression get = Expression.Field(fromLocal, fieldInfo);

                    // toLocal.Field = Clone...Internal(fromLocal.Field)
                    Expression call = (Expression)Expression.Call(methodInfo, get, state);
                    if (!fieldInfo.FieldType.IsValueType()) {
                        call = Expression.Convert(call, fieldInfo.FieldType);
                    }

                    // should handle specially
                    // todo: think about optimization, but it rare case
                    bool isReadonly = _readonlyFields.GetOrAdd(fieldInfo, f => f.IsInitOnly);
                    if (isReadonly) {
                        // var setMethod = fieldInfo.GetType().GetMethod("SetValue", new[] { typeof(object), typeof(object) });
                        // expressionList.Add(Expression.Call(Expression.Constant(fieldInfo), setMethod, toLocal, call));
                        MethodInfo setMethod = typeof(DeepClonerExprGenerator).GetPrivateStaticMethod("ForceSetField");
                        expressionList.Add(Expression.Call(setMethod, Expression.Constant(fieldInfo), Expression.Convert(toLocal, typeof(object)),
                            Expression.Convert(call, typeof(object))));
                    }
                    else {
                        expressionList.Add(Expression.Assign(Expression.Field(toLocal, fieldInfo), call));
                    }
                }
            }

            expressionList.Add(Expression.Convert(toLocal, methodType));

            Type funcType = typeof(Func<,,>).MakeGenericType(methodType, typeof(DeepCloneState), methodType);

            List<ParameterExpression> blockParams = new List<ParameterExpression>();
            if (from != fromLocal) {
                blockParams.Add(fromLocal);
            }

            blockParams.Add(toLocal);

            return Expression.Lambda(funcType, Expression.Block(blockParams, expressionList), from, state).Compile();
        }

        private static object GenerateProcessArrayMethod(Type type) {
            Type elementType = type.GetElementType();
            int rank = type.GetArrayRank();

            MethodInfo methodInfo;

            // multidim or not zero-based arrays
            if (rank != 1 || type != elementType.MakeArrayType()) {
                if (rank == 2 && type == elementType.MakeArrayType(2)) {
                    // small optimization for 2 dim arrays
                    methodInfo = typeof(DeepClonerGenerator).GetPrivateStaticMethod("Clone2DimArrayInternal").MakeGenericMethod(elementType);
                }
                else {
                    methodInfo = typeof(DeepClonerGenerator).GetPrivateStaticMethod("CloneAbstractArrayInternal");
                }
            }
            else {
                string methodName = "Clone1DimArrayClassInternal";
                if (DeepClonerSafeTypes.CanReturnSameObject(elementType)) {
                    methodName = "Clone1DimArraySafeInternal";
                }
                else if (elementType.IsValueType()) {
                    methodName = "Clone1DimArrayStructInternal";
                }

                methodInfo = typeof(DeepClonerGenerator).GetPrivateStaticMethod(methodName).MakeGenericMethod(elementType);
            }

            ParameterExpression from = Expression.Parameter(typeof(object));
            ParameterExpression state = Expression.Parameter(typeof(DeepCloneState));
            MethodCallExpression call = Expression.Call(methodInfo, Expression.Convert(from, type), state);

            Type funcType = typeof(Func<,,>).MakeGenericType(typeof(object), typeof(DeepCloneState), typeof(object));

            return Expression.Lambda(funcType, call, from, state).Compile();
        }

        private static object GenerateProcessTupleMethod(Type type) {
            ParameterExpression from = Expression.Parameter(typeof(object));
            ParameterExpression state = Expression.Parameter(typeof(DeepCloneState));

            ParameterExpression local = Expression.Variable(type);
            BinaryExpression assign = Expression.Assign(local, Expression.Convert(from, type));

            Type funcType = typeof(Func<object, DeepCloneState, object>);

            int tupleLength = type.GenericArguments().Length;

            BinaryExpression constructor = Expression.Assign(local,
                Expression.New(type.GetPublicConstructors().First(x => x.GetParameters().Length == tupleLength),
                    type.GetPublicProperties().OrderBy(x => x.Name)
                        .Where(x => x.CanRead && x.Name.StartsWith("Item") && char.IsDigit(x.Name[4]))
                        .Select(x => Expression.Property(local, x.Name))));

            return Expression.Lambda(funcType, Expression.Block(new[] { local },
                    assign, constructor, Expression.Call(state, typeof(DeepCloneState).GetMethod("AddKnownRef"), from, local),
                    from),
                from, state).Compile();
        }
    }
}