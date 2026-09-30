using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DLC_PRO.Core.Calibration {
    /// <summary>교정 판정 기준.</summary>
    public sealed class CalibrationOptions {
        /// <summary>현재(또는 예상) 튜닝 계수 [MHz/V, 부호 포함]. 탐색 범위의 중심. 0이면 328 MHz/V.</summary>
        public double ExpectedMHzPerV { get; init; } = 328;
        /// <summary>
        /// 주파수 방향: 0 = 자동, +1 = 전압↑ → 주파수↑, −1 = 전압↑ → 주파수↓.
        /// 자동일 때 선이 3개 이상이면 패턴으로 판단하고, 2개뿐이면(방향을 알 수 없음) ExpectedMHzPerV의 부호를 따른다.
        /// </summary>
        public int Direction { get; init; }
        /// <summary>탐색 범위: 예상값 × [MinFactor, MaxFactor] (양/음 방향 모두).</summary>
        public double MinFactor { get; init; } = 0.35;
        public double MaxFactor { get; init; } = 3.0;
        /// <summary>두 간격에서 구한 계수의 허용 차이 (비율). 기본 3 %.</summary>
        public double ConsistencyTolerance { get; init; } = 0.03;
        /// <summary>모든 선 위치의 이론 패턴 대비 잔차 허용 [MHz] (선이 3개 이상일 때).</summary>
        public double MaxPatternRmsMHz { get; init; } = 10;
        /// <summary>분산형 판정: 0 V 기준 양/음 신호 대칭도 최소값 (0 = 한쪽 극성뿐(흡수형), 1 = 완전 대칭(분산형)).</summary>
        public double MinBipolarity { get; init; } = 0.15;
        /// <summary>선 폭 초기값 (HWHM, MHz).</summary>
        public double InitialHwhmMHz { get; init; } = 8;
    }

    /// <summary>찾은 선 하나.</summary>
    public sealed record FittedLine(string Name, double OffsetMHz, double CenterV, double CenterErrV, double HwhmV,
                                    double Absorptive, double Dispersive) {
        /// <summary>분산형 성분 비율 |D| / (|A| + |D|).</summary>
        public double DispersiveFraction => Math.Abs(Dispersive) + Math.Abs(Absorptive) > 0
            ? Math.Abs(Dispersive) / (Math.Abs(Dispersive) + Math.Abs(Absorptive)) : 0;
    }

    public sealed record IntervalResult(string Name, double TheoryMHz, double MeasuredV, double MHzPerV, double ErrMHzPerV);

    public sealed class CalibrationResult {
        public bool Success { get; set; }
        public CalibrationPreset Preset { get; set; } = CalibrationPresets.CsD2;
        /// <summary>교정된 튜닝 계수 [MHz/V] (부호 = 전압 증가 시 주파수 방향). 실패해도 계산됐으면 값이 있다.</summary>
        public double MHzPerV { get; set; } = double.NaN;
        public double GHzPerV => MHzPerV / 1000.0;
        public List<FittedLine> Lines { get; } = new List<FittedLine>();
        public List<IntervalResult> Intervals { get; } = new List<IntervalResult>();
        /// <summary>간격별 계수 차이 (최대-최소)/평균.</summary>
        public double Consistency { get; set; } = double.NaN;
        public double PatternRmsMHz { get; set; } = double.NaN;
        public double Bipolarity { get; set; } = double.NaN;
        /// <summary>기준선(마지막 간격의 To) 근처 신호가 0 V를 지나는 위치의 공명 대비 차이 [MHz] (없으면 NaN).</summary>
        public double ZeroCrossingOffsetMHz { get; set; } = double.NaN;
        public double FitRms { get; set; } = double.NaN;
        public double Noise { get; set; } = double.NaN;
        public List<string> Failures { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Details { get; } = new List<string>();

        public string Summary {
            get {
                CultureInfo inv = CultureInfo.InvariantCulture;
                if (Success) return string.Format(inv, "SUCCESS · 튜닝 계수 {0:F1} MHz/V ({1:F5} GHz/V)", MHzPerV, GHzPerV);
                return "FAIL · " + (Failures.Count > 0 ? Failures[0] : "교정할 수 없습니다");
            }
        }
    }

    /// <summary>
    /// 스캔 스펙트럼(피에조 전압 vs 신호)에서 알려진 원자 선 간격으로 피에조 튜닝 계수를 구한다.
    /// 1) 모든 선 패턴(교차 공명 포함)을 격자 탐색 + Levenberg–Marquardt로 맞춰 선 배정
    ///    (선 모양 = 흡수형·분산형 Lorentzian 혼합, 배경 = 4차 다항식, 진폭은 선형 최소제곱)
    /// 2) 선마다 중심/폭을 따로 풀어 정밀 중심 전압을 구함
    /// 3) 이론 간격 / 측정 전압 간격 = 튜닝 계수, 간격끼리 비교해 일관성 확인
    /// 4) 0 V 기준 신호 대칭도로 분산형(편광 분광 신호) 우세 여부 확인
    /// </summary>
    public static class TuningCalibrator {
        private const int PolyDegree = 4;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static CalibrationResult Analyze(double[] x, double[] y, CalibrationPreset preset, CalibrationOptions? options = null) {
            CalibrationOptions o = options ?? new CalibrationOptions();
            CalibrationResult res = new CalibrationResult { Preset = preset };
            if (x.Length != y.Length || x.Length < 100) {
                res.Failures.Add("데이터 점이 부족합니다 (100점 이상 필요).");
                return res;
            }
            double xmin = x.Min(), xmax = x.Max();
            if (!(xmax - xmin > 1e-6)) {
                res.Failures.Add("피에조 전압 범위가 0입니다 (스캔이 멈춘 상태의 데이터).");
                return res;
            }
            double expected = Math.Abs(o.ExpectedMHzPerV) > 1 ? Math.Abs(o.ExpectedMHzPerV) : 328;
            double kMin = Math.Max(10, expected * o.MinFactor), kMax = Math.Min(20000, expected * o.MaxFactor);
            double[] nu = preset.Lines.Select(l => l.OffsetMHz).ToArray();
            int nl = nu.Length;
            double span = nu.Max() - nu.Min();
            if (span / kMax > xmax - xmin) {
                res.Failures.Add(string.Format(Inv, "스캔 범위 {0:F3} V가 선 전체 간격({1:F0} MHz)보다 좁습니다. 스캔 폭을 넓혀 주세요.", xmax - xmin, span));
                return res;
            }
            Model full = new Model(x, y, xmin, xmax);
            res.Noise = full.Noise();
            res.Bipolarity = Bipolarity(y);

            // ---------------- 1) 격자 탐색 (축소 데이터, 공통 폭) ----------------
            (double[] xd, double[] yd) = Downsample(x, y, 400);
            Model small = new Model(xd, yd, xmin, xmax);
            List<(double rss, double x0, double k)> best = new List<(double, double, double)>();
            int nk = 70;
            int dir = o.Direction != 0 ? Math.Sign(o.Direction) : nl < 3 ? (o.ExpectedMHzPerV < 0 ? -1 : 1) : 0;
            if (o.Direction == 0 && nl < 3)
                res.Warnings.Add("선이 2개뿐이라 주파수 방향을 스펙트럼으로 알 수 없어 현재 계수의 부호(" + (dir > 0 ? "+" : "−") + ")를 따랐습니다.");
            for (int sgn = -1; sgn <= 1; sgn += 2) {
                if (dir != 0 && sgn != dir) continue;
                for (int ik = 0; ik < nk; ik++) {
                    double k = sgn * kMin * Math.Pow(kMax / kMin, ik / (double)(nk - 1));
                    double g = o.InitialHwhmMHz / Math.Abs(k);
                    double lo = nu.Min(v => v / k), hi = nu.Max(v => v / k);
                    double a = xmin + 2 * g - lo, b = xmax - 2 * g - hi;
                    if (b < a) continue;
                    double step = Math.Max(g, (b - a) / 2000);
                    double[] cs = new double[nl], gs = Enumerable.Repeat(g, nl).ToArray();
                    for (double x0 = a; x0 <= b; x0 += step) {
                        for (int i = 0; i < nl; i++) cs[i] = x0 + nu[i] / k;
                        double rss = small.Rss(cs, gs);
                        Keep(best, (rss, x0, k), g);
                    }
                }
            }
            if (best.Count == 0) {
                res.Failures.Add("스캔 범위 안에서 선 패턴을 찾지 못했습니다.");
                return res;
            }

            // ---------------- 2) 패턴 제약 맞춤 (x0, k, 공통 폭) ----------------
            double bestCost = double.MaxValue;
            double[] bp = Array.Empty<double>();
            foreach (var cand in best.OrderBy(c => c.rss).Take(6)) {
                double[] p0 = { cand.x0, cand.k, Math.Log(o.InitialHwhmMHz / Math.Abs(cand.k)) };
                double[] p = Lm.Minimize(q => full.Residual(PatternCenters(q, nu), Enumerable.Repeat(Math.Exp(q[2]), nl).ToArray()), p0, 60, out double cost);
                double[] cs = PatternCenters(p, nu);
                if (cs.Min() < xmin || cs.Max() > xmax) continue;
                if (cost < bestCost) { bestCost = cost; bp = p; }
            }
            if (bp.Length == 0) {
                res.Failures.Add("선 패턴 맞춤이 수렴하지 않았습니다.");
                return res;
            }
            double[] c0 = PatternCenters(bp, nu);
            double g0 = Math.Exp(bp[2]);

            // ---------------- 3) 선별 중심/폭 자유 맞춤 ----------------
            double[] q0 = new double[2 * nl];
            for (int i = 0; i < nl; i++) { q0[i] = c0[i]; q0[nl + i] = Math.Log(g0); }
            Func<double[], double[]> free = q => full.Residual(q.Take(nl).ToArray(), q.Skip(nl).Select(Math.Exp).ToArray());
            double[] qf = Lm.Minimize(free, q0, 120, out double fcost);
            double[] cf = qf.Take(nl).ToArray(), gf = qf.Skip(nl).Select(Math.Exp).ToArray();
            double[] ce = Lm.ParameterErrors(free, qf, fcost, full.N - 2 * nl - full.M(nl));
            // 자유 맞춤이 이웃 선으로 건너가면(선이 겹친 경우) 패턴 위치를 사용
            double minSpacing = double.MaxValue;
            for (int i = 0; i < nl; i++) for (int j = i + 1; j < nl; j++) minSpacing = Math.Min(minSpacing, Math.Abs(c0[i] - c0[j]));
            for (int i = 0; i < nl; i++) {
                if (Math.Abs(cf[i] - c0[i]) > Math.Max(3 * g0, 0.45 * minSpacing) || gf[i] > 20 * g0 || gf[i] < g0 / 20) {
                    res.Warnings.Add(preset.Lines[i].Name + ": 선이 뚜렷하지 않아 패턴 위치를 사용했습니다.");
                    cf[i] = c0[i]; gf[i] = g0; ce[i] = double.NaN;
                }
            }
            double[] amp = full.Amplitudes(cf, gf);
            res.FitRms = Math.Sqrt(full.Rss(cf, gf) / full.N);
            for (int i = 0; i < nl; i++)
                res.Lines.Add(new FittedLine(preset.Lines[i].Name, nu[i], cf[i], ce[i], gf[i], amp[PolyDegree + 1 + 2 * i], amp[PolyDegree + 2 + 2 * i]));

            // ---------------- 4) 간격 → 튜닝 계수 ----------------
            List<double> ks = new List<double>();
            foreach (CalibrationInterval iv in preset.Intervals) {
                double dv = cf[iv.To] - cf[iv.From];
                double dnu = preset.IntervalMHz(iv);
                double k = dnu / dv;
                double ev = Math.Sqrt(Sq(ce[iv.To]) + Sq(ce[iv.From]));
                res.Intervals.Add(new IntervalResult(preset.IntervalName(iv), dnu, dv, k, double.IsNaN(ev) ? double.NaN : Math.Abs(k) * ev / Math.Abs(dv)));
                ks.Add(k);
            }
            double kAvg = ks.Average();
            res.MHzPerV = kAvg;
            if (ks.Count >= 2) res.Consistency = (ks.Max() - ks.Min()) / Math.Abs(kAvg);
            if (nl >= 3) {
                double off = 0;
                for (int i = 0; i < nl; i++) off += nu[i] - kAvg * cf[i];
                off /= nl;
                double s = 0;
                for (int i = 0; i < nl; i++) s += Sq(nu[i] - (off + kAvg * cf[i]));
                res.PatternRmsMHz = Math.Sqrt(s / nl);
            }
            int refIdx = preset.Intervals[0].To;
            res.ZeroCrossingOffsetMHz = ZeroCrossing(x, y, cf[refIdx], 4 * gf[refIdx], kAvg);

            // ---------------- 5) 판정 ----------------
            double ak = Math.Abs(kAvg);
            if (ak < kMin * 1.02 || ak > kMax * 0.98)
                res.Failures.Add(string.Format(Inv, "튜닝 계수 {0:F1} MHz/V가 예상 범위({1:F0}~{2:F0} MHz/V) 경계입니다. 선 배정을 확인해 주세요.", ak, kMin, kMax));
            if (!double.IsNaN(res.Consistency) && res.Consistency > o.ConsistencyTolerance)
                res.Failures.Add(string.Format(Inv, "두 간격에서 구한 계수 차이 {0:F1}%가 허용 {1:F1}%를 넘습니다 (선 간격이 이론 비율과 맞지 않음).",
                    res.Consistency * 100, o.ConsistencyTolerance * 100));
            if (!double.IsNaN(res.PatternRmsMHz) && res.PatternRmsMHz > o.MaxPatternRmsMHz)
                res.Failures.Add(string.Format(Inv, "선 위치가 이론 패턴과 {0:F1} MHz(rms) 어긋납니다 (허용 {1:F0} MHz).", res.PatternRmsMHz, o.MaxPatternRmsMHz));
            if (res.Bipolarity < o.MinBipolarity)
                res.Failures.Add(string.Format(Inv, "흡수형 신호가 우세합니다 (0 V 기준 양/음 대칭도 {0:F2} < {1:F2}). 편광 분광 λ/2·PBS 균형을 맞춘 뒤 다시 측정해 주세요.",
                    res.Bipolarity, o.MinBipolarity));
            else if (res.Bipolarity < 0.5)
                res.Warnings.Add(string.Format(Inv, "분산형 신호이지만 대칭도가 낮습니다 ({0:F2}). 오프셋이 커서 락 지점이 공명에서 벗어날 수 있습니다.", res.Bipolarity));
            if (preset.Intervals.Count < 2)
                res.Warnings.Add("이론 간격이 하나뿐이라 간격끼리 비교하는 확인은 하지 않았습니다. 선 배정을 그래프에서 확인해 주세요.");
            if (!double.IsNaN(res.ZeroCrossingOffsetMHz) && Math.Abs(res.ZeroCrossingOffsetMHz) > 2 * gf[refIdx] * ak)
                res.Warnings.Add(string.Format(Inv, "{0}의 0 V 교차점이 공명에서 {1:+0.0;-0.0} MHz 떨어져 있습니다.", preset.Lines[refIdx].Name, res.ZeroCrossingOffsetMHz));
            res.Success = res.Failures.Count == 0;

            res.Details.Add(string.Format(Inv, "주파수 방향: 전압 증가 → 주파수 {0}", kAvg > 0 ? "증가 (+)" : "감소 (−)"));
            foreach (IntervalResult ir in res.Intervals)
                res.Details.Add(string.Format(Inv, "{0}: {1:F2} MHz / {2:F4} V = {3:F1} MHz/V{4}", ir.Name, Math.Abs(ir.TheoryMHz), Math.Abs(ir.MeasuredV), Math.Abs(ir.MHzPerV),
                    double.IsNaN(ir.ErrMHzPerV) ? "" : string.Format(Inv, " (±{0:F1})", ir.ErrMHzPerV)));
            if (!double.IsNaN(res.Consistency)) res.Details.Add(string.Format(Inv, "간격별 계수 차이: {0:F2}% (허용 {1:F1}%)", res.Consistency * 100, o.ConsistencyTolerance * 100));
            if (!double.IsNaN(res.PatternRmsMHz)) res.Details.Add(string.Format(Inv, "전체 선 위치 잔차: {0:F1} MHz rms", res.PatternRmsMHz));
            res.Details.Add(string.Format(Inv, "분산형 판정 (0 V 기준 양/음 대칭도): {0:F2} (기준 ≥ {1:F2})", res.Bipolarity, o.MinBipolarity));
            foreach (FittedLine l in res.Lines)
                res.Details.Add(string.Format(Inv, "  {0,-7} {1,9:F4} V  FWHM {2,5:F1} MHz  분산형 비율 {3:F2}", l.Name, l.CenterV, 2 * l.HwhmV * ak, l.DispersiveFraction));
            return res;
        }

        private static double[] PatternCenters(double[] p, double[] nu) {
            double[] c = new double[nu.Length];
            for (int i = 0; i < nu.Length; i++) c[i] = p[0] + nu[i] / p[1];
            return c;
        }

        private static void Keep(List<(double rss, double x0, double k)> best, (double rss, double x0, double k) c, double g) {
            // 같은 해(비슷한 k, x0)는 하나만
            for (int i = 0; i < best.Count; i++) {
                var b = best[i];
                if (Math.Sign(b.k) == Math.Sign(c.k) && Math.Abs(b.k / c.k - 1) < 0.08 && Math.Abs(b.x0 - c.x0) < 3 * g) {
                    if (c.rss < b.rss) best[i] = c;
                    return;
                }
            }
            best.Add(c);
            if (best.Count > 24) {
                best.Sort((a, b) => a.rss.CompareTo(b.rss));
                best.RemoveAt(best.Count - 1);
            }
        }

        private static double Sq(double v) => v * v;

        /// <summary>0 V 기준 양/음 신호 크기 대칭도: min(P, N) / max(P, N). P, N = 99.5 % 분위수.</summary>
        public static double Bipolarity(double[] y) {
            double[] s = (double[])y.Clone();
            Array.Sort(s);
            double hi = s[(int)Math.Floor(0.995 * (s.Length - 1))], lo = s[(int)Math.Ceiling(0.005 * (s.Length - 1))];
            double p = Math.Max(hi, 0), n = Math.Max(-lo, 0);
            double m = Math.Max(p, n);
            return m > 0 ? Math.Min(p, n) / m : 0;
        }

        private static double ZeroCrossing(double[] x, double[] y, double c, double window, double k) {
            double best = double.NaN;
            double[] ys = Smooth(y, 5);
            for (int i = 1; i < x.Length; i++) {
                if (Math.Sign(ys[i]) == Math.Sign(ys[i - 1]) || ys[i] == 0) continue;
                double xz = x[i - 1] + (x[i] - x[i - 1]) * ys[i - 1] / (ys[i - 1] - ys[i]);
                if (Math.Abs(xz - c) > Math.Max(window, 0.05 * (x[^1] - x[0]))) continue;
                if (double.IsNaN(best) || Math.Abs(xz - c) < Math.Abs(best)) best = xz - c;
            }
            return double.IsNaN(best) ? double.NaN : best * k;
        }

        private static double[] Smooth(double[] y, int w) {
            double[] r = new double[y.Length];
            for (int i = 0; i < y.Length; i++) {
                int a = Math.Max(0, i - w / 2), b = Math.Min(y.Length - 1, i + w / 2);
                double s = 0;
                for (int j = a; j <= b; j++) s += y[j];
                r[i] = s / (b - a + 1);
            }
            return r;
        }

        private static (double[], double[]) Downsample(double[] x, double[] y, int n) {
            if (x.Length <= n) return (x, y);
            double[] xs = new double[n], ys = new double[n];
            for (int i = 0; i < n; i++) {
                int a = (int)((long)i * x.Length / n), b = (int)((long)(i + 1) * x.Length / n);
                double sx = 0, sy = 0;
                for (int j = a; j < b; j++) { sx += x[j]; sy += y[j]; }
                xs[i] = sx / (b - a);
                ys[i] = sy / (b - a);
            }
            return (xs, ys);
        }

        // ------------------------------------------------------------------
        // 모델: 다항식 배경 + Σ (A·L + D·Dsp), 진폭/배경은 선형 최소제곱으로 제거 (variable projection)
        // ------------------------------------------------------------------

        private sealed class Model {
            private readonly double[] _x, _y, _t;
            private readonly double[][] _poly;
            public int N => _x.Length;
            public int M(int lines) => PolyDegree + 1 + 2 * lines;

            public Model(double[] x, double[] y, double xmin, double xmax) {
                _x = x;
                _y = y;
                _t = new double[x.Length];
                double mid = (xmin + xmax) / 2, half = Math.Max(1e-12, (xmax - xmin) / 2);
                for (int i = 0; i < x.Length; i++) _t[i] = (x[i] - mid) / half;
                _poly = new double[PolyDegree + 1][];
                for (int p = 0; p <= PolyDegree; p++) {
                    double[] col = new double[x.Length];
                    for (int i = 0; i < x.Length; i++) col[i] = Math.Pow(_t[i], p);
                    _poly[p] = col;
                }
            }

            public double Noise() {
                double s = 0;
                int n = 0;
                for (int i = 1; i + 1 < _y.Length; i++) {
                    double d = _y[i] - (_y[i - 1] + _y[i + 1]) / 2;
                    s += d * d;
                    n++;
                }
                return n > 0 ? Math.Sqrt(s / n / 1.5) : 0;
            }

            private double[][] Basis(double[] cs, double[] gs) {
                int m = M(cs.Length);
                double[][] B = new double[m][];
                for (int p = 0; p <= PolyDegree; p++) B[p] = _poly[p];
                for (int l = 0; l < cs.Length; l++) {
                    double[] la = new double[N], ld = new double[N];
                    double c = cs[l], g = gs[l];
                    for (int i = 0; i < N; i++) {
                        double u = (_x[i] - c) / g, den = 1 / (1 + u * u);
                        la[i] = den;
                        ld[i] = u * den;
                    }
                    B[PolyDegree + 1 + 2 * l] = la;
                    B[PolyDegree + 2 + 2 * l] = ld;
                }
                return B;
            }

            public double[] Amplitudes(double[] cs, double[] gs) => Solve(Basis(cs, gs));

            private double[] Solve(double[][] B) {
                int m = B.Length;
                double[] norm = new double[m];
                for (int j = 0; j < m; j++) {
                    double s = 0;
                    double[] c = B[j];
                    for (int i = 0; i < N; i++) s += c[i] * c[i];
                    norm[j] = s > 0 ? 1 / Math.Sqrt(s) : 0;
                }
                double[,] A = new double[m, m];
                double[] b = new double[m];
                for (int j = 0; j < m; j++) {
                    double[] cj = B[j];
                    double sb = 0;
                    for (int i = 0; i < N; i++) sb += cj[i] * _y[i];
                    b[j] = sb * norm[j];
                    for (int k = j; k < m; k++) {
                        double[] ck = B[k];
                        double s = 0;
                        for (int i = 0; i < N; i++) s += cj[i] * ck[i];
                        A[j, k] = A[k, j] = s * norm[j] * norm[k];
                    }
                    A[j, j] += 1e-10;
                }
                double[] sol = Linear.SolveSpd(A, b);
                for (int j = 0; j < m; j++) sol[j] *= norm[j];
                return sol;
            }

            public double[] Residual(double[] cs, double[] gs) {
                double[][] B = Basis(cs, gs);
                double[] coef = Solve(B);
                double[] r = new double[N];
                for (int i = 0; i < N; i++) {
                    double s = -_y[i];
                    for (int j = 0; j < B.Length; j++) s += coef[j] * B[j][i];
                    r[i] = s;
                }
                return r;
            }

            public double Rss(double[] cs, double[] gs) {
                double[] r = Residual(cs, gs);
                double s = 0;
                for (int i = 0; i < r.Length; i++) s += r[i] * r[i];
                return s;
            }
        }
    }

    /// <summary>작은 선형 시스템 풀이.</summary>
    internal static class Linear {
        /// <summary>대칭 양의 정부호 행렬 (Cholesky, 실패 시 가우스 소거).</summary>
        public static double[] SolveSpd(double[,] A, double[] b) {
            int n = b.Length;
            double[,] L = new double[n, n];
            for (int i = 0; i < n; i++) {
                for (int j = 0; j <= i; j++) {
                    double s = A[i, j];
                    for (int k = 0; k < j; k++) s -= L[i, k] * L[j, k];
                    if (i == j) {
                        if (s <= 0) return Gauss(A, b);
                        L[i, i] = Math.Sqrt(s);
                    }
                    else L[i, j] = s / L[j, j];
                }
            }
            double[] z = new double[n];
            for (int i = 0; i < n; i++) {
                double s = b[i];
                for (int k = 0; k < i; k++) s -= L[i, k] * z[k];
                z[i] = s / L[i, i];
            }
            double[] x = new double[n];
            for (int i = n - 1; i >= 0; i--) {
                double s = z[i];
                for (int k = i + 1; k < n; k++) s -= L[k, i] * x[k];
                x[i] = s / L[i, i];
            }
            return x;
        }

        public static double[] Gauss(double[,] A0, double[] b0) {
            int n = b0.Length;
            double[,] A = (double[,])A0.Clone();
            double[] b = (double[])b0.Clone();
            for (int c = 0; c < n; c++) {
                int p = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[p, c])) p = r;
                if (Math.Abs(A[p, c]) < 1e-300) continue;
                if (p != c) {
                    for (int k = 0; k < n; k++) (A[c, k], A[p, k]) = (A[p, k], A[c, k]);
                    (b[c], b[p]) = (b[p], b[c]);
                }
                for (int r = c + 1; r < n; r++) {
                    double f = A[r, c] / A[c, c];
                    if (f == 0) continue;
                    for (int k = c; k < n; k++) A[r, k] -= f * A[c, k];
                    b[r] -= f * b[c];
                }
            }
            double[] x = new double[n];
            for (int i = n - 1; i >= 0; i--) {
                double s = b[i];
                for (int k = i + 1; k < n; k++) s -= A[i, k] * x[k];
                x[i] = Math.Abs(A[i, i]) > 1e-300 ? s / A[i, i] : 0;
            }
            return x;
        }

        public static double[,] Inverse(double[,] A) {
            int n = A.GetLength(0);
            double[,] inv = new double[n, n];
            for (int j = 0; j < n; j++) {
                double[] e = new double[n];
                e[j] = 1;
                double[] col = Gauss(A, e);
                for (int i = 0; i < n; i++) inv[i, j] = col[i];
            }
            return inv;
        }
    }

    /// <summary>Levenberg–Marquardt (수치 야코비안).</summary>
    internal static class Lm {
        public static double[] Minimize(Func<double[], double[]> f, double[] p0, int maxIter, out double cost) {
            double[] p = (double[])p0.Clone();
            double[] r = f(p);
            cost = Dot(r, r);
            double lambda = 1e-3;
            for (int it = 0; it < maxIter; it++) {
                double[][] J = Jacobian(f, p, r);
                int n = p.Length;
                double[,] A = new double[n, n];
                double[] g = new double[n];
                for (int a = 0; a < n; a++) {
                    g[a] = Dot(J[a], r);
                    for (int b = a; b < n; b++) A[a, b] = A[b, a] = Dot(J[a], J[b]);
                }
                bool improved = false;
                for (int tries = 0; tries < 10; tries++) {
                    double[,] Ad = (double[,])A.Clone();
                    for (int a = 0; a < n; a++) Ad[a, a] += lambda * Math.Max(A[a, a], 1e-30);
                    double[] ng = new double[n];
                    for (int a = 0; a < n; a++) ng[a] = -g[a];
                    double[] d = Linear.Gauss(Ad, ng);
                    double[] pn = new double[n];
                    for (int a = 0; a < n; a++) pn[a] = p[a] + d[a];
                    double[] rn = f(pn);
                    double cn = Dot(rn, rn);
                    if (double.IsFinite(cn) && cn < cost) {
                        double rel = (cost - cn) / Math.Max(cost, 1e-300);
                        p = pn; r = rn; cost = cn;
                        lambda = Math.Max(lambda / 3, 1e-9);
                        improved = true;
                        if (rel < 1e-10) return p;
                        break;
                    }
                    lambda *= 4;
                }
                if (!improved) break;
            }
            return p;
        }

        /// <summary>매개변수 표준오차 (잔차 분산 × (JᵀJ)⁻¹).</summary>
        public static double[] ParameterErrors(Func<double[], double[]> f, double[] p, double cost, int dof) {
            double[] r = f(p);
            double[][] J = Jacobian(f, p, r);
            int n = p.Length;
            double[,] A = new double[n, n];
            for (int a = 0; a < n; a++) for (int b = a; b < n; b++) A[a, b] = A[b, a] = Dot(J[a], J[b]);
            double[,] inv = Linear.Inverse(A);
            double s2 = cost / Math.Max(1, dof);
            double[] e = new double[n];
            for (int a = 0; a < n; a++) e[a] = Math.Sqrt(Math.Max(0, inv[a, a] * s2));
            return e;
        }

        private static double[][] Jacobian(Func<double[], double[]> f, double[] p, double[] r) {
            double[][] J = new double[p.Length][];
            for (int a = 0; a < p.Length; a++) {
                double h = 1e-6 * Math.Max(Math.Abs(p[a]), 1e-2);
                double[] q = (double[])p.Clone();
                q[a] += h;
                double[] rq = f(q);
                double[] col = new double[r.Length];
                for (int i = 0; i < r.Length; i++) col[i] = (rq[i] - r[i]) / h;
                J[a] = col;
            }
            return J;
        }

        private static double Dot(double[] a, double[] b) {
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
            return s;
        }
    }
}
