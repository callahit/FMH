using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Model;

namespace Fmh.Desktop.ViewModels;

/// <summary>Urlaub (AP 11): Dauer wählen und festlegen, was Co-Trainer und Sportdirektor übernehmen.</summary>
public sealed partial class HolidayDialogViewModel : ViewModelBase
{
    private static readonly int[] Weeks = { 1, 2, 4, 8 };
    private readonly ShellViewModel _shell;

    public HolidayDialogViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Durations = Weeks.Select(w => w == 1 ? T["holiday.week"] : T.Format("holiday.weeks", w)).Append(T["holiday.seasonEnd"]).ToList();
    }

    public IReadOnlyList<string> Durations { get; }
    [ObservableProperty] private int _durationIndex = 1;
    [ObservableProperty] private bool _lineup = true;
    [ObservableProperty] private bool _training = true;
    [ObservableProperty] private bool _transfers;

    /// <summary>Letzter Tag des Urlaubs (Rückkehr am Morgen dieses Tages).</summary>
    public DateOnly Until
    {
        get
        {
            var s = Session;
            if (DurationIndex < Weeks.Length) return s.World.Date.AddDays(Weeks[DurationIndex] * 7);
            var last = s.World.AllFixtures.Where(f => f.Involves(s.HumanClub.Id) && f.Kind != CompetitionKind.Friendly).Select(f => f.Date).DefaultIfEmpty(s.World.Date).Max();
            return last.AddDays(1);
        }
    }

    [RelayCommand]
    private void Start()
    {
        MainWindowViewModel.Instance?.CloseDialog();
        var report = Session.TakeHoliday(Until, new HolidayOptions { Lineup = Lineup, Training = Training, Transfers = Transfers });
        var lines = new List<string> { T.Format("holiday.summary", report.Matches.Count, report.Won, report.Drawn, report.Lost, report.Decisions) };
        if (report.StoppedKey is { } stopped) lines.Insert(0, T[stopped]);
        var w = Session.World;
        lines.AddRange(report.Matches.Where(f => f.Result is not null).Select(f =>
            $"{Fmt.ShortDate(f.Date)}  {w.Clubs[f.HomeId].Name} {f.Result!.HomeGoals}:{f.Result.AwayGoals} {w.Clubs[f.AwayId].Name}"));
        _shell.AfterHoliday(new MessageDialogViewModel(T.Format("holiday.report", Fmt.Date(report.From), Fmt.Date(report.To)), lines));
    }

    [RelayCommand]
    private void Cancel() => MainWindowViewModel.Instance?.CloseDialog();
}
