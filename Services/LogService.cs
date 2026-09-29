using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DLC_PRO.Models;

namespace DLC_PRO.Services {
    /// <summary>
    /// 앱 로그 + 상태 표시줄 메시지.
    /// 어느 스레드에서 호출해도 되며 목록 갱신은 UI 스레드에서 한다.
    /// 로그 파일: %AppData%\DLC_PRO\logs\yyyy-MM-dd.log
    /// </summary>
    public sealed partial class LogService : ObservableObject, IDisposable {
        private const int MaxEntries = 3000;
        private readonly ConcurrentQueue<LogEntry> _pending = new ConcurrentQueue<LogEntry>();
        private StreamWriter? _file;
        private readonly object _fileLock = new object();
        private int _flushScheduled;

        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        /// <summary>상태 표시줄의 최근 메시지.</summary>
        [ObservableProperty]
        private string _status = "";

        /// <summary>마지막으로 추가된 항목 (목록 자동 스크롤용).</summary>
        [ObservableProperty]
        private LogEntry? _lastEntry;

        public LogService() {
            try {
                Directory.CreateDirectory(AppSettings.LogFolder);
                string path = Path.Combine(AppSettings.LogFolder, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                _file = new StreamWriter(path, true, Encoding.UTF8) { AutoFlush = true };
            }
            catch {
                _file = null;
            }
        }

        public void Info(string msg) => Add(LogLevel.Info, msg);
        public void Warn(string msg) => Add(LogLevel.Warn, msg);
        public void Error(string msg) => Add(LogLevel.Error, msg);

        /// <summary>DlcDevice.LogMessage 형식 ("INFO"/"WARN"/"ERROR"/"TX"/"RX").</summary>
        public void Add(string level, string msg) => Add(LogEntry.ParseLevel(level), msg);

        public void Add(LogLevel level, string msg) {
            LogEntry e = new LogEntry(DateTime.Now, level, msg ?? "");
            try {
                lock (_fileLock) _file?.WriteLine(e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\t" + e.LevelText + "\t" + e.Message);
            }
            catch {
                // 파일 기록 실패는 무시
            }
            _pending.Enqueue(e);
            // 여러 스레드에서 몰려도 UI 갱신은 한 번에 모아서
            if (System.Threading.Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        public void SetStatus(string msg) {
            if (Dispatcher.UIThread.CheckAccess()) Status = msg;
            else Dispatcher.UIThread.Post(() => Status = msg);
        }

        private void Flush() {
            System.Threading.Interlocked.Exchange(ref _flushScheduled, 0);
            LogEntry? last = null;
            int n = 0;
            while (n++ < 500 && _pending.TryDequeue(out LogEntry? e)) {
                Entries.Add(e);
                last = e;
            }
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
            if (!_pending.IsEmpty && System.Threading.Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
            if (last != null) LastEntry = last;
        }

        public void Clear() => Entries.Clear();

        public void Dispose() {
            lock (_fileLock) {
                try { _file?.Dispose(); }
                catch { }
                _file = null;
            }
        }
    }
}
