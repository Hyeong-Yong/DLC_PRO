using System;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DLC_PRO.Models.Plot;
using ScottPlot;
using ScottPlot.Avalonia;
using SPColor = ScottPlot.Color;

namespace DLC_PRO.Controls {
    /// <summary>
    /// ScottPlot(AvaPlot) 래퍼 컨트롤.
    /// - <see cref="Model"/>(PlotModel, ViewModel 소유)의 데이터를 그린다. Model.Invalidate() → 다시 그리기.
    /// - 한 번 클릭 → <see cref="ClickCommand"/>(PlotPoint 데이터 좌표) 실행 (click-and-lock)
    /// - 더블클릭/가운데 클릭 → 자동 스케일, 드래그/휠 → 수동 스케일 유지
    /// - 오른쪽 클릭 메뉴: 자동 스케일 / 스케일 고정 / PNG 저장 / CSV 저장
    /// View 전용 로직만 있고 장비 관련 로직은 없다 (MVVM).
    /// </summary>
    public partial class PlotView : UserControl {
        public static readonly StyledProperty<PlotModel?> ModelProperty =
            AvaloniaProperty.Register<PlotView, PlotModel?>(nameof(Model));

        public static readonly StyledProperty<ICommand?> ClickCommandProperty =
            AvaloniaProperty.Register<PlotView, ICommand?>(nameof(ClickCommand));

        public PlotModel? Model {
            get => GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        /// <summary>데이터 영역 한 번 클릭 시 실행 (매개변수: <see cref="PlotPoint"/>).</summary>
        public ICommand? ClickCommand {
            get => GetValue(ClickCommandProperty);
            set => SetValue(ClickCommandProperty, value);
        }

        private readonly AvaPlot _plot;
        private readonly ScottPlot.Plottables.Annotation _coord;
        private readonly DispatcherTimer _clickTimer;
        private PlotModel? _attached;
        private Point _downPt;
        private bool _leftDown, _dragged;
        private PlotPoint _pendingClick;
        private string _lastLabels = "";
        private static string _uiFont = "";

        public PlotView() {
            InitializeComponent();
            _plot = this.FindControl<AvaPlot>("Plot") ?? throw new InvalidOperationException("AvaPlot not found");

            // 더블클릭 벤치마크 표시 끄기 (더블클릭 = 자동 스케일로 사용)
            _plot.UserInputProcessor.DoubleLeftClickBenchmark(false);
            if (_uiFont.Length == 0) _uiFont = PlotTypography.FontName;
            ApplyDarkStyle(_plot.Plot);

            _coord = new ScottPlot.Plottables.Annotation { Text = "", Alignment = Alignment.LowerRight };
            _coord.LabelFontColor = SPColor.FromARGB(0xFFE6E678);
            _coord.LabelBackgroundColor = Colors.Transparent;
            _coord.LabelBorderColor = Colors.Transparent;
            _coord.LabelShadowColor = Colors.Transparent;
            _coord.LabelFontName = _uiFont;

            if (_plot.Menu != null) {
                _plot.Menu.Clear();
                _plot.Menu.Add("자동 스케일", _ => Model?.ResetScale());
                _plot.Menu.Add("현재 스케일 고정", _ => {
                    if (Model == null) return;
                    Model.AutoScaleX = Model.AutoScaleY = false;
                    Render();
                });
                _plot.Menu.AddSeparator();
                _plot.Menu.Add("그래프 PNG로 저장...", _ => SavePng());
                _plot.Menu.Add("데이터 CSV로 저장...", _ => SaveCsv());
            }

            _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            _clickTimer.Tick += (_, _) => {
                // 더블클릭이 아니었으면 이때 한 번 클릭으로 처리
                _clickTimer.Stop();
                ICommand? cmd = ClickCommand;
                if (cmd != null && cmd.CanExecute(_pendingClick)) cmd.Execute(_pendingClick);
            };

            // AvaPlot이 자체 마우스 처리를 하므로 handledEventsToo로 함께 받는다
            _plot.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            _plot.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            _plot.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            _plot.AddHandler(PointerWheelChangedEvent, (_, _) => SetManualScale(), RoutingStrategies.Tunnel, true);
            _plot.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel, true);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ModelProperty) Attach(Model);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Attach(Model);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Attach(null);
            _clickTimer.Stop();
        }

        private void Attach(PlotModel? m) {
            if (ReferenceEquals(_attached, m)) return;
            if (_attached != null) _attached.Updated -= Render;
            _attached = m;
            if (m != null) {
                m.Updated += Render;
                _lastLabels = "";
                Render();
            }
        }

