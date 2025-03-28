using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace LegendaryConverters
{

    public class DynamicDicToObjConverter : IDicToObjConverter
    {
        #region 私有
        private ConcurrentDictionary<Type, Func<IDictionary<string, object?>, object>> _cacheFunc = new ConcurrentDictionary<Type, Func<IDictionary<string, object?>, object>>();

        private static readonly MethodInfo ContainsKey = typeof(IDictionary<string, object?>).GetMethod(nameof(IDictionary<string, object?>.ContainsKey), BindingFlags.Public | BindingFlags.Instance)!;
        private static readonly PropertyInfo Item = typeof(IDictionary<string, object?>).GetProperty("Item", BindingFlags.Public | BindingFlags.Instance)!;
        private static readonly MethodInfo convertMethod = typeof(Convert).GetMethod(nameof(System.Convert.ChangeType), BindingFlags.Public | BindingFlags.Static, null,new[] {typeof(object),typeof(Type) },null )!;
        private static readonly MethodInfo toStringMethod = typeof(object).GetMethod(nameof(object.ToString), BindingFlags.Public | BindingFlags.Instance, null,new Type[] { },null )!;

        private object DynamicCompiledConvert(Type type, IDictionary<string, object?> dic)
        {
            return _cacheFunc.GetOrAdd(type, type =>
            {
                ParameterExpression paramDic = Expression.Parameter(typeof(IDictionary<string, object?>), nameof(dic));
                LabelTarget returnLabel = Expression.Label(type, "return");
                ParameterExpression localResult = Expression.Parameter(type, "result");

                ConstructorInfo? constructor = type.GetConstructor(Array.Empty<Type>());
                if (constructor == null)
                {
                    throw new ArgumentException("没有公共无参的构造");
                }
                List<Expression> expressions = new List<Expression>()
                {
                Expression.Assign(localResult, Expression.New(constructor)),
                };

                //System.Convert.GetTypeCode()
                foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(a => a.SetMethod != null & a.GetCustomAttribute<IgnoreConvertAttribute>(true) == null))
                {
                    
                    Expression propName = Expression.Constant(prop.Name);
                    Expression propType = Expression.Constant(prop.PropertyType);
                    Expression dicIndex = Expression.MakeIndex(paramDic, Item, new Expression[] { propName });
                    Type realType = Nullable.GetUnderlyingType(prop.PropertyType)?? prop.PropertyType;
                    Expression realTypeExpression = Expression.Constant(realType);

                    var converter = TypeDescriptor.GetConverter(prop.PropertyType);
                    var IsValidMethod = converter.GetType().GetMethod(nameof(converter.IsValid), BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(object) }, null)!;
                    var ConvertFormMethod = converter.GetType().GetMethod(nameof(converter.ConvertFrom), BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(object) }, null)!;
                    Expression converterExpression = Expression.Constant(converter);

                    Expression elseExpression = Expression.IfThen(
                        Expression.Call(converterExpression, IsValidMethod,dicIndex),
                        Expression.Assign(
                                Expression.Property(localResult, prop),
                                Expression.Convert(Expression.Call(converterExpression, ConvertFormMethod, dicIndex), prop.PropertyType)
                                )
                        );
                    if (realType == typeof(string))
                    {
                        elseExpression = Expression.IfThenElse(
                        Expression.Call(converterExpression, IsValidMethod, dicIndex),
                        Expression.Assign(
                                Expression.Property(localResult, prop),
                                Expression.Convert(Expression.Call(converterExpression, ConvertFormMethod, dicIndex), prop.PropertyType)
                                ),
                        Expression.Assign(
                                Expression.Property(localResult, prop),
                                Expression.Call(dicIndex, toStringMethod)
                                )
                        );
                    }
                    if (realType.IsAssignableTo(typeof(IConvertible)))
                    {
                        elseExpression = Expression.IfThenElse(
                            Expression.AndAlso(
                                Expression.TypeIs(dicIndex,typeof(IConvertible)),
                                Expression.Not(Expression.TypeIs(dicIndex,typeof(string)))
                                ),
                            Expression.Assign(
                                Expression.Property(localResult, prop),
                                Expression.Convert(Expression.Call(null, convertMethod,dicIndex, realTypeExpression), prop.PropertyType)
                                ),
                            elseExpression
                            );
                    }
                    Expression ifthen = Expression.IfThen(
                        Expression.AndAlso(
                            Expression.Call(paramDic, ContainsKey, propName),
                            Expression.NotEqual(Expression.Constant(null), dicIndex)
                            ),
                        Expression.IfThenElse(
                            Expression.TypeIs(dicIndex, prop.PropertyType),
                            Expression.Assign(
                                Expression.Property(localResult, prop),
                                Expression.Convert(dicIndex, prop.PropertyType)
                                ),
                            elseExpression
                            )
                        );
                    expressions.Add(ifthen);
                }



                expressions.Add(Expression.Return(returnLabel, localResult));
                expressions.Add(Expression.Label(returnLabel, localResult));

                Expression block = Expression.Block(
                    new ParameterExpression[] {
                    localResult
                    },
                    expressions
                    );
                var lambda = Expression.Lambda<Func<IDictionary<string, object?>, object>>(block, paramDic);
                return lambda.Compile();
            }).Invoke(dic);
        }

        private object ReflectionConvert(Type type, IDictionary<string, object?> dic)
        {
            ConstructorInfo? constructor = type.GetConstructor(Array.Empty<Type>());
            if (constructor == null)
            {
                throw new ArgumentException("没有公共无参的构造");
            }
            object obj = Activator.CreateInstance(type)!;
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(a => a.SetMethod != null & a.GetCustomAttribute<IgnoreConvertAttribute>(true) == null))
            {
                if (!dic.ContainsKey(prop.Name))
                {
                    continue;
                }
                object? dicObj = dic[prop.Name];
                if (dicObj == null)
                {
                    continue;
                }
                Type realType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (realType.IsAssignableTo(typeof(IConvertible)) && dicObj is IConvertible && dicObj is not string)
                {
                    prop.SetValue(obj, System.Convert.ChangeType(dicObj,realType));
                    continue;
                }
                var converter = TypeDescriptor.GetConverter(prop.PropertyType);
                if (converter.IsValid(dicObj))
                {
                    prop.SetValue(obj, converter.ConvertFrom(dicObj));
                    continue;
                }
                if (realType != typeof(string))
                {
                    continue;
                }
                prop.SetValue(obj, dicObj.ToString());
            }
            return obj;
        }

        #endregion
        public T Convert<T>(IDictionary<string, object?> dic) where T : class, new()
        {
            return (T)Convert(typeof(T), dic);
        }
        public object Convert(Type type, IDictionary<string, object?> dic)
        {
            if (RuntimeFeature.IsDynamicCodeCompiled)
            {
                return DynamicCompiledConvert(type, dic);
            }
            return ReflectionConvert(type, dic);
        }
    }
    public class IgnoreConvertAttribute : Attribute { }
}