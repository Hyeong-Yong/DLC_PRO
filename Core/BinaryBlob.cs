#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DLC_PRO.Core
{
    /// <summary>락 포인트 후보 (Command Reference 4.2.4): x, y (float) + 타입 (1 byte).</summary>
    public struct LockCandidate
    {
        public float X;
        public float Y;
        /// <summary>0 none, 1 top, 2 bottom, 3 positive-edge, 4 negative-edge</summary>
        public byte Type;

        public bool IsValid { get { return Type != 0; } }

        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case 1: return "top";
                    case 2: return "bottom";
                    case 3: return "positive-edge";
                    case 4: return "negative-edge";
                    default: return "none";
                }
            }
        }
    }

    /// <summary>
    /// 스코프/락/레코더 바이너리 데이터 (Command Reference 4.2).
    /// 블록 = ID(1 byte) + 길이(10진 문자열) + '\0' + payload. 숫자는 little-endian.
    /// ID: x,y,Y = float 배열 / l,t = 후보 1개 / c = 후보 배열 / s = 락 상태 1 byte /
    ///     a,A,b,B = 와이드스캔 포락선 / i = get-data 인덱스 범위 (int32 2개)
    /// </summary>
    public sealed class BinaryBlob
    {
        private readonly List<KeyValuePair<char, byte[]>> _blocks = new List<KeyValuePair<char, byte[]>>();

        public IList<KeyValuePair<char, byte[]>> Blocks { get { return _blocks; } }

        public static BinaryBlob Parse(byte[] data)
        {
            BinaryBlob b = new BinaryBlob();
            if (data == null) return b;
            int pos = 0;
            while (pos < data.Length)
            {
                char id = (char)data[pos++];
                int zero = Array.IndexOf(data, (byte)0, pos);
                if (zero < 0) throw new DecofException("바이너리 블록 헤더 손상 (길이 종료 문자 없음)");
                string lenStr = Encoding.ASCII.GetString(data, pos, zero - pos);
                int len;
                if (!int.TryParse(lenStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out len) || len < 0)
                    throw new DecofException("바이너리 블록 길이 오류: '" + lenStr + "'");
                pos = zero + 1;
                if ((long)pos + len > data.Length) throw new DecofException("바이너리 블록 길이 초과");
                byte[] payload = new byte[len];
                Array.Copy(data, pos, payload, 0, len);
                b._blocks.Add(new KeyValuePair<char, byte[]>(id, payload));
                pos += len;
            }
            return b;
        }

        public bool Has(char id)
        {
            foreach (KeyValuePair<char, byte[]> kv in _blocks) if (kv.Key == id) return true;
            return false;
        }

        public byte[] Get(char id)
        {
            foreach (KeyValuePair<char, byte[]> kv in _blocks) if (kv.Key == id) return kv.Value;
            return null;
        }

        public float[] Floats(char id)
        {
            byte[] p = Get(id);
            if (p == null) return null;
            if (p.Length % 4 != 0) throw new DecofException("실수 배열 블록 길이 오류");
            int n = p.Length / 4;
            float[] f = new float[n];
            for (int i = 0; i < n; i++) f[i] = ReadSingleLE(p, i * 4);
            return f;
        }

        public int[] Ints(char id)
        {
            byte[] p = Get(id);
            if (p == null) return null;
            if (p.Length % 4 != 0) throw new DecofException("정수 배열 블록 길이 오류");
            int n = p.Length / 4;
            int[] v = new int[n];
            for (int i = 0; i < n; i++)
                v[i] = p[i * 4] | (p[i * 4 + 1] << 8) | (p[i * 4 + 2] << 16) | (p[i * 4 + 3] << 24);
            return v;
        }

        public LockCandidate[] Candidates(char id)
        {
            byte[] p = Get(id);
            if (p == null) return null;
            if (p.Length % 9 != 0) throw new DecofException("락 후보 블록 길이 오류");
            int n = p.Length / 9;
            LockCandidate[] c = new LockCandidate[n];
            for (int i = 0; i < n; i++)
            {
                c[i].X = ReadSingleLE(p, i * 9);
                c[i].Y = ReadSingleLE(p, i * 9 + 4);
                c[i].Type = p[i * 9 + 8];
            }
            return c;
        }

        public int? StateByte(char id)
        {
            byte[] p = Get(id);
            if (p == null || p.Length < 1) return null;
            return p[0];
        }

        private static float ReadSingleLE(byte[] p, int offset)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(p, offset);
            byte[] t = new byte[] { p[offset + 3], p[offset + 2], p[offset + 1], p[offset] };
            return BitConverter.ToSingle(t, 0);
        }

        // ---------- 시뮬레이터/테스트용 인코더 ----------
        public static byte[] Build(IEnumerable<KeyValuePair<char, byte[]>> blocks)
        {
            List<byte> o = new List<byte>();
            foreach (KeyValuePair<char, byte[]> kv in blocks)
            {
                o.Add((byte)kv.Key);
                o.AddRange(Encoding.ASCII.GetBytes(kv.Value.Length.ToString(CultureInfo.InvariantCulture)));
                o.Add(0);
                o.AddRange(kv.Value);
            }
            return o.ToArray();
        }

        public static byte[] FloatsToBytes(float[] f)
        {
            byte[] b = new byte[f.Length * 4];
            for (int i = 0; i < f.Length; i++) Array.Copy(BitConverter.GetBytes(f[i]), 0, b, i * 4, 4);
            return b;
        }

        public static byte[] CandidatesToBytes(IList<LockCandidate> c)
        {
            byte[] b = new byte[c.Count * 9];
            for (int i = 0; i < c.Count; i++)
            {
                Array.Copy(BitConverter.GetBytes(c[i].X), 0, b, i * 9, 4);
                Array.Copy(BitConverter.GetBytes(c[i].Y), 0, b, i * 9 + 4, 4);
                b[i * 9 + 8] = c[i].Type;
            }
            return b;
        }
    }
}
