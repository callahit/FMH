using Fmh.Core.Game;
using Fmh.Core.Model;
using Fmh.Core.Persistence;

namespace Fmh.Tests;

/// <summary>Nachwuchsleistungszentrum, Lizenzverfahren mit Stichtagen, FFP, Europa-Zulassung, Stadionprognose und Neubau.</summary>
public class AcademyLicenseTests
{
    private static void AdvanceTo(GameSession s, DateOnly date) => TestData.PlayUntil(s, _ => s.World.Date >= date, 20_000);

    private static DateOnly Spring(GameSession s, int month, int day) => new(s.World.SeasonYear + 1, month, day);

    [Fact]
    public void AcademyStartsWithThreeAgeGroupsFrom14()
    {
        var s = TestData.NewSession();
        var club = s.HumanClub;
        Assert.True(club.Academy.Initialized);
        Assert.Equal(s.ReferenceAcademyBudget(club), club.Academy.Budget);
        var youth = s.YouthOf(club);
        Assert.All(youth, p => Assert.InRange(p.Age(s.World.Date), 14, 18));
        foreach (var g in Enum.GetValues<YouthAgeGroup>()) Assert.NotEmpty(s.YouthGroup(club, g));
        Assert.True(s.YouthGroup(club, YouthAgeGroup.U15).Count > s.YouthGroup(club, YouthAgeGroup.U19).Count);
        Assert.InRange(club.Academy.Stars, 0, 3);
        foreach (var other in s.World.Leagues.SelectMany(l => l.ClubIds).Select(id => s.World.Clubs[id]))
            Assert.True(other.Academy.Budget > 0, other.Name);
    }

    [Fact]
    public void AcademyBudgetIsValidatedPaidAndDrivesQuality()
    {
        var s = TestData.NewSession(31);
        var club = s.HumanClub;
        Assert.Equal("academy.error.shares", s.SetAcademyBudget(1_000_000, 50, 50, 50));
        var rich = s.ReferenceAcademyBudget(club) * 3;
        Assert.Null(s.SetAcademyBudget(rich, 50, 30, 20));
        var before = (club.Academy.Coaching, club.Academy.Recruitment, club.Academy.Education);
        var target = s.AcademyTargets(club);
        Assert.True(target.coaching > before.Coaching);
        AdvanceTo(s, s.World.Date.AddMonths(4));
        Assert.True(club.Academy.Coaching > before.Coaching);
        Assert.True(club.Academy.Recruitment > before.Recruitment);
        Assert.True(club.Finance.Season.GetValueOrDefault(TransactionType.Youth) <= -rich / 12 * 3);

        Assert.Null(s.SetAcademyBudget(0, 50, 30, 20));
        var high = club.Academy.Coaching;
        AdvanceTo(s, s.World.Date.AddMonths(3));
        Assert.True(club.Academy.Coaching < high);
    }

    [Fact]
    public void YearlyIntakeBringsFourteenYearOldsIntoTheU15()
    {
        var s = TestData.NewSession(32);
        var club = s.HumanClub;
        var known = club.YouthIds.ToHashSet();
        AdvanceTo(s, Spring(s, 3, 16));
        var fresh = s.YouthOf(club).Where(p => !known.Contains(p.Id)).ToList();
        Assert.NotEmpty(fresh);
        Assert.Contains(fresh, p => p.Age(s.World.Date) == 14);
        Assert.Contains(s.Inbox, m => m.Action == MessageAction.YouthIntake);
    }

    [Fact]
    public void YouthCoachesAreAssignedToAgeGroups()
    {
        var s = TestData.NewSession(33);
        var club = s.HumanClub;
        club.Budget.WageBudget *= 3;
        foreach (var coach in s.StaffMarket.Where(x => x.Role == StaffRole.YouthCoach).Take(3).ToList()) s.HireStaff(coach);
        var coaches = s.StaffOf(club).Where(x => x.Role == StaffRole.YouthCoach).ToList();
        Assert.Equal(3, coaches.Count);
        Assert.All(Enum.GetValues<YouthAgeGroup>(), g => Assert.NotNull(s.GroupCoach(club, g)));
        var u15 = s.GroupCoach(club, YouthAgeGroup.U15)!;
        var u19 = s.GroupCoach(club, YouthAgeGroup.U19)!;
        Assert.Null(s.AssignYouthCoach(u15, YouthAgeGroup.U19));
        Assert.Same(u15, s.GroupCoach(club, YouthAgeGroup.U19));
        Assert.Same(u19, s.GroupCoach(club, YouthAgeGroup.U15));
    }

