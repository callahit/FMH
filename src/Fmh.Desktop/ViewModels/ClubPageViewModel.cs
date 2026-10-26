using Avalonia.Media;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record HistoryLine(string Season, string League, string Champion, string Own, IBrush Brush);

/// <summary>Verein: Stammdaten, Stadion, Kaderwerte und Historie.</summary>
public sealed class ClubPageViewModel : PageViewModel
{
    public ClubPageViewModel(ShellViewModel shell) : base(shell)
    {
        var club = Session.HumanClub;
        var world = Session.World;
        var manager = Session.HumanManager;
        var squad = world.SquadOf(club).ToList();
        Name = club.Name;
        Primary = club.PrimaryColor;
        Secondary = club.SecondaryColor;
        Initials = Crest.InitialsOf(club.Name);
        var wages = squad.Sum(p => (long)(p.Contract?.WeeklyWage ?? 0));
        Facts = new List<InfoLine>
        {
            new(T["club.city"], club.City),
            new(T["club.league"], UiText.Competition(club.LeagueId)),
            new(T["club.stadium"], $"{club.StadiumName} ({Fmt.Int(club.StadiumCapacity)} {T["unit.seats"]})"),
            new(T["club.reputation"], $"{club.Reputation}/20"),
            new(T["club.colors"], $"{T["color." + club.PrimaryColor]} / {T["color." + club.SecondaryColor]}"),
            new(T["club.manager"], $"{manager.Name} ({T["club.managerReputation"]} {manager.Reputation}/20)"),
            new(T["club.squadSize"], squad.Count.ToString()),
            new(T["club.averageAge"], Fmt.Num(squad.Average(p => p.Age(world.Date)), "0.0")),
            new(T["club.teamStrength"], UiText.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(club, world, Data)))),
            new(T["club.wageBill"], UiText.Wage((int)Math.Min(int.MaxValue, wages), manager.Profile, UiText.Currency(club))),
            new(T["transfer.balance"], Fmt.Money(club.Budget.Balance, UiText.Currency(club))),
            new(T["neg.transferBudget"], Fmt.Money(club.Budget.TransferBudget, UiText.Currency(club))),
            new(T["transfer.wageBudget"], UiText.Wage((int)Math.Min(int.MaxValue, club.Budget.WageBudget), manager.Profile, UiText.Currency(club))),
            new(T["club.coachQuality"], $"{club.CoachQuality}/20"),
            new(T["club.youthFacilities"], $"{club.YouthFacilities}/20"),
            new(T["club.youthRecruitment"], $"{club.YouthRecruitment}/20"),
            new(T["club.youthSquad"], club.YouthIds.Count.ToString()),
        };
        Reputation = club.Reputation;
        History = world.History.OrderByDescending(h => h.SeasonYear).ThenBy(h => h.LeagueId).Select(h =>
        {
            var pos = h.FinalOrder.IndexOf(club.Id);
            var isLeague = world.FindLeague(h.LeagueId) is not null;
            return new HistoryLine(GameSession.SeasonLabel(h.SeasonYear), T.TryGet("competition." + h.LeagueId) ?? h.LeagueName, h.ChampionName,
                pos >= 0 && isLeague ? T.Format("club.ownPlace", pos + 1) : "",
                new SolidColorBrush(h.ChampionId == club.Id ? P.Gold : P.Text));
        }).ToList();
    }

    public override string Title => Name;
    public string Name { get; }
    public string Primary { get; }
    public string Secondary { get; }
    public string Initials { get; }
    public double Reputation { get; }
    public IReadOnlyList<InfoLine> Facts { get; }
    public IReadOnlyList<HistoryLine> History { get; }
    public bool NoHistory => History.Count == 0;
}
