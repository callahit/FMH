using Avalonia.Markup.Xaml;
using Fmh.Core.Data;

namespace Fmh.Editor.Services;

/// <summary>Datenverzeichnis und Texte des Editors (aus den Sprachdateien wie im Spiel).</summary>
public static class EditorState
{
    public static string DataRoot { get; private set; } = "";
    public static Texts T { get; private set; } = new(new(), "de");

    public static void Initialise(string dataRoot, string language)
    {
        DataRoot = dataRoot;
        var data = PackageLoader.Load(dataRoot, language);
        T = data.Texts;
    }

    /// <summary>Text mit Platzhaltern {0}, {1} …</summary>
    public static string F(string key, params object[] args) => string.Format(System.Globalization.CultureInfo.CurrentCulture, T[key], args);
}

/// <summary>Text aus der Sprachdatei in XAML: <c>Text="{s:L editor.save}"</c>.</summary>
public sealed class L : MarkupExtension
{
    public L() { }

    public L(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => EditorState.T[Key];
}
