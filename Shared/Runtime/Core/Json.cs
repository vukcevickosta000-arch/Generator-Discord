using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bloodfall.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Minimal dependency-free JSON DOM. Shared by the Unity client and the .NET servers so that game
    /// data parsing is byte-for-byte identical on both sides (no Newtonsoft / System.Text.Json drift).
    /// </summary>
    public sealed class JsonNode
    {
        public static readonly JsonNode Null = new JsonNode(JsonKind.Null);

        public readonly JsonKind Kind;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue;
        public List<JsonNode> ArrayValue;
        public Dictionary<string, JsonNode> ObjectValue;
        /// <summary>Preserves declaration order of object keys (useful for tooling / stable output).</summary>
        public List<string> KeyOrder;

        public JsonNode(JsonKind kind)
        {
            Kind = kind;
            if (kind == JsonKind.Array) ArrayValue = new List<JsonNode>();
            if (kind == JsonKind.Object) { ObjectValue = new Dictionary<string, JsonNode>(StringComparer.Ordinal); KeyOrder = new List<string>(); }
        }

        public static JsonNode From(string s) => s == null ? Null : new JsonNode(JsonKind.String) { StringValue = s };
        public static JsonNode From(double d) => new JsonNode(JsonKind.Number) { NumberValue = d };
        public static JsonNode From(bool b) => new JsonNode(JsonKind.Bool) { BoolValue = b };
        public static JsonNode NewObject() => new JsonNode(JsonKind.Object);
        public static JsonNode NewArray() => new JsonNode(JsonKind.Array);

        public bool IsNull => Kind == JsonKind.Null;
        public int Count => Kind == JsonKind.Array ? ArrayValue.Count : Kind == JsonKind.Object ? ObjectValue.Count : 0;

        public JsonNode this[string key]
        {
            get
            {
                if (Kind != JsonKind.Object) return Null;
                return ObjectValue.TryGetValue(key, out var v) ? v : Null;
            }
            set
            {
                if (Kind != JsonKind.Object) throw new InvalidOperationException("Not an object");
                if (!ObjectValue.ContainsKey(key)) KeyOrder.Add(key);
                ObjectValue[key] = value ?? Null;
            }
        }

        public JsonNode this[int index] => Kind == JsonKind.Array && index >= 0 && index < ArrayValue.Count ? ArrayValue[index] : Null;

        public bool Has(string key) => Kind == JsonKind.Object && ObjectValue.ContainsKey(key);
        public void Add(JsonNode n) { if (Kind != JsonKind.Array) throw new InvalidOperationException("Not an array"); ArrayValue.Add(n ?? Null); }

        public string AsString(string fallback = null) => Kind == JsonKind.String ? StringValue : Kind == JsonKind.Number ? NumberValue.ToString(CultureInfo.InvariantCulture) : fallback;
        public double AsDouble(double fallback = 0) => Kind == JsonKind.Number ? NumberValue : Kind == JsonKind.String && double.TryParse(StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
        public float AsFloat(float fallback = 0) => (float)AsDouble(fallback);
        public int AsInt(int fallback = 0) => Kind == JsonKind.Number ? (int)Math.Round(NumberValue) : fallback;
        public long AsLong(long fallback = 0) => Kind == JsonKind.Number ? (long)Math.Round(NumberValue) : fallback;
        public bool AsBool(bool fallback = false) => Kind == JsonKind.Bool ? BoolValue : fallback;

        public IEnumerable<KeyValuePair<string, JsonNode>> Properties()
        {
            if (Kind != JsonKind.Object) yield break;
            foreach (var k in KeyOrder) yield return new KeyValuePair<string, JsonNode>(k, ObjectValue[k]);
        }

        public override string ToString() => Json.Write(this, false);
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public static class Json
    {
        public static JsonNode Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWs();
            var node = p.ParseValue();
            p.SkipWs();
            if (!p.End) throw p.Error("Trailing characters after JSON value");
            return node;
        }

        public static bool TryParse(string text, out JsonNode node, out string error)
        {
            try { node = Parse(text); error = null; return true; }
            catch (JsonException e) { node = null; error = e.Message; return false; }
        }

        private struct Parser
        {
            private readonly string _s;
            private int _i;
            public Parser(string s) { _s = s; _i = 0; }
            public bool End => _i >= _s.Length;

            public JsonException Error(string msg)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++) { if (_s[k] == '\n') { line++; col = 1; } else col++; }
                return new JsonException($"{msg} (line {line}, column {col})");
            }

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') { _i++; continue; }
                    // Allow // line comments and /* */ block comments in data files (authoring convenience).
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/') { while (_i < _s.Length && _s[_i] != '\n') _i++; continue; }
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                    {
                        _i += 2;
                        while (_i + 1 < _s.Length && !(_s[_i] == '*' && _s[_i + 1] == '/')) _i++;
                        _i += 2; continue;
                    }
                    break;
                }
            }

            public JsonNode ParseValue()
            {
                if (End) throw Error("Unexpected end of input");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return JsonNode.From(ParseString());
                    case 't': Expect("true"); return JsonNode.From(true);
                    case 'f': Expect("false"); return JsonNode.From(false);
                    case 'n': Expect("null"); return JsonNode.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return JsonNode.From(ParseNumber());
                        throw Error($"Unexpected character '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error($"Expected '{word}'");
                _i += word.Length;
            }

            private JsonNode ParseObject()
            {
                var obj = JsonNode.NewObject();
                _i++; SkipWs();
                if (!End && _s[_i] == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (End || _s[_i] != '"') throw Error("Expected property name");
                    string key = ParseString();
                    SkipWs();
                    if (End || _s[_i] != ':') throw Error("Expected ':'");
                    _i++; SkipWs();
                    obj[key] = ParseValue();
                    SkipWs();
                    if (End) throw Error("Unterminated object");
                    if (_s[_i] == ',')
                    {
                        _i++; SkipWs();
                        if (!End && _s[_i] == '}') { _i++; return obj; } // trailing comma tolerated
                        continue;
                    }
                    if (_s[_i] == '}') { _i++; return obj; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private JsonNode ParseArray()
            {
                var arr = JsonNode.NewArray();
                _i++; SkipWs();
                if (!End && _s[_i] == ']') { _i++; return arr; }
                while (true)
                {
                    SkipWs();
                    arr.Add(ParseValue());
                    SkipWs();
                    if (End) throw Error("Unterminated array");
                    if (_s[_i] == ',')
                    {
                        _i++; SkipWs();
                        if (!End && _s[_i] == ']') { _i++; return arr; }
                        continue;
                    }
                    if (_s[_i] == ']') { _i++; return arr; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                _i++; // opening quote
                StringBuilder sb = null;
                int start = _i;
                while (true)
                {
                    if (End) throw Error("Unterminated string");
                    char c = _s[_i];
                    if (c == '"')
                    {
                        string result = sb == null ? _s.Substring(start, _i - start) : sb.Append(_s, start, _i - start).ToString();
                        _i++;
                        return result;
                    }
                    if (c == '\\')
                    {
                        if (sb == null) sb = new StringBuilder();
                        sb.Append(_s, start, _i - start);
                        _i++;
                        if (End) throw Error("Bad escape");
                        char e = _s[_i++];
                        switch (e)
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
                                if (_i + 4 > _s.Length) throw Error("Bad unicode escape");
                                sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                _i += 4; break;
                            default: throw Error($"Bad escape '\\{e}'");
                        }
                        start = _i;
                        continue;
                    }
                    _i++;
                }
            }

            private double ParseNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') _i++;
                    else break;
                }
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw Error("Invalid number");
                return d;
            }
        }

        public static string Write(JsonNode node, bool pretty = true)
        {
            var sb = new StringBuilder();
            WriteNode(sb, node ?? JsonNode.Null, pretty, 0);
            return sb.ToString();
        }

        private static void WriteNode(StringBuilder sb, JsonNode n, bool pretty, int depth)
        {
            switch (n.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(n.BoolValue ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(FormatNumber(n.NumberValue)); break;
                case JsonKind.String: WriteString(sb, n.StringValue); break;
                case JsonKind.Array:
                {
                    sb.Append('[');
                    bool simple = true;
                    foreach (var c in n.ArrayValue) if (c.Kind == JsonKind.Array || c.Kind == JsonKind.Object) { simple = false; break; }
                    for (int i = 0; i < n.ArrayValue.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty && !simple) { sb.Append('\n'); Indent(sb, depth + 1); }
                        else if (pretty && i > 0) sb.Append(' ');
                        WriteNode(sb, n.ArrayValue[i], pretty, depth + 1);
                    }
                    if (pretty && !simple && n.ArrayValue.Count > 0) { sb.Append('\n'); Indent(sb, depth); }
                    sb.Append(']');
                    break;
                }
                case JsonKind.Object:
                {
                    sb.Append('{');
                    int i = 0;
                    foreach (var k in n.KeyOrder)
                    {
                        if (i++ > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n'); Indent(sb, depth + 1); }
                        WriteString(sb, k);
                        sb.Append(pretty ? ": " : ":");
                        WriteNode(sb, n.ObjectValue[k], pretty, depth + 1);
                    }
                    if (pretty && n.KeyOrder.Count > 0) { sb.Append('\n'); Indent(sb, depth); }
                    sb.Append('}');
                    break;
                }
            }
        }

        private static void Indent(StringBuilder sb, int depth) { sb.Append(' ', depth * 2); }

        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