    [Fact]
    public void LicenseFollowsTheDeadlinesAndEuropeanPlacesAreAllocated()
    {
        var s = TestData.NewSession(34);
        var club = s.HumanClub;
        var league = s.World.LeagueOf(club);
        AdvanceTo(s, Spring(s, 3, 16));
        Assert.Equal(LicenseStage.Applied, club.License.Stage);
        Assert.Contains(s.Inbox, m => m.Action == MessageAction.License);

        // Ein KI-Verein ist überschuldet (kurz vor der Entscheidung, damit die Insolvenzregel nicht vorher greift).
        AdvanceTo(s, Spring(s, 4, 29));
        var broke = s.World.Clubs[league.ClubIds.First(id => id != club.Id)];
        broke.Budget.Balance = -100_000_000;
        AdvanceTo(s, Spring(s, 5, 1));
        Assert.Equal(LicenseStage.Decided, club.License.Stage);
        Assert.Equal(LicenseOutcome.Refused, broke.License.Outcome);
        Assert.True(broke.License.ForcedRelegation);
        Assert.False(broke.License.UefaLicense);

        // Ein Spitzenclub ist wegen Financial Fair Play ausgeschlossen.
        var table = Fmh.Core.Logic.LeagueTable.Compute(league);
        var top = s.World.Clubs[table.First(r => r.ClubId != broke.Id).ClubId];
        top.License.ExcludedUntilSeason = s.World.SeasonYear + 1;
        TestData.PlayUntil(s, r => r.Reason == StopReason.SeasonEnd || s.World.SeasonFinished, 20_000);
        AdvanceTo(s, Spring(s, 6, 1));
        Assert.Equal(LicenseStage.Final, club.License.Stage);
        var entries = s.EuropeanEntriesOf(s.World.SeasonYear + 1);
        Assert.Equal(7, entries.Count);
        Assert.DoesNotContain(entries, e => e.ClubId == top.Id || e.ClubId == broke.Id);
        Assert.Contains(entries, e => e.Replacement);
        Assert.Equal(4, entries.Count(e => e.CompetitionId == "UCL"));

        TestData.PlayUntil(s, r => r.Reason == StopReason.NewSeason || s.World.Date.Month == 7, 20_000);
        AdvanceTo(s, s.World.Date.AddDays(2));
        Assert.NotEqual(league.Id, broke.LeagueId);
        Assert.NotEmpty(club.Finance.Seasons);
        Assert.All(entries, e => Assert.True(s.World.Clubs[e.ClubId].Finance.Season.GetValueOrDefault(TransactionType.European) > 0));
    }

    [Fact]
    public void StadiumForecastOnlyPaysWhenDemandFillsTheGround()
    {
        var s = TestData.NewSession(35);
        var club = s.HumanClub;
        var cheap = club.Finance.Prices.Clone();
        cheap.Standing = 1;
        cheap.Seating = 1;
        s.SetTicketPrices(cheap);
        club.Fans.Mood = 100;
        var full = s.ForecastProject(club, ProjectKind.Standing, StandSide.North)!;
        Assert.True(full.ExtraSpectators > 500, $"Zuschauer {full.ExtraSpectators}");
        Assert.True(full.ExtraIncome > 0);
        Assert.True(full.ExtraUpkeep > 0);

        var dear = cheap.Clone();
        dear.Standing = 200;
        dear.Seating = 400;
        s.SetTicketPrices(dear);
        club.Fans.Mood = 10;
        var empty = s.ForecastProject(club, ProjectKind.Standing, StandSide.North)!;
        Assert.True(empty.ExtraSpectators < full.ExtraSpectators);
        Assert.True(empty.PaybackYears is null || empty.PaybackYears > full.PaybackYears);
    }

