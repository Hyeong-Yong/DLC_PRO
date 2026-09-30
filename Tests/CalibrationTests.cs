using DLC_PRO.Core;
using DLC_PRO.Core.Calibration;
using DLC_PRO.Models;

// 튜닝 계수 교정 엔진 검증 (UI 없음). 측정 데이터: Tests/Data/PS_D2_line2.csv, PS_D2_line3.csv (Cs D2 편광 분광, 2026-09-30)
internal static class CalibrationTests
{
    private static int _count;
    private static void Check(bool value, string message) {
        if (!value) throw new Exception("FAIL CAL " + message);
        _count++; Console.WriteLine("PASS CAL " + message);
    }

    public static string DataFile(string name) {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }) {
            for (DirectoryInfo d = new DirectoryInfo(start); d != null; d = d.Parent) {
                string p = Path.Combine(d.FullName, "Tests", "Data", name);
                if (File.Exists(p)) return p;
                p = Path.Combine(d.FullName, "Data", name);
                if (File.Exists(p)) return p;
            }
        }
        throw new FileNotFoundException("테스트 데이터 없음: " + name);
    }

    public static void Run() {
        // 측정 데이터
        var l2 = SpectrumCsv.Load(DataFile("PS_D2_line2.csv"));
        Check(l2.X.Length == 1010 && l2.XName == "Piezo Voltage (V)" && l2.YName == "Fine In 1 (V)", "DLC pro CSV format parsed (semicolon, quoted header)");
        var r2 = TuningCalibrator.Analyze(l2.X, l2.Y, CalibrationPresets.CsD2);
        Check(!r2.Success && r2.Consistency > 0.03 && r2.Bipolarity < 0.15, $"line2 FAIL (interval mismatch {r2.Consistency:P1}, absorptive dominant {r2.Bipolarity:F2})");
        var l3 = SpectrumCsv.Load(DataFile("PS_D2_line3.csv"));
        var r3 = TuningCalibrator.Analyze(l3.X, l3.Y, CalibrationPresets.CsD2);
        Check(r3.Success && Math.Abs(r3.MHzPerV - 329.6) < 1.0, $"line3 SUCCESS {r3.MHzPerV:F1} MHz/V");
        Check(r3.Intervals.Count == 2 && Math.Abs(r3.Intervals[0].MHzPerV - 328.2) < 1 && Math.Abs(r3.Intervals[1].MHzPerV - 331.0) < 1, "line3 F'5-F'4 and F'5-F'3 estimates");
        Check(Math.Abs(r3.Lines[5].CenterV - 25.7815) < 0.003 && Math.Abs(r3.Lines[2].CenterV - 25.0165) < 0.003, "line3 F'=5 / F'=4 centers");
        var rs = TuningCalibrator.Analyze(l3.X, l3.Y, CalibrationPresets.CsD2, new CalibrationOptions { ExpectedMHzPerV = 600 });
        Check(rs.Success && Math.Abs(rs.MHzPerV - r3.MHzPerV) < 0.5, "assignment independent of starting coefficient");
        var rr = TuningCalibrator.Analyze(l3.X.Select(v => 60 - v).Reverse().ToArray(), l3.Y.Reverse().ToArray(), CalibrationPresets.CsD2);
        Check(rr.Success && Math.Abs(rr.MHzPerV + r3.MHzPerV) < 0.5, "reversed scan gives negative coefficient");

        // 6P3/2 → 7S1/2 (합성 데이터, 310 MHz/V)
        double k = 310, g = 12 / k, c4 = 17.5, c3 = c4 - 2183.48 / k;
        var rnd = new Random(3);
        double Disp(double x, double c) { double u = (x - c) / g; return u / (1 + u * u); }
        double[] x7 = Enumerable.Range(0, 2000).Select(i => 9.0 + 10.0 * i / 1999).ToArray();
        double[] y7 = x7.Select(x => 0.3 * Disp(x, c4) + 0.2 * Disp(x, c3) + 0.05 * Disp(x, c4 - 145 / k) + (rnd.NextDouble() - 0.5) * 0.004).ToArray();
        var r7 = TuningCalibrator.Analyze(x7, y7, CalibrationPresets.Cs7S);
        Check(r7.Success && Math.Abs(r7.MHzPerV - 310) < 1, $"7S1/2 F''=4–F''=3 preset {r7.MHzPerV:F1} MHz/V");
        var r7n = TuningCalibrator.Analyze(x7, y7.Select(v => v - 0.5).ToArray(), CalibrationPresets.Cs7S);
        Check(!r7n.Success && r7n.Failures.Any(f => f.Contains("흡수형")), "signal with large offset (no zero crossing) rejected");

        // CSV: 그래프 CSV(쉼표) 형식, MHz 축 거부
        var lines = new List<string> { "# x: Piezo Voltage [V] / y: Fine In 1 [V]", "Fine In 1_x,Fine In 1_y" };
        for (int i = 0; i < 50; i++) lines.Add((20 + i * 0.01).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",0.1");
        Check(SpectrumCsv.Parse(lines, "plot.csv").X.Length == 50, "plot CSV (comma) format parsed");
        lines[0] = "# x: Relative Frequency [MHz]   (0 MHz = 25.000 V) / y: Fine In 1 [V]";
        bool rejected = false;
        try { SpectrumCsv.Parse(lines, "mhz.csv"); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "CSV with MHz x-axis rejected");
        string tmp = Path.Combine(Path.GetTempPath(), "cal-save-" + Guid.NewGuid().ToString("N") + ".csv");
        SpectrumCsv.Save(tmp, "Piezo Voltage (V)", l3.X.Select(v => (float)v).ToArray(), new List<(string, IReadOnlyList<float>)> { ("Fine In 1 (V)", l3.Y.Select(v => (float)v).ToArray()) });
        var back = SpectrumCsv.Load(tmp);
        Check(back.X.Length == l3.X.Length && back.YName == "Fine In 1 (V)" && TuningCalibrator.Analyze(back.X, back.Y, CalibrationPresets.CsD2).Success, "scan CSV save → load → calibrate round trip");
        File.Delete(tmp);

        // 기본 튜닝 계수 328 MHz/V, 0(초기화) → 328, 교정 정보 저장
        string old = AppSettings.FolderOverride;
        AppSettings.FolderOverride = Path.Combine(Path.GetTempPath(), "DLC_PRO-cal-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(AppSettings.FolderOverride);
            Check(Math.Abs(AppSettings.Load(1).Freq.CoefGHzPerV - 0.328) < 1e-12 && Math.Abs(AppSettings.Load(2).Freq.CoefGHzPerV - 0.328) < 1e-12, "fresh settings default to 328 MHz/V for both lasers");
            File.WriteAllText(Path.Combine(AppSettings.FolderOverride, "settings.ini"), "Freq.CoefGHzPerV=0\n");
            Check(Math.Abs(AppSettings.Load(1).Freq.CoefGHzPerV - 0.328) < 1e-12, "zero (reset) coefficient restored to 328 MHz/V");
            var s2 = AppSettings.Load(2);
            s2.Freq.CoefGHzPerV = 0.30973; s2.Freq.CalibrationPreset = "cs-6p-7s"; s2.Freq.CalibrationSource = "scan.csv"; s2.Freq.CalibratedAt = "2026-10-01 10:00:00"; s2.Freq.CalibrationMHzPerV = 309.73;
            s2.Save();
            var s2b = AppSettings.Load(2);
            Check(Math.Abs(s2b.Freq.CoefGHzPerV - 0.30973) < 1e-12 && s2b.Freq.CalibrationPreset == "cs-6p-7s" && Math.Abs(s2b.Freq.CalibrationMHzPerV - 309.73) < 1e-9, "laser2 calibration persisted separately");
            Check(Math.Abs(AppSettings.Load(1).Freq.CoefGHzPerV - 0.328) < 1e-12, "laser1 coefficient unaffected");
            // LongTerm 기본 단위
            Check(WavemeterSettings.Load().LtUnit == 0, "LongTerm default unit = Wavelength, vac.");
            File.WriteAllText(WavemeterSettings.FilePath, "LongTerm.Unit=-1\n");
            Check(WavemeterSettings.Load().LtUnit == 0, "old 'follow WLM' default migrated to Wavelength, vac.");
            File.WriteAllText(WavemeterSettings.FilePath, "Version=2\nLongTerm.Unit=-1\n");
            Check(WavemeterSettings.Load().LtUnit == -1, "explicit 'follow WLM' choice kept");
        }
        finally { AppSettings.FolderOverride = old; }
        Console.WriteLine($"CAL PASS: {_count} assertions");
    }
}
