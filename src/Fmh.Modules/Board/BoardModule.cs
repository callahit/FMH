using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Vorstand, Fans und Managerkarriere (Kapitel 10).</summary>
public sealed partial class BoardModule : GameModule, IBoardService
{
    public override string Id => "board";

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
            .Select(r => new BoardMember { Role = r, Name = $"{Rng.Pick(names.FirstNames)} {Rng.Pick(names.LastNames)}", Trait = Rng.Pick(Enum.GetValues<BoardTrait>()) })
            .ToList();
        b.Goal = ExpectedGoal(club);
        b.ManagerContractUntil = World.SeasonYear + 2;
        b.ManagerWeeklyWage = (int)(Math.Round(2_500 * ClubRules.Scale(club) / 100) * 100);
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
        var playoff = World.Country(club.CountryCode).Playoffs.Any(p => p.UpperDivision == club.LeagueId && p.AppliesTo(World.SeasonYear)) ? 1 : 0;
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

    public void OfferSeasonGoal()
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

    public void ChangeTrust(Club club, double delta)
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
    public void AfterHumanMatch(Fixture f)
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

        if (club.Fans.Mood < 25 && outcome < 0 && Rng.Chance(0.5))
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
    public void BoardWeekly()
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
                if (b.Trust < 60 || Finance.Equity(club) < 0 || HasTrait(club, BoardTrait.Thrifty) && b.Trust < 75)
                {
                    ChangeTrust(club, -1);
                    return TR("board.request.budget.no");
                }
                var extra = MarketValue.Round(Math.Max(club.Budget.TransferBudget * 0.15, 200_000 * ClubRules.Scale(club)));
                club.Budget.TransferBudget += extra;
                club.Budget.WageBudget = (long)(club.Budget.WageBudget * 1.05);
                return TR("board.request.budget.yes", TextArg.Money(extra, Currency));
            case "contract":
                if (b.Trust < 65) return TR("board.request.contract.no");
                b.ManagerContractUntil = Math.Max(b.ManagerContractUntil, World.SeasonYear) + 2;
                b.ManagerWeeklyWage = (int)(b.ManagerWeeklyWage * 1.15);
                return TR("board.request.contract.yes", b.ManagerContractUntil + 1);
            case "youth":
                if (b.Trust < 55 || club.Budget.Balance < 500_000 * ClubRules.Scale(club)) return TR("board.request.youth.no");
                var grant = MarketValue.Round(400_000 * ClubRules.Scale(club));
                Finance.Book(club, TransactionType.Other, grant, TR("tx.boardGrant"));
                return TR("board.request.youth.yes", TextArg.Money(grant, Currency));
        }
        return TR("board.request.tooSoon");
    }

    /// <summary>Saisonende: Bewertung des Saisonziels durch den Vorstand.</summary>
    public bool EvaluateSeason(Club club, int position)
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
        club.Fans.Members = (int)(club.Reputation * club.Reputation * 250 * Rng.Range(0.8, 1.2));
        club.Fans.FanClubs = Math.Max(1, club.Fans.Members / 2_500);
    }

    private void FansWeekly(Club club)
    {
        var f = club.Fans;
        double target = 55 + Facilities.Effect(club, "fans.mood") * 10 + (club.Finance.PriceSatisfaction - 1) * 40;
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
            f.Members = Math.Max(100, (int)(f.Members * (1 + (f.Mood - 50) / 2_000 + Facilities.Effect(club, "members") / 12)));
            f.FanClubs = Math.Max(1, f.Members / 2_500);
        }
    }

    /// <summary>Vor Topspielen zu Hause: Choreografie der Fans (Kapitel 10).</summary>
    public void ChoreoBeforeMatch(Fixture f)
    {
        var club = HumanClub;
        if (f.HomeId != club.Id || club.Fans.Mood < 65) return;
        var away = World.Clubs[f.AwayId];
        if (!Finance.IsTopMatch(club, away, f)) return;
        var variant = Rng.Next(1, 5);
        Notify(MessageType.Fans, "msg.sender.fans", TR("msg.choreo.subject", away.Name), TR("msg.choreo." + variant, club.Name, away.Name));
        foreach (var p in World.SquadOf(club)) p.State.Morale = (Morale)Math.Min(6, (int)p.State.Morale + (Rng.Chance(0.3) ? 1 : 0));
    }

    /// <summary>Publikumslieblinge der neuen Saison: die prägenden Spieler der letzten.</summary>
    public void UpdateFavourites(Club club)
    {
        club.Fans.Favourites = World.SquadOf(club)
            .OrderByDescending(p => p.History.Where(h => h.ClubId == club.Id).Sum(h => h.Appearances + h.Goals * 2) + p.Reputation)
            .Take(2).Select(p => p.Id).ToList();
    }

    /// <summary>Jahreshauptversammlung im November: Stimmung, kritische Fragen, Abstimmung über Großprojekte.</summary>
    public void AnnualMeeting()
    {
        var club = HumanClub;
        var mood = club.Fans.Mood;
        var questions = new List<TextRef>();
        if (club.Budget.Balance < 0) questions.Add(TR("agm.q.debt"));
        if (club.Finance.Prices.Standing > Finance.DivisionFinance(club).Standing * 1.2) questions.Add(TR("agm.q.prices"));
        if (club.Board.Trust < 40) questions.Add(TR("agm.q.manager", HumanManager.Name));
        if (club.Grounds.Projects.Any(p => p.State == ProjectState.Building)) questions.Add(TR("agm.q.construction"));
        if (questions.Count < 2) questions.Add(TR("agm.q.youth"));
        if (questions.Count < 2) questions.Add(TR("agm.q.sausage"));
        var big = club.Grounds.Projects.Where(p => p.State is ProjectState.AwaitingPermit or ProjectState.Building && p.Cost > World.Country(club.CountryCode).Finance.BoardApprovalAbove * 3).ToList();
        var vote = big.Count == 0 ? TR("agm.noVote") : mood >= 45 ? TR("agm.voteYes", Facilities.ProjectName(big[0])) : TR("agm.voteNo", Facilities.ProjectName(big[0]));
        if (big.Count > 0 && mood < 45)
        {
            // Mitglieder stoppen das Projekt: Geld zurück abzüglich Planungskosten.
            var p = big[0];
            p.State = ProjectState.Rejected;
            Finance.Book(club, TransactionType.Construction, (long)(p.Cost * 0.8), TR("tx.constructionRefund", Facilities.ProjectName(p)));
            if (p.BuildingId is { } bid) club.Grounds.Buildings.RemoveAll(b => b.Id == bid && b.Level == 0);
            foreach (var b in club.Grounds.Buildings.Where(b => b.Id == p.BuildingId)) b.UnderConstruction = false;
            foreach (var s in club.Stadium.Stands) s.Closed = 0;
            Facilities.InvalidateEffects(club);
        }
        ChangeTrust(club, (mood - 50) / 10);
        Notify(MessageType.Board, "msg.sender.board", TR("msg.agm.subject", World.SeasonYear),
            TR("msg.agm.body", club.Fans.Members, (int)mood, TextArg.Join("\n", questions.Select(q => (object?)q)), vote));
    }
}
