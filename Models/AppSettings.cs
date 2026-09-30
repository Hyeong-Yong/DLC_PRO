using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DLC_PRO.Core;

namespace DLC_PRO.Models {
    /// <summary>
    /// 사용자 설정 (%AppData%\DLC_PRO\settings.ini).
    /// 처음 실행 시 기존 WinForms 버전(DlcProControl)의 설정 파일이 있으면 그것을 읽어온다.
    /// </summary>
    public sealed class AppSettings {
        public int LaserId { get; private set; } = 1;
        public string Host = "192.168.0.100";
        public int CmdPort = 1998;
        public int MonPort = 1999;
        public string ConnectionType = "TCP";
        public string ComPort = "COM3";
        public bool LogTraffic = false;
        public double ScopeMaxRate = 15;
        public bool SideMenuExpanded = true;
        public AmpSafetySettings Safety = new AmpSafetySettings();
        public FrequencyAxisSettings Freq = new FrequencyAxisSettings();

        internal static string? FolderOverride;
        public static string Folder => FolderOverride ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DLC_PRO");

        public static string LogFolder => Path.Combine(Folder, "logs");

        private static string PathFor(int id) => Path.Combine(Folder, id == 1 ? "settings.ini" : "settings-laser" + id + ".ini");
        private string FilePath => PathFor(LaserId);

        /// <summary>이전 WinForms 버전 설정 파일 (마이그레이션용).</summary>
        private static string LegacyFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DlcProControl", "settings.ini");

