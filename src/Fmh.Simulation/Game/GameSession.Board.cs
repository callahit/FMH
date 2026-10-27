using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Vorstand, Fans und Managerkarriere (Kapitel 10).</summary>
public sealed partial class GameSession
{
    public static readonly string[] AchievementIds =
    {
        "firstWin", "firstSeason", "title", "promotion", "cupWin", "survival", "unbeatenHome", "bigWin", "giantKiller",
        "youthStar", "recordSigning", "profitSeason", "bigStadium", "builder", "sponsorKing", "fansLove", "pressDarling",
        "ultimatumSurvived", "journeyman", "proLicense", "decade", "topToBottom", "doubleWin", "cleanSheets", "comeback",
    };

    // ================================================================== Vorstand

    public void CreateBoard(Club club)
    {
        var names = Data.Names.TryGetValue(club.CountryCode, out var n) ? n : Data.Names.Values.First();
        var b = club.Board;
        b.Trust = 60;
        b.Members = new[] { BoardRole.President, BoardRole.Treasurer, BoardRole.SportsDirector, BoardRole.Member, BoardRole.Member }
            .Select(r => new BoardMember { Role = r, Name = $"{_rng.Pick(names.FirstNames)} {_rng.Pick(names.LastNames)}", Trait = _rng.Pick(Enum.GetValues<BoardTrait>()) })
            .ToList();
        b.Goal = ExpectedGoal(club);
        b.ManagerContractUntil = World.SeasonYear + 2;
        b.ManagerWeeklyWage = (int)(Math.Round(2_500 * Scale(club) / 100) * 100);
    }

    private bool HasTrait(Club club, BoardTrait t) => club.Board.Members.Any(m => m.Trait == t);

    /// <summary>Erwartung des Umfelds aus der Reputationsrangfolge in der Liga.</summary>
    public SeasonGoal ExpectedGoal(Club club)
    {
        if (World.FindLeague(club.LeagueId) is not { } league) return SeasonGoal.MidTable;
        var rank = league.ClubIds.Select(id => World.Clubs[id]).OrderByDescending(c => c.Reputation).ToList().FindIndex(c => c.Id == club.Id) + 1;
        var n = league.ClubIds.Count;
        if (league.Format.Promotion > 0)
            return rank <= 3 ? SeasonGoal.Promotion : rank <= n / 3 ? SeasonGoal.TopThird : rank <= n * 2 / 3 ? SeasonGoal.MidTable : SeasonGoal.Survival;
        return rank <= 2 ? SeasonGoal.Title : rank <= 5 ? SeasonGoal.Europe : rank <= n / 3 + 1 ? SeasonGoal.TopThird : rank <= n * 2 / 3 ? SeasonGoal.MidTable : SeasonGoal.Survival;
    }

    /// <summary>Tabellenplatz, der für das Saisonziel mindestens erreicht werden muss.</summary>
    public int GoalPosition(Club club, SeasonGoal goal)
    {
        var league = World.FindLeague(club.LeagueId);
        var n = league?.ClubIds.Count ?? 18;
        var relegation = league?.Format.Relegation ?? 2;
        var playoff = World.Country(club.CountryCode).Playoffs.Any(p => p.UpperDivision == club.LeagueId) ? 1 : 0;
        return goal switch
        {
            SeasonGoal.Title => 1,
            SeasonGoal.Europe => 4,
            SeasonGoal.TopThird => (int)Math.Ceiling(n / 3.0),
            SeasonGoal.MidTable => (int)Math.Ceiling(n * 0.6),
            SeasonGoal.Promotion => Math.Max(1, league?.Format.Promotion ?? 2),
            _ => n - relegation - playoff,
        };
    }

    /// <summary>Ziele, die der Vorstand im Juli anbietet: bescheiden, erwartet, ehrgeizig.</summary>
    public IReadOnlyList<SeasonGoal> GoalChoices(Club club)
    {
        var promotionLeague = World.FindLeague(club.LeagueId)?.Format.Promotion > 0;
        var ladder = promotionLeague
            ? new[] { SeasonGoal.Survival, SeasonGoal.MidTable, SeasonGoal.TopThird, SeasonGoal.Promotion }
            : new[] { SeasonGoal.Survival, SeasonGoal.MidTable, SeasonGoal.TopThird, SeasonGoal.Europe, SeasonGoal.Title };
        var i = Array.IndexOf(ladder, ExpectedGoal(club));
        if (i < 0) i = 1;
        return ladder.Skip(Math.Max(0, i - 1)).Take(i == 0 ? 2 : 3).ToList();
    }

