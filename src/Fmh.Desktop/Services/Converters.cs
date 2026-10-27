using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Fmh.Core;

namespace Fmh.Desktop.Services;

public static class Converters
{
    /// <summary>Rahmen für ausgewählte Karten.</summary>
    public static readonly IValueConverter SelectedBorder =
        new FuncValueConverter<bool, IBrush>(b => b ? new SolidColorBrush(Palette.Current.Accent) : Brushes.Transparent);

    public static readonly IValueConverter AttributeBrush =
        new FuncValueConverter<int, IBrush>(v => new SolidColorBrush(Palette.Current.AttributeColor(v)));

    public static readonly IValueConverter ColorToBrush =
        new FuncValueConverter<Color, IBrush>(c => new SolidColorBrush(c));

    /// <summary>Geldeingaben: zeigt Euro-Beträge in der Anzeigewährung und rechnet Eingaben zurück in Euro.</summary>
    public static readonly IValueConverter Money = new MoneyConverter();

    private sealed class MoneyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is null ? null : (decimal)Math.Round(Fmt.ToDisplay(System.Convert.ToDouble(value, CultureInfo.InvariantCulture)));

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null) return null;
            var euro = Math.Round(Fmt.FromDisplay(System.Convert.ToDouble(value, CultureInfo.InvariantCulture)));
            var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return System.Convert.ChangeType(euro, type, CultureInfo.InvariantCulture);
        }
    }
}
