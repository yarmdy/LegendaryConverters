using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private static readonly MethodInfo ContainsKey = typeof(IDictionary<string, object?>).GetMethod("ContainsKey", BindingFlags.Public | BindingFlags.Instance)!;
        private static readonly PropertyInfo Item = typeof(IDictionary<string, object?>).GetProperty("Item", BindingFlags.Public | BindingFlags.Instance)!;
        private static MethodInfo isSameTypeMethod = typeof(DynamicDicToObjConverter).GetMethod(nameof(isSameType), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static bool isSameType(Type type1, object obj2)
        {
            Type type2 = obj2.GetType();
            if (type1.IsAssignableFrom(type2))
            {
                return true;
            }
            type1 = Nullable.GetUnderlyingType(type1) ?? type1;
            type2 = Nullable.GetUnderlyingType(type2) ?? type2;
            if (type1.IsAssignableFrom(type2))
            {
                return true;
            }
            return false;
        }
        private static MethodInfo convertToMethod = typeof(DynamicDicToObjConverter).GetMethod(nameof(convertTo), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static object? convertTo(object obj2, Type type)
        {
            try
            {
                return System.Convert.ChangeType(obj2, type);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return null;
        }
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
                    ParameterExpression obj = Expression.Parameter(typeof(object), "obj");
                    Expression propName = Expression.Constant(prop.Name);
                    Expression propType = Expression.Constant(prop.PropertyType);
                    Type real = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    Expression realType = Expression.Constant(real);
                    Expression dicIndex = Expression.MakeIndex(paramDic, Item, new Expression[] { propName });
                    bool isConvertible = real.IsAssignableTo(typeof(IConvertible));
                    Expression elseExpression = Expression.Empty();
                    if (isConvertible)
                    {
                        elseExpression = Expression.IfThen(
                            Expression.TypeIs(dicIndex, typeof(IConvertible)),
                            Expression.Block(
                                new ParameterExpression[] { obj },
                                Expression.Assign(obj, Expression.Call(null, convertToMethod, dicIndex, realType)),
                                Expression.IfThen(
                                    Expression.NotEqual(obj, Expression.Constant(null)),
                                    Expression.Assign(
                                        Expression.Property(localResult, prop),
                                        Expression.Convert(obj, prop.PropertyType)
                                        )
                                    )
                                )
                        );
                    }
                    Expression ifthen = Expression.IfThen(
                            Expression.AndAlso(
                                Expression.Call(paramDic, ContainsKey, propName),
                                Expression.NotEqual(Expression.Constant(null), dicIndex)
                                ),
                            Expression.IfThenElse(
                                Expression.Call(null, isSameTypeMethod, new Expression[] { propType, dicIndex }),
                                Expression.Assign(
                                    Expression.Property(localResult, prop),
                                    Expression.Convert(dicIndex, prop.PropertyType)),
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

        private object RelfectionConvert(Type type, IDictionary<string, object?> dic)
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
                if (isSameType(prop.PropertyType, dicObj))
                {
                    prop.SetValue(obj, dicObj);
                    continue;
                }
                if (!prop.PropertyType.IsAssignableTo(typeof(IConvertible)) || dicObj is not IConvertible)
                {
                    continue;
                }
                Type real = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                prop.SetValue(obj, System.Convert.ChangeType(dicObj, real));
            }
            return obj;
        }

        private static readonly Type[] ConvertTypes = new Type[]
        {
            null!,
            typeof(object),
            typeof(DBNull),
            typeof(bool),
            typeof(char),
            typeof(sbyte),
            typeof(byte),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(decimal),
            typeof(DateTime),
            typeof(object),
            typeof(string)
        };

        private static object? changeType(object? value, Type conversionType, IFormatProvider? provider)
        {
            if ((object)conversionType == null)
            {
                throw new ArgumentNullException("conversionType");
            }
            if (value == null)
            {
                if (conversionType.IsValueType)
                {
                    return Activator.CreateInstance(conversionType);
                }
                return null;
            }
            if (!(value is IConvertible convertible))
            {
                if (value.GetType() == conversionType)
                {
                    return value;
                }
                throw new InvalidCastException("无法转换类型，没有实现转化器接口");
            }
            if ((object)conversionType == ConvertTypes[3])
            {
                if (convertible is string str)
                {
                    if (bool.TryParse(str, out bool result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToBoolean(provider);
            }
            if ((object)conversionType == ConvertTypes[4])
            {
                if (convertible is string str)
                {
                    if (char.TryParse(str, out char result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToChar(provider);
            }
            if ((object)conversionType == ConvertTypes[5])
            {
                if (convertible is string str)
                {
                    if (sbyte.TryParse(str, out sbyte result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToSByte(provider);
            }
            if ((object)conversionType == ConvertTypes[6])
            {
                if (convertible is string str)
                {
                    if (byte.TryParse(str, out byte result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToByte(provider);
            }
            if ((object)conversionType == ConvertTypes[7])
            {
                if (convertible is string str)
                {
                    if (short.TryParse(str, out short result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToInt16(provider);
            }
            if ((object)conversionType == ConvertTypes[8])
            {
                if (convertible is string str)
                {
                    if (ushort.TryParse(str, out ushort result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToUInt16(provider);
            }
            if ((object)conversionType == ConvertTypes[9])
            {
                if (convertible is string str)
                {
                    if (int.TryParse(str, out int result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToInt32(provider);
            }
            if ((object)conversionType == ConvertTypes[10])
            {
                if (convertible is string str)
                {
                    if (uint.TryParse(str, out uint result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToUInt32(provider);
            }
            if ((object)conversionType == ConvertTypes[11])
            {
                if (convertible is string str)
                {
                    if (long.TryParse(str, out long result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToInt64(provider);
            }
            if ((object)conversionType == ConvertTypes[12])
            {
                if (convertible is string str)
                {
                    if (ulong.TryParse(str, out ulong result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToUInt64(provider);
            }
            if ((object)conversionType == ConvertTypes[13])
            {
                if (convertible is string str)
                {
                    if (float.TryParse(str, out float result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToSingle(provider);
            }
            if ((object)conversionType == ConvertTypes[14])
            {
                if (convertible is string str)
                {
                    if (double.TryParse(str, out double result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToDouble(provider);
            }
            if ((object)conversionType == ConvertTypes[15])
            {
                if (convertible is string str)
                {
                    if (decimal.TryParse(str, out decimal result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToDecimal(provider);
            }
            if ((object)conversionType == ConvertTypes[16])
            {
                if (convertible is string str)
                {
                    if (DateTime.TryParse(str, out DateTime result))
                    {
                        return result;
                    }
                    return null;
                }
                return convertible.ToDateTime(provider);
            }
            if ((object)conversionType == ConvertTypes[18])
            {
                return convertible.ToString(provider);
            }
            if ((object)conversionType == ConvertTypes[1])
            {
                return value;
            }
            return convertible.ToType(conversionType, provider);
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
            return RelfectionConvert(type, dic);
        }
    }
    public class IgnoreConvertAttribute : Attribute { }
}