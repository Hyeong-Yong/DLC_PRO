using System.Diagnostics;

namespace DLC_PRO.Models.Plot {
    /// <summary>시간-값 추세용 원형 버퍼 (Stabilization 페이지).</summary>
    public sealed class TrendBuffer {
        private readonly float[] _t, _v;
        private int _count, _head;
        private readonly Stopwatch _sw;   // 시스템 시계 변경 영향 없음

        public TrendBuffer(int capacity, Stopwatch clock) {
            _t = new float[capacity];
            _v = new float[capacity];
            _sw = clock;
        }

        public int Count => _count;

        public void Add(double value) {
            _t[_head] = (float)_sw.Elapsed.TotalSeconds;
            _v[_head] = (float)value;
            _head = (_head + 1) % _t.Length;
            if (_count < _t.Length) _count++;
        }

        public void Clear() {
            _count = 0;
            _head = 0;
        }

        public void CopyTo(PlotSeries s) {
            float[] x = new float[_count], y = new float[_count];
            int start = (_head - _count + _t.Length) % _t.Length;
            for (int i = 0; i < _count; i++) {
                x[i] = _t[(start + i) % _t.Length];
                y[i] = _v[(start + i) % _t.Length];
            }
            s.X = x;
            s.Y = y;
        }
    }
}
