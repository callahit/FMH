using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data.Editing;
using Fmh.Editor.Services;

namespace Fmh.Editor.ViewModels;

/// <summary>Eine Szene (oder seltene Szene) im Editor; Felder schreiben sofort in rules/scenes.json.</summary>
public sealed partial class SceneItem : ObservableObject
{
    private readonly ScenesSectionViewModel _owner;

    public SceneItem(ScenesSectionViewModel owner, bool rare, int index)
    {
        (_owner, Rare, Index) = (owner, rare, index);
        LoadSteps();
    }

    public bool Rare { get; }
    public int Index { get; }
    private JsonPath Path => JsonPath.Root.Then(Rare ? "rare" : "scenes").Then(Index);
    public JsonObject Node => Path.Resolve(_owner.Root) as JsonObject ?? new JsonObject();
    private JsonObject? When => Node["when"] as JsonObject;

    public IReadOnlyList<string> Languages => _owner.Languages;
    public ObservableCollection<SceneStepRow> Steps { get; } = new();

    /// <summary>Listeneintrag: Kennung, dazu die Ereignisse, wenn sie nicht schon in der Kennung stehen.</summary>
    public string Label => Rare ? "✦ " + Id : Id.StartsWith(On, StringComparison.Ordinal) ? Id : Id + "  ·  " + On;
    public bool Problem => Steps.Any(s => s.Problem);

    public string Id { get => Node["id"]?.ToString() ?? ""; set => Set("id", Text(value)); }

    /// <summary>Ereignisschlüssel, durch Komma getrennt (nur normale Szenen).</summary>
    public string On
    {
        get => Node["on"] is JsonArray a ? string.Join(", ", a.Select(n => n?.ToString())) : "";
        set => Set("on", new JsonArray(Split(value).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()));
    }

    /// <summary>Gewicht (Szene) bzw. Wahrscheinlichkeit je Spiel (seltene Szene).</summary>
    public string Weight
    {
        get => Node[Rare ? "chance" : "weight"]?.ToString() ?? "";
        set => SetNumber(Rare ? "chance" : "weight", value);
    }

    public string After { get => List("after"); set => SetWhen("after", Split(value).Count == 0 ? null : new JsonArray(Split(value).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray())); }
    public string Weather { get => List("weather"); set => SetWhen("weather", Split(value).Count == 0 ? null : new JsonArray(Split(value).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray())); }
    public string MinMinute { get => WhenValue("minMinute"); set => SetWhenNumber("minMinute", value); }
    public string MaxMinute { get => WhenValue("maxMinute"); set => SetWhenNumber("maxMinute", value); }
    public string MinXg { get => WhenValue("minXg"); set => SetWhenNumber("minXg", value); }
    public string MaxXg { get => WhenValue("maxXg"); set => SetWhenNumber("maxXg", value); }
    /// <summary>Level, Leading oder Trailing; leer = egal.</summary>
    public string Score { get => WhenValue("score"); set => SetWhen("score", value.Trim().Length == 0 ? null : JsonValue.Create(value.Trim())); }
    /// <summary>ja, nein oder leer.</summary>
    public string Floodlit { get => Flag("floodlit"); set => SetFlag("floodlit", value); }
    public string Knockout { get => Flag("knockout"); set => SetFlag("knockout", value); }
    /// <summary>Attribute als „Finishing&gt;=15, Composure&lt;=8“ (handelnder Spieler).</summary>
    public string Attrs { get => AttrText("attrs"); set => SetWhen("attrs", ParseAttrs(value)); }
    public string KeeperAttrs { get => AttrText("keeperAttrs"); set => SetWhen("keeperAttrs", ParseAttrs(value)); }

