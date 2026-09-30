#nullable disable
using System;
using System.Globalization;

namespace DLC_PRO.Core
{
    /// <summary>
    /// 피에조 전압 → 상대 주파수(MHz) 변환 설정.
    /// f [MHz] = (V − V0) × 계수 [GHz/V] × 1000
    /// </summary>
    public sealed class FrequencyAxisSettings
    {
        /// <summary>그래프 X축을 상대 주파수(MHz)로 표시.</summary>
        public bool ShowFrequency;
        /// <summary>설정 파일이 없거나 계수가 0(미설정)일 때 쓰는 기본 튜닝 계수 (328 MHz/V, Cs D2 편광 분광 측정값).</summary>
        public const double DefaultCoefGHzPerV = 0.328;
        /// <summary>피에조 튜닝 계수 (GHz/V). 스캔 스펙트럼 교정 또는 레이저 헤드 사양서 값.
        /// 전압을 올릴 때 주파수가 내려가면 음수.</summary>
        public double CoefGHzPerV = DefaultCoefGHzPerV;
        /// <summary>마지막 스펙트럼 교정 정보 (없으면 빈 값).</summary>
        public string CalibrationPreset = "";
        public string CalibratedAt = "";
        public string CalibrationSource = "";
        public double CalibrationMHzPerV = double.NaN;
        /// <summary>false: 스캔 중심(스캔 offset)을 0 MHz로 사용 (기본). true: ZeroVoltage 사용.</summary>
        public bool UseCustomZero;
        /// <summary>사용자 지정 0 MHz 기준 전압 (V).</summary>
        public double ZeroVoltage;

        public bool HasCoefficient { get { return Math.Abs(CoefGHzPerV) > 1e-12; } }

        public FrequencyAxisSettings Clone()
        {
            return new FrequencyAxisSettings
            {
                ShowFrequency = ShowFrequency, CoefGHzPerV = CoefGHzPerV,
                UseCustomZero = UseCustomZero, ZeroVoltage = ZeroVoltage,
                CalibrationPreset = CalibrationPreset, CalibratedAt = CalibratedAt,
                CalibrationSource = CalibrationSource, CalibrationMHzPerV = CalibrationMHzPerV
            };
        }
    }

    /// <summary>한 그래프에 적용할 변환 (기준 전압과 계수가 확정된 상태).</summary>
    public struct FrequencyTransform
    {
        public double ZeroVoltage;
        public double CoefGHzPerV;

        public FrequencyTransform(double zeroVoltage, double coefGHzPerV)
        {
            ZeroVoltage = zeroVoltage;
            CoefGHzPerV = coefGHzPerV;
        }

        /// <summary>V → MHz</summary>
        public double ToMHz(double volt) { return (volt - ZeroVoltage) * CoefGHzPerV * 1000.0; }

        /// <summary>MHz → V (그래프 클릭 좌표를 장비 명령용 전압으로 되돌릴 때)</summary>
        public double ToVolt(double mhz) { return ZeroVoltage + mhz / (CoefGHzPerV * 1000.0); }

        public float[] ToMHz(float[] volts)
        {
            if (volts == null) return null;
            float[] r = new float[volts.Length];
            for (int i = 0; i < volts.Length; i++) r[i] = (float)ToMHz(volts[i]);
            return r;
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "0 MHz = {0:F3} V; {1:G5} GHz/V", ZeroVoltage, CoefGHzPerV);
        }

        /// <summary>
        /// 설정과 현재 스캔 중심으로 변환을 만든다. 계수가 없거나 표시가 꺼져 있으면 null.
        /// </summary>
        public static FrequencyTransform? Create(FrequencyAxisSettings s, double scanCenterVolt)
        {
            if (s == null || !s.ShowFrequency || !s.HasCoefficient) return null;
            double v0 = s.UseCustomZero ? s.ZeroVoltage : scanCenterVolt;
            if (double.IsNaN(v0) || double.IsInfinity(v0)) return null;
            return new FrequencyTransform(v0, s.CoefGHzPerV);
        }
    }
}
