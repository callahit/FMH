using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;

namespace Fmh.Desktop.ViewModels;

public sealed partial class ClubChoice : ObservableObject
{
    public required ClubDefinition Def { get; init; }
    public required string LeagueName { get; init; }
    public string Name => Def.Name;
    public string Initials => Crest.InitialsOf(Def.Name);
    public string Primary => Def.PrimaryColor;
    public string Secondary => Def.SecondaryColor;
    public double Reputation => Def.Reputation;
    public string Info => $"{LeagueName} · {Def.City}";

    [ObservableProperty]
    private bool _isSelected;
}

public sealed record OptionItem(string Label, int Value);

/// <summary>Neues Spiel (Kapitel 2): Manager, Werdegang, Verein, Weltregeln.</summary>
public sealed partial class NewGameViewModel : ViewModelBase
{
    private static readonly (string key, int reputation)[] BackgroundDefs =
    {
        ("background.hobby", 2), ("background.amateur", 4), ("background.pro", 8), ("background.international", 12),
    };
    private static readonly double[] InjuryLevels = { 0.5, 1.0, 1.6 };

    private readonly MainWindowViewModel _main;

    public NewGameViewModel(MainWindowViewModel main)
    {
        _main = main;
        var divisions = Data.Countries.SelectMany(c => c.Divisions).ToDictionary(d => d.Id);
        Clubs = Data.Clubs
            .OrderBy(c => c.Division).ThenByDescending(c => c.Reputation).ThenBy(c => c.Name)
            .Select(c => new ClubChoice { Def = c, LeagueName = divisions.TryGetValue(c.Division, out var d) ? d.Name : c.Division })
            .ToList();
        Backgrounds = BackgroundDefs.Select((b, i) => new OptionItem(T[b.key], i)).ToList();
        InjuryOptions = Enumerable.Range(0, 3).Select(i => new OptionItem(T[$"newGame.injuries.{i}"], i)).ToList();
        _selectedBackground = Backgrounds[1];
        _selectedInjury = InjuryOptions[1];
        _managerName = Environment.UserName is { Length: > 0 } u ? u : "Manager";
        _seed = ((ulong)DateTime.Now.Ticks % 100_000_000UL).ToString();
        SelectClub(Clubs.FirstOrDefault(c => c.Def.Key == "nordheide") ?? Clubs[0]);
    }

    public IReadOnlyList<ClubChoice> Clubs { get; }
    public IReadOnlyList<OptionItem> Backgrounds { get; }
    public IReadOnlyList<OptionItem> InjuryOptions { get; }

    [ObservableProperty] private string _managerName;
    [ObservableProperty] private string _seed;
    [ObservableProperty] private bool _ironman;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundInfo))]
    private OptionItem _selectedBackground;

    [ObservableProperty] private OptionItem _selectedInjury;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Expectation), nameof(StadiumInfo))]
    private ClubChoice? _selected;

    public string BackgroundInfo => T[BackgroundDefs[SelectedBackground.Value].key + ".info"];

    public string StadiumInfo => Selected is null ? "" : $"{Selected.Def.Stadium} · {Fmt.Int(Selected.Def.Capacity)} {T["unit.seats"]}";

    public string Expectation
    {
        get
        {
            if (Selected is null) return "";
            var division = Data.Countries.SelectMany(c => c.Divisions).FirstOrDefault(d => d.Id == Selected.Def.Division);
            var key = Selected.Def.Reputation switch
            {
                >= 17 => "expectation.title",
                >= 14 => "expectation.europe",
                >= 11 => "expectation.midtable",
                >= 9 when division?.Promotion > 0 => "expectation.promotion",
                _ => "expectation.survival",
            };
            return T.Format("newGame.expectation", T[key]);
        }
    }

    [RelayCommand]
    private void SelectClub(ClubChoice? club)
    {
        if (club is null) return;
        foreach (var c in Clubs) c.IsSelected = c == club;
        Selected = club;
    }

    [RelayCommand]
    private void Back() => _main.Navigate(new StartViewModel(_main));

    [RelayCommand]
    private void Start()
    {
        if (Selected is null) return;
        var name = string.IsNullOrWhiteSpace(ManagerName) ? "Manager" : ManagerName.Trim();
        var seed = ulong.TryParse(Seed, out var s) ? s : 1UL;
        var world = WorldGenerator.Create(Data, new NewGameOptions
        {
            Seed = seed,
            ManagerName = name,
            ClubKey = Selected.Def.Key,
            Rules = new WorldRules { InjuryFrequency = InjuryLevels[SelectedInjury.Value], Ironman = Ironman },
        });
        world.Managers[0].Reputation = BackgroundDefs[SelectedBackground.Value].reputation;
        State.Session = new GameSession(world, Data, State.Engine);
        _main.Navigate(new ShellViewModel(_main));
    }
}
