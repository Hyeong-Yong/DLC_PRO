using System;

namespace DLC_PRO.Models {
    /// <summary>로그 레벨 (화면 색 구분용).</summary>
    public enum LogLevel { Info, Warn, Error, Traffic }

    /// <summary>하단 로그 목록의 한 줄.</summary>
    public sealed class LogEntry {
        public LogEntry(DateTime time, LogLevel level, string message) {
            Time = time;
            Level = level;
            Message = message;
        }

        public DateTime Time { get; }
        public LogLevel Level { get; }
        public string Message { get; }

        public string TimeText => Time.ToString("HH:mm:ss.fff");
        public string LevelText => Level.ToString().ToUpperInvariant();

        public static LogLevel ParseLevel(string level) {
            switch ((level ?? "").ToUpperInvariant()) {
                case "WARN": return LogLevel.Warn;
                case "ERROR": return LogLevel.Error;
                case "TX":
                case "RX": return LogLevel.Traffic;
                default: return LogLevel.Info;
            }
        }
    }
}
