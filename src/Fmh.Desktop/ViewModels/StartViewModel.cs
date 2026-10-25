using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Persistence;
using Fmh.Desktop.Controls;

namespace Fmh.Desktop.ViewModels;

public sealed class SaveItem
{
    public required SaveInfo Info { get; init; }
    public string Title => Info.Valid ? $"{Info.ClubName}" : Info.Name;
    public string Subtitle => Info.Valid
        ? $"{Info.ManagerName} · {(Info.GameDate is { } d ? Fmt.Date(d) : "")} · {Info.Name}"
        : T["load.invalid"];
    public string Saved => Info.Modified.ToString("dd.MM.yyyy HH:mm");
    public string Primary { get; init; } = "Gray";
    public string Secondary { get; init; } = "White";
    public string Initials => Crest.InitialsOf(Info.ClubName);

    private static Fmh.Core.Data.Texts T => Services.AppState.Current.T;
}

/// <summary>Hauptmenü: Neues Spiel, Spielstände laden, Beenden.</summary>
public sealed partial class StartViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;

    public StartViewModel(MainWindowViewModel main)
    {
        _main = main;
        Saves = SaveGame.List(State.SaveDirectory)
            .Select(s =>
            {
                var def = Data.Clubs.FirstOrDefault(c => c.Key == s.ClubKey);
                return new SaveItem { Info = s, Primary = def?.PrimaryColor ?? "Gray", Secondary = def?.SecondaryColor ?? "White" };
            })
            .ToList();
    }

    public IReadOnlyList<SaveItem> Saves { get; }
    public bool HasSaves => Saves.Count > 0;
    public string Version => typeof(StartViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    [RelayCommand]
    private void NewGame() => _main.Navigate(new NewGameViewModel(_main));

    [RelayCommand]
    private void Load(SaveItem? item)
    {
        if (item is null) return;
        try
        {
            var world = SaveGame.Load(item.Info.Path);
            State.Session = new GameSession(world, Data, State.Engine);
            _main.Navigate(new ShellViewModel(_main));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or Microsoft.Data.Sqlite.SqliteException or FormatException)
        {
            _main.ShowDialog(new MessageDialogViewModel(T["load.error"], new[] { ex.Message }));
        }
    }

    [RelayCommand]
    private void Quit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)
            d.Shutdown();
    }

    public override bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (key == Key.N) { NewGame(); return true; }
        if (key == Key.Enter && Saves.Count > 0) { Load(Saves[0]); return true; }
        return false;
    }
}
