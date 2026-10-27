using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Vorstand und Karriere: Managerkarriere, Trainerlizenz und Jobangebote.</summary>
public sealed partial class BoardModule
{
    // ================================================================== Managerkarriere

    public void Achieve(string id)
    {
        var c = HumanManager.Career;
        if (c.Achievements.Contains(id)) return;
        c.Achievements.Add(id);
        Notify(MessageType.Career, "msg.sender.career", TR("msg.achievement.subject", TR("achievement." + id)), TR("msg.achievement.body", TR("achievement." + id), TR("achievement." + id + ".info")));
    }

    public static CoachingLicense StartLicense(int background) => background switch { 0 => CoachingLicense.C, 1 => CoachingLicense.B, 2 => CoachingLicense.A, _ => CoachingLicense.A };

    /// <summary>Für einen Verein nötige Lizenz: 1. Liga Fußballlehrer, 2. Liga A, sonst B.</summary>
    public CoachingLicense RequiredLicense(Club club) => (World.FindLeague(club.LeagueId)?.Level ?? 3) switch { 1 => CoachingLicense.A, 2 => CoachingLicense.B, _ => CoachingLicense.C };

    public static int CourseReputation(CoachingLicense next) => next switch { CoachingLicense.B => 3, CoachingLicense.A => 6, CoachingLicense.Pro => 10, _ => 0 };

    /// <summary>Lehrgang für die nächste Lizenz (nur in der Sommerpause, 4 Wochen).</summary>
    public string? StartCourse()
    {
        var c = HumanManager.Career;
        if (c.License == CoachingLicense.Pro) return "career.error.maxLicense";
        if (c.CourseUntil is not null) return "career.error.course";
        if (World.Date.Month is not (6 or 7)) return "career.error.summer";
        var next = c.License + 1;
        if (HumanManager.Reputation < CourseReputation(next)) return "career.error.reputation";
        c.CourseUntil = World.Date.AddDays(28);
        Finance.Book(HumanClub, TransactionType.Other, -MarketValue.Round(15_000 * (int)next), TR("tx.course", TR("license." + next)));
        return null;
    }

    private void CareerWeekly()
    {
        var c = HumanManager.Career;
        if (c.CourseUntil is { } until && World.Date >= until)
        {
            c.CourseUntil = null;
            c.License++;
            if (c.License == CoachingLicense.Pro) Achieve("proLicense");
            Notify(MessageType.Career, "msg.sender.career", TR("msg.license.passed.subject"), TR("msg.license.passed.body", TR("license." + c.License)));
            Staff.RefreshStaffEffects(HumanClub);
        }
        c.Offers.RemoveAll(o => o.Expires < World.Date);
        if (HumanManager.Career.Media >= 90) Achieve("pressDarling");
        if (HumanClub.Stadium.Capacity >= 40_000) Achieve("bigStadium");
        if (HumanClub.Grounds.Buildings.Count(b => b.Level > 0) >= 25) Achieve("builder");
        if (Sponsors.AllSlots().All(s => Sponsors.DealFor(HumanClub, s.slot, s.index) is not null)) Achieve("sponsorKing");
    }

    /// <summary>Jobangebote: bei Erfolg größere Vereine, nach einer Entlassung kleinere.</summary>
    private void MakeJobOffers(bool afterSacking)
    {
        var m = HumanManager;
        var current = HumanClub;
        var candidates = World.Clubs.Values
            .Where(c => c.Id != current.Id && World.FindLeague(c.LeagueId) is not null)
            .Where(c => afterSacking ? c.Reputation <= Math.Max(3, m.Reputation + 2) : c.Reputation > current.Reputation && c.Reputation <= m.Reputation + 3)
            .Where(c => RequiredLicense(c) <= m.Career.License || afterSacking && c.Reputation <= 7)
            .OrderByDescending(c => c.Reputation)
            .Take(8)
            .ToList();
        Rng.Shuffle(candidates);
        var offers = candidates.Take(afterSacking ? 3 : Rng.Next(1, 2)).ToList();
        if (afterSacking && offers.Count == 0)
            offers = World.Clubs.Values.Where(c => c.Id != current.Id && World.FindLeague(c.LeagueId) is not null).OrderBy(c => c.Reputation).Take(2).ToList();
        foreach (var club in offers)
        {
            var offer = new JobOffer { ClubId = club.Id, Expires = World.Date.AddDays(afterSacking ? 60 : 14), WeeklyWage = (int)(Math.Round(2_500 * ClubRules.Scale(club) / 100) * 100), Years = 2 };
            m.Career.Offers.Add(offer);
            Notify(MessageType.Career, "msg.sender.agent", TR("msg.jobOffer.subject", club.Name),
                TR("msg.jobOffer.body", club.Name, Comp(club.LeagueId), TR("goal." + ExpectedGoal(club)), TextArg.Money(offer.WeeklyWage, Currency)),
                MessageAction.JobOffer, club.Id, required: false);
        }
    }

    /// <summary>Entlassung: Station endet, Jobangebote kleinerer Vereine (Pflicht-Entscheidung).</summary>
    public void SackManager(TextRef reason)
    {
        var club = HumanClub;
        if (HumanManager.Career.Stations.LastOrDefault() is { } st && st.To is null) { st.To = World.Date; st.Exit = "sacked"; }
        club.Board.Ultimatum = null;
        // Ein Interimstrainer (KI) übernimmt bis zur Neubesetzung.
        club.ManagerId = null;
        Notify(MessageType.Board, "msg.sender.board", TR("msg.sacked.subject"), TR("msg.sacked.body", HumanManager.Name, club.Name, reason));
        HumanManager.Reputation = Math.Max(1, HumanManager.Reputation - 2);
        MakeJobOffers(afterSacking: true);
    }