    /// <summary>Saisonziel vereinbaren: ehrgeizige Ziele bringen mehr Budget und höhere Erwartungen.</summary>
    public void ChooseSeasonGoal(SeasonGoal goal)
    {
        var club = HumanClub;
        var expected = ExpectedGoal(club);
        club.Board.Goal = goal;
        club.Board.GoalAgreed = true;
        var factor = goal == expected ? 1.0 : (int)goal < (int)expected || goal == SeasonGoal.Promotion && expected != SeasonGoal.Promotion ? 1.12 : 0.9;
        club.Budget.TransferBudget = MarketValue.Round(club.Budget.TransferBudget * factor);
        club.Budget.WageBudget = (long)(club.Budget.WageBudget * (factor > 1 ? 1.05 : factor < 1 ? 0.97 : 1));
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.SeasonGoal && !m.Resolved)) m.Resolved = true;
    }

    private void OfferSeasonGoal()
    {
        var club = HumanClub;
        club.Board.Goal = ExpectedGoal(club);
        club.Board.GoalAgreed = false;
        Notify(MessageType.Board, "msg.sender.board", TR("msg.seasonGoal.subject"),
            TR("msg.seasonGoal.body", TR("goal." + club.Board.Goal), TextArg.Money(club.Budget.TransferBudget, Currency)),
            MessageAction.SeasonGoal, null, required: true);
    }

    private double DifficultyTrustFactor => HumanManager.Profile.Difficulty switch
    {
        Difficulty.Easy => 0.6,
        Difficulty.Hard => 1.3,
        Difficulty.Realistic => 1.6,
        _ => 1.0,
    };

    private void ChangeTrust(Club club, double delta)
    {
        if (delta < 0)
        {
            delta *= DifficultyTrustFactor;
            if (HasTrait(club, BoardTrait.Impatient)) delta *= 1.3;
            if (HasTrait(club, BoardTrait.Patient)) delta *= 0.7;
        }
        club.Board.Trust = Math.Clamp(club.Board.Trust + delta, 0, 100);
    }

    /// <summary>Nach jedem Spiel des Menschen: Vertrauen, Fans, Ultimatum, Karriere, Erfolge.</summary>
    private void AfterHumanMatch(Fixture f)
    {
        var club = HumanClub;
        var home = f.HomeId == club.Id;
        var r = f.Result!;
        var gf = home ? r.HomeGoals : r.AwayGoals;
        var ga = home ? r.AwayGoals : r.HomeGoals;
        if (r.PenaltiesHome is { } ph && r.PenaltiesAway is { } pa) { gf += home ? (ph > pa ? 1 : 0) : (pa > ph ? 1 : 0); ga += home ? (pa > ph ? 1 : 0) : (ph > pa ? 1 : 0); }
        var opp = World.Clubs[home ? f.AwayId : f.HomeId];
        var derby = club.Rivals.Contains(opp.Id);
        var outcome = Math.Sign(gf - ga);
        ChangeTrust(club, outcome switch { 1 => 1.2, 0 => 0.1, _ => -1.6 } * (derby ? 1.5 : 1));
        club.Fans.Mood = Math.Clamp(club.Fans.Mood + outcome switch { 1 => 3, 0 => 0, _ => -3 } * (derby ? 2 : 1), 0, 100);

        var station = HumanManager.Career.Stations.LastOrDefault();
        if (station is not null)
        {
            station.Matches++;
            if (outcome > 0) station.Wins++; else if (outcome == 0) station.Draws++; else station.Losses++;
        }
        if (outcome > 0) Achieve("firstWin");
        if (gf - ga >= 5) Achieve("bigWin");
        if (outcome > 0 && opp.Reputation >= club.Reputation + 6) Achieve("giantKiller");
        if (outcome > 0 && (home ? r.HalfTimeHome < r.HalfTimeAway : r.HalfTimeAway < r.HalfTimeHome)) Achieve("comeback");
        var lastFive = AllFixtures.Where(x => x.Involves(club.Id) && x.Result is not null).OrderByDescending(x => x.Date).Take(5).ToList();
        if (lastFive.Count == 5 && lastFive.All(x => (x.HomeId == club.Id ? x.Result!.AwayGoals : x.Result!.HomeGoals) == 0)) Achieve("cleanSheets");

        if (club.Fans.Mood < 25 && outcome < 0 && _rng.Chance(0.5))
        {
            ChangeTrust(club, -2);
            Notify(MessageType.Fans, "msg.sender.fans", TR("msg.protest.subject"), TR("msg.protest.body", club.Name, HumanManager.Name));
        }

        if (f.Kind == CompetitionKind.League && club.Board.Ultimatum is { } u)
        {
            u.MatchesPlayed++;
            u.Points += outcome switch { 1 => 3, 0 => 1, _ => 0 };
            if (u.Points >= u.PointsNeeded)
            {
                club.Board.Ultimatum = null;
                ChangeTrust(club, 12);
                Achieve("ultimatumSurvived");
                Notify(MessageType.Board, "msg.sender.board", TR("msg.ultimatumMet.subject"), TR("msg.ultimatumMet.body"));
            }
            else if (u.MatchesPlayed >= u.Matches)
            {
                SackManager(TR("career.exit.ultimatum"));
                return;
            }
        }
        CheckBoard(club);
    }

    private void CheckBoard(Club club)
    {
        var b = club.Board;
        var sackAt = HumanManager.Profile.Difficulty == Difficulty.Realistic ? 20 : 15;
        if (b.Trust < sackAt && World.FindLeague(club.LeagueId)?.CurrentMatchday >= 6)
        {
            SackManager(TR("career.exit.trust"));
            return;
        }
        if (b.Trust < 30 && b.Ultimatum is null && World.FindLeague(club.LeagueId)?.CurrentMatchday >= 6)
        {
            var needed = HumanManager.Profile.Difficulty == Difficulty.Easy ? 4 : 6;
            b.Ultimatum = new Ultimatum { Matches = 3, PointsNeeded = needed };
            Notify(MessageType.Board, "msg.sender.board", TR("msg.ultimatum.subject"), TR("msg.ultimatum.body", 3, needed), required: false);
        }
    }

    /// <summary>Wöchentlich: Tabellenplatz gegen Saisonziel, Finanzen, Jugend; Fanstimmung.</summary>
    private void BoardWeekly()
    {
        var club = HumanClub;
        if (World.FindLeague(club.LeagueId) is { } league && league.CurrentMatchday >= 5)
        {
            var pos = LeagueTable.Compute(league).FindIndex(r => r.ClubId == club.Id) + 1;
            var target = GoalPosition(club, club.Board.Goal);
            ChangeTrust(club, Math.Clamp((target - pos) * 0.15, -1.5, 1.0));
        }
        if (club.Budget.Balance < 0) ChangeTrust(club, HasTrait(club, BoardTrait.Thrifty) ? -1.0 : -0.5);
        if (HasTrait(club, BoardTrait.YouthFriendly) && World.SquadOf(club).Any(p => p.Age(World.Date) <= 20 && p.SeasonStats.Appearances >= 5))
            ChangeTrust(club, 0.3);
        CheckBoard(club);
        FansWeekly(club);
        CareerWeekly();
    }

    public static readonly string[] BoardRequests = { "budget", "contract", "youth" };

    /// <summary>Anfrage an den Vorstand. Rückgabe: Text der Antwort.</summary>
    public TextRef BoardRequest(string kind)
    {
        var club = HumanClub;
        var b = club.Board;
        if (b.LastRequests.TryGetValue(kind, out var last) && last.AddDays(60) > World.Date) return TR("board.request.tooSoon");
        b.LastRequests[kind] = World.Date;
        switch (kind)
        {
            case "budget":
                if (b.Trust < 60 || Equity(club) < 0 || HasTrait(club, BoardTrait.Thrifty) && b.Trust < 75)
                {
                    ChangeTrust(club, -1);
                    return TR("board.request.budget.no");
                }
                var extra = MarketValue.Round(Math.Max(club.Budget.TransferBudget * 0.15, 200_000 * Scale(club)));
                club.Budget.TransferBudget += extra;
                club.Budget.WageBudget = (long)(club.Budget.WageBudget * 1.05);
                return TR("board.request.budget.yes", TextArg.Money(extra, Currency));
            case "contract":
                if (b.Trust < 65) return TR("board.request.contract.no");
                b.ManagerContractUntil = Math.Max(b.ManagerContractUntil, World.SeasonYear) + 2;
                b.ManagerWeeklyWage = (int)(b.ManagerWeeklyWage * 1.15);
                return TR("board.request.contract.yes", b.ManagerContractUntil + 1);
            case "youth":
                if (b.Trust < 55 || club.Budget.Balance < 500_000 * Scale(club)) return TR("board.request.youth.no");
                var grant = MarketValue.Round(400_000 * Scale(club));
                Book(club, TransactionType.Other, grant, TR("tx.boardGrant"));
                return TR("board.request.youth.yes", TextArg.Money(grant, Currency));
        }
        return TR("board.request.tooSoon");
    }

    /// <summary>Saisonende: Bewertung des Saisonziels durch den Vorstand.</summary>
    private bool EvaluateSeason(Club club, int position)
    {
        var target = GoalPosition(club, club.Board.Goal);
        var reached = position <= target;
        ChangeTrust(club, reached ? 15 + (target - position) * 1.5 : -12 - (position - target) * 2);
        var m = HumanManager;
        m.Reputation = Math.Clamp(m.Reputation + (reached ? 1 : -1) + (position == 1 ? 1 : 0), 1, 20);
        HumanManager.Career.Media = Math.Clamp(HumanManager.Career.Media + (reached ? 5 : -5), 0, 100);
        Notify(MessageType.Board, "msg.sender.board", TR("msg.boardVerdict.subject"),
            TR(reached ? "msg.boardVerdict.reached" : "msg.boardVerdict.missed", TR("goal." + club.Board.Goal), position, (int)club.Board.Trust));
        if (club.Board.Trust < 15) SackManager(TR("career.exit.season"));
        return reached;
    }

    // ================================================================== Fans

    public void CreateFans(Club club)
    {
        club.Fans.Mood = 60;
        club.Fans.Members = (int)(club.Reputation * club.Reputation * 250 * _rng.Range(0.8, 1.2));
        club.Fans.FanClubs = Math.Max(1, club.Fans.Members / 2_500);
    }

    private void FansWeekly(Club club)
    {
        var f = club.Fans;
        double target = 55 + Effect(club, "fans.mood") * 10 + (club.Finance.PriceSatisfaction - 1) * 40;
        if (World.FindLeague(club.LeagueId) is { } league && league.CurrentMatchday >= 3)
        {
            var table = LeagueTable.Compute(league);
            var row = table.First(r => r.ClubId == club.Id);
            target += row.Form.Sum(c => c switch { 'S' => 4, 'N' => -4, _ => 0 });
            target += (GoalPosition(club, club.Board.Goal) - row.Position) * 1.5;
        }
        f.Mood = Math.Clamp(f.Mood + (target - f.Mood) * 0.2, 0, 100);
        if (f.Mood >= 90) Achieve("fansLove");
        if (World.Date.Day <= 7)
        {
            f.Members = Math.Max(100, (int)(f.Members * (1 + (f.Mood - 50) / 2_000 + Effect(club, "members") / 12)));
            f.FanClubs = Math.Max(1, f.Members / 2_500);
        }
    }

    /// <summary>Vor Topspielen zu Hause: Choreografie der Fans (Kapitel 10).</summary>
    private void ChoreoBeforeMatch(Fixture f)
    {
        var club = HumanClub;
        if (f.HomeId != club.Id || club.Fans.Mood < 65) return;
        var away = World.Clubs[f.AwayId];
        if (!IsTopMatch(club, away, f)) return;
        var variant = _rng.Next(1, 5);
        Notify(MessageType.Fans, "msg.sender.fans", TR("msg.choreo.subject", away.Name), TR("msg.choreo." + variant, club.Name, away.Name));
        foreach (var p in World.SquadOf(club)) p.State.Morale = (Morale)Math.Min(6, (int)p.State.Morale + (_rng.Chance(0.3) ? 1 : 0));
    }

    /// <summary>Publikumslieblinge der neuen Saison: die prägenden Spieler der letzten.</summary>
    private void UpdateFavourites(Club club)
    {
        club.Fans.Favourites = World.SquadOf(club)
            .OrderByDescending(p => p.History.Where(h => h.ClubId == club.Id).Sum(h => h.Appearances + h.Goals * 2) + p.Reputation)
            .Take(2).Select(p => p.Id).ToList();
    }

    /// <summary>Jahreshauptversammlung im November: Stimmung, kritische Fragen, Abstimmung über Großprojekte.</summary>
    private void AnnualMeeting()
    {
        var club = HumanClub;
        var mood = club.Fans.Mood;
        var questions = new List<TextRef>();
        if (club.Budget.Balance < 0) questions.Add(TR("agm.q.debt"));
        if (club.Finance.Prices.Standing > DivisionFinance(club).Standing * 1.2) questions.Add(TR("agm.q.prices"));
        if (club.Board.Trust < 40) questions.Add(TR("agm.q.manager", HumanManager.Name));
        if (club.Grounds.Projects.Any(p => p.State == ProjectState.Building)) questions.Add(TR("agm.q.construction"));
        if (questions.Count < 2) questions.Add(TR("agm.q.youth"));
        if (questions.Count < 2) questions.Add(TR("agm.q.sausage"));
        var big = club.Grounds.Projects.Where(p => p.State is ProjectState.AwaitingPermit or ProjectState.Building && p.Cost > World.Country(club.CountryCode).Finance.BoardApprovalAbove * 3).ToList();
        var vote = big.Count == 0 ? TR("agm.noVote") : mood >= 45 ? TR("agm.voteYes", ProjectName(big[0])) : TR("agm.voteNo", ProjectName(big[0]));
        if (big.Count > 0 && mood < 45)
        {
            // Mitglieder stoppen das Projekt: Geld zurück abzüglich Planungskosten.
            var p = big[0];
            p.State = ProjectState.Rejected;
            Book(club, TransactionType.Construction, (long)(p.Cost * 0.8), TR("tx.constructionRefund", ProjectName(p)));
            if (p.BuildingId is { } bid) club.Grounds.Buildings.RemoveAll(b => b.Id == bid && b.Level == 0);
            foreach (var b in club.Grounds.Buildings.Where(b => b.Id == p.BuildingId)) b.UnderConstruction = false;
            foreach (var s in club.Stadium.Stands) s.Closed = 0;
            InvalidateEffects(club);
        }
        ChangeTrust(club, (mood - 50) / 10);
        Notify(MessageType.Board, "msg.sender.board", TR("msg.agm.subject", World.SeasonYear),
            TR("msg.agm.body", club.Fans.Members, (int)mood, TextArg.Join("\n", questions.Select(q => (object?)q)), vote));
    }

    // ================================================================== Managerkarriere

    private void Achieve(string id)
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
        Book(HumanClub, TransactionType.Other, -MarketValue.Round(15_000 * (int)next), TR("tx.course", TR("license." + next)));
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
            RefreshStaffEffects(HumanClub);
        }
        c.Offers.RemoveAll(o => o.Expires < World.Date);
        if (HumanManager.Career.Media >= 90) Achieve("pressDarling");
        if (HumanClub.Stadium.Capacity >= 40_000) Achieve("bigStadium");
        if (HumanClub.Grounds.Buildings.Count(b => b.Level > 0) >= 25) Achieve("builder");
        if (AllSlots().All(s => DealFor(HumanClub, s.slot, s.index) is not null)) Achieve("sponsorKing");
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
        _rng.Shuffle(candidates);
        var offers = candidates.Take(afterSacking ? 3 : _rng.Next(1, 2)).ToList();
        if (afterSacking && offers.Count == 0)
            offers = World.Clubs.Values.Where(c => c.Id != current.Id && World.FindLeague(c.LeagueId) is not null).OrderBy(c => c.Reputation).Take(2).ToList();
        foreach (var club in offers)
        {
            var offer = new JobOffer { ClubId = club.Id, Expires = World.Date.AddDays(afterSacking ? 60 : 14), WeeklyWage = (int)(Math.Round(2_500 * Scale(club) / 100) * 100), Years = 2 };
            m.Career.Offers.Add(offer);
            Notify(MessageType.Career, "msg.sender.agent", TR("msg.jobOffer.subject", club.Name),
                TR("msg.jobOffer.body", club.Name, Comp(club.LeagueId), TR("goal." + ExpectedGoal(club)), TextArg.Money(offer.WeeklyWage, Currency)),
                MessageAction.JobOffer, club.Id, required: false);
        }
    }

    /// <summary>Entlassung: Station endet, Jobangebote kleinerer Vereine (Pflicht-Entscheidung).</summary>
    private void SackManager(TextRef reason)
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
        if (!World.Scouts.Any(s => s.ClubId == newClub.Id)) CreateScouts(World, newClub, Data, _rng, 2);
        RefreshStaffEffects(newClub);
        RefreshStaffEffects(oldClub);
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

    private void StartStation(Club club) =>
        HumanManager.Career.Stations.Add(new CareerStation { ClubId = club.Id, ClubName = club.Name, From = World.Date });

    /// <summary>Saisonende: Titel und Erfolge, Jobangebote nach guter Saison, Vertragsende des Managers.</summary>
    private void CareerSeasonEnd(Club club, int position, bool goalReached, bool promoted, bool champion)
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
