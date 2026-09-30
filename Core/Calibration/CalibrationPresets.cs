using System.Collections.Generic;

namespace DLC_PRO.Core.Calibration {
    /// <summary>분광선 하나 (기준선 대비 주파수 차이, MHz).</summary>
    public sealed record CalibrationLine(string Name, double OffsetMHz);

    /// <summary>튜닝 계수를 계산할 선 간격 (Lines 인덱스).</summary>
    public sealed record CalibrationInterval(int From, int To);

    /// <summary>
    /// 튜닝 계수 교정에 쓰는 원자 전이 선택지.
    /// Lines: 스캔에서 보이는 모든 선(패턴 인식용, 교차 공명 포함),
    /// Intervals: 튜닝 계수 계산에 쓰는 이론 간격.
    /// </summary>
    public sealed class CalibrationPreset {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public IReadOnlyList<CalibrationLine> Lines { get; init; } = new List<CalibrationLine>();
        public IReadOnlyList<CalibrationInterval> Intervals { get; init; } = new List<CalibrationInterval>();
        /// <summary>이론값 출처 (결과 화면에 표시).</summary>
        public string Reference { get; init; } = "";

        public double IntervalMHz(CalibrationInterval iv) => Lines[iv.To].OffsetMHz - Lines[iv.From].OffsetMHz;
        public string IntervalName(CalibrationInterval iv) => Lines[iv.To].Name + " – " + Lines[iv.From].Name;
        public override string ToString() => Name;
    }

    /// <summary>
    /// 기본 선택지 2개. 새 전이를 추가하려면 여기에 CalibrationPreset을 하나 더 만들고 All에 넣으면 된다.
    /// </summary>
    public static class CalibrationPresets {
        // 6P3/2 초미세 간격 (D. A. Steck, "Cesium D Line Data", rev. 2.3.3):
        //   F'=5–F'=4 251.0916 MHz, F'=4–F'=3 201.2871 MHz  → F'=5–F'=3 452.3787 MHz
        private const double F54 = 251.0916, F43 = 201.2871;

        /// <summary>
        /// Cs D2 852 nm, 6S1/2 F=4 → 6P3/2 F'=3,4,5 (포화흡수/편광 분광).
        /// 스캔에는 전이 3개와 교차 공명(crossover) 3개, 모두 6개가 보인다.
        /// 튜닝 계수는 F'=5–F'=4, F'=5–F'=3 두 간격으로 각각 계산하고 서로 비교한다.
        /// </summary>
        public static readonly CalibrationPreset CsD2 = new CalibrationPreset {
            Id = "cs-d2-f4",
            Name = "Cs D2 852 nm · 6S1/2 F=4 → 6P3/2 F'=3,4,5",
            Description = "F'=5–F'=4 (251.09 MHz), F'=5–F'=3 (452.38 MHz) 간격으로 계산 · 교차 공명 포함 6개 선으로 선 배정",
            Lines = new List<CalibrationLine> {
                new("F'=3", -(F54 + F43)),
                new("CO 3-4", -(F54 + F43 / 2)),
                new("F'=4", -F54),
                new("CO 3-5", -(F54 + F43) / 2),
                new("CO 4-5", -F54 / 2),
                new("F'=5", 0),
            },
            Intervals = new List<CalibrationInterval> { new(2, 5), new(0, 5) },
            Reference = "D. A. Steck, Cesium D Line Data (6P3/2 hyperfine)",
        };

        // 7S1/2 초미세 간격 F''=4–F''=3: 2183.48(4) MHz (Appl. Sci. 10, 525 (2020), OODR)
        private const double S7 = 2183.48;

        /// <summary>
        /// Cs 1470 nm, 6P3/2 → 7S1/2 (852 nm와 계단형 2단 여기, EIT/OODR 스펙트럼).
        /// 같은 중간 준위(F'=4)에서 7S1/2 F''=3과 F''=4로 가는 두 선 간격(7S1/2 초미세 분리)을 쓴다.
        /// 같은 속도군 원자에서 나오는 두 선이므로 도플러 이동이 같아 간격이 그대로 보인다.
        /// 간격이 하나뿐이라 두 값 비교는 할 수 없고, 계수 범위와 신호 모양만 확인한다.
        /// </summary>
        public static readonly CalibrationPreset Cs7S = new CalibrationPreset {
            Id = "cs-6p-7s",
            Name = "Cs 1470 nm · 6P3/2 → 7S1/2 F''=3,4",
            Description = "7S1/2 초미세 분리 F''=4–F''=3 (2183.48 MHz) 간격으로 계산 · 가장 뚜렷한 두 선을 자동 선택",
            Lines = new List<CalibrationLine> {
                new("F''=3", -S7),
                new("F''=4", 0),
            },
            Intervals = new List<CalibrationInterval> { new(0, 1) },
            Reference = "Cs 7S1/2 HFS 2183.48 MHz (Appl. Sci. 10, 525 (2020))",
        };

        public static readonly IReadOnlyList<CalibrationPreset> All = new[] { CsD2, Cs7S };

        public static CalibrationPreset ById(string? id) {
            foreach (CalibrationPreset p in All) if (p.Id == id) return p;
            return CsD2;
        }
    }
}