    public bool AwaitingNewJob => HumanManager.Career.Stations.LastOrDefault() is { To: not null };

    /// <summary>Rücktritt: Station endet, Angebote anderer Vereine.</summary>
    public void Resign()
    {
        if (HumanManager.Career.Stations.LastOrDefault() is { } st && st.To is null) { st.To = World.Date; st.Exit = "resigned"; }
        HumanClub.ManagerId = null;
        Notify(MessageType.Career, "msg.sender.board", TR("msg.resigned.subject"), TR("msg.resigned.body", HumanClub.Name));
        MakeJobOffers(afterSacking: true);
    }

    /// <summary>Jobangebot annehmen: der Manager wechselt den Verein, der alte wird von der KI geführt.</summary>
    public string? AcceptJob(JobOffer offer)
    {
        var m = HumanManager;
        if (!m.Career.Offers.Contains(offer)) return "career.error.offer";
        var oldClub = HumanClub;
        var newClub = World.Clubs[offer.ClubId];
        if (m.Career.Stations.LastOrDefault() is { } st && st.To is null) { st.To = World.Date; st.Exit = "moved"; }
        oldClub.ManagerId = null;
        newClub.ManagerId = m.Id;
        m.ClubId = newClub.Id;
        m.Career.Offers.Clear();
        foreach (var msg in World.Messages.Where(x => x.Action == MessageAction.JobOffer && !x.Resolved)) msg.Resolved = true;
        StartStation(newClub);
        newClub.Board.Trust = 60;
        newClub.Board.ManagerWeeklyWage = offer.WeeklyWage;
        newClub.Board.ManagerContractUntil = World.SeasonYear + offer.Years;
        newClub.Board.Ultimatum = null;
        if (!World.Scouts.Any(s => s.ClubId == newClub.Id)) ScoutGenerator.CreateScouts(World, newClub, Data, Rng, 2);
        Staff.RefreshStaffEffects(newClub);
        Staff.RefreshStaffEffects(oldClub);
        LineupSelector.Repair(newClub, World, Data, BenchSize(newClub));
        if (m.Career.Stations.Select(s => s.ClubId).Distinct().Count() >= 3) Achieve("journeyman");
        Notify(MessageType.Board, "msg.sender.board", TR("msg.welcome.subject", newClub.Name),
            TR("msg.newJob.body", m.Name, newClub.Name, TR("goal." + ExpectedGoal(newClub))));
        return null;
    }

    public void DeclineJob(JobOffer offer)
    {
        HumanManager.Career.Offers.Remove(offer);
        foreach (var msg in World.Messages.Where(x => x.Action == MessageAction.JobOffer && x.ActionRefId == offer.ClubId)) msg.Resolved = true;
    }

    public void StartStation(Club club) =>
        HumanManager.Career.Stations.Add(new CareerStation { ClubId = club.Id, ClubName = club.Name, From = World.Date });

    /// <summary>Saisonende: Titel und Erfolge, Jobangebote nach guter Saison, Vertragsende des Managers.</summary>
    public void CareerSeasonEnd(Club club, int position, bool goalReached, bool promoted, bool champion)
    {
        var st = HumanManager.Career.Stations.LastOrDefault();
        // Titel als "Wettbewerbs-Id|Saison", damit die Anzeige den Namen in der gewählten Sprache zeigt.
        if (champion) { st?.Titles.Add($"{club.LeagueId}|{World.SeasonYear}"); Achieve("title"); }
        if (promoted) Achieve("promotion");
        if (goalReached && club.Board.Goal == SeasonGoal.Survival) Achieve("survival");
        if (HumanManager.Career.Stations.Sum(s => 1) > 0) Achieve("firstSeason");
        if (World.SeasonYear - HumanManager.Career.Stations.First().From.Year >= 10) Achieve("decade");
        var home = World.FindLeague(club.LeagueId)?.Fixtures.Where(f => f.HomeId == club.Id && f.Result is not null).ToList() ?? new();
        if (home.Count > 5 && home.All(f => f.Result!.HomeGoals >= f.Result.AwayGoals)) Achieve("unbeatenHome");
        var cupWon = World.Cups.Any(c => c.Kind == CompetitionKind.Cup && c.WinnerId == club.Id);
        if (cupWon) { st?.Titles.Add($"{World.Cups.First(c => c.Kind == CompetitionKind.Cup && c.WinnerId == club.Id).Id}|{World.SeasonYear}"); Achieve("cupWin"); }
        if (cupWon && champion) Achieve("doubleWin");
        if (club.Finance.Season.Values.Sum() > 0) Achieve("profitSeason");
        if (World.SquadOf(club).Any(p => p.History.Any(h => h.SeasonYear == World.SeasonYear && h.ClubId == club.Id && h.Appearances >= 20) && p.Age(World.Date) <= 21 && p.Contract is { Status: SquadStatus.Prospect })) Achieve("youthStar");
        if (champion && HumanManager.Career.Stations.Count > 0 && World.FindLeague(club.LeagueId)?.Level == 1
            && HumanManager.Career.Stations.Any(s => World.Clubs.TryGetValue(s.ClubId, out var c2) && c2.Reputation <= 7)) Achieve("topToBottom");
        if (goalReached && !AwaitingNewJob) MakeJobOffers(afterSacking: false);
        if (club.Board.ManagerContractUntil <= World.SeasonYear && !AwaitingNewJob)
        {
            if (club.Board.Trust >= 45)
            {
                club.Board.ManagerContractUntil = World.SeasonYear + 2;
                Notify(MessageType.Board, "msg.sender.board", TR("msg.contractRenewed.subject"), TR("msg.contractRenewed.body", World.SeasonYear + 3));
            }
            else SackManager(TR("career.exit.contract"));
        }
    }
}
