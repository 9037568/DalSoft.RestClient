using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DalSoft.RestClient.Extensions
{
    internal static class Object //TODO: this whole class is ugly and hurts my eye
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new ConcurrentDictionary<Type, PropertyInfo[]>();

        private static PropertyInfo[] GetCachedProperties(this Type type)
        {
            return PropertyCache.GetOrAdd(type, t => t.GetProperties());
        }

        /// <summary>
        /// Flattens into a list of key/value pairs.
        /// </summary>
        public static IEnumerable<KeyValuePair<string, object>> FlattenToKeyValuePairs(
            this object obj,
            Func<Type, bool> includeThisType,
            string prefix = null)
        {
            if (obj == null)
                yield break;

            var type = obj.GetType();

            // 1. Leaf value: primitive / value type / string / Guid / DateTime etc.
            if (includeThisType(type))
            {
                yield return new KeyValuePair<string, object>(prefix ?? string.Empty, obj);
                yield break;
            }

            // 2. Dictionary support (IDictionary)
            if (obj is IDictionary dict)
            {
                foreach (var key in dict.Keys)
                {
                    var value = dict[key];
                    if (value == null)
                        continue;

                    var keyString = key.ToString();
                    var childPrefix = string.IsNullOrEmpty(prefix)
                        ? keyString
                        : $"{prefix}.{keyString}";

                    foreach (var kvp in FlattenToKeyValuePairs(value, includeThisType, childPrefix))
                    {
                        yield return kvp;
                    }
                }

                yield break;
            }

            // 3. Enumerable support (arrays, lists, etc.) but NOT string
            if (obj is IEnumerable enumerable && !(obj is string))
            {
                var index = 0;
                foreach (var item in enumerable)
                {
                    if (item == null)
                    {
                        index++;
                        continue;
                    }

                    var childPrefix = string.IsNullOrEmpty(prefix)
                        ? index.ToString()
                        : $"{prefix}[{index}]";

                    foreach (var kvp in FlattenToKeyValuePairs(item, includeThisType, childPrefix))
                    {
                        yield return kvp;
                    }

                    index++;
                }

                yield break;
            }

            // 4. Complex object: reflect properties (anonymous types, POCOs, etc.)
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            foreach (var property in properties)
            {
                var value = property.GetValue(obj);
                if (value == null)
                    continue;

                var propName = property.Name;
                var childPrefix = string.IsNullOrEmpty(prefix)
                    ? propName
                    : $"{prefix}.{propName}";

                foreach (var kvp in FlattenToKeyValuePairs(value, includeThisType, childPrefix))
                {
                    yield return kvp;
                }
            }
        }

        internal static string FormatAsString(this object o)
        {
            if (o is DateTime)
                return ((DateTime) o).ToString("s", CultureInfo.InvariantCulture);

            return o.ToString();
        }

        internal static bool IsValueTypeOrPrimitiveOrStringOrGuid(TypeInfo type)
        {
            return type.IsValueType || type.IsPrimitive || type.AsType() == typeof(string) || type.AsType() == typeof(Guid);
        }

        internal static bool IsValueTypeOrPrimitiveOrStringOrGuidOrDateTime(TypeInfo type)
        {
            return IsValueTypeOrPrimitiveOrStringOrGuid(type) || type.AsType() == typeof(DateTime);
        }

        internal static bool IsValueTypeOrPrimitiveOrStringOrGuidOrDateTimeOrByteArrayOrStream(TypeInfo type)
        {
            return IsValueTypeOrPrimitiveOrStringOrGuidOrDateTime(type) || type.AsType() == typeof(byte[]) || type.AsType() == typeof(Stream);
        }
    }
}
