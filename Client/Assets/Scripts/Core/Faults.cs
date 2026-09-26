using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bloodfall.Client.Core
{
    /// <summary>
    /// Contains client faults. A per-frame system that throws is logged once and then at most every ten seconds (with a
    /// repeat count) instead of writing a stack trace every frame, which would stall the game on its own; the rest of the
    /// frame keeps running. Faults are also counted so the session can report them.
    /// </summary>
    public static class Faults
    {
        private const float RepeatInterval = 10f;
        private static readonly Dictionary<string, float> LastLogged = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> Suppressed = new Dictionary<string, int>();

        /// <summary>Faults contained since start-up.</summary>
        public static int Count { get; private set; }

        public static void Report(string where, Exception e)
        {
            Count++;
            string key = where + "|" + e.GetType().FullName + "|" + e.Message;
            float now = Time.realtimeSinceStartup;
            if (LastLogged.TryGetValue(key, out var last) && now - last < RepeatInterval)
            {
                Suppressed[key] = Suppressed.TryGetValue(key, out var n) ? n + 1 : 1;
                return;
            }
            if (LastLogged.Count > 256) { LastLogged.Clear(); Suppressed.Clear(); }
            LastLogged[key] = now;
            int repeats = Suppressed.TryGetValue(key, out var r) ? r : 0;
            Suppressed[key] = 0;
            Debug.LogError($"[{where}] {(repeats > 0 ? $"(repeated {repeats}x) " : "")}{e}");
        }

        /// <summary>Runs <paramref name="a"/>, containing and reporting anything it throws.</summary>
        public static void Run(string where, Action a)
        {
            try { a(); }
            catch (Exception e) { Report(where, e); }
        }
    }
}
