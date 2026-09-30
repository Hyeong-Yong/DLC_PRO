using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels {
    /// <summary>
    /// "주파수 축 (Piezo V → 상대 주파수 MHz)" 설정 패널 (WinForms FrequencyAxisGroup 대응).
    /// Scan &amp; Lock / Wide Scan 페이지가 각자 인스턴스를 가지며, 설정(AppSettings.Freq)은 공유한다.
    /// f [MHz] = (V − V0) × 계수[GHz/V] × 1000, V0 = 스캔 중심(기본) 또는 사용자 지정 전압.
    /// </summary>
    public partial class FrequencyAxisViewModel : ViewModelBase {
        private readonly DeviceService _dev;
        private readonly LogService _log;
        private readonly Func<double> _centerVolt;
        private readonly Func<double>? _lockpointVolt;
        private bool _loading;

        public FrequencyAxisViewModel(DeviceService dev, LogService log, Func<double> centerVolt, string centerName, Func<double>? lockpointVolt = null) {
            _dev = dev;
            _log = log;
            _centerVolt = centerVolt;
            _lockpointVolt = lockpointVolt;
            CenterName = centerName;
            ZeroCenterText = centerName + " = 0 MHz (기본)";
            LoadFromSettings();
            dev.FreqSettingsChanged += LoadFromSettings;
        }

        public string CenterName { get; }
        public string ZeroCenterText { get; }
        public bool HasLockpointButton => _lockpointVolt != null;

        [ObservableProperty] private bool _showFrequency;
        [ObservableProperty] private decimal? _coefficient;
        [ObservableProperty] private bool _useCustomZero;
        [ObservableProperty] private decimal? _zeroVoltage;
        [ObservableProperty] private string _info = "";

        /// <summary>라디오 버튼용 (UseCustomZero의 반대).</summary>
        public bool UseCenterZero {
            get => !UseCustomZero;
            set {
                if (value) UseCustomZero = false;
            }
        }

        partial void OnShowFrequencyChanged(bool value) => Save();
        partial void OnCoefficientChanged(decimal? value) => Save();
        partial void OnZeroVoltageChanged(decimal? value) => Save();

        partial void OnUseCustomZeroChanged(bool value) {
            OnPropertyChanged(nameof(UseCenterZero));
            Save();
        }

        private void LoadFromSettings() {
            FrequencyAxisSettings s = _dev.Settings.Freq;
            _loading = true;
            try {
                ShowFrequency = s.ShowFrequency;
                Coefficient = ToDecimal(s.CoefGHzPerV);
                UseCustomZero = s.UseCustomZero;
                ZeroVoltage = ToDecimal(s.ZeroVoltage);
            }
            finally { _loading = false; }
            UpdateInfo();
        }

        private static decimal ToDecimal(double v) {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0m;
            return (decimal)Math.Max(-1000, Math.Min(1000, v));
        }

        private void Save() {
            if (_loading) return;
            FrequencyAxisSettings s = _dev.Settings.Freq;
            s.ShowFrequency = ShowFrequency;
            s.CoefGHzPerV = (double)(Coefficient ?? 0m);
            s.UseCustomZero = UseCustomZero;
            s.ZeroVoltage = (double)(ZeroVoltage ?? 0m);
            _dev.NotifyFreqChanged();   // 저장 + 모든 그래프/패널 동기화 (LoadFromSettings 포함)
        }

        /// <summary>
        /// 스펙트럼 교정 결과를 튜닝 계수 입력란에 넣고 저장 (설정 파일 → 프로그램을 꺼도 유지).
        /// </summary>
        public void ApplyCalibration(double ghzPerV, string presetId, string source) {
            FrequencyAxisSettings s = _dev.Settings.Freq;
            s.CalibrationPreset = presetId;
            s.CalibratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            s.CalibrationSource = source;
            s.CalibrationMHzPerV = ghzPerV * 1000;
            Coefficient = ToDecimal(Math.Round(ghzPerV, 6));   // → Save() → 설정 저장 + 모든 화면 동기화
            Save();   // 값이 같아 변경 알림이 없어도 교정 정보는 저장
        }

        [RelayCommand]
        private void CenterToZero() {
            double v = _centerVolt();
            if (double.IsNaN(v)) {
                _log.SetStatus("스캔 중심 전압을 알 수 없습니다 (연결/스캔 출력 확인).");
                return;
            }
            SetCustomZero(v);
        }

        [RelayCommand]
        private void LockpointToZero() {
            double v = _lockpointVolt?.Invoke() ?? double.NaN;
            if (double.IsNaN(v)) {
                _log.SetStatus("선택된 락 포인트가 없습니다.");
                return;
            }
            SetCustomZero(v);
        }

        private void SetCustomZero(double v) {
            _loading = true;
            try {
                ZeroVoltage = ToDecimal(Math.Round(v, 4));
                UseCustomZero = true;
            }
            finally { _loading = false; }
            Save();
        }

        /// <summary>안내 문구 갱신 (페이지 Tick에서 호출).</summary>
        public void UpdateInfo() {
            FrequencyAxisSettings s = _dev.Settings.Freq;
            string t;
            if (!s.HasCoefficient) t = "튜닝 계수를 입력하면 MHz로 표시됩니다.";
            else {
                double center = _centerVolt();
                double v0 = s.UseCustomZero ? s.ZeroVoltage : center;
                if (double.IsNaN(v0)) t = "기준 전압을 알 수 없음 (" + CenterName + " 확인)";
                else {
                    double mhzPerV = s.CoefGHzPerV * 1000.0;
                    t = string.Format(CultureInfo.InvariantCulture, "현재 0 MHz = {0:F3} V   (1 V = {1:G5} MHz)", v0, mhzPerV);
                    if (s.UseCustomZero && !double.IsNaN(center))
                        t += string.Format(CultureInfo.InvariantCulture, "\n{0} {1:F3} V = {2:F1} MHz", CenterName, center, (center - v0) * mhzPerV);
                }
                if (!s.ShowFrequency) t += "\n(표시 꺼짐 — 위 체크박스를 켜세요)";
            }
            Info = t;
        }
    }
}
