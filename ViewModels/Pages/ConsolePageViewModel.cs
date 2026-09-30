using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Data;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// Console 페이지 — 읽기 전용 명령 콘솔. 단일 조회 표현식만 허용.
    /// 예: (param-ref 'laser1:dl:cc:current-act), (param-disp 'laser1:dl:lock)
    /// Settings에서 송수신 로그를 켜면 TX/RX도 여기에 표시된다.
    /// </summary>
    public partial class ConsolePageViewModel : PageViewModel {
        private const int MaxChars = 200000;
        private readonly List<string> _history = new List<string>();
        private int _histPos;
        private readonly ConcurrentQueue<string> _traffic = new ConcurrentQueue<string>();
        private readonly StringBuilder _out = new StringBuilder();

        public ConsolePageViewModel(DeviceService dev) : base(ApplicationPageNames.Console, dev) {
            for (int i = 0; i < QuickCommands.Count; i++)
                ((string[])QuickCommands)[i] = QuickCommands[i].Replace("laser1:", "laser" + dev.Device.LaserId + ":", StringComparison.Ordinal);
            Append("DLC pro 읽기 전용 콘솔 — 쓰기·권한 변경·복합 명령 차단. Enter로 조회, ↑/↓ 이력. 예) (param-ref 'laser1:dl:cc:current-act)\n");
            dev.Device.Traffic += (d, t) => _traffic.Enqueue("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + d + ": " + t.Replace("\r", "").Replace("\n", " ⏎ "));
        }

        public override string Title => "Console";
        public override string Subtitle => "Laser " + Dev.Device.LaserId + " · 읽기 전용: param-ref / param-disp / 시스템 메시지 조회만 허용됩니다.";

        public IReadOnlyList<string> QuickCommands { get; } = new[] {
            "(param-disp 'laser1:dl)", "(param-disp 'laser1:amp)", "(param-disp 'laser1:dl:lock)",
            "(param-disp 'laser1:scan)", "(param-ref 'system-health-txt)", "(exec 'system-messages:show-new)",
        };

        [ObservableProperty] private string _output = "";
        [ObservableProperty] private string _input = "";
        /// <summary>출력 끝으로 스크롤할 때마다 증가 (View가 감지).</summary>
        [ObservableProperty] private int _outputVersion;

        protected override void OnTick() {
            if (_traffic.IsEmpty) return;
            StringBuilder sb = new StringBuilder();
            int n = 0;
            while (n++ < 500 && _traffic.TryDequeue(out string? s)) {
                if (s.Length > 400) s = s.Substring(0, 400) + " …";
                sb.Append(s).Append('\n');
            }
            Append(sb.ToString());
        }

        private void Append(string s) {
            _out.Append(s);
            if (_out.Length > MaxChars) _out.Remove(0, _out.Length - MaxChars / 2);
            Output = _out.ToString();
            OutputVersion++;
        }

        [RelayCommand]
        private async Task SendAsync() {
            string cmd = (Input ?? "").Trim();
            if (cmd.Length == 0) return;
            if (_history.Count == 0 || _history[_history.Count - 1] != cmd) _history.Add(cmd);
            _histPos = _history.Count;
            Input = "";
            Append("> " + cmd + "\n");
            try {
                string r = await Dev.Device.SendRawAsync(cmd);
                Append(r.Replace("\r", "") + "\n");
            }
            catch (Exception ex) { Append("!! " + DeviceService.Unwrap(ex).Message + "\n"); }
        }

        [RelayCommand]
        private async Task QuickAsync(string cmd) {
            Input = cmd;
            await SendAsync();
        }

        [RelayCommand]
        private void HistoryUp() {
            if (_history.Count == 0) return;
            _histPos = Math.Max(0, _histPos - 1);
            Input = _history[_histPos];
        }

        [RelayCommand]
        private void HistoryDown() {
            if (_history.Count == 0) return;
            _histPos = Math.Min(_history.Count, _histPos + 1);
            Input = _histPos < _history.Count ? _history[_histPos] : "";
        }

        [RelayCommand]
        private void Clear() {
            _out.Clear();
            Output = "";
        }
    }
}
