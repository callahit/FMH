using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;

namespace Fmh.Editor.ViewModels;

/// <summary>Ergebnis der Prüfung wie beim Spielstart (Datenvalidator); Fehler in eigenen Dateien springen zum Feld.</summary>
public sealed class ValidationSectionViewModel : SectionViewModel
{
    private readonly Action<string, JsonPath?> _open;

    public ValidationSectionViewModel(PackageWorkspace ws, Action<string, JsonPath?>? open = null) : base(ws, "editor.section.validation") =>
        _open = open ?? ((_, _) => { });

    public ObservableCollection<string> Errors { get; } = new();
    public ObservableCollection<ValidationItem> Items { get; } = new();

    public void Run()
    {
        Errors.Clear();
        Items.Clear();
        foreach (var e in Workspace.Validate())
        {
            Errors.Add(e);
            var (file, path) = Locate(e);
            Items.Add(new ValidationItem(e, file, path, _open));
        }
        OnPropertyChanged(nameof(IsValid));
    }

    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Ort einer Schema-Meldung „&lt;paket&gt;/&lt;datei&gt;: &lt;pfad&gt;: Text“ im eigenen Paket; andere Meldungen haben keinen.
    /// </summary>
    public (string? file, JsonPath? path) Locate(string error)
    {
        var prefix = Workspace.Manifest.Id + "/";
        if (!error.StartsWith(prefix, StringComparison.Ordinal)) return (null, null);
        var rest = error[prefix.Length..];
        var colon = rest.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0) return (null, null);
        var file = rest[..colon];
        if (!file.EndsWith(".json", StringComparison.Ordinal)) return (null, null);
        var detail = rest[(colon + 2)..];
        var second = detail.IndexOf(": ", StringComparison.Ordinal);
        return (file, second > 0 ? ParsePath(detail[..second]) : null);
    }

    /// <summary>„levels[0].divisions[1].teams“ → Pfad; null, wenn der Text kein Pfad ist.</summary>
    public static JsonPath? ParsePath(string text)
    {
        var segments = new List<object>();
        var name = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '.') { if (name.Length > 0) segments.Add(name.ToString()); name.Clear(); continue; }
            if (c == '[')
            {
                if (name.Length > 0) segments.Add(name.ToString());
                name.Clear();
                var end = text.IndexOf(']', i);
                if (end < 0 || !int.TryParse(text[(i + 1)..end], out var index)) return null;
                segments.Add(index);
                i = end;
                continue;
            }
            if (char.IsWhiteSpace(c)) return null;
            name.Append(c);
        }
        if (name.Length > 0) segments.Add(name.ToString());
        return segments.Count == 0 ? null : new JsonPath(segments);
    }
}

/// <summary>Eine Meldung; mit Datei lässt sie sich im Baum öffnen.</summary>
public sealed partial class ValidationItem
{
    private readonly Action<string, JsonPath?> _open;

    public ValidationItem(string text, string? file, JsonPath? path, Action<string, JsonPath?> open) =>
        (Text, File, Path, _open) = (text, file, path, open);

    public string Text { get; }
    public string? File { get; }
    public JsonPath? Path { get; }
    public bool CanOpen => File is not null;

    [RelayCommand]
    private void Open()
    {
        if (File is not null) _open(File, Path);
    }
}
