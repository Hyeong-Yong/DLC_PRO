#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DLC_PRO.Core
{
    /// <summary>레코더(또는 와이드스캔) 기록 데이터.</summary>
    public sealed class RecorderData
    {
        public float[] X = new float[0];
        public float[] Y1 = new float[0];
        public float[] Y2 = new float[0];
        public string XName = "x", XUnit = "";
        public string Y1Name = "ch1", Y1Unit = "";
        public string Y2Name = "ch2", Y2Unit = "";

        public int Count { get { return X.Length; } }

        /// <summary>CH2 데이터가 실제로 있는지 (없으면 NaN으로 채워짐).</summary>
        public bool HasY2
        {
            get { foreach (float v in Y2) if (!float.IsNaN(v)) return true; return false; }
        }

        /// <summary>
        /// laser1:recorder:data:get-data 를 1024개 단위로 반복 호출하여 전체 데이터를 읽는다 (Command Reference 3.x / 4.2).
        /// 반드시 백그라운드 스레드(DlcDevice.RunAsync)에서 호출.
        /// </summary>
        public static RecorderData Fetch(DecofClient c, Action<int, int> progress)
        {
            RecorderData d = ReadHeader(c);
            int total = d._total;
            List<float> xs = new List<float>(total), y1 = new List<float>(total), y2 = new List<float>(total);
            int index = 0;
            while (index < total)
            {
                int got = ReadChunk(c, index, Math.Min(1024, total - index), xs, y1, y2);
                if (got <= 0) break;
                index += got;
                if (progress != null) progress(index, total);
            }
            d.Finish(xs, y1, y2, total);
            return d;
        }

        /// <summary>
        /// 1024점씩 나눠 각각 별도 작업으로 읽는다 (그 사이에 안전 명령 등 다른 명령이 끼어들 수 있음). 권장.
        /// </summary>
        public static async System.Threading.Tasks.Task<RecorderData> FetchAsync(DlcDevice dev, Action<int, int> progress)
        {
            long session = dev.SessionVersion;
            RecorderData d = await dev.RunForSessionAsync(session, c => ReadHeader(c));
            int total = d._total;
            List<float> xs = new List<float>(total), y1 = new List<float>(total), y2 = new List<float>(total);
            int index = 0;
            while (index < total)
            {
                int start = index, count = Math.Min(1024, total - index);
                int got = await dev.RunForSessionAsync(session, c => ReadChunk(c, start, count, xs, y1, y2));
                if (got <= 0) break;
                index += got;
                if (progress != null) progress(index, total);
            }
            d.Finish(xs, y1, y2, total);
            return d;
        }

        private int _total;

        private static RecorderData ReadHeader(DecofClient c)
        {
            RecorderData d = new RecorderData();
            d._total = Math.Max(0, c.GetInt(P.RecDataCount));
            d.XName = SafeStr(c, P.RecDataChxName, "x"); d.XUnit = SafeStr(c, P.RecDataChxUnit, "");
            d.Y1Name = SafeStr(c, P.RecDataCh1Name, "ch1"); d.Y1Unit = SafeStr(c, P.RecDataCh1Unit, "");
            d.Y2Name = SafeStr(c, P.RecDataCh2Name, "ch2"); d.Y2Unit = SafeStr(c, P.RecDataCh2Unit, "");
            return d;
        }

        /// <summary>한 덩어리 읽기. 받은 점 수 반환 (x/y/Y 배열 길이가 다르면 짧은 쪽에 맞춤).</summary>
        private static int ReadChunk(DecofClient c, int index, int count, List<float> xs, List<float> y1, List<float> y2)
        {
            string resp = c.Exec(P.CmdRecGetData, new object[] { index, count }, 15000);
            BinaryBlob b = BinaryBlob.Parse(DecofValue.Binary(DecofClient.LastLine(resp)));
            int[] range = b.Ints('i');
            if (range != null && range.Length >= 2 && range[0] != index)
                throw new DecofException("레코더 데이터 인덱스 불일치: 요청 " + index + ", 응답 " + range[0]);
            float[] fx = b.Floats('x') ?? new float[0], f1 = b.Floats('y'), f2 = b.Floats('Y');
            int got = (range != null && range.Length >= 2) ? range[1] : fx.Length;
            got = Math.Min(got, fx.Length);
            if (f1 != null) got = Math.Min(got, f1.Length);
            if (f2 != null) got = Math.Min(got, f2.Length);
            if (got <= 0) return 0;
            for (int i = 0; i < got; i++)
            {
                xs.Add(fx[i]);
                y1.Add(f1 != null ? f1[i] : float.NaN);
                y2.Add(f2 != null ? f2[i] : float.NaN);
            }
            return got;
        }

        private void Finish(List<float> xs, List<float> y1, List<float> y2, int total)
        {
            X = xs.ToArray(); Y1 = y1.ToArray(); Y2 = y2.ToArray();
            Incomplete = X.Length < total;
        }

        /// <summary>요청한 샘플 수보다 적게 받았으면 true.</summary>
        public bool Incomplete { get; private set; }

        private static string SafeStr(DecofClient c, string name, string fallback)
        {
            try { string s = c.GetString(name); return string.IsNullOrEmpty(s) ? fallback : s; }
            catch { return fallback; }
        }

        public void SaveCsv(string path) { SaveCsv(path, null, null); }

        /// <summary>CSV 저장. extraX가 있으면 X를 변환한 열(예: 상대 주파수 MHz)을 추가.</summary>
        public void SaveCsv(string path, Func<double, double> extraX, string extraName)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                w.WriteLine("# DLC pro recorder data, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv));
                w.WriteLine(string.Format("{0} [{1}],{2} [{3}],{4} [{5}]", XName, XUnit, Y1Name, Y1Unit, Y2Name, Y2Unit)
                            + (extraX != null ? "," + (extraName ?? "x2") : ""));
                int n = X.Length;
                for (int i = 0; i < n; i++)
                {
                    w.Write(X[i].ToString("R", inv)); w.Write(',');
                    if (i < Y1.Length && !float.IsNaN(Y1[i])) w.Write(Y1[i].ToString("R", inv));
                    w.Write(',');
                    if (i < Y2.Length && !float.IsNaN(Y2[i])) w.Write(Y2[i].ToString("R", inv));
                    if (extraX != null) { w.Write(','); w.Write(extraX(X[i]).ToString("R", inv)); }
                    w.WriteLine();
                }
            }
        }
    }
}
