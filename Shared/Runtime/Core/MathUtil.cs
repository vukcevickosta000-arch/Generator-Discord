using System;
using System.Numerics;

namespace Bloodfall.Core
{
    public static class MathUtil
    {
        public const float Pi = (float)Math.PI;
        public const float TwoPi = (float)(Math.PI * 2);
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;

        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-6f ? 0 : Clamp01((v - a) / (b - a));

        /// <summary>Wraps an angle to (-PI, PI].</summary>
        public static float WrapAngle(float a)
        {
            while (a > Pi) a -= TwoPi;
            while (a <= -Pi) a += TwoPi;
            return a;
        }

        public static float AngleOf(Vector2 dir) => (float)Math.Atan2(dir.Y, dir.X);
        public static Vector2 FromAngle(float a) => new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
        public static float AngleDelta(float from, float to) => WrapAngle(to - from);

        /// <summary>Rotates 'current' toward 'target' by at most maxDelta radians.</summary>
        public static float RotateTowards(float current, float target, float maxDelta)
        {
            float d = AngleDelta(current, target);
            if (Math.Abs(d) <= maxDelta) return WrapAngle(target);
            return WrapAngle(current + Math.Sign(d) * maxDelta);
        }

        public static Vector2 SafeNormalize(Vector2 v, Vector2 fallback)
        {
            float len = v.Length();
            return len > 1e-5f ? v / len : fallback;
        }

        public static float DistSq(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b);
        public static float Dist(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

        /// <summary>Closest point on segment ab to p.</summary>
        public static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-8f) return a;
            float t = Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return a + ab * t;
        }

        public static Vector2 MoveTowards(Vector2 from, Vector2 to, float maxDist)
        {
            var d = to - from;
            float len = d.Length();
            if (len <= maxDist || len < 1e-6f) return to;
            return from + d / len * maxDist;
        }

        /// <summary>
        /// Armor damage multiplier used by Bloodfall (classic MOBA curve):
        /// 1 - (0.06 * armor) / (1 + 0.06 * |armor|). Negative armor amplifies damage.
        /// </summary>
        public static float ArmorMultiplier(float armor)
        {
            return 1f - (0.06f * armor) / (1f + 0.06f * Math.Abs(armor));
        }

        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
    }

    /// <summary>Small fast deterministic PRNG (xorshift128+). Same results on client and server.</summary>
    public sealed class DeterministicRandom
    {
        private ulong _s0, _s1;

        public DeterministicRandom(ulong seed)
        {
            // SplitMix64 to spread the seed.
            _s0 = SplitMix(ref seed);
            _s1 = SplitMix(ref seed);
            if (_s0 == 0 && _s1 == 0) _s1 = 1;
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong s1 = _s0, s0 = _s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
            return _s1 + s0;
        }

        /// <summary>[0,1)</summary>
        public float NextFloat() => (NextULong() >> 40) * (1.0f / (1UL << 24));
        public float Range(float min, float max) => min + (max - min) * NextFloat();
        /// <summary>[min, maxExclusive)</summary>
        public int Range(int min, int maxExclusive) => maxExclusive <= min ? min : min + (int)(NextULong() % (ulong)(maxExclusive - min));
        public bool Chance(float p) => NextFloat() < p;
    }

    /// <summary>
    /// Pseudo-random distribution: probability grows each failed roll so procs are evenly spread
    /// (the classic competitive-MOBA approach for bashes/crits; avoids streaky RNG).
    /// </summary>
    public struct PseudoRandom
    {
        private int _failures;

        public bool Roll(DeterministicRandom rng, float nominalChance)
        {
            if (nominalChance <= 0) return false;
            if (nominalChance >= 1) return true;
            float c = PrdConstant(nominalChance);
            float p = c * (_failures + 1);
            if (rng.NextFloat() < p) { _failures = 0; return true; }
            _failures++;
            return false;
        }

        private static readonly System.Collections.Generic.Dictionary<int, float> CCache = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>PRD constant C such that the long-run proc rate equals p (solved by bisection, cached per 0.1%).</summary>
        public static float PrdConstant(float p)
        {
            int key = (int)Math.Round(p * 1000);
            lock (CCache)
            {
                if (CCache.TryGetValue(key, out var cached)) return cached;
                double target = key / 1000.0, lo = 0, hi = target;
                for (int it = 0; it < 40; it++)
                {
                    double mid = (lo + hi) * 0.5;
                    if (RateForC(mid) > target) hi = mid; else lo = mid;
                }
                float c = (float)((lo + hi) * 0.5);
                CCache[key] = c;
                return c;
            }
        }

        private static double RateForC(double c)
        {
            // Expected trials until success: sum over n of n * P(first success at n).
            double expected = 0, notYet = 1;
            int maxN = (int)Math.Ceiling(1.0 / Math.Max(c, 1e-6));
            for (int n = 1; n <= maxN; n++)
            {
                double pn = Math.Min(1.0, c * n);
                expected += n * notYet * pn;
                notYet *= 1 - pn;
            }
            return 1.0 / expected;
        }
    }
}
