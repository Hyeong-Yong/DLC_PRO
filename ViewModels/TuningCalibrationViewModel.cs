using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Core.Calibration;
using DLC_PRO.Models.Plot;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Dialogs;

namespace DLC_PRO.ViewModels {
    /// <summary>
    /// 스캔 스펙트럼으로 피에조 튜닝 계수 교정 (Scan &amp; Lock → MHz 축 탭).
    /// 레이저마다 따로 존재하며(레이저별 DeviceService/설정), 성공하면 튜닝 계수 입력란에 넣고 설정 파일에 저장한다.
    /// 흐름: 스캔 CSV 저장 → [CSV 불러와 교정] (또는 [현재 스캔으로 교정]) → 판정 SUCCESS/FAIL
    /// </summary>
    public partial class TuningCalibrationViewModel : ViewModelBase {
        private readonly DeviceService _dev;
        private readonly LogService _log;
        private readonly DialogService _dialogs;
        private readonly FrequencyAxisViewModel _axis;
        private readonly Func<SpectrumData?>? _currentScan;
        private readonly PlotSeries _data;
        private SpectrumData? _loaded;
        private string? _loadedPath;
        private bool _loading;

        public TuningCalibrationViewModel(DeviceService dev, LogService log, DialogService dialogs, FrequencyAxisViewModel axis, Func<SpectrumData?>? currentScan) {
            _dev = dev;
            _log = log;
            _dialogs = dialogs;
            _axis = axis;
            _currentScan = currentScan;
            _loading = true;
            _selectedPreset = CalibrationPresets.ById(dev.Settings.Freq.CalibrationPreset);
            _loading = false;
            Plot = new PlotModel { Title = "", XLabel = "Piezo Voltage [V]", YLabel = "Signal [V]" };
            _data = Plot.AddSeries("", PlotColors.Trace1, false, 1.1f);
            dev.FreqSettingsChanged += () => OnPropertyChanged(nameof(LastCalibrationText));
        }

        public string LaserTitle => "Laser " + _dev.Device.LaserId + " 튜닝 계수 교정";
        public IReadOnlyList<CalibrationPreset> Presets => CalibrationPresets.All;
        public IReadOnlyList<string> Directions { get; } = new[] { "자동 판단", "전압↑ → 주파수↑ (+)", "전압↑ → 주파수↓ (−)" };
        public bool CanUseCurrentScan => _currentScan != null;
        public PlotModel Plot { get; }
        public ObservableCollection<string> Columns { get; } = new ObservableCollection<string>();

        [ObservableProperty] private CalibrationPreset _selectedPreset;
        [ObservableProperty] private int _directionIndex;
        [ObservableProperty] private double? _tolerancePercent = 3;
        [ObservableProperty] private int _selectedColumn = -1;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _hasResult;
        [ObservableProperty] private bool _isSuccess;
        [ObservableProperty] private string _resultTitle = "";
        [ObservableProperty] private string _resultText = "";
        [ObservableProperty] private string _sourceText = "스캔 CSV를 불러오면 선을 찾아 튜닝 계수를 계산합니다.";

        public string PresetDescription => SelectedPreset.Description;
        public bool HasColumns => Columns.Count > 2;

        public string LastCalibrationText {
            get {
                FrequencyAxisSettings s = _dev.Settings.Freq;
                if (string.IsNullOrEmpty(s.CalibratedAt)) return "교정 기록 없음 · 현재 계수 " + (s.CoefGHzPerV * 1000).ToString("F1", CultureInfo.InvariantCulture) + " MHz/V";
                return string.Format(CultureInfo.InvariantCulture, "마지막 교정: {0} · {1} · {2} · {3:F1} MHz/V",
                    s.CalibratedAt, CalibrationPresets.ById(s.CalibrationPreset).Name.Split('·')[0].Trim(), s.CalibrationSource, s.CalibrationMHzPerV);
            }
        }

        partial void OnSelectedPresetChanged(CalibrationPreset value) {
            OnPropertyChanged(nameof(PresetDescription));
            if (_loading) return;
            _dev.Settings.Freq.CalibrationPreset = value.Id;
            _dev.SaveSettings();
        }

        partial void OnSelectedColumnChanged(int value) {
            if (_loading || _loaded == null || value < 1 || value == _loaded.YColumn) return;
            _ = ReloadColumnAsync(value);
        }

        // ------------------------------------------------------------------
        // 명령
        // ------------------------------------------------------------------

        [RelayCommand]
        private async Task LoadCsvAsync() {
            string? path = await _dialogs.OpenFilePickerAsync("교정용 스캔 CSV 열기", "스캔 CSV", "*.csv", "*.txt");
            if (path == null) return;
            await AnalyzeFileAsync(path);
        }

        /// <summary>파일을 읽어 교정 (테스트에서도 사용).</summary>
        public async Task AnalyzeFileAsync(string path, int yColumn = -1) {
            SpectrumData d;
            try { d = await Task.Run(() => SpectrumCsv.Load(path, yColumn)); }
            catch (Exception ex) {
                await ReportFailure("CSV를 읽을 수 없습니다", Path.GetFileName(path) + "\n" + ex.Message);
                return;
            }
            _loadedPath = path;
            await AnalyzeAsync(d);
        }

        [RelayCommand]
        private async Task UseCurrentScanAsync() {
            SpectrumData? d = _currentScan?.Invoke();
            if (d == null) {
                await ReportFailure("현재 스캔을 사용할 수 없습니다",
                    "스코프가 XY 모드이고 X축이 피에조 전압([50] 또는 스캔 출력=피에조인 [101])이어야 합니다.\n스캔을 켜고 스펙트럼이 보이는 상태에서 다시 시도해 주세요.");
                return;
            }
            _loadedPath = null;
            await AnalyzeAsync(d);
        }