        // ------------------------------------------------------------------
        // 그리기
        // ------------------------------------------------------------------

        private static void ApplyDarkStyle(Plot plot) {
            plot.FigureBackground.Color = SPColor.FromARGB(0xFF181B2E);   // CardBackground
            plot.DataBackground.Color = SPColor.FromARGB(0xFF10131F);
            plot.Axes.Color(SPColor.FromARGB(0xFFAEB4D4));
            plot.Grid.MajorLineColor = SPColor.FromARGB(0xFF262A45);
            plot.Legend.BackgroundColor = SPColor.FromARGB(0xFF1E2238);
            plot.Legend.FontColor = SPColor.FromARGB(0xFFCFCFCF);
            plot.Legend.OutlineColor = SPColor.FromARGB(0xFF2E3252);
            plot.Legend.Alignment = Alignment.UpperRight;
        }

        private static SPColor C(uint argb) => SPColor.FromARGB(argb);

        private static LinePattern Pattern(PlotDash d) => d switch {
            PlotDash.Dash => LinePattern.Dashed,
            PlotDash.Dot => LinePattern.Dotted,
            _ => LinePattern.Solid,
        };

        private static double[] D(float[] f, int n) {
            double[] d = new double[n];
            for (int i = 0; i < n; i++) d[i] = f[i];
            return d;
        }

        private static bool AllFinite(float[] y, int n) {
            for (int i = 0; i < n; i++) if (float.IsNaN(y[i]) || float.IsInfinity(y[i])) return false;
            return true;
        }

        private static bool IsAscending(float[] x, int n) {
            for (int i = 1; i < n; i++) if (!(x[i] >= x[i - 1])) return false;
            return true;
        }

