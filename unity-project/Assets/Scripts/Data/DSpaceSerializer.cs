// DSpaceSerializer.cs — Custom JSON serializer for D-Space data types
// Wraps Unity's JsonUtility with support for Dictionaries, nullable types,
// polymorphic lists, and proper handling of Unity math types.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DaemonVision.Data
{
    /// <summary>
    /// Custom JSON serializer that extends Unity's JsonUtility to handle types
    /// it cannot natively serialize: Dictionaries, nullable value types, and
    /// polymorphic collections. All D-Space persistence flows through this class.
    /// </summary>
    public static class DSpaceSerializer
    {
        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Serialize an object to a JSON string. Handles Unity types, Dictionaries,
        /// Lists, nullable types, and primitives.
        /// </summary>
        public static string Serialize<T>(T obj)
        {
            if (obj == null)
                return "null";

            try
            {
                return SerializeValue(obj, typeof(T));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DSpaceSerializer] Serialization failed for {typeof(T).Name}: {ex.Message}");
                return "null";
            }
        }

        /// <summary>
        /// Deserialize a JSON string to an object of type T. Falls back gracefully
        /// on malformed input, returning default(T).
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null")
                return default;

            try
            {
                return (T)DeserializeValue(json.Trim(), typeof(T));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DSpaceSerializer] Deserialization failed for {typeof(T).Name}: {ex.Message}");
                return default;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Serialization
        // ─────────────────────────────────────────────────────────────────

        private static string SerializeValue(object obj, Type type)
        {
            if (obj == null)
                return "null";

            // Unwrap nullable
            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                type = underlying;
            }

            // Primitives and strings
            if (type == typeof(string))
                return EscapeJsonString((string)obj);
            if (type == typeof(bool))
                return (bool)obj ? "true" : "false";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                return obj.ToString();
            if (type == typeof(float))
                return ((float)obj).ToString("R", CultureInfo.InvariantCulture);
            if (type == typeof(double))
                return ((double)obj).ToString("R", CultureInfo.InvariantCulture);
            if (type.IsEnum)
                return EscapeJsonString(obj.ToString());

            // Unity math types — serialize as compact objects
            if (type == typeof(Vector2))
                return SerializeVector2((Vector2)obj);
            if (type == typeof(Vector3))
                return SerializeVector3((Vector3)obj);
            if (type == typeof(Vector4))
                return SerializeVector4((Vector4)obj);
            if (type == typeof(Quaternion))
                return SerializeQuaternion((Quaternion)obj);
            if (type == typeof(Color))
                return SerializeColor((Color)obj);
            if (type == typeof(Color32))
                return SerializeColor32((Color32)obj);
            if (type == typeof(Rect))
                return SerializeRect((Rect)obj);
            if (type == typeof(Vector2Int))
                return SerializeVector2Int((Vector2Int)obj);
            if (type == typeof(Vector3Int))
                return SerializeVector3Int((Vector3Int)obj);

            // Dictionary<string, V>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                return SerializeDictionary(obj, type);

            // Lists and arrays
            if (obj is IList list)
                return SerializeList(list);

            // Array
            if (type.IsArray)
                return SerializeArray(obj, type);

            // Complex objects — use JsonUtility for [Serializable] types,
            // then patch back through our serializer if needed.
            if (type.IsClass || (type.IsValueType && !type.IsPrimitive))
                return SerializeComplexObject(obj, type);

            return obj.ToString();
        }

        private static string SerializeComplexObject(object obj, Type type)
        {
            // For types with [Serializable], prefer JsonUtility as it handles
            // Unity serialization rules. But it cannot handle Dictionaries inside,
            // so we use a field-by-field approach.
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);

            var sb = new StringBuilder();
            sb.Append('{');

            // If the type uses polymorphic serialization, store the type name
            bool hasTypeHint = type.IsAbstract || type.IsInterface;
            if (hasTypeHint || obj.GetType() != type)
            {
                sb.Append("\"$type\":");
                sb.Append(EscapeJsonString(obj.GetType().AssemblyQualifiedName));
                if (fields.Length > 0)
                    sb.Append(',');
            }

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];

                // Skip fields marked with NonSerialized
                if (Attribute.IsDefined(field, typeof(NonSerializedAttribute)))
                    continue;

                object value = field.GetValue(obj);
                Type fieldType = field.FieldType;

                sb.Append(EscapeJsonString(field.Name));
                sb.Append(':');
                sb.Append(SerializeValue(value, fieldType));

                if (i < fields.Length - 1)
                    sb.Append(',');
            }

            // Remove trailing comma if the last field was NonSerialized
            string result = sb.ToString();
            if (result.EndsWith(","))
                result = result.Substring(0, result.Length - 1);

            return result + "}";
        }

        private static string SerializeDictionary(object obj, Type dictType)
        {
            var sb = new StringBuilder();
            sb.Append('{');

            Type[] genericArgs = dictType.GetGenericArguments();
            Type valueType = genericArgs[1];

            var dict = obj as IDictionary;
            if (dict == null)
                return "{}";

            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;

                sb.Append(EscapeJsonString(entry.Key.ToString()));
                sb.Append(':');
                sb.Append(SerializeValue(entry.Value, entry.Value?.GetType() ?? valueType));
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static string SerializeList(IList list)
        {
            var sb = new StringBuilder();
            sb.Append('[');

            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                object item = list[i];
                sb.Append(SerializeValue(item, item?.GetType() ?? typeof(object)));
            }

            sb.Append(']');
            return sb.ToString();
        }

        private static string SerializeArray(object obj, Type arrayType)
        {
            var array = (Array)obj;
            Type elemType = arrayType.GetElementType();
            var sb = new StringBuilder();
            sb.Append('[');

            for (int i = 0; i < array.Length; i++)
            {
                if (i > 0) sb.Append(',');
                object item = array.GetValue(i);
                sb.Append(SerializeValue(item, item?.GetType() ?? elemType));
            }

            sb.Append(']');
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Unity Type Serializers
        // ─────────────────────────────────────────────────────────────────

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string SerializeVector2(Vector2 v) =>
            $"{{\"x\":{F(v.x)},\"y\":{F(v.y)}}}";

        private static string SerializeVector3(Vector3 v) =>
            $"{{\"x\":{F(v.x)},\"y\":{F(v.y)},\"z\":{F(v.z)}}}";

        private static string SerializeVector4(Vector4 v) =>
            $"{{\"x\":{F(v.x)},\"y\":{F(v.y)},\"z\":{F(v.z)},\"w\":{F(v.w)}}}";

        private static string SerializeQuaternion(Quaternion q) =>
            $"{{\"x\":{F(q.x)},\"y\":{F(q.y)},\"z\":{F(q.z)},\"w\":{F(q.w)}}}";

        private static string SerializeColor(Color c) =>
            $"{{\"r\":{F(c.r)},\"g\":{F(c.g)},\"b\":{F(c.b)},\"a\":{F(c.a)}}}";

        private static string SerializeColor32(Color32 c) =>
            $"{{\"r\":{c.r},\"g\":{c.g},\"b\":{c.b},\"a\":{c.a}}}";

        private static string SerializeRect(Rect r) =>
            $"{{\"x\":{F(r.x)},\"y\":{F(r.y)},\"width\":{F(r.width)},\"height\":{F(r.height)}}}";

        private static string SerializeVector2Int(Vector2Int v) =>
            $"{{\"x\":{v.x},\"y\":{v.y}}}";

        private static string SerializeVector3Int(Vector3Int v) =>
            $"{{\"x\":{v.x},\"y\":{v.y},\"z\":{v.z}}}";

        // ─────────────────────────────────────────────────────────────────
        //  Deserialization
        // ─────────────────────────────────────────────────────────────────

        private static object DeserializeValue(string json, Type type)
        {
            if (json == null || json == "null")
                return null;

            // Unwrap nullable
            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                if (json == "null")
                    return null;
                type = underlying;
            }

            // Primitives
            if (type == typeof(string))
                return UnescapeJsonString(json);
            if (type == typeof(bool))
                return json.Trim().ToLower() == "true";
            if (type == typeof(int))
                return int.Parse(json.Trim(), CultureInfo.InvariantCulture);
            if (type == typeof(long))
                return long.Parse(json.Trim(), CultureInfo.InvariantCulture);
            if (type == typeof(short))
                return short.Parse(json.Trim(), CultureInfo.InvariantCulture);
            if (type == typeof(byte))
                return byte.Parse(json.Trim(), CultureInfo.InvariantCulture);
            if (type == typeof(float))
                return float.Parse(json.Trim(), CultureInfo.InvariantCulture);
            if (type == typeof(double))
                return double.Parse(json.Trim(), CultureInfo.InvariantCulture);
            if (type.IsEnum)
            {
                string enumStr = UnescapeJsonString(json);
                return Enum.Parse(type, enumStr);
            }

            // Unity types — use JsonUtility for these as it handles them well
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
                type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect) ||
                type == typeof(Vector2Int) || type == typeof(Vector3Int))
            {
                return JsonUtility.FromJson(json, type);
            }

            if (type == typeof(Color32))
                return DeserializeColor32(json);

            // Dictionary<string, V>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                return DeserializeDictionary(json, type);

            // List<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return DeserializeList(json, type);

            // Arrays
            if (type.IsArray)
                return DeserializeArray(json, type);

            // Complex objects
            if (type.IsClass || (type.IsValueType && !type.IsPrimitive))
                return DeserializeComplexObject(json, type);

            return null;
        }

        private static object DeserializeComplexObject(string json, Type type)
        {
            json = json.Trim();
            if (!json.StartsWith("{"))
                return null;

            // Check for $type hint for polymorphic deserialization
            var fields = ParseJsonObject(json);
            if (fields.TryGetValue("$type", out string typeHint))
            {
                string actualTypeName = UnescapeJsonString(typeHint);
                Type actualType = Type.GetType(actualTypeName);
                if (actualType != null)
                    type = actualType;
            }

            // Try JsonUtility first for Serializable types — it's faster and
            // handles Unity's serialization attributes correctly.
            try
            {
                object obj = Activator.CreateInstance(type);
                var typeFields = type.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                foreach (var field in typeFields)
                {
                    if (Attribute.IsDefined(field, typeof(NonSerializedAttribute)))
                        continue;

                    if (fields.TryGetValue(field.Name, out string fieldJson))
                    {
                        object value = DeserializeValue(fieldJson, field.FieldType);
                        if (value != null)
                            field.SetValue(obj, value);
                    }
                }

                return obj;
            }
            catch
            {
                // Fall back to JsonUtility
                try
                {
                    return JsonUtility.FromJson(json, type);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static object DeserializeDictionary(string json, Type dictType)
        {
            Type[] genericArgs = dictType.GetGenericArguments();
            Type keyType = genericArgs[0];
            Type valueType = genericArgs[1];

            var dict = (IDictionary)Activator.CreateInstance(dictType);
            var fields = ParseJsonObject(json);

            foreach (var kvp in fields)
            {
                object key;
                if (keyType == typeof(string))
                    key = kvp.Key;
                else if (keyType.IsEnum)
                    key = Enum.Parse(keyType, kvp.Key);
                else
                    key = Convert.ChangeType(kvp.Key, keyType, CultureInfo.InvariantCulture);

                object value = DeserializeValue(kvp.Value, valueType);
                dict[key] = value;
            }

            return dict;
        }

        private static object DeserializeList(string json, Type listType)
        {
            Type elemType = listType.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(listType);
            var elements = ParseJsonArray(json);

            foreach (string elemJson in elements)
            {
                object value = DeserializeValue(elemJson, elemType);
                list.Add(value);
            }

            return list;
        }

        private static object DeserializeArray(string json, Type arrayType)
        {
            Type elemType = arrayType.GetElementType();
            var elements = ParseJsonArray(json);
            var array = Array.CreateInstance(elemType, elements.Count);

            for (int i = 0; i < elements.Count; i++)
            {
                object value = DeserializeValue(elements[i], elemType);
                array.SetValue(value, i);
            }

            return array;
        }

        private static Color32 DeserializeColor32(string json)
        {
            var fields = ParseJsonObject(json);
            byte r = fields.TryGetValue("r", out string rs) ? byte.Parse(rs) : (byte)0;
            byte g = fields.TryGetValue("g", out string gs) ? byte.Parse(gs) : (byte)0;
            byte b = fields.TryGetValue("b", out string bs) ? byte.Parse(bs) : (byte)0;
            byte a = fields.TryGetValue("a", out string aStr) ? byte.Parse(aStr) : (byte)255;
            return new Color32(r, g, b, a);
        }

        // ─────────────────────────────────────────────────────────────────
        //  JSON Parsing Helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parse a JSON object string into a key-value dictionary of raw JSON strings.
        /// Keys are unescaped; values are raw JSON tokens (may be objects, arrays, strings, etc.).
        /// </summary>
        private static Dictionary<string, string> ParseJsonObject(string json)
        {
            var result = new Dictionary<string, string>();
            json = json.Trim();

            if (json.Length < 2 || json[0] != '{' || json[json.Length - 1] != '}')
                return result;

            // Strip outer braces
            string inner = json.Substring(1, json.Length - 2);
            int pos = 0;

            while (pos < inner.Length)
            {
                SkipWhitespace(inner, ref pos);
                if (pos >= inner.Length) break;

                // Parse key
                string key = ReadJsonString(inner, ref pos);
                if (key == null) break;

                SkipWhitespace(inner, ref pos);
                if (pos >= inner.Length || inner[pos] != ':') break;
                pos++; // skip ':'

                SkipWhitespace(inner, ref pos);

                // Parse value (raw token)
                string value = ReadJsonToken(inner, ref pos);
                if (value == null) break;

                result[key] = value;

                SkipWhitespace(inner, ref pos);
                if (pos < inner.Length && inner[pos] == ',')
                    pos++;
            }

            return result;
        }

        /// <summary>
        /// Parse a JSON array string into a list of raw JSON string tokens.
        /// </summary>
        private static List<string> ParseJsonArray(string json)
        {
            var result = new List<string>();
            json = json.Trim();

            if (json.Length < 2 || json[0] != '[' || json[json.Length - 1] != ']')
                return result;

            string inner = json.Substring(1, json.Length - 2);
            int pos = 0;

            while (pos < inner.Length)
            {
                SkipWhitespace(inner, ref pos);
                if (pos >= inner.Length) break;

                string value = ReadJsonToken(inner, ref pos);
                if (value == null) break;

                result.Add(value);

                SkipWhitespace(inner, ref pos);
                if (pos < inner.Length && inner[pos] == ',')
                    pos++;
            }

            return result;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos]))
                pos++;
        }

        /// <summary>
        /// Read a JSON string token (including quotes), returning the unescaped content.
        /// </summary>
        private static string ReadJsonString(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '"')
                return null;

            pos++; // skip opening quote
            var sb = new StringBuilder();
            bool escaped = false;

            while (pos < s.Length)
            {
                char c = s[pos];

                if (escaped)
                {
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 < s.Length)
                            {
                                string hex = s.Substring(pos + 1, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                pos += 4;
                            }
                            break;
                        default: sb.Append(c); break;
                    }
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    pos++; // skip closing quote
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }

                pos++;
            }

            return sb.ToString(); // Unterminated string
        }

        /// <summary>
        /// Read a raw JSON value token (string, number, object, array, true, false, null).
        /// Returns the raw text.
        /// </summary>
        private static string ReadJsonToken(string s, ref int pos)
        {
            if (pos >= s.Length)
                return null;

            char c = s[pos];

            // String
            if (c == '"')
            {
                int start = pos;
                ReadJsonString(s, ref pos); // advances pos past closing quote
                return s.Substring(start, pos - start);
            }

            // Object or Array
            if (c == '{' || c == '[')
            {
                char open = c;
                char close = c == '{' ? '}' : ']';
                int depth = 1;
                int start = pos;
                pos++;
                bool inString = false;
                bool escaped = false;

                while (pos < s.Length && depth > 0)
                {
                    char ch = s[pos];

                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\' && inString)
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = !inString;
                    }
                    else if (!inString)
                    {
                        if (ch == open) depth++;
                        else if (ch == close) depth--;
                    }

                    pos++;
                }

                return s.Substring(start, pos - start);
            }

            // Number, bool, null
            {
                int start = pos;
                while (pos < s.Length && s[pos] != ',' && s[pos] != '}' && s[pos] != ']' &&
                       !char.IsWhiteSpace(s[pos]))
                {
                    pos++;
                }
                return s.Substring(start, pos - start);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  String Escaping
        // ─────────────────────────────────────────────────────────────────

        private static string EscapeJsonString(string s)
        {
            if (s == null) return "null";

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');

            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static string UnescapeJsonString(string json)
        {
            if (json == null || json == "null")
                return null;

            json = json.Trim();

            // Remove surrounding quotes if present
            if (json.Length >= 2 && json[0] == '"' && json[json.Length - 1] == '"')
                json = json.Substring(1, json.Length - 2);

            var sb = new StringBuilder(json.Length);
            bool escaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 < json.Length)
                            {
                                string hex = json.Substring(i + 1, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                i += 4;
                            }
                            break;
                        default: sb.Append(c); break;
                    }
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
