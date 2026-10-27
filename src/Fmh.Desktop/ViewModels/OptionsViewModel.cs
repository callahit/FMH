using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>Persönliche Einstellungen des Managerprofils (Kapitel 2); Weltregeln nur zur Ansicht.</summary>
public sealed partial class OptionsViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    public OptionsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        var p = Session.HumanManager.Profile;
        string[] Names<TEnum>(string prefix) where TEnum : struct, Enum => Enum.GetValues<TEnum>().Select(v => T[prefix + v]).ToArray();
        int Idx<TEnum>(TEnum v) where TEnum : struct, Enum => Array.IndexOf(Enum.GetValues<TEnum>(), v);
        TEnum At<TEnum>(int i) where TEnum : struct, Enum => Enum.GetValues<TEnum>()[i];
        var yesNo = new[] { T["ui.no"], T["ui.yes"] };
        var themes = Palette.All.Select(t => t.Name).ToList();
        var languages = State.Languages;
        Settings = new List<ChoiceSetting>
        {
            new(T["options.wage"], Names<WageDisplay>("wage."), Idx(p.WageDisplay), i => p.WageDisplay = At<WageDisplay>(i)),
            new(T["options.attributes"], Names<AttributeDisplay>("attrDisplay."), Idx(p.AttributeDisplay), i => p.AttributeDisplay = At<AttributeDisplay>(i)),
            new(T["options.matchDisplay"], Names<MatchDisplayMode>("matchDisplay."), Idx(p.MatchDisplay), i => p.MatchDisplay = At<MatchDisplayMode>(i)),
            new(T["options.matchSpeed"], Enumerable.Range(1, 5).Select(i => T["speed." + i]).ToList(), p.MatchSpeed - 1, i => p.MatchSpeed = i + 1),
            new(T["options.pauseGoals"], yesNo, p.PauseOnGoals ? 1 : 0, i => p.PauseOnGoals = i == 1),
            new(T["options.pauseRed"], yesNo, p.PauseOnRedCards ? 1 : 0, i => p.PauseOnRedCards = i == 1),
            new(T["options.language"], languages.Select(l => l.Name).ToList(), Math.Max(0, languages.ToList().FindIndex(l => l.Code == State.Language)), i =>
            {
                if (languages[i].Code == State.Language) return;
                State.SetLanguage(languages[i].Code);
                // Erst nach dem Auswahlereignis neu aufbauen, die Auswahlbox gehört zur alten Ansicht.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => MainWindowViewModel.Instance?.Reload());
            }),
            new(T["options.style"], Names<MediaStyle>("style."), Idx(p.Style), i => p.Style = At<MediaStyle>(i)),
            new(T["options.events"], Names<EventFrequency>("events."), Idx(p.Events), i => p.Events = At<EventFrequency>(i)),
            new(T["options.delegatePress"], yesNo, p.DelegatePress ? 1 : 0, i => p.DelegatePress = i == 1),
            new(T["options.theme"], themes.Select(t => T.TryGet("theme." + t) ?? t).ToList(), Math.Max(0, themes.IndexOf(p.Theme)), i =>
            {
                p.Theme = themes[i];
                Palette.Apply(p.Theme);
            }),
        };
        var rules = Session.World.Rules;
        World = new List<InfoLine>
        {
            new(T["options.difficulty"], T["difficulty." + p.Difficulty]),
            new(T["newGame.injuryRule"], $"{rules.InjuryFrequency:0.0}x"),
            new(T["newGame.ironman"], rules.Ironman ? T["ui.yes"] : T["ui.no"]),
            new(T["options.autosave"], rules.AutosaveSlots.ToString()),
        };
    }

    public string Title => T.Format("options.title", Session.HumanManager.Name);
    public IReadOnlyList<ChoiceSetting> Settings { get; }
    public IReadOnlyList<InfoLine> World { get; }

    [RelayCommand]
    private void Close()
    {
        MainWindowViewModel.Instance?.CloseDialog();
        // Seite neu aufbauen, damit Gehalts- und Attributanzeige greifen.
        if (_shell.Nav.FirstOrDefault(n => n.IsActive) is { } active) _shell.Open(active);
    }
}
