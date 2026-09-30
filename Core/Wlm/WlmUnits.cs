using System;
using System.Globalization;

namespace DLC_PRO.Core.Wlm {
    /// <summary>WLM 결과 단위 (Result unit), 오류 값 해석, 표시 형식.</summary>
    public static class WlmUnits {
        public const double SpeedOfLight = 299792.458;      // nm·THz
        public const double HcEvNm = 1239.8419843320026;    // eV·nm

        public static readonly string[] Names = {
            "Wavelength, vac.", "Wavelength, air", "Frequency", "Wavenumber", "Photon energy",
        };

        public static readonly string[] Units = { "nm", "nm", "THz", "1/cm", "eV" };

        public static string Name(int unit) => unit >= 0 && unit < Names.Length ? Names[unit] : "?";
        public static string Unit(int unit) => unit >= 0 && unit < Units.Length ? Units[unit] : "";
        public static string Title(int unit) => Name(unit) + " [" + Unit(unit) + "]";

        /// <summary>단위별 표시 소수 자릿수 (WS/6 분해능 기준, nm 6자리 ≈ 1 fm).</summary>
        public static int Decimals(int unit) => unit switch { 2 => 6, 3 => 5, 4 => 8, _ => 6 };

        public static string Format(double value, int unit) =>
            value.ToString("F" + Decimals(unit).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        /// <summary>측정값이 유효한 결과인지 (음수/0은 오류 값).</summary>
        public static bool IsValid(double v) => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>GetWavelength/GetFrequency 오류 값 → 설명.</summary>
        public static string ErrorText(double v) {
            if (IsValid(v)) return "";
            int code = (int)Math.Round(v);
            return code switch {
                WlmConst.ErrNoValue => "값 없음",
                WlmConst.ErrNoSignal => "No signal (신호 없음)",
                WlmConst.ErrBadSignal => "Bad signal (계산 불가)",
                WlmConst.ErrLowSignal => "Underexposed (신호 약함)",
                WlmConst.ErrBigSignal => "Overexposed (신호 과다)",
                WlmConst.ErrWlmMissing => "WLM 서버 없음",
                WlmConst.ErrNotAvailable => "지원 안 됨",
                WlmConst.ErrNoPulse => "펄스 분리 실패",
                WlmConst.ErrChannelNotAvailable => "채널 없음",
                WlmConst.ErrOutOfRange => "Out of range (측정 범위 밖)",
                WlmConst.ErrUnitNotAvailable => "단위 사용 불가",
                _ => "오류 " + code.ToString(CultureInfo.InvariantCulture),
            };
        }

        /// <summary>Set… 함수 반환 코드 → 설명 (ResERR_…).</summary>
        public static string SetErrorText(int rc) => rc switch {
            0 => "OK",
            -1 => "WLM 서버가 실행 중이 아닙니다",
            -2 => "설정할 수 없습니다",
            -3 => "값이 허용 범위를 벗어났습니다",
            -4 => "WLM 리소스 부족",
            -5 => "WLM 내부 오류",
            -6 => "이 WLM 버전에서 지원하지 않습니다",
            -7 => "WLM이 사용 중입니다",
            -8 => "측정 모드가 아닐 때만 가능합니다",
            -9 => "측정 모드에서만 가능합니다",
            -10 => "채널을 사용할 수 없습니다",
            _ => "오류 코드 " + rc.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>
        /// 진공 파장(nm) → 단위. 공기 파장은 <paramref name="airRatio"/>(λvac/λair, 서비스가 WLM ConvertUnit으로 구함)를 쓴다.
        /// </summary>
        public static double FromVacuum(double vacNm, int unit, double airRatio) {
            if (!IsValid(vacNm)) return vacNm;
            return unit switch {
                WlmConst.cReturnWavelengthAir => vacNm / (airRatio > 1 ? airRatio : 1.000274),
                WlmConst.cReturnFrequency => SpeedOfLight / vacNm,
                WlmConst.cReturnWavenumber => 1e7 / vacNm,
                WlmConst.cReturnPhotonEnergy => HcEvNm / vacNm,
                _ => vacNm,
            };
        }

        /// <summary>시뮬레이터용 해석적 변환 (공기 굴절률: Edlén 1966).</summary>
        public static double ConvertAnalytic(double value, int from, int to, double tempC, double pressureMbar) {
            if (value <= 0) return value;
            if (from < 0 || from > 4 || to < 0 || to > 4) return WlmConst.ErrUnitNotAvailable;
            double vac = from switch {
                WlmConst.cReturnWavelengthAir => AirToVac(value, tempC, pressureMbar),
                WlmConst.cReturnFrequency => SpeedOfLight / value,
                WlmConst.cReturnWavenumber => 1e7 / value,
                WlmConst.cReturnPhotonEnergy => HcEvNm / value,
                _ => value,
            };
            if (to == WlmConst.cReturnWavelengthAir) return vac / AirIndex(vac, tempC, pressureMbar);
            return FromVacuum(vac, to, 0);
        }

        public static double AirIndex(double vacNm, double tempC, double pressureMbar) {
            double s2 = Math.Pow(1000.0 / vacNm, 2);   // (1/λ[µm])²
            double ns = (8342.54 + 2406147 / (130 - s2) + 15998 / (38.9 - s2)) * 1e-8;
            double p = pressureMbar * 100;              // Pa
            double t = double.IsNaN(tempC) || tempC < -50 ? 20 : tempC;
            if (p <= 0) p = 101325;
            return 1 + ns * p * (1 + p * (0.601 - 0.00972 * t) * 1e-8) / (96095.43 * (1 + 0.003661 * t));
        }

        private static double AirToVac(double airNm, double t, double p) {
            double vac = airNm * 1.00027;
            for (int i = 0; i < 4; i++) vac = airNm * AirIndex(vac, t, p);
            return vac;
        }
    }
}