    [RelayCommand]
    private void AddStep()
    {
        var key = "scene." + (Id.Length > 0 ? Id : "new") + ".line" + (Steps.Count + 1);
        var step = new JsonObject { ["text"] = key };
        if (Node["steps"] is JsonArray) _owner.Apply(new InsertItem(ScenesSectionViewModel.File, Path.Then("steps"), Steps.Count, step));
        else _owner.Apply(new SetValue(ScenesSectionViewModel.File, Path.Then("steps"), new JsonArray(step)));
        LoadSteps();
    }

    [RelayCommand]
    private void RemoveStep(SceneStepRow? row)
    {
        if (row is null) return;
        _owner.Apply(new RemoveItem(ScenesSectionViewModel.File, Path.Then("steps"), row.Index));
        LoadSteps();
    }

    internal void LoadSteps()
    {
        Steps.Clear();
        if (Node["steps"] is JsonArray arr)
            for (var i = 0; i < arr.Count; i++) Steps.Add(new SceneStepRow(_owner, this, Path.Then("steps").Then(i), i));
        OnPropertyChanged(string.Empty);
    }

    internal void StepChanged() => OnPropertyChanged(nameof(Problem));

    /// <summary>Werte neu lesen; Zeilen nur neu anlegen, wenn sich ihre Zahl geändert hat.</summary>
    internal void Reload()
    {
        var count = (Node["steps"] as JsonArray)?.Count ?? 0;
        if (count != Steps.Count) { LoadSteps(); return; }
        foreach (var s in Steps) s.Reload();
        OnPropertyChanged(string.Empty);
    }

    private void Set(string field, JsonNode? value)
    {
        _owner.Apply(new SetValue(ScenesSectionViewModel.File, Path.Then(field), value));
        OnPropertyChanged(string.Empty);
    }

    private void SetNumber(string field, string text)
    {
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
        Set(field, JsonValue.Create(v));
    }

    private string WhenValue(string field) => When?[field]?.ToString() ?? "";
    private string List(string field) => When?[field] is JsonArray a ? string.Join(", ", a.Select(n => n?.ToString())) : "";
    private string Flag(string field) => When?[field] is JsonValue v && v.TryGetValue<bool>(out var b) ? EditorState.T[b ? "editor.scenes.yes" : "editor.scenes.no"] : "";

    private void SetFlag(string field, string text)
    {
        var t = text.Trim().ToLowerInvariant();
        bool? value = t.Length == 0 ? null : t == EditorState.T["editor.scenes.yes"].ToLowerInvariant() || t is "ja" or "yes" or "true";
        SetWhen(field, value is { } b ? JsonValue.Create(b) : null);
    }

    private void SetWhenNumber(string field, string text)
    {
        if (text.Trim().Length == 0) { SetWhen(field, null); return; }
        if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
        SetWhen(field, field.EndsWith("Minute", StringComparison.Ordinal) ? JsonValue.Create((int)v) : JsonValue.Create(v));
    }

    /// <summary>Setzt oder entfernt ein Feld der Bedingung; legt „when“ bei Bedarf an.</summary>
    private void SetWhen(string field, JsonNode? value)
    {
        var file = ScenesSectionViewModel.File;
        if (When is null)
        {
            if (value is null) return;
            _owner.Apply(new SetValue(file, Path.Then("when"), new JsonObject { [field] = value }));
        }
        else if (value is null) _owner.Apply(new SetValue(file, Path.Then("when").Then(field), null, Remove: true));
        else _owner.Apply(new SetValue(file, Path.Then("when").Then(field), value));
        OnPropertyChanged(string.Empty);
    }

    private string AttrText(string field) => When?[field] is JsonArray a
        ? string.Join(", ", a.OfType<JsonObject>().SelectMany(o => new[]
            {
                o["min"] is { } min ? $"{o["attr"]}>={min}" : null,
                o["max"] is { } max ? $"{o["attr"]}<={max}" : null,
            }).Where(s => s is not null))
        : "";

    private static readonly Regex AttrRule = new(@"^\s*(\w+)\s*(>=|<=)\s*(\d+)\s*$");

