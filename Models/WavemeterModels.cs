using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DLC_PRO.Models {
    /// <summary>파장계 측정 하나 (진공 파장 nm 또는 오류 값).</summary>
    public readonly record struct WlmSample(DateTime Time, double Seconds, int WlmTimestampMs, double VacuumNm);

    /// <summary>작업 스레드가 주기적으로 읽는 WLM 상태 스냅샷.</summary>
    public sealed record WlmStatus {
        public bool ServerRunning { get; init; }
        public int WlmType { get; init; }
        public int WlmVersion { get; init; }
        public int OperationState { get; init; }
        public int ResultMode { get; init; }
        public int Range { get; init; } = -1;
        public int PulseMode { get; init; }
        public int WideMode { get; init; }
        public bool FastMode { get; init; }
        public int Exposure { get; init; }
        public bool ExposureAuto { get; init; }
        public int ExposureMin { get; init; } = 1;
        public int ExposureMax { get; init; } = 9999;
        public bool IntervalMode { get; init; }
        public int Interval { get; init; }
        public bool AutoCal { get; init; }
        public int AutoCalPeriod { get; init; }
        public int AutoCalUnit { get; init; }
        public int AverageCount { get; init; } = 1;
        public int AverageMode { get; init; } = 1;
        public int AverageType { get; init; }
        public double Temperature { get; init; } = double.NaN;
        public double Pressure { get; init; } = double.NaN;
        public bool Link { get; init; }
        /// <summary>λvac/λair (공기 파장 변환용, WLM ConvertUnit 결과).</summary>
        public double AirRatio { get; init; }
    }

    /// <summary>파장계 화면 설정 (%AppData%\DLC_PRO\wavemeter.ini).</summary>
    public sealed class WavemeterSettings {
        public bool OpenLongTermOnStart = true;
        public bool AutoConnect;
        public bool ShowSignal;
        public string RangeNames = "330 - 1000 nm;1000 - 1750 nm";

        // LongTerm 창
        public int LtUnit = 0;             // -1 = WLM Result unit 따름, 0 = Wavelength, vac. [nm] (기본)
        public bool LtDelta;               // Δf [MHz] 표시
        public bool LtTimeAxis;
        public int LtPerPage = 10000;
        public bool LtFixedX = true;
        public int LtStatCount = 10;
        public bool LtStatSinceReset;
        public bool LtFixedY;
        public double LtYMin, LtYMax;
        public bool LtTopmost;
        public double LtX = double.NaN, LtY = double.NaN, LtWidth = 980, LtHeight = 620;

        public static string FilePath => Path.Combine(AppSettings.Folder, "wavemeter.ini");

        public static WavemeterSettings Load() {
            WavemeterSettings s = new WavemeterSettings();
            try {
                if (!File.Exists(FilePath)) return s;
                Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8)) {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                    kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                s.OpenLongTermOnStart = B(kv, "OpenLongTermOnStart", s.OpenLongTermOnStart);
                s.AutoConnect = B(kv, "AutoConnect", s.AutoConnect);
                s.ShowSignal = B(kv, "ShowSignal", s.ShowSignal);
                if (kv.TryGetValue("RangeNames", out string? rn) && rn.Length > 0) s.RangeNames = rn;
                s.LtUnit = (int)N(kv, "LongTerm.Unit", s.LtUnit);
                // 버전 1 파일의 -1은 예전 기본값(WLM 단위 따름) → 새 기본값 Wavelength, vac.으로
                if (N(kv, "Version", 1) < 2 && s.LtUnit == -1) s.LtUnit = 0;
                s.LtDelta = B(kv, "LongTerm.Delta", s.LtDelta);
                s.LtTimeAxis = B(kv, "LongTerm.TimeAxis", s.LtTimeAxis);
                s.LtPerPage = (int)N(kv, "LongTerm.PerPage", s.LtPerPage);
                s.LtFixedX = B(kv, "LongTerm.FixedX", s.LtFixedX);
                s.LtStatCount = (int)N(kv, "LongTerm.StatCount", s.LtStatCount);
                s.LtStatSinceReset = B(kv, "LongTerm.StatSinceReset", s.LtStatSinceReset);
                s.LtFixedY = B(kv, "LongTerm.FixedY", s.LtFixedY);
                s.LtYMin = N(kv, "LongTerm.YMin", s.LtYMin);
                s.LtYMax = N(kv, "LongTerm.YMax", s.LtYMax);
                s.LtTopmost = B(kv, "LongTerm.Topmost", s.LtTopmost);
                s.LtX = N(kv, "LongTerm.X", s.LtX);
                s.LtY = N(kv, "LongTerm.Y", s.LtY);
                s.LtWidth = N(kv, "LongTerm.Width", s.LtWidth);
                s.LtHeight = N(kv, "LongTerm.Height", s.LtHeight);
            }
            catch {
                // 손상된 설정은 기본값으로
            }
            s.Sanitize();
            return s;
        }

        public void Sanitize() {
            if (LtUnit < -1 || LtUnit > 4) LtUnit = 0;
            if (LtPerPage < 10 || LtPerPage > 1_000_000) LtPerPage = 10000;
            if (LtStatCount < 2 || LtStatCount > 1_000_000) LtStatCount = 10;
            if (!(LtWidth >= 400 && LtWidth <= 10000)) LtWidth = 980;
            if (!(LtHeight >= 300 && LtHeight <= 10000)) LtHeight = 620;
            if (double.IsInfinity(LtYMin) || double.IsNaN(LtYMin)) LtYMin = 0;
            if (double.IsInfinity(LtYMax) || double.IsNaN(LtYMax)) LtYMax = 0;
        }

        public void Save() {
            try {
                Directory.CreateDirectory(AppSettings.Folder);
                CultureInfo inv = CultureInfo.InvariantCulture;
                StringBuilder b = new StringBuilder();
                b.AppendLine("# DLC_PRO 파장계(WLM) 설정");
                b.AppendLine("Version=2");
                b.AppendLine("OpenLongTermOnStart=" + T(OpenLongTermOnStart));
                b.AppendLine("AutoConnect=" + T(AutoConnect));
                b.AppendLine("ShowSignal=" + T(ShowSignal));
                b.AppendLine("RangeNames=" + RangeNames);
                b.AppendLine("LongTerm.Unit=" + LtUnit.ToString(inv));
                b.AppendLine("LongTerm.Delta=" + T(LtDelta));
                b.AppendLine("LongTerm.TimeAxis=" + T(LtTimeAxis));
                b.AppendLine("LongTerm.PerPage=" + LtPerPage.ToString(inv));
                b.AppendLine("LongTerm.FixedX=" + T(LtFixedX));
                b.AppendLine("LongTerm.StatCount=" + LtStatCount.ToString(inv));
                b.AppendLine("LongTerm.StatSinceReset=" + T(LtStatSinceReset));
                b.AppendLine("LongTerm.FixedY=" + T(LtFixedY));
                b.AppendLine("LongTerm.YMin=" + LtYMin.ToString("R", inv));
                b.AppendLine("LongTerm.YMax=" + LtYMax.ToString("R", inv));
                b.AppendLine("LongTerm.Topmost=" + T(LtTopmost));
                b.AppendLine("LongTerm.X=" + LtX.ToString("R", inv));
                b.AppendLine("LongTerm.Y=" + LtY.ToString("R", inv));
                b.AppendLine("LongTerm.Width=" + LtWidth.ToString("R", inv));
                b.AppendLine("LongTerm.Height=" + LtHeight.ToString("R", inv));
                File.WriteAllText(FilePath, b.ToString(), new UTF8Encoding(false));
            }
            catch {
                // 저장 실패는 무시 (다음 실행 시 기본값)
            }
        }

        public string[] RangeNameList => RangeNames.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static string T(bool b) => b ? "true" : "false";
        private static bool B(Dictionary<string, string> kv, string k, bool d) => kv.TryGetValue(k, out string? v) ? v.Equals("true", StringComparison.OrdinalIgnoreCase) : d;
        private static double N(Dictionary<string, string> kv, string k, double d) =>
            kv.TryGetValue(k, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : d;
    }
}
