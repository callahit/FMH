using Avalonia.Markup.Xaml;

namespace Fmh.Desktop.Services;

/// <summary>Text aus der Sprachdatei in XAML: <c>Text="{s:L menu.squad}"</c>.</summary>
public sealed class L : MarkupExtension
{
    public L() { }

    public L(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => AppState.Current.T[Key];
}
