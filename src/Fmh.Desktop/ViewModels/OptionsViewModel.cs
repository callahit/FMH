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
        var currencies = Data.Currencies.Rates.Select(r => r.Code).ToList();
        Settings = new List<ChoiceSetting>
        {
            new(T["options.wage"], Names<WageDisplay>("wage."), Idx(p.WageDisplay), i => p.WageDisplay = At<WageDisplay>(i)),
            new(T["options.currency"], currencies.Select(c => T.TryGet("currency." + c) ?? c).ToList(), Math.Max(0, currencies.IndexOf(p.DisplayCurrency)), i =>
            {
                p.DisplayCurrency = currencies[i];
                State.ApplyCurrency();
                OnPropertyChanged(nameof(Rate));
                OnPropertyChanged(nameof(RateEditable));
                OnPropertyChanged(nameof(RateLabel));
            }),
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
        // Bedienung (AP 12b): geräteweit, nicht im Spielstand.
        var scales = Data.Ui.FontScales;
        Access = new List<ChoiceSetting>
        {
            new(T["options.fontScale"], scales.Select(f => $"{f * 100:0} %").ToList(), Math.Max(0, scales.IndexOf(State.FontScale)), i =>
                MainWindowViewModel.Instance?.SetScale(scales[i])),
            new(T["options.tutorial"], yesNo, State.Settings.Tutorial ? 1 : 0, i =>
            {
                State.Settings.Tutorial = i == 1;
                State.SaveSettings();
                _shell.RefreshTutorial();
            }),
        };
        // Anstoß-Elemente (AP 13): je Manager im Spielstand, auch im laufenden Spiel schaltbar.
        Anstoss = Data.Anstoss.Elements.Count == 0 ? new List<ChoiceSetting>() : new List<ChoiceSetting>
        {
            new(T["options.anstoss"], yesNo, Session.AnstossOn ? 1 : 0, i => Session.SetAnstoss(i == 1)),
        };
        foreach (var e in Data.Anstoss.Elements)
            Anstoss.Add(new ChoiceSetting("  " + T["anstoss." + e.Id + ".name"], yesNo, Session.AnstossElementOn(e.Id) ? 1 : 0,
                i => Session.SetAnstossElement(e.Id, i == 1)));
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

    /// <summary>Kurs der Anzeigewährung (Einheiten je Euro); der Euro selbst ist die Leitwährung und nicht änderbar.</summary>
    public decimal Rate
    {
        get => (decimal)State.RateOf(Session.HumanManager.Profile.DisplayCurrency);
        set
        {
            var p = Session.HumanManager.Profile;
            if (p.DisplayCurrency == Fmh.Core.Data.CurrencyConfig.Base || value <= 0) return;
            p.ExchangeRates[p.DisplayCurrency] = (double)value;
            State.ApplyCurrency();
            OnPropertyChanged();
        }
    }

    public bool RateEditable => Session.HumanManager.Profile.DisplayCurrency != Fmh.Core.Data.CurrencyConfig.Base;

    public string RateLabel => T.Format("options.rate", Session.HumanManager.Profile.DisplayCurrency, Data.Currencies.AsOf);

    /// <summary>Eigenen Kurs verwerfen und den Standardkurs aus den Daten nutzen.</summary>
    [RelayCommand]
    private void ResetRate()
    {
        Session.HumanManager.Profile.ExchangeRates.Remove(Session.HumanManager.Profile.DisplayCurrency);
        State.ApplyCurrency();
        OnPropertyChanged(nameof(Rate));
    }
    public IReadOnlyList<ChoiceSetting> Settings { get; }
    public IReadOnlyList<ChoiceSetting> Access { get; }
    public List<ChoiceSetting> Anstoss { get; }
    public bool HasAnstoss => Anstoss.Count > 0;

    [RelayCommand]
    private void KeyBindings() => MainWindowViewModel.Instance?.ShowDialog(new KeyBindingsViewModel(_shell));

    /// <summary>Alle Tutorial-Hinweise erneut zeigen.</summary>
    [RelayCommand]
    private void ResetTutorial()
    {
        State.Settings.TutorialSeen.Clear();
        State.Settings.Tutorial = true;
        State.SaveSettings();
        _shell.RefreshTutorial();
        Access[1].SelectedIndex = 1;
    }
    public IReadOnlyList<InfoLine> World { get; }

    [RelayCommand]
    private void Close()
    {
        MainWindowViewModel.Instance?.CloseDialog();
        // Seite neu aufbauen, damit Gehalts- und Attributanzeige greifen.
        if (_shell.Nav.FirstOrDefault(n => n.IsActive) is { } active) _shell.Open(active);
    }
}