        private void Render() {
            PlotModel? m = _attached;
            Plot plot = _plot.Plot;
            plot.Clear();
            if (m == null) {
                _plot.Refresh();
                return;
            }
            bool usesY2 = false;
            int named = 0;

            foreach (PlotSeries s in m.Series) {
                if (!s.Visible || s.X == null || s.Y == null) continue;
                int n = Math.Min(s.X.Length, s.Y.Length);
                if (n == 0) continue;
                double[] xs = D(s.X, n), ys = D(s.Y, n);
                if (!s.Points && n > 5000 && IsAscending(s.X, n) && AllFinite(s.Y, n)) {
                    // 대용량 + x 단조증가 (레코더/와이드스캔): SignalXY 가 훨씬 빠름
                    ScottPlot.Plottables.SignalXY sig = plot.Add.SignalXY(xs, ys, C(s.Color));
                    sig.LineWidth = s.Width;
                    sig.LinePattern = Pattern(s.Dash);
                    sig.LegendText = s.Name ?? "";
                    if (s.UseY2) sig.Axes.YAxis = plot.Axes.Right;
                }
                else {
                    ScottPlot.Plottables.Scatter sc = plot.Add.Scatter(xs, ys, C(s.Color));
                    if (s.Points) {
                        sc.LineWidth = 0;
                        sc.MarkerSize = 3;
                        sc.MarkerShape = MarkerShape.FilledCircle;
                    }
                    else {
                        sc.LineWidth = s.Width;
                        sc.MarkerSize = 0;
                        sc.LinePattern = Pattern(s.Dash);
                    }
                    sc.LegendText = s.Name ?? "";
                    if (s.UseY2) sc.Axes.YAxis = plot.Axes.Right;
                }
                if (s.UseY2) usesY2 = true;
                if (!string.IsNullOrEmpty(s.Name)) named++;
            }

            foreach (PlotLine l in m.Lines) {
                if (l.Vertical) {
                    ScottPlot.Plottables.VerticalLine v = plot.Add.VerticalLine(l.Value, 1.2f, C(l.Color), Pattern(l.Dash));
                    if (!string.IsNullOrEmpty(l.Label)) v.Text = l.Label;
                }
                else {
                    ScottPlot.Plottables.HorizontalLine h = plot.Add.HorizontalLine(l.Value, 1.2f, C(l.Color), Pattern(l.Dash));
                    if (!string.IsNullOrEmpty(l.Label)) h.Text = l.Label;
                }
            }

            foreach (PlotMarker mk in m.Markers) {
                float size = mk.Size > 0 ? mk.Size * 2 : 10;
                MarkerShape shape = mk.Shape switch {
                    PlotMarkerShape.TriangleUp => MarkerShape.FilledTriangleDown,    // 피크 위에서 아래를 가리킴
                    PlotMarkerShape.TriangleDown => MarkerShape.FilledTriangleUp,    // 골 아래에서 위를 가리킴
                    PlotMarkerShape.Diamond => MarkerShape.FilledDiamond,
                    PlotMarkerShape.Cross => MarkerShape.Cross,
                    _ => MarkerShape.OpenCircle,
                };
                ScottPlot.Plottables.Marker p = plot.Add.Marker(mk.X, mk.Y, shape, size, C(mk.Color));
                if (shape == MarkerShape.OpenCircle || shape == MarkerShape.Cross) p.MarkerLineWidth = 2;
                if (!string.IsNullOrEmpty(mk.Label)) {
                    ScottPlot.Plottables.Text t = plot.Add.Text(mk.Label, mk.X, mk.Y);
                    t.LabelFontColor = C(mk.Color);
                    t.LabelFontSize = 12;
                    t.LabelFontName = _uiFont;
                    t.OffsetX = size / 2 + 4;
                    t.OffsetY = -(size / 2 + 16);
                }
            }

            if (!string.IsNullOrEmpty(m.Overlay)) {
                ScottPlot.Plottables.Annotation a = plot.Add.Annotation(m.Overlay, Alignment.UpperLeft);
                a.LabelFontColor = C(0xFFE6E678);
                a.LabelBackgroundColor = C(0xA010131F);
                a.LabelBorderColor = Colors.Transparent;
                a.LabelShadowColor = Colors.Transparent;
                a.LabelFontName = _uiFont;
            }
            plot.PlottableList.Add(_coord);

            // 축 라벨 (바뀔 때만 한글 글꼴 자동 선택)
            string labels = m.Title + "|" + m.XLabel + "|" + m.YLabel + "|" + m.Y2Label + "|" + usesY2;
            if (labels != _lastLabels) {
                plot.Title(m.Title ?? "");
                plot.XLabel(m.XLabel ?? "");
                plot.YLabel(m.YLabel ?? "");
                plot.Axes.Right.Label.Text = usesY2 ? (m.Y2Label ?? "") : "";
                plot.Axes.Right.Label.ForeColor = C(PlotColors.Trace2);
                plot.Axes.Title.Label.ForeColor = C(0xFFE8EAF6);
                _lastLabels = labels;
            }
            // Re-created series/annotations and legend also need the Korean font on every render.
            PlotTypography.Apply(plot);
            plot.Legend.IsVisible = named > 0;

            // 오른쪽 축을 쓰지 않으면 이전 눈금이 남지 않도록 초기화
            if (!usesY2) plot.Axes.Right.Range.Reset();

            // 스케일 (X만/Y만 자동일 때는 해당 축만 — 사용자가 맞춘 다른 축 유지)
            if (m.AutoScaleX && m.AutoScaleY) plot.Axes.AutoScale();
            else if (m.AutoScaleX) plot.Axes.AutoScaleX();
            else if (m.AutoScaleY) {
                plot.Axes.AutoScaleY(plot.Axes.Left);
                if (usesY2) plot.Axes.AutoScaleY(plot.Axes.Right);
            }
            if (m.AutoScaleY) {
                Widen(plot, plot.Axes.Left, m.MinYSpan);
                if (usesY2) Widen(plot, plot.Axes.Right, m.MinY2Span);
            }
            _plot.Refresh();
        }

        private static void Widen(Plot plot, IYAxis axis, double span) {
            if (span <= 0) return;
            double lo = axis.Min, hi = axis.Max;
            if (hi - lo >= span) return;
            double c = (lo + hi) / 2;
            plot.Axes.SetLimitsY(c - span / 2, c + span / 2, axis);
        }

        // ------------------------------------------------------------------
        // 마우스 / 키보드
        // ------------------------------------------------------------------

        private void SetManualScale() {
            if (_attached == null) return;
            _attached.AutoScaleX = _attached.AutoScaleY = false;
        }

        private void OnKey(object? sender, KeyEventArgs e) {
            if (e.Key == Key.A) _attached?.ResetScale();
            else if (e.Key != Key.LeftShift && e.Key != Key.RightShift && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl
                     && e.Key != Key.LeftAlt && e.Key != Key.RightAlt) SetManualScale();
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e) {
            PointerPoint pt = e.GetCurrentPoint(_plot);
            _downPt = pt.Position;
            _dragged = false;
            _leftDown = pt.Properties.IsLeftButtonPressed;
            if (_leftDown && e.ClickCount >= 2) {
                // 더블클릭: 대기 중인 한 번 클릭을 취소하고 자동 스케일 복귀
                _clickTimer.Stop();
                _leftDown = false;
                _attached?.ResetScale();
            }
            else if (pt.Properties.IsMiddleButtonPressed) {
                _attached?.ResetScale();
            }
        }

