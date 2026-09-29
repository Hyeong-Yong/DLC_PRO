using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DLC_PRO.Models;

namespace DLC_PRO.Converters {
    /// <summary>LedState → 표시등 브러시.</summary>
    public sealed class LedBrushConverter : IValueConverter {
        public static readonly LedBrushConverter Instance = new LedBrushConverter();

        private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#2E3252"));
        private static readonly IBrush On = new SolidColorBrush(Color.Parse("#22C55E"));
        private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#F59E0B"));
        private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#EF4444"));
        private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#0EA5E9"));
        private static readonly IBrush Emission = new SolidColorBrush(Color.Parse("#FDE047"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return value is LedState s
                ? s switch {
                    LedState.On => On,
                    LedState.Warn => Warn,
                    LedState.Error => Error,
                    LedState.Info => Info,
                    LedState.Emission => Emission,
                    _ => Off,
                }
                : Off;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>LedState → 표시등 발광 효과 여부 (꺼짐이 아니면 true).</summary>
    public sealed class LedIsLitConverter : IValueConverter {
        public static readonly LedIsLitConverter Instance = new LedIsLitConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is LedState s && s != LedState.Off;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>LogLevel → 글자 브러시.</summary>
    public sealed class LogLevelBrushConverter : IValueConverter {
        public static readonly LogLevelBrushConverter Instance = new LogLevelBrushConverter();

        private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#AEB4D4"));
        private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#F59E0B"));
        private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#F87171"));
        private static readonly IBrush Traffic = new SolidColorBrush(Color.Parse("#6B7299"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return value is LogLevel l
                ? l switch {
                    LogLevel.Warn => Warn,
                    LogLevel.Error => Error,
                    LogLevel.Traffic => Traffic,
                    _ => Info,
                }
                : Info;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>LockEventKind → 글자 브러시 (락 = 녹색, 해제 = 빨강).</summary>
    public sealed class LockEventBrushConverter : IValueConverter {
        public static readonly LockEventBrushConverter Instance = new LockEventBrushConverter();

        private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#CFCFCF"));
        private static readonly IBrush Locked = new SolidColorBrush(Color.Parse("#4ADE80"));
        private static readonly IBrush Unlocked = new SolidColorBrush(Color.Parse("#F87171"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return value is LockEventKind k
                ? k switch {
                    LockEventKind.Locked => Locked,
                    LockEventKind.Unlocked => Unlocked,
                    _ => Neutral,
                }
                : Neutral;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
