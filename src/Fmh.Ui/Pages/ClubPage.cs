using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Ui.Rendering;
using Fmh.Ui.Views;

namespace Fmh.Ui.Pages;

/// <summary>Verein: Stammdaten, Stadion, Kaderwerte und Historie (Meister aller Ligen).</summary>
public sealed class ClubPage : Page
{
    public ClubPage(GameView shell) : base(shell) { }

    public override string TitleKey => "menu.club";

    public override void Render(Canvas c, Rect area)
    {
        var club = S.HumanClub;
        var world = S.World;
        var manager = S.HumanManager;
        var squad = world.SquadOf(club).ToList();
        Title(c, area, club.Name);

        var y = area.Y + 2;
        void Line(string label, string value)
        {
            c.WriteField(area.X, y, 20, label, Th.Dim, Th.Bg);
            c.Write(area.X + 21, y, value, Th.Fg, Th.Bg, area.W - 21);
            y++;
        }
        Line(T["club.city"], club.City);
        Line(T["club.league"], world.LeagueOf(club).Name);
        Line(T["club.stadium"], $"{club.StadiumName} ({Fmt.Int(club.StadiumCapacity)} {T["unit.seats"]})");
        Line(T["club.reputation"], $"{club.Reputation}/20");
        Line(T["club.colors"], $"{T["color." + club.PrimaryColor]} / {T["color." + club.SecondaryColor]}");
        Line(T["club.manager"], $"{manager.Name} ({T["club.managerReputation"]} {manager.Reputation}/20)");
        Line(T["club.squadSize"], squad.Count.ToString());
        Line(T["club.averageAge"], Fmt.Num(squad.Average(p => p.Age(world.Date)), "0.0"));
        Line(T["club.teamStrength"], Display.Strength(Ratings.StrengthFromRating(AiTactics.TeamStrength(club, world, App.Data))));
        var wages = squad.Sum(p => (long)(p.Contract?.WeeklyWage ?? 0));
        Line(T["club.wageBill"], Display.Wage((int)Math.Min(int.MaxValue, wages), manager.Profile, world.Country(club.CountryCode).Currency, T));

        y++;
        c.Write(area.X, y++, T["club.history"], Th.Accent, Th.Bg);
        if (world.History.Count == 0)
        {
            c.Write(area.X, y, T["club.noHistory"], Th.Dim, Th.Bg);
            return;
        }
        foreach (var rec in world.History.OrderByDescending(h => h.SeasonYear).ThenBy(h => h.LeagueId))
        {
            if (y >= area.Bottom) break;
            var pos = rec.FinalOrder.IndexOf(club.Id);
            var own = pos >= 0 ? T.Format("club.ownPlace", pos + 1) : "";
            c.Write(area.X, y++, $"{GameSession.SeasonLabel(rec.SeasonYear)}  {rec.LeagueName,-8} {T["club.champion"]}: {rec.ChampionName,-26} {own}",
                rec.ChampionId == club.Id ? Th.Accent : Th.Fg, Th.Bg, area.W);
        }
    }
}
