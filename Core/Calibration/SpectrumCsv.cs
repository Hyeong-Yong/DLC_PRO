using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DLC_PRO.Core.Calibration {
    /// <summary>불러온 스캔 데이터 (x = 피에조 전압 V).</summary>
    public sealed class SpectrumData {
        public double[] X { get; init; } = Array.Empty<double>();
        public double[] Y { get; init; } = Array.Empty<double>();
        public string XName { get; init; } = "";
        public string YName { get; init; } = "";
        /// <summary>파일의 모든 열 이름.</summary>
        public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
        public int YColumn { get; init; }
        public string Source { get; init; } = "";
    }

    /// <summary>
    /// 스캔 CSV 읽기/쓰기.
    /// 읽기 지원 형식:
    ///  - DLC pro/TOPAS 내보내기: "Piezo Voltage (V)";"Fine In 1 (V)";  (세미콜론, 끝에 구분자)
    ///  - 이 앱의 "스캔 CSV 저장" (위와 같은 형식)
    ///  - 이 앱 그래프 오른쪽 클릭 "데이터 CSV로 저장" (쉼표, CH1_x,CH1_y,...)
    /// 첫 열은 피에조 전압이어야 한다. MHz/시간 축으로 저장된 파일은 거부한다.
    /// </summary>
    public static class SpectrumCsv {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static SpectrumData Load(string path, int yColumn = -1) =>
            Parse(File.ReadAllLines(path, Encoding.UTF8), Path.GetFileName(path), yColumn);

        public static SpectrumData Parse(IEnumerable<string> lines, string source, int yColumn = -1) {
            List<string> comments = new List<string>();
            string[]? header = null;
            char sep = '\0';
            List<double[]> rows = new List<double[]>();
            foreach (string raw in lines) {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0) continue;
                if (line.StartsWith("#")) { comments.Add(line); continue; }
                if (sep == '\0') sep = line.Contains(';') ? ';' : line.Contains('\t') ? '\t' : ',';
                string[] f = Split(line, sep);
                if (f.Length == 0) continue;
                if (!TryNum(f[0], sep, out _)) {
                    if (header == null && rows.Count == 0) header = f.Select(Unquote).ToArray();
                    continue;
                }
                double[] v = new double[f.Length];
                bool ok = true;
                for (int i = 0; i < f.Length; i++) {
                    if (f[i].Length == 0) { v[i] = double.NaN; continue; }
                    if (!TryNum(f[i], sep, out v[i])) { ok = false; break; }
                }
                if (ok) rows.Add(v);
            }
            if (rows.Count < 20) throw new InvalidDataException("데이터 행이 너무 적습니다 (" + rows.Count + "행). 스캔 CSV인지 확인해 주세요.");
            int cols = rows.Max(r => r.Length);
            if (cols < 2) throw new InvalidDataException("열이 2개 이상 필요합니다 (피에조 전압, 신호).");
            string[] names = new string[cols];
            for (int i = 0; i < cols; i++) names[i] = header != null && i < header.Length && header[i].Length > 0 ? header[i] : "Column " + (i + 1);

            string xName = names[0];
            string xInfo = xName + " " + string.Join(" ", comments);
            if (xInfo.IndexOf("MHz", StringComparison.OrdinalIgnoreCase) >= 0 || xName.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0
                || xName.IndexOf("Frequency", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidDataException("X축이 피에조 전압(V)이 아닙니다: " + xName +
                    "\n그래프를 MHz 축으로 저장했다면 'X축 MHz'를 끄고 저장하거나 [스캔 CSV 저장]을 사용해 주세요.");

            int yc = yColumn;
            if (yc < 1 || yc >= cols) {
                yc = -1;
                for (int i = 1; i < cols && yc < 0; i++)
                    if (names[i].IndexOf("In ", StringComparison.OrdinalIgnoreCase) >= 0 || names[i].EndsWith("_y", StringComparison.OrdinalIgnoreCase)) yc = i;
                if (yc < 0) yc = 1;
            }
            List<(double x, double y)> pts = new List<(double, double)>(rows.Count);
            foreach (double[] r in rows) {
                if (r.Length <= yc) continue;
                double x = r[0], y = r[yc];
                if (double.IsFinite(x) && double.IsFinite(y)) pts.Add((x, y));
            }
            if (pts.Count < 20) throw new InvalidDataException("'" + names[yc] + "' 열에 유효한 값이 부족합니다.");
            pts.Sort((a, b) => a.x.CompareTo(b.x));
            return new SpectrumData {
                X = pts.Select(p => p.x).ToArray(),
                Y = pts.Select(p => p.y).ToArray(),
                XName = xName,
                YName = names[yc],
                Columns = names,
                YColumn = yc,
                Source = source,
            };
        }

        /// <summary>스캔 데이터를 DLC pro 내보내기와 같은 형식으로 저장 ("Piezo Voltage (V)";"Fine In 1 (V)";…).</summary>
        public static void Save(string path, string xName, IReadOnlyList<float> x, IReadOnlyList<(string Name, IReadOnlyList<float> Values)> ys) {
            StringBuilder b = new StringBuilder();
            b.Append(Q(xName)).Append(';');
            foreach (var y in ys) b.Append(Q(y.Name)).Append(';');
            b.Append('\n');
            for (int i = 0; i < x.Count; i++) {
                b.Append(((double)x[i]).ToString("0.######", Inv)).Append(';');
                foreach (var y in ys) b.Append(i < y.Values.Count ? ((double)y.Values[i]).ToString("0.########", Inv) : "").Append(';');
                b.Append('\n');
            }
            File.WriteAllText(path, b.ToString(), new UTF8Encoding(false));
        }

        private static string Q(string s) => "\"" + s.Replace("\"", "'") + "\"";

        private static string[] Split(string line, char sep) {
            string[] f = line.Split(sep);
            int n = f.Length;
            while (n > 0 && f[n - 1].Trim().Length == 0) n--;   // 끝의 빈 필드 (DLC pro 형식의 마지막 ';')
            string[] r = new string[n];
            for (int i = 0; i < n; i++) r[i] = f[i].Trim();
            return r;
        }

        private static string Unquote(string s) => s.Trim().Trim('"').Trim();

        private static bool TryNum(string s, char sep, out double v) {
            s = Unquote(s);
            if (double.TryParse(s, NumberStyles.Float, Inv, out v)) return true;
            // 세미콜론 형식에서 소수점이 쉼표인 경우 (유럽식 로캘)
            return sep != ',' && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, Inv, out v);
        }
    }
}
