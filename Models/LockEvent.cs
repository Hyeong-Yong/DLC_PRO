using System;

namespace DLC_PRO.Models {
    /// <summary>락 상태 변화 기록 (ReLock 페이지).</summary>
    public sealed class LockEvent {
        public LockEvent(DateTime time, string from, string to, string note, LockEventKind kind) {
            Time = time;
            From = from;
            To = to;
            Note = note;
            Kind = kind;
        }

        public DateTime Time { get; }
        public string From { get; }
        public string To { get; }
        public string Note { get; }
        public LockEventKind Kind { get; }

        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss.f");
    }

    public enum LockEventKind { Neutral, Locked, Unlocked }
}
