using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Bloodfall.Core
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class JsonIgnoreAttribute : Attribute { }

    /// <summary>Types that parse/serialize themselves (e.g. LeveledValue accepts a number or an array).</summary>
    public interface IJsonCustom
    {
        void ReadJson(JsonNode node);
        JsonNode WriteJson();
    }

    /// <summary>
    /// Reflection based JSON &lt;-&gt; object mapper working on public instance fields.
    /// Property names are written in camelCase and matched case-insensitively on read, which keeps the
    /// wire format compatible with ASP.NET Core's System.Text.Json (configured with IncludeFields + camelCase).
    /// </summary>
    public static class JsonMapper
    {
        private sealed class TypeInfoCache
        {
            public FieldInfo[] Fields;
            public string[] JsonNames;
            public Dictionary<string, FieldInfo> ByLowerName;
        }

        private static readonly Dictionary<Type, TypeInfoCache> Cache = new Dictionary<Type, TypeInfoCache>();
        private static readonly object CacheLock = new object();

        /// <summary>Optional sink for authoring warnings (unknown keys etc.).</summary>
        [ThreadStatic] public static List<string> Warnings;

        public static T FromJson<T>(string json) => (T)FromNode(Json.Parse(json), typeof(T), typeof(T).Name);
        public static T FromNode<T>(JsonNode node) => (T)FromNode(node, typeof(T), typeof(T).Name);
        public static object FromNode(JsonNode node, Type type) => FromNode(node, type, type.Name);

        public static string ToJson(object obj, bool pretty = false) => Json.Write(ToNode(obj), pretty);

        private static TypeInfoCache GetInfo(Type t)
        {
            lock (CacheLock)
            {
                if (Cache.TryGetValue(t, out var info)) return info;
                var fields = new List<FieldInfo>();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    if (f.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    fields.Add(f);
                }
                info = new TypeInfoCache
                {
                    Fields = fields.ToArray(),
                    JsonNames = new string[fields.Count],
                    ByLowerName = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase)
                };
                for (int i = 0; i < fields.Count; i++)
                {
                    info.JsonNames[i] = CamelCase(fields[i].Name);
                    info.ByLowerName[fields[i].Name] = fields[i];
                }
                Cache[t] = info;
                return info;
            }
        }

        public static string CamelCase(string name)
        {
            if (string.IsNullOrEmpty(name) || char.IsLower(name[0])) return name;
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static object FromNode(JsonNode node, Type type, string path)
        {
            if (node == null || node.IsNull)
                return type.IsValueType ? Activator.CreateInstance(type) : null;

            if (typeof(IJsonCustom).IsAssignableFrom(type))
            {
                var custom = (IJsonCustom)Activator.CreateInstance(type);
                custom.ReadJson(node);
                return custom;
            }
            if (type == typeof(string)) return node.AsString();
            if (type == typeof(bool)) return node.AsBool();
            if (type == typeof(int)) return node.AsInt();
            if (type == typeof(long)) return node.AsLong();
            if (type == typeof(float)) return node.AsFloat();
            if (type == typeof(double)) return node.AsDouble();
            if (type == typeof(byte)) return (byte)node.AsInt();
            if (type == typeof(short)) return (short)node.AsInt();
            if (type == typeof(uint)) return (uint)node.AsLong();
            if (type == typeof(ulong)) return (ulong)node.AsDouble();
            if (type == typeof(Guid)) return Guid.TryParse(node.AsString(), out var g) ? g : Guid.Empty;
            if (type == typeof(DateTime))
            {
                var s = node.AsString();
                return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : default;
            }
            if (type.IsEnum) return ParseEnum(node, type, path);
            if (Nullable.GetUnderlyingType(type) is Type inner) return FromNode(node, inner, path);

            if (type.IsArray)
            {
                var elem = type.GetElementType();
                if (node.Kind != JsonKind.Array)
                {
                    // Allow a scalar where an array is expected ("roles": "Carry").
                    var single = Array.CreateInstance(elem, 1);
                    single.SetValue(FromNode(node, elem, path), 0);
                    return single;
                }
                var arr = Array.CreateInstance(elem, node.ArrayValue.Count);
                for (int i = 0; i < node.ArrayValue.Count; i++) arr.SetValue(FromNode(node.ArrayValue[i], elem, $"{path}[{i}]"), i);
                return arr;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(type);
                if (node.Kind != JsonKind.Array) { list.Add(FromNode(node, elem, path)); return list; }
                for (int i = 0; i < node.ArrayValue.Count; i++) list.Add(FromNode(node.ArrayValue[i], elem, $"{path}[{i}]"));
                return list;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = type.GetGenericArguments();
                var dict = (IDictionary)Activator.CreateInstance(type);
                if (node.Kind != JsonKind.Object) return dict;
                foreach (var kv in node.Properties())
                {
                    object key = args[0] == typeof(string) ? kv.Key : args[0].IsEnum ? Enum.Parse(args[0], kv.Key, true) : Convert.ChangeType(kv.Key, args[0], CultureInfo.InvariantCulture);
                    dict[key] = FromNode(kv.Value, args[1], $"{path}.{kv.Key}");
                }
                return dict;
            }
            if (type == typeof(JsonNode)) return node;

            if (node.Kind != JsonKind.Object)
                throw new JsonException($"{path}: expected object for {type.Name} but found {node.Kind}");

            var obj = Activator.CreateInstance(type);
            var info = GetInfo(type);
            foreach (var kv in node.Properties())
            {
                if (kv.Key.StartsWith("$") || kv.Key.StartsWith("_")) continue; // comments / editor metadata
                if (info.ByLowerName.TryGetValue(kv.Key, out var field))
                {
                    field.SetValue(obj, FromNode(kv.Value, field.FieldType, $"{path}.{kv.Key}"));
                }
                else
                {
                    Warnings?.Add($"{path}: unknown property '{kv.Key}' for {type.Name}");
                }
            }
            if (obj is IJsonPostLoad post) post.OnAfterLoad();
            return obj;
        }

        private static object ParseEnum(JsonNode node, Type type, string path)
        {
            if (node.Kind == JsonKind.Number) return Enum.ToObject(type, node.AsInt());
            if (node.Kind == JsonKind.Array && type.GetCustomAttribute<FlagsAttribute>() != null)
            {
                long v = 0;
                foreach (var item in node.ArrayValue) v |= Convert.ToInt64(ParseEnum(item, type, path), CultureInfo.InvariantCulture);
                return Enum.ToObject(type, v);
            }
            var s = node.AsString();
            if (string.IsNullOrEmpty(s)) return Activator.CreateInstance(type);
            try { return Enum.Parse(type, s.Replace("|", ","), true); }
            catch (ArgumentException) { throw new JsonException($"{path}: '{s}' is not a valid {type.Name}"); }
        }

        public static JsonNode ToNode(object obj)
        {
            if (obj == null) return JsonNode.Null;
            var type = obj.GetType();
            if (obj is IJsonCustom custom) return custom.WriteJson();
            if (obj is JsonNode jn) return jn;
            if (obj is string s) return JsonNode.From(s);
            if (obj is bool b) return JsonNode.From(b);
            if (obj is int i) return JsonNode.From(i);
            if (obj is long l) return JsonNode.From(l);
            if (obj is float f) return JsonNode.From(f);
            if (obj is double d) return JsonNode.From(d);
            if (obj is byte by) return JsonNode.From(by);
            if (obj is short sh) return JsonNode.From(sh);
            if (obj is uint ui) return JsonNode.From(ui);
            if (obj is ulong ul) return JsonNode.From(ul);
            if (obj is Guid guid) return JsonNode.From(guid.ToString());
            if (obj is DateTime dt) return JsonNode.From(dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            if (type.IsEnum) return JsonNode.From(obj.ToString());
            if (obj is IDictionary dict)
            {
                var o = JsonNode.NewObject();
                foreach (DictionaryEntry e in dict) o[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = ToNode(e.Value);
                return o;
            }
            if (obj is IEnumerable en)
            {
                var a = JsonNode.NewArray();
                foreach (var item in en) a.Add(ToNode(item));
                return a;
            }
            var node = JsonNode.NewObject();
            var info = GetInfo(type);
            for (int k = 0; k < info.Fields.Length; k++)
            {
                var v = info.Fields[k].GetValue(obj);
                if (v == null) continue;
                node[info.JsonNames[k]] = ToNode(v);
            }
            return node;
        }

        /// <summary>
        /// Never called. References generic instantiations over value types so IL2CPP ahead-of-time
        /// compilation generates them (the mapper constructs them via reflection).
        /// </summary>
        internal static void AotHints()
        {
            new List<int>(); new List<float>(); new List<long>(); new List<bool>(); new List<double>();
            new Dictionary<string, int>(); new Dictionary<string, float>(); new Dictionary<string, string>();
            new Dictionary<string, long>(); new Dictionary<string, bool>(); new Dictionary<string, double>();
        }
    }

    public interface IJsonPostLoad
    {
        void OnAfterLoad();
    }
}
