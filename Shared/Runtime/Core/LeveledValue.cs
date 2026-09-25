using System;
using System.Globalization;

namespace Bloodfall.Core
{
    /// <summary>
    /// A number that may vary per ability/item level. In JSON it is either a single number (<c>40</c>)
    /// or an array indexed by level (<c>[40, 80, 120, 160]</c>). Levels are 1-based; out of range clamps.
    /// </summary>
    public sealed class LeveledValue : IJsonCustom
    {
        public float[] Values = { 0f };

        public LeveledValue() { }
        public LeveledValue(params float[] values) { Values = values != null && values.Length > 0 ? values : new[] { 0f }; }

        public static implicit operator LeveledValue(float v) => new LeveledValue(v);

        public float Get(int level)
        {
            if (Values == null || Values.Length == 0) return 0;
            int idx = Math.Max(0, Math.Min(Values.Length - 1, level - 1));
            return Values[idx];
        }

        public bool IsZero
        {
            get
            {
                if (Values == null) return true;
                foreach (var v in Values) if (Math.Abs(v) > 1e-6f) return false;
                return true;
            }
        }

        public void ReadJson(JsonNode node)
        {
            if (node.Kind == JsonKind.Array)
            {
                Values = new float[node.Count];
                for (int i = 0; i < node.Count; i++) Values[i] = node[i].AsFloat();
                if (Values.Length == 0) Values = new[] { 0f };
            }
            else Values = new[] { node.AsFloat() };
        }

        public JsonNode WriteJson()
        {
            if (Values.Length == 1) return JsonNode.From(Values[0]);
            var arr = JsonNode.NewArray();
            foreach (var v in Values) arr.Add(JsonNode.From(v));
            return arr;
        }

        public override string ToString()
        {
            if (Values.Length == 1) return Values[0].ToString("0.##", CultureInfo.InvariantCulture);
            return string.Join(" / ", Array.ConvertAll(Values, v => v.ToString("0.##", CultureInfo.InvariantCulture)));
        }
    }
}
