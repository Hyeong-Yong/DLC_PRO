using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DLC_PRO.Converters {
    /// <summary>
    /// 정수 값 == ConverterParameter → true (RadioButton 그룹을 정수 속성 하나에 바인딩).
    /// 되돌릴 때는 체크된 버튼의 값만 쓴다 (해제되는 버튼은 무시).
    /// </summary>
    public sealed class IntEqualsConverter : IValueConverter {
        public static readonly IntEqualsConverter Instance = new IntEqualsConverter();

        private static int? P(object? parameter) =>
            parameter != null && int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : null;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int v && P(parameter) is int p && v == p;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true && P(parameter) is int p ? p : BindingOperations.DoNothing;
    }
}