        private void OnMoved(object? sender, PointerEventArgs e) {
            PointerPoint pt = e.GetCurrentPoint(_plot);
            bool anyDown = pt.Properties.IsLeftButtonPressed || pt.Properties.IsRightButtonPressed || pt.Properties.IsMiddleButtonPressed;
            if (anyDown && (Math.Abs(pt.Position.X - _downPt.X) > 4 || Math.Abs(pt.Position.Y - _downPt.Y) > 4)) {
                // 왼쪽 드래그(이동), 오른쪽 드래그(축 확대), 가운데 드래그(영역 확대) → 수동 스케일
                _dragged = true;
                SetManualScale();
            }
            Coordinates c = _plot.Plot.GetCoordinates(new Pixel((float)pt.Position.X, (float)pt.Position.Y));
            AxisLimits lim = _plot.Plot.Axes.GetLimits();
            bool auto = _attached == null || _attached.AutoScaleX || _attached.AutoScaleY;
            _coord.Text = "x=" + Fmt(c.X, lim.Right - lim.Left) + "  y=" + Fmt(c.Y, lim.Top - lim.Bottom)
                          + (auto ? "" : "   (수동 스케일 · 더블클릭 = 자동)");
            _plot.Refresh();
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e) {
            bool wasLeft = _leftDown;
            _leftDown = false;
            if (!wasLeft || _dragged || e.InitialPressMouseButton != MouseButton.Left) return;
            Point p = e.GetPosition(_plot);
            Coordinates c = _plot.Plot.GetCoordinates(new Pixel((float)p.X, (float)p.Y));
            AxisLimits lim = _plot.Plot.Axes.GetLimits();
            if (c.X < lim.Left || c.X > lim.Right || c.Y < lim.Bottom || c.Y > lim.Top) return;   // 데이터 영역 밖
            if (ClickCommand == null) return;
            _pendingClick = new PlotPoint(c.X, c.Y);
            _clickTimer.Stop();
            // 한 번 클릭 판정은 OS 더블클릭 시간이 지난 뒤 (더블클릭 = 자동 스케일이 락 포인트 선택으로 오인되지 않도록)
            TimeSpan dbl = Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500);
            _clickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, dbl.TotalMilliseconds + 20));
            _clickTimer.Start();
        }

        private static string Fmt(double v, double range) {
            double r = Math.Abs(range) / 100;
            string f = r >= 100 ? "0" : r >= 10 ? "0.#" : r >= 1 ? "0.##" : r >= 0.1 ? "0.###" : r >= 0.01 ? "0.####" : "0.#####E+0";
            return v.ToString(f, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // 저장
        // ------------------------------------------------------------------

        private async void SavePng() {
            try {
                IStorageFile? f = await PickSaveFile("그래프 PNG로 저장", "plot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png", "PNG 이미지", "*.png");
                if (f == null) return;
                string? path = f.TryGetLocalPath();
                if (path == null) return;
                int w = Math.Max(400, (int)_plot.Bounds.Width), h = Math.Max(300, (int)_plot.Bounds.Height);
                _plot.Plot.SavePng(path, w, h);
                _plot.Refresh();   // 저장용 렌더 후 화면 좌표계를 되돌림
            }
            catch (Exception ex) {
                if (_attached != null) {
                    _attached.Overlay = "PNG 저장 실패: " + ex.Message;
                    Render();
                }
            }
        }

        private async void SaveCsv() {
            PlotModel? m = _attached;
            if (m == null) return;
            try {
                IStorageFile? f = await PickSaveFile("데이터 CSV로 저장", "plot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv", "CSV 파일", "*.csv");
                string? path = f?.TryGetLocalPath();
                if (path != null) m.SaveCsv(path);
            }
            catch (Exception ex) {
                m.Overlay = "CSV 저장 실패: " + ex.Message;
                Render();
            }
        }

        private async System.Threading.Tasks.Task<IStorageFile?> PickSaveFile(string title, string name, string typeName, string pattern) {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null) return null;
            return await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = title,
                SuggestedFileName = name,
                FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { pattern } } },
            });
        }
    }
}
