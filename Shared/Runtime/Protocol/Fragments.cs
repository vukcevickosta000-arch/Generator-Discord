using System;
using System.Collections.Generic;

namespace Bloodfall.Protocol
{
    /// <summary>
    /// Splits large unreliable messages (snapshots) into MTU-sized parts and reassembles them. A snapshot whose parts
    /// do not all arrive is simply dropped - the next one supersedes it, which avoids head-of-line blocking that a
    /// reliable channel would cause.
    /// Part layout: [0xF0][u16 messageId][u8 index][u8 count][payload...]
    /// </summary>
    public static class Fragments
    {
        public const byte PartMarker = 0xF0;
        public const int MaxPayload = 900; // LiteNetLib's conservative unreliable limit is ~1023 bytes

        public static bool NeedsSplit(byte[] data) => data.Length > MaxPayload;

        public static List<byte[]> Split(byte[] data, ushort messageId)
        {
            int count = (data.Length + MaxPayload - 1) / MaxPayload;
            if (count > 255) throw new InvalidOperationException("Message too large to fragment");
            var parts = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                int off = i * MaxPayload;
                int len = Math.Min(MaxPayload, data.Length - off);
                var p = new byte[len + 5];
                p[0] = PartMarker;
                p[1] = (byte)messageId;
                p[2] = (byte)(messageId >> 8);
                p[3] = (byte)i;
                p[4] = (byte)count;
                Buffer.BlockCopy(data, off, p, 5, len);
                parts.Add(p);
            }
            return parts;
        }
    }

    public sealed class FragmentAssembler
    {
        private sealed class Pending
        {
            public byte[][] Parts;
            public int Received;
            public int Age;
        }

        private readonly Dictionary<ushort, Pending> _pending = new Dictionary<ushort, Pending>();

        /// <summary>Returns a complete message when the last missing part arrives, otherwise null.</summary>
        public byte[] Add(byte[] data, int offset, int count)
        {
            if (count < 5 || data[offset] != Fragments.PartMarker) return null;
            ushort id = (ushort)(data[offset + 1] | (data[offset + 2] << 8));
            int index = data[offset + 3], total = data[offset + 4];
            if (total == 0 || index >= total) return null;
            if (!_pending.TryGetValue(id, out var p))
            {
                p = new Pending { Parts = new byte[total][] };
                _pending[id] = p;
                // Age out stale partial messages.
                var stale = new List<ushort>();
                foreach (var kv in _pending) if (++kv.Value.Age > 8) stale.Add(kv.Key);
                foreach (var s in stale) _pending.Remove(s);
            }
            if (p.Parts.Length != total || p.Parts[index] != null) return null;
            var chunk = new byte[count - 5];
            Buffer.BlockCopy(data, offset + 5, chunk, 0, chunk.Length);
            p.Parts[index] = chunk;
            if (++p.Received < total) return null;
            _pending.Remove(id);
            int len = 0;
            foreach (var c in p.Parts) len += c.Length;
            var result = new byte[len];
            int o = 0;
            foreach (var c in p.Parts) { Buffer.BlockCopy(c, 0, result, o, c.Length); o += c.Length; }
            return result;
        }
    }
}