    [Fact]
    public void NewStadiumNeedsTheCityEventAndReplacesTheGround()
    {
        var s = TestData.NewSession(36);
        var club = s.HumanClub;
        club.Budget.Balance += 200_000_000;
        club.Board.Trust = 90;
        var design = new StadiumDesign { Capacity = 45_000, StandingPercent = 30, Boxes = 50, Roof = true, Name = "Testarena" };
        Assert.Equal("build.error.noSite", s.OrderNewStadium(design, Contractor.Premium, false));

        var e = s.TriggerRandomEvent("citySite")!;
        Assert.NotNull(e);
        Assert.Null(s.ResolveEvent(e, "accept"));
        var site = s.AvailableSite(club)!;
        Assert.NotNull(site);
        design.Capacity = Math.Min(design.Capacity, site.MaxCapacity);
        Assert.Equal("build.error.capacity", s.OrderNewStadium(new StadiumDesign { Capacity = site.MaxCapacity + 5000 }, Contractor.Premium, false));
        var impact = s.ForecastNewStadium(club, design);
        Assert.True(impact.Cost > 20_000_000);
        Assert.Null(s.OrderNewStadium(design, Contractor.Premium, false));
        Assert.True(site.Used);
        var project = club.Grounds.Projects.Single(p => p.Kind == ProjectKind.NewStadium);
        // Bauzeit abkürzen: Genehmigung erteilt, Fertigstellung morgen.
        project.State = ProjectState.Building;
        project.End = s.World.Date.AddDays(1);
        AdvanceTo(s, s.World.Date.AddDays(3));
        Assert.Equal(ProjectState.Done, project.State);
        Assert.InRange(club.Stadium.Capacity, design.Capacity - 30, design.Capacity);
        Assert.Equal("Testarena", club.StadiumName);
        Assert.Equal(4, club.Stadium.Floodlight);
    }

    [Fact]
    public void AcademyLicenseAndEuropeSurviveSaveAndLoad()
    {
        var s = TestData.NewSession(37);
        var club = s.HumanClub;
        s.SetAcademyBudget(1_250_000, 40, 40, 20);
        club.License.Conditions.Add("license.gap.equity");
        club.License.Ffp = FfpStatus.Warning;
        club.Finance.Seasons.Add(new SeasonFinance { SeasonYear = 2025, Revenue = 10, Expenses = -12, BreakEven = -2, SquadCostRatio = 0.6 });
        s.World.EuropeanEntries.Add(new EuropeanEntry { ClubId = club.Id, CompetitionId = "UEL", SeasonYear = s.World.SeasonYear, Stage = 1, Replacement = true });
        var e = s.TriggerRandomEvent("citySite")!;
        s.ResolveEvent(e, "accept");
        s.SyncRng();
        var path = Path.Combine(Path.GetTempPath(), "fmh-acad-" + Guid.NewGuid() + ".db");
        try
        {
            SqliteStore.Save(s.World, path);
            var w = SqliteStore.Load(path);
            var c = w.Clubs[club.Id];
            Assert.Equal(1_250_000, c.Academy.Budget);
            Assert.Equal(40, c.Academy.ShareRecruitment);
            Assert.Equal(club.Academy.Coaching, c.Academy.Coaching);
            Assert.Equal(club.Academy.Graduates, c.Academy.Graduates);
            Assert.Equal(new[] { "license.gap.equity" }, c.License.Conditions);
            Assert.Equal(FfpStatus.Warning, c.License.Ffp);
            Assert.Single(c.Finance.Seasons);
            Assert.NotNull(c.Grounds.Site);
            var entry = Assert.Single(w.EuropeanEntries);
            Assert.True(entry.Replacement);
            Assert.Equal(s.World.Staff.Where(x => x.AgeGroup is not null).Select(x => (x.Id, x.AgeGroup)), w.Staff.Where(x => x.AgeGroup is not null).Select(x => (x.Id, x.AgeGroup)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
