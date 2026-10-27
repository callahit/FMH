using Avalonia.Data.Converters;
using Avalonia.Media;

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
}
