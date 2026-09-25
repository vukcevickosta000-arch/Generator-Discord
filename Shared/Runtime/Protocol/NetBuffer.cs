using System;
using System.Numerics;
using System.Text;

namespace Bloodfall.Protocol
{
    /// <summary>Growable little-endian binary writer with varints and game-specific quantisation.</summary>
    public sealed class NetWriter
    {
        private byte[] _buf;
        public int Length { get; private set; }

        public NetWriter(int capacity = 256) { _buf = new byte[capacity]; }

        public void Reset() => Length = 0;
        public byte[] Buffer => _buf;
        public byte[] ToArray()
        {
            var a = new byte[Length];
            System.Buffer.BlockCopy(_buf, 0, a, 0, Length);
            return a;
        }

        private void Ensure(int extra)
        {
            if (Length + extra <= _buf.Length) return;
            int n = Math.Max(_buf.Length * 2, Length + extra);
            Array.Resize(ref _buf, n);
        }

        public void WriteByte(byte v) { Ensure(1); _buf[Length++] = v; }
        public void WriteBool(bool v) => WriteByte((byte)(v ? 1 : 0));
        public void WriteSByte(sbyte v) => WriteByte(unchecked((byte)v));

        public void WriteUShort(ushort v) { Ensure(2); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); }
        public void WriteInt(int v) { Ensure(4); for (int i = 0; i < 4; i++) _buf[Length++] = (byte)(v >> (8 * i)); }
        public void WriteLong(long v) { Ensure(8); for (int i = 0; i < 8; i++) _buf[Length++] = (byte)(v >> (8 * i)); }

        public void WriteFloat(float v)
        {
            var bytes = BitConverter.GetBytes(v);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Ensure(4);
            System.Buffer.BlockCopy(bytes, 0, _buf, Length, 4);
            Length += 4;
        }

        public void WriteVarUInt(uint v)
        {
            Ensure(5);
            while (v >= 0x80) { _buf[Length++] = (byte)(v | 0x80); v >>= 7; }
            _buf[Length++] = (byte)v;
        }

        public void WriteVarInt(int v) => WriteVarUInt((uint)((v << 1) ^ (v >> 31)));

        public void WriteString(string s)
        {
            if (s == null) { WriteVarUInt(0); return; }
            var bytes = Encoding.UTF8.GetBytes(s);
            WriteVarUInt((uint)bytes.Length + 1);
            Ensure(bytes.Length);
            System.Buffer.BlockCopy(bytes, 0, _buf, Length, bytes.Length);
            Length += bytes.Length;
        }

        public void WriteBytes(byte[] data, int offset, int count)
        {
            Ensure(count);
            System.Buffer.BlockCopy(data, offset, _buf, Length, count);
            Length += count;
        }

        /// <summary>World position quantised to 1/256 m over 0..256 m.</summary>
        public void WritePos(Vector2 p)
        {
            WriteUShort(QuantizePos(p.X));
            WriteUShort(QuantizePos(p.Y));
        }

        public static ushort QuantizePos(float v) => (ushort)Math.Max(0, Math.Min(65535, (int)Math.Round(v * 256f)));

        /// <summary>Angle in radians quantised to one byte (1.4 degree steps).</summary>
        public void WriteAngle(float radians)
        {
            float t = radians / (float)(Math.PI * 2);
            t -= (float)Math.Floor(t);
            WriteByte((byte)((int)Math.Round(t * 256f) & 0xFF));
        }

        /// <summary>Non-negative value with 0.1 precision as varuint (cooldowns, timers).</summary>
        public void WriteTenths(float v) => WriteVarUInt((uint)Math.Max(0, (int)Math.Round(v * 10f)));
    }

    public sealed class NetReader
    {
        private readonly byte[] _buf;
        private readonly int _end;
        public int Position { get; private set; }

        public NetReader(byte[] buf) : this(buf, 0, buf.Length) { }
        public NetReader(byte[] buf, int offset, int count) { _buf = buf; Position = offset; _end = offset + count; }

        public bool AtEnd => Position >= _end;
        public int Remaining => _end - Position;

        private void Need(int n) { if (Position + n > _end) throw new FormatException("Packet truncated"); }

        public byte ReadByte() { Need(1); return _buf[Position++]; }
        public bool ReadBool() => ReadByte() != 0;
        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());
        public ushort ReadUShort() { Need(2); ushort v = (ushort)(_buf[Position] | (_buf[Position + 1] << 8)); Position += 2; return v; }
        public int ReadInt() { Need(4); int v = 0; for (int i = 0; i < 4; i++) v |= _buf[Position++] << (8 * i); return v; }
        public long ReadLong() { Need(8); long v = 0; for (int i = 0; i < 8; i++) v |= (long)_buf[Position++] << (8 * i); return v; }

        public float ReadFloat()
        {
            Need(4);
            var tmp = new byte[4];
            System.Buffer.BlockCopy(_buf, Position, tmp, 0, 4);
            if (!BitConverter.IsLittleEndian) Array.Reverse(tmp);
            Position += 4;
            return BitConverter.ToSingle(tmp, 0);
        }

        public uint ReadVarUInt()
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                if (shift > 28) throw new FormatException("VarUInt too long");
                byte b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
        }

        public int ReadVarInt()
        {
            uint v = ReadVarUInt();
            return (int)(v >> 1) ^ -(int)(v & 1);
        }

        public string ReadString(int maxBytes = 4096)
        {
            uint len = ReadVarUInt();
            if (len == 0) return null;
            int n = (int)len - 1;
            if (n > maxBytes) throw new FormatException("String too long");
            Need(n);
            var s = Encoding.UTF8.GetString(_buf, Position, n);
            Position += n;
            return s;
        }

        public Vector2 ReadPos() => new Vector2(ReadUShort() / 256f, ReadUShort() / 256f);
        public float ReadAngle() => ReadByte() / 256f * (float)(Math.PI * 2);
        public float ReadTenths() => ReadVarUInt() / 10f;
    }
}
