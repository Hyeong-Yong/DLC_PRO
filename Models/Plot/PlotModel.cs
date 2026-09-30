using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DLC_PRO.Models.Plot {
    public enum PlotDash { Solid, Dash, Dot }

    public enum PlotMarkerShape { TriangleUp, TriangleDown, Diamond, Circle, Cross }

    /// <summary>그래프에 그릴 한 개의 곡선 (UI 라이브러리와 무관한 순수 데이터).</summary>
    public sealed class PlotSeries {
        public string Name = "";
        public float[]? X;
        public float[]? Y;
        /// <summary>
        /// 배정밀도 데이터 (설정하면 X/Y 대신 사용). 파장계처럼 유효숫자가 float(7자리)보다 많은 값용.
        /// </summary>
        public double[]? XD, YD;

        /// <summary>그릴 점 수.</summary>
        public int Count => XD != null && YD != null ? Math.Min(XD.Length, YD.Length)
            : X != null && Y != null ? Math.Min(X.Length, Y.Length) : 0;
        /// <summary>0xAARRGGBB</summary>
        public uint Color = PlotColors.Trace1;
        public float Width = 1.3f;
        /// <summary>선 대신 점으로 표시.</summary>
        public bool Points;
        public bool Visible = true;
        /// <summary>오른쪽 Y축 사용.</summary>
        public bool UseY2;
        public PlotDash Dash = PlotDash.Solid;
    }

    public struct PlotMarker {
        public double X, Y;
        public uint Color;
        public PlotMarkerShape Shape;
        public float Size;
        public string? Label;
    }

    public struct PlotLine {
        public double Value;
        public bool Vertical;
        public uint Color;
        public string? Label;
        public PlotDash Dash;
    }

    /// <summary>그래프 클릭 위치 (데이터 좌표).</summary>
    public readonly record struct PlotPoint(double X, double Y);

    /// <summary>다크 테마용 기본 색.</summary>
    public static class PlotColors {
        public const uint Trace1 = 0xFF22D3EE;      // cyan
        public const uint Trace2 = 0xFFF59E0B;      // amber
        public const uint Trace3 = 0xFFE879F9;      // fuchsia
        public const uint Background = 0xFF7B82A8;  // 회색 (락 직전 스펙트럼)
        public const uint Candidate = 0xFFFF5A5A;
        public const uint LockPoint = 0xFF22C55E;
        public const uint LockLine = 0x7822C55E;
        public const uint Tracking = 0xFFFFFFFF;
        public const uint Setpoint = 0xA0FFA000;
        public const uint SetpointTrace = 0xA0FFFFFF;
    }

    /// <summary>
    /// ViewModel이 채우고 View(PlotView)가 그리는 그래프 모델.
    /// ViewModel은 데이터만 바꾸고 <see cref="Invalidate"/>를 호출한다 → PlotView가 ScottPlot으로 다시 그림.
    /// </summary>
    public sealed class PlotModel {
        public readonly List<PlotSeries> Series = new List<PlotSeries>();
        public readonly List<PlotMarker> Markers = new List<PlotMarker>();
        public readonly List<PlotLine> Lines = new List<PlotLine>();

        public string Title = "";
        public string XLabel = "";
        public string YLabel = "";
        public string Y2Label = "";
        /// <summary>그래프 왼쪽 위 안내 문구 (여러 줄 가능).</summary>
        public string Overlay = "";
        public double MinYSpan, MinY2Span;

        /// <summary>
        /// 자동 스케일일 때 쓸 고정 범위 (null이면 데이터에 맞춤). 예: LongTerm 창의 페이지 단위 가로축, Vert. axis Fixed.
        /// </summary>
        public double? XMin, XMax, YMin, YMax;

        /// <summary>자동 스케일 여부 (사용자가 드래그/휠로 확대하면 View가 false로 바꾼다).</summary>
        public bool AutoScaleX = true, AutoScaleY = true;

        /// <summary>다시 그려야 할 때 (UI 스레드).</summary>
        public event Action? Updated;

        public PlotSeries AddSeries(string name, uint color, bool useY2 = false, float width = 1.3f, PlotDash dash = PlotDash.Solid) {
            PlotSeries s = new PlotSeries { Name = name, Color = color, UseY2 = useY2, Width = width, Dash = dash };
            Series.Add(s);
            return s;
        }

        public void Invalidate() => Updated?.Invoke();

        public void ResetScale() {
            AutoScaleX = AutoScaleY = true;
            Invalidate();
        }

        public bool HasData {
            get {
                foreach (PlotSeries s in Series)
                    if (s.Visible && s.Count > 0) return true;
                return false;
            }
        }

        /// <summary>표시 중인 곡선을 CSV로 저장 (series_x, series_y 열 쌍).</summary>
        public void SaveCsv(string path) {
            CultureInfo inv = CultureInfo.InvariantCulture;
            List<PlotSeries> list = new List<PlotSeries>();
            foreach (PlotSeries s in Series) if (s.Visible && s.Count > 0) list.Add(s);
            int max = 0;
            foreach (PlotSeries s in list) max = Math.Max(max, s.Count);
            using StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(true));
            w.WriteLine("# x: " + XLabel + " / y: " + YLabel + (string.IsNullOrEmpty(Y2Label) ? "" : " / y2: " + Y2Label));
            StringBuilder h = new StringBuilder();
            foreach (PlotSeries s in list) {
                string n = string.IsNullOrEmpty(s.Name) ? "series" : s.Name;
                h.Append(n).Append("_x,").Append(n).Append("_y,");
            }
            w.WriteLine(h.ToString().TrimEnd(','));
            for (int i = 0; i < max; i++) {
                StringBuilder r = new StringBuilder();
                foreach (PlotSeries s in list) {
                    int n = s.Count;
                    if (i >= n) r.Append(",,");
                    else if (s.XD != null && s.YD != null) r.Append(s.XD[i].ToString("R", inv)).Append(',').Append(s.YD[i].ToString("R", inv)).Append(',');
                    else r.Append(s.X![i].ToString("R", inv)).Append(',').Append(s.Y![i].ToString("R", inv)).Append(',');
                }
                w.WriteLine(r.ToString().TrimEnd(','));
            }
        }
    }
}
