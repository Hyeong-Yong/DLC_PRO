#nullable disable
using System.Collections.Generic;

namespace DLC_PRO.Core
{
    /// <summary>
    /// 신호 채널 ID 표 (Command Reference 부록 4.1 "Signal Channel IDs").
    /// </summary>
    public static class SignalChannels
    {
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { -3, "none" },
            { -2, "Time" },
            { -1, "Frequency" },
            { 0, "Fine In 1" },
            { 1, "Fine In 2" },
            { 2, "Fast In 3" },
            { 4, "Fast In 4" },
            { 20, "Output A" },
            { 21, "Output B" },
            { 30, "Lock-In Out" },
            { 31, "PID 1 Out" },
            { 32, "PID 2 Out" },
            { 34, "Scan Output" },
            { 35, "Aux Scan Output" },
            { 40, "PDH Error 1" },
            { 41, "PDH In 1" },
            { 42, "PDH Error 2" },
            { 43, "PDH In 2" },
            { 50, "Piezo Voltage" },
            { 51, "CC Current (Laser Current)" },
            { 52, "CC AIn A" },
            { 53, "CC AIn B" },
            { 54, "Laser PD (Monitor PD)" },
            { 55, "PD EXT (user calibrated)" },
            { 56, "Laser Set Temperature" },
            { 57, "Laser Actual Temperature" },
            { 58, "EOM Voltage" },
            { 60, "AMPCC AIn" },
            { 61, "Seed Power" },
            { 62, "Amplifier Power" },
            { 63, "Amplifier Current" },
            { 69, "CTL Laser Photodiode" },
            { 70, "CTL Laser Power" },
            { 78, "CTL Set Wavelength" },
            { 79, "CTL Actual Wavelength" },
            { 100, "Lock Input (alias)" },
            { 101, "Scan Output Channel (alias)" },
            { 102, "PowerLock Input (alias)" },
            { 103, "Aux Scan Output Channel (alias)" },
        };

        /// <summary>외부/내부 입력 채널 (PID 입력, 락 입력, 윈도우 입력 등).</summary>
        public static readonly int[] Inputs = { 0, 1, 2, 4, 30, 31, 32, 40, 41, 42, 43, 52, 53, 54, 55, 57, 60, 61, 62 };

        /// <summary>출력 채널 (스캔, PID 출력, ReLock, 와이드스캔 등).</summary>
        public static readonly int[] Outputs = { 20, 21, 50, 51, 56, 63 };

        /// <summary>Lock-In 변조 가능 출력.</summary>
        public static readonly int[] ModulationOutputs = { 20, 21, 50, 51, 56 };

        /// <summary>스코프/레코더에 표시 가능한 신호.</summary>
        public static readonly int[] Display = { -3, 0, 1, 2, 4, 20, 21, 30, 31, 32, 34, 35, 40, 41, 42, 43, 50, 51, 52, 53, 54, 55, 56, 57, 60, 61, 62, 63, 100, 101, 102 };

        /// <summary>출력 안정화 입력 가능 신호.</summary>
        public static readonly int[] PowerStabInputs = { 54, 55, 62, 70 };

        public static string Name(int id)
        {
            string n;
            return Names.TryGetValue(id, out n) ? n : ("채널 " + id);
        }
    }
}