        private async Task ReloadColumnAsync(int column) {
            if (_loadedPath == null || !File.Exists(_loadedPath)) return;
            await AnalyzeFileAsync(_loadedPath, column);
        }

        private async Task AnalyzeAsync(SpectrumData d) {
            if (IsBusy) return;
            IsBusy = true;
            _loaded = d;
            _loading = true;
            try {
                Columns.Clear();
                foreach (string c in d.Columns) Columns.Add(c);
                SelectedColumn = d.YColumn;
            }
            finally { _loading = false; }
            OnPropertyChanged(nameof(HasColumns));
            SourceText = string.Format(CultureInfo.InvariantCulture, "{0} · {1} vs {2} · {3}점 · {4:F3}~{5:F3} V",
                Path.GetFileName(d.Source), d.YName, d.XName, d.X.Length, d.X.First(), d.X.Last());
            ShowData(d, null);
            CalibrationPreset preset = SelectedPreset;
            double expected = _dev.Settings.Freq.CoefGHzPerV * 1000;
            CalibrationOptions opt = new CalibrationOptions {
                ExpectedMHzPerV = Math.Abs(expected) > 1 ? expected : FrequencyAxisSettings.DefaultCoefGHzPerV * 1000,
                Direction = DirectionIndex == 1 ? 1 : DirectionIndex == 2 ? -1 : 0,
                ConsistencyTolerance = Math.Clamp(TolerancePercent ?? 3, 0.1, 50) / 100.0,
            };
            CalibrationResult r;
            try { r = await Task.Run(() => TuningCalibrator.Analyze(d.X, d.Y, preset, opt)); }
            catch (Exception ex) {
                IsBusy = false;
                await ReportFailure("교정 계산 오류", ex.Message);
                return;
            }
            IsBusy = false;
            ShowData(d, r);
            ShowResult(r);
            string laser = "Laser " + _dev.Device.LaserId;
            if (r.Success) {
                _axis.ApplyCalibration(r.GHzPerV, preset.Id, Path.GetFileName(d.Source));
                _log.Info(string.Format(CultureInfo.InvariantCulture, "[{0}] 튜닝 계수 교정 성공: {1:F1} MHz/V ({2}, {3})", laser, r.MHzPerV, preset.Name, Path.GetFileName(d.Source)));
                _log.SetStatus(laser + " 튜닝 계수 " + r.MHzPerV.ToString("F1", CultureInfo.InvariantCulture) + " MHz/V 적용·저장");
                OnPropertyChanged(nameof(LastCalibrationText));
            }
            else {
                _log.Error("[" + laser + "] 튜닝 계수 교정 실패: " + string.Join(" / ", r.Failures));
                await _dialogs.AlertAsync(laser + " 튜닝 계수 교정 실패",
                    string.Join("\n", r.Failures.Select(f => "• " + f)) +
                    (double.IsNaN(r.MHzPerV) ? "" : string.Format(CultureInfo.InvariantCulture, "\n\n계산값 {0:F1} MHz/V는 입력하지 않았습니다. 현재 계수를 유지합니다.", r.MHzPerV)),
                    DialogKind.Warning);
            }
        }

        private async Task ReportFailure(string title, string message) {
            HasResult = true;
            IsSuccess = false;
            ResultTitle = "FAIL · " + title;
            ResultText = message;
            _log.Error("[Laser " + _dev.Device.LaserId + "] " + title + ": " + message.Replace('\n', ' '));
            await _dialogs.AlertAsync(title, message, DialogKind.Warning);
        }

        private void ShowResult(CalibrationResult r) {
            HasResult = true;
            IsSuccess = r.Success;
            ResultTitle = r.Summary;
            StringBuilder b = new StringBuilder();
            foreach (string f in r.Failures) b.AppendLine("✗ " + f);
            foreach (string w in r.Warnings) b.AppendLine("! " + w);
            foreach (string s in r.Details) b.AppendLine(s);
            b.Append("이론값: " + r.Preset.Reference);
            ResultText = b.ToString();
        }

        private void ShowData(SpectrumData d, CalibrationResult? r) {
            _data.XD = d.X;
            _data.YD = d.Y;
            _data.Name = d.YName;
            Plot.YLabel = d.YName;
            Plot.XLabel = d.XName;
            Plot.Lines.Clear();
            Plot.Lines.Add(new PlotLine { Value = 0, Color = 0x60FFFFFF, Dash = PlotDash.Dot });
            if (r != null) {
                uint color = r.Success ? PlotColors.LockPoint : PlotColors.Candidate;
                HashSet<int> used = new HashSet<int>();
                foreach (CalibrationInterval iv in r.Preset.Intervals) { used.Add(iv.From); used.Add(iv.To); }
                for (int i = 0; i < r.Lines.Count; i++) {
                    FittedLine l = r.Lines[i];
                    Plot.Lines.Add(new PlotLine {
                        Value = l.CenterV, Vertical = true, Label = used.Contains(i) ? l.Name : null,   // 간격 계산에 쓴 선만 이름 표시 (겹침 방지)
                        Color = used.Contains(i) ? color : 0x80AEB4D4, Dash = used.Contains(i) ? PlotDash.Solid : PlotDash.Dash,
                    });
                }
            }
            Plot.ResetScale();
        }
    }
}