    private static JsonNode? ParseAttrs(string text)
    {
        var list = new JsonArray();
        foreach (var part in Split(text))
            if (AttrRule.Match(part) is { Success: true } m)
                list.Add(new JsonObject { ["attr"] = m.Groups[1].Value, [m.Groups[2].Value == ">=" ? "min" : "max"] = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) });
        return list.Count == 0 ? null : list;
    }

    private static JsonNode? Text(string value) => JsonValue.Create(value.Trim());

    internal static List<string> Split(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

/// <summary>Eine Zeile der Szene: Textschlüssel, Wahrscheinlichkeit und die Varianten je Sprache (eine Variante je Zeile).</summary>
public sealed class SceneStepRow : ObservableObject
{
    private readonly ScenesSectionViewModel _owner;
    private readonly SceneItem _scene;
    private readonly JsonPath _path;

    public SceneStepRow(ScenesSectionViewModel owner, SceneItem scene, JsonPath path, int index)
    {
        (_owner, _scene, _path, Index) = (owner, scene, path, index);
        Texts = owner.Languages.Select(l => new SceneStepText(this, l)).ToList();
    }

    public int Index { get; }
    /// <summary>Ein Eingabefeld je Sprache (bleibt bestehen, damit der Fokus beim Tippen erhalten bleibt).</summary>
    public IReadOnlyList<SceneStepText> Texts { get; }
    private JsonObject Node => _path.Resolve(_owner.Root) as JsonObject ?? new JsonObject();

    public string Key
    {
        get => Node["text"]?.ToString() ?? "";
        set
        {
            if (value.Trim() == Key || value.Trim().Length == 0) return;
            _owner.Apply(new SetValue(ScenesSectionViewModel.File, _path.Then("text"), JsonValue.Create(value.Trim())));
            Changed();
        }
    }

    public string Chance
    {
        get => Node["chance"]?.ToString() ?? "1";
        set
        {
            if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 1) return;
            _owner.Apply(v == 1 ? new SetValue(ScenesSectionViewModel.File, _path.Then("chance"), null, Remove: true)
                : new SetValue(ScenesSectionViewModel.File, _path.Then("chance"), JsonValue.Create(v)));
            Changed();
        }
    }

    /// <summary>Varianten einer Sprache, eine je Zeile.</summary>
    public string this[string language]
    {
        get => string.Join("\n", _owner.Texts(language, Key).Select(n => n?.ToString()));
        set
        {
            if (value == this[language]) return;
            var lines = value.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).Select(l => (JsonNode?)JsonValue.Create(l)).ToArray();
            _owner.Apply(new SetValue($"lang/{language}.scenes.json", JsonPath.Root.Then(Key), new JsonArray(lines)));
            Changed();
        }
    }

    private static readonly Regex Placeholder = new(@"\{\w+\}");

    private string Placeholders(string language) =>
        string.Join(",", Placeholder.Matches(this[language]).Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal));

    public bool Problem => _owner.Languages.Any(l => this[l].Length == 0) || _owner.Languages.Select(Placeholders).Distinct().Count() > 1;

    public string ProblemText => _owner.Languages.Any(l => this[l].Length == 0) ? EditorState.T["editor.languages.missing"]
        : Problem ? EditorState.T["editor.languages.placeholders"] : "";

    internal void Reload()
    {
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Chance));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(ProblemText));
        foreach (var t in Texts) t.Reload();
    }

    private void Changed()
    {
        Reload();
        _scene.StepChanged();
    }
}

/// <summary>Varianten einer Sprache für eine Zeile (eine Variante je Textzeile).</summary>
public sealed class SceneStepText : ObservableObject
{
    private readonly SceneStepRow _row;

    public SceneStepText(SceneStepRow row, string language) => (_row, Language) = (row, language);

    public string Language { get; }
    public string Text { get => _row[Language]; set => _row[Language] = value; }

    internal void Reload() => OnPropertyChanged(nameof(Text));
}