        public static AppSettings Load(int laserId = 1) {
            AppSettings s = new AppSettings { LaserId = laserId };
            try {
                string path = File.Exists(s.FilePath) || laserId != 1 ? s.FilePath : LegacyFilePath;
                if (!File.Exists(path)) return s;
                Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8)) {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                    kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                s.Host = Str(kv, "Host", s.Host);
                s.CmdPort = (int)Num(kv, "CmdPort", s.CmdPort);
                s.MonPort = (int)Num(kv, "MonPort", s.MonPort);
                s.ConnectionType = Str(kv, "ConnectionType", s.ConnectionType);
                s.ComPort = Str(kv, "ComPort", s.ComPort);
                s.LogTraffic = Str(kv, "LogTraffic", "false") == "true";
                s.ScopeMaxRate = Num(kv, "ScopeMaxRate", s.ScopeMaxRate);
                s.SideMenuExpanded = Str(kv, "SideMenuExpanded", "true") == "true";
                s.Safety.MinSeedPowerMw = Num(kv, "Safety.MinSeedPowerMw", s.Safety.MinSeedPowerMw);
                s.Safety.MaxAmpCurrentMa = Num(kv, "Safety.MaxAmpCurrentMa", s.Safety.MaxAmpCurrentMa);
                s.Safety.RampStepMa = Num(kv, "Safety.RampStepMa", s.Safety.RampStepMa);
                s.Safety.RampIntervalMs = (int)Num(kv, "Safety.RampIntervalMs", s.Safety.RampIntervalMs);
                s.Safety.ConfirmDeltaMa = Num(kv, "Safety.ConfirmDeltaMa", s.Safety.ConfirmDeltaMa);
                s.Safety.WatchdogEnabled = Str(kv, "Safety.WatchdogEnabled", "true") == "true";
                s.Safety.WatchdogDelayMs = (int)Num(kv, "Safety.WatchdogDelayMs", s.Safety.WatchdogDelayMs);
                s.Freq.ShowFrequency = Str(kv, "Freq.ShowFrequency", "false") == "true";
                s.Freq.CoefGHzPerV = Num(kv, "Freq.CoefGHzPerV", s.Freq.CoefGHzPerV);
                s.Freq.UseCustomZero = Str(kv, "Freq.UseCustomZero", "false") == "true";
                s.Freq.ZeroVoltage = Num(kv, "Freq.ZeroVoltage", s.Freq.ZeroVoltage);
                s.Freq.CalibrationPreset = Str(kv, "Freq.Calibration.Preset", "");
                s.Freq.CalibratedAt = Str(kv, "Freq.Calibration.Time", "");
                s.Freq.CalibrationSource = Str(kv, "Freq.Calibration.Source", "");
                s.Freq.CalibrationMHzPerV = Num(kv, "Freq.Calibration.MHzPerV", double.NaN);
            }
            catch {
                // 설정 파일이 손상되어도 기본값으로 시작
            }
            s.Safety.Sanitize();
            // 계수 0(미설정·초기화)이면 기본 328 MHz/V
            if (!s.Freq.HasCoefficient || Math.Abs(s.Freq.CoefGHzPerV) > 1000) s.Freq.CoefGHzPerV = FrequencyAxisSettings.DefaultCoefGHzPerV;
            if (s.CmdPort <= 0 || s.CmdPort > 65535) s.CmdPort = 1998;
            if (s.MonPort <= 0 || s.MonPort > 65535) s.MonPort = 1999;
            if (s.ScopeMaxRate < 0.5 || s.ScopeMaxRate > 30) s.ScopeMaxRate = 15;
            return s;
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Folder);
                CultureInfo inv = CultureInfo.InvariantCulture;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# DLC_PRO (Avalonia) settings");
                sb.AppendLine("Host=" + Host);
                sb.AppendLine("CmdPort=" + CmdPort.ToString(inv));
                sb.AppendLine("MonPort=" + MonPort.ToString(inv));
                sb.AppendLine("ConnectionType=" + ConnectionType);
                sb.AppendLine("ComPort=" + ComPort);
                sb.AppendLine("LogTraffic=" + B(LogTraffic));
                sb.AppendLine("ScopeMaxRate=" + ScopeMaxRate.ToString(inv));
                sb.AppendLine("SideMenuExpanded=" + B(SideMenuExpanded));
                sb.AppendLine("Safety.MinSeedPowerMw=" + Safety.MinSeedPowerMw.ToString(inv));
                sb.AppendLine("Safety.MaxAmpCurrentMa=" + Safety.MaxAmpCurrentMa.ToString(inv));
                sb.AppendLine("Safety.RampStepMa=" + Safety.RampStepMa.ToString(inv));
                sb.AppendLine("Safety.RampIntervalMs=" + Safety.RampIntervalMs.ToString(inv));
                sb.AppendLine("Safety.ConfirmDeltaMa=" + Safety.ConfirmDeltaMa.ToString(inv));
                sb.AppendLine("Safety.WatchdogEnabled=" + B(Safety.WatchdogEnabled));
                sb.AppendLine("Safety.WatchdogDelayMs=" + Safety.WatchdogDelayMs.ToString(inv));
                sb.AppendLine("Freq.ShowFrequency=" + B(Freq.ShowFrequency));
                sb.AppendLine("Freq.CoefGHzPerV=" + Freq.CoefGHzPerV.ToString("R", inv));
                sb.AppendLine("Freq.UseCustomZero=" + B(Freq.UseCustomZero));
                sb.AppendLine("Freq.ZeroVoltage=" + Freq.ZeroVoltage.ToString("R", inv));
                sb.AppendLine("Freq.Calibration.Preset=" + Freq.CalibrationPreset);
                sb.AppendLine("Freq.Calibration.Time=" + Freq.CalibratedAt);
                sb.AppendLine("Freq.Calibration.Source=" + Freq.CalibrationSource.Replace('\n', ' ').Replace('\r', ' '));
                sb.AppendLine("Freq.Calibration.MHzPerV=" + (double.IsNaN(Freq.CalibrationMHzPerV) ? "" : Freq.CalibrationMHzPerV.ToString("R", inv)));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch {
                // 저장 실패는 치명적이지 않음
            }
        }

        private static string B(bool b) => b ? "true" : "false";

        private static string Str(Dictionary<string, string> kv, string k, string d) =>
            kv.TryGetValue(k, out string? v) && v.Length > 0 ? v : d;

        private static double Num(Dictionary<string, string> kv, string k, double d) =>
            kv.TryGetValue(k, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double r)
            && !double.IsNaN(r) && !double.IsInfinity(r) ? r : d;
    }
}
