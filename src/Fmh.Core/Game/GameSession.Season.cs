using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Saisonablauf (Kapitel 3): Saisonende, Auf- und Abstieg, Vertragsende, Entwicklung, neue Saison.</summary>
public sealed partial class GameSession
{
    /// <summary>Saisonabschluss: Meister, Pokalsieger, Endtabellen, Bilanz für den Manager.</summary>
    private void FinishSeason()
    {
        World.SeasonFinished = true;
        foreach (var league in World.Leagues)
        {
            var table = LeagueTable.Compute(league);
            var champion = World.Clubs[table[0].ClubId];
            World.History.Add(new SeasonRecord
            {
                SeasonYear = World.SeasonYear,
                LeagueId = league.Id,
                LeagueName = league.Name,
                ChampionId = champion.Id,
                ChampionName = champion.Name,
                FinalOrder = table.Select(r => r.ClubId).ToList(),
            });
        }
        foreach (var cup in World.Cups.Where(c => c.Kind == CompetitionKind.Cup && c.WinnerId is not null))
        {
            var winner = World.Clubs[cup.WinnerId!.Value];
            World.History.Add(new SeasonRecord
            {
                SeasonYear = World.SeasonYear, LeagueId = cup.Id, LeagueName = cup.Name,
                ChampionId = winner.Id, ChampionName = winner.Name, FinalOrder = new List<int> { winner.Id },
            });
        }

        var own = HumanClub;
        var ownLeague = World.LeagueOf(own);
        var ownTable = LeagueTable.Compute(ownLeague);
        var row = ownTable.First(r => r.ClubId == own.Id);
        var champ = World.Clubs[ownTable[0].ClubId];
        var playoff = World.Cups.FirstOrDefault(c => c.Kind == CompetitionKind.Playoff && c.ParticipantIds.Contains(own.Id));
        var verdictKey = playoff is not null
            ? (playoff.WinnerId == own.Id ? (playoff.UpperDivision == own.LeagueId ? "msg.seasonEnd.stayedUp" : "msg.seasonEnd.promoted") : (playoff.UpperDivision == own.LeagueId ? "msg.seasonEnd.relegated" : "msg.seasonEnd.neutral"))
            : row.Position <= ownLeague.Format.Promotion ? "msg.seasonEnd.promoted"
            : row.Position > ownTable.Count - ownLeague.Format.Relegation ? "msg.seasonEnd.relegated"
            : row.Position == 1 ? "msg.seasonEnd.champion" : "msg.seasonEnd.neutral";
        Notify(MessageType.Season, "msg.sender.league",
            TR("msg.seasonEnd.subject", Comp(ownLeague.Id), SeasonLabel(World.SeasonYear)),
            TR("msg.seasonEnd.body", champ.Name, row.Position, row.Points, TR(verdictKey, own.Name)));
    }

    /// <summary>Zum Saisonstart: Auf- und Abstieg, Vertragsende, Entwicklung, Karriereenden, neue Wettbewerbe.</summary>
    private bool StartNewSeasonIfDue()
    {
        if (!World.SeasonFinished) return false;
        var startMonth = World.Countries[0].Season.StartMonth;
        if (World.Date != new DateOnly(World.SeasonYear + 1, startMonth, 1)) return false;

        PromoteAndRelegate();
        World.SeasonYear++;
        World.SeasonFinished = false;

        ExpireContractsAndLoans();
        foreach (var club in World.Clubs.Values) RenewSquad(club);
        RetireFreeAgents();

        foreach (var league in World.Leagues)
            ScheduleGenerator.CreateFixtures(World, league, World.Country(league.CountryCode), _rng);
        World.Cups.Clear();
        CreateCups();

        foreach (var club in World.Clubs.Values)
        {
            FillSquad(club);
            Budgets.SetSeasonBudget(club, World);
            if (!IsHuman(club)) club.Tactic.FormationId = AiTactics.BestFormation(club, World, Data).Id;
            LineupSelector.Repair(club, World, Data, BenchSize(club));
        }

        var b = HumanClub.Budget;
        var currency = World.Country(HumanClub.CountryCode).Currency;
        Notify(MessageType.Board, "msg.sender.board",
            TR("msg.newSeason.subject", SeasonLabel(World.SeasonYear)),
            TR("msg.newSeason.body", HumanClub.Name, Comp(HumanClub.LeagueId),
                TextArg.Money(b.TransferBudget, currency), TextArg.Money(b.WageBudget, currency)));
        return true;
    }

    private void PromoteAndRelegate()
    {
        foreach (var country in World.Countries)
        {
            var leagues = World.Leagues.Where(l => l.CountryCode == country.Code).OrderBy(l => l.Level).ToList();
            for (var i = 0; i < leagues.Count - 1; i++)
            {
                var upper = leagues[i];
                var lower = leagues[i + 1];
                var count = Math.Min(upper.Format.Relegation, lower.Format.Promotion);
                var down = count <= 0 ? new List<int>() : LeagueTable.Compute(upper).TakeLast(count).Select(r => r.ClubId).ToList();
                var up = count <= 0 ? new List<int>() : LeagueTable.Compute(lower).Take(count).Select(r => r.ClubId).ToList();
                // Relegation: Verlierer des Erstligisten steigt ab, Sieger des Zweitligisten auf.
                foreach (var po in World.Cups.Where(c => c.Kind == CompetitionKind.Playoff && c.UpperDivision == upper.Id && c.LowerDivision == lower.Id && c.WinnerId is not null))
                {
                    var upperClub = po.ParticipantIds.First(id => upper.ClubIds.Contains(id));
                    var lowerClub = po.ParticipantIds.First(id => lower.ClubIds.Contains(id));
                    if (po.WinnerId == lowerClub) { down.Add(upperClub); up.Add(lowerClub); }
                }
                foreach (var id in down) { upper.ClubIds.Remove(id); lower.ClubIds.Add(id); World.Clubs[id].LeagueId = lower.Id; ApplyRelegationClauses(id); }
                foreach (var id in up) { lower.ClubIds.Remove(id); upper.ClubIds.Add(id); World.Clubs[id].LeagueId = upper.Id; }
            }
        }
    }

    /// <summary>Abstiegsklausel: Gehalt −30 % nach Abstieg (Kapitel 7).</summary>
    private void ApplyRelegationClauses(int clubId)
    {
        foreach (var p in World.SquadOf(World.Clubs[clubId]))
            if (p.Contract is { RelegationClause: true } c)
            {
                c.WeeklyWage = (int)(c.WeeklyWage * 0.7);
                c.RelegationClause = false;
            }
    }

    /// <summary>Saisonwechsel je Verein: Entwicklung, Statistik, Karriereenden.</summary>
    private void RenewSquad(Club club)
    {
        var today = World.Date;
        var squad = World.SquadOf(club).ToList();
        foreach (var p in squad.Concat(club.YouthIds.Select(id => World.Players[id])))
        {
            Development.ApplySeason(p, today, Data.PositionProfiles, _rng);
            if (p.SeasonStats.Appearances > 0) p.History.Add(p.SeasonStats);
            p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
            p.State.YellowCardsAccumulated = 0;
            p.State.Condition = Math.Max(p.State.Condition, 92);
            p.State.Morale = Morale.Okay;
            if (p.Contract is { YearlyRaisePercent: > 0 } c) c.WeeklyWage = (int)(c.WeeklyWage * (1 + c.YearlyRaisePercent / 100.0));
        }

        foreach (var p in squad.Where(p => _rng.Chance(Development.RetirementChance(p, today))).ToList())
        {
            RemoveFromClub(p);
            p.Retired = true;
            p.Contract = null;
            if (club.Id == HumanClub.Id)
                Notify(MessageType.Info, "msg.sender.assistant",
                    TR("msg.retired.subject", p.Name), TR("msg.retired.body", p.Name, p.Age(today)));
        }
        YouthSeasonStart(club);
    }

    /// <summary>Verein verlassen: Kader, Jugend, Aufstellung, Bank und Beobachtungslisten.</summary>
    private void RemoveFromClub(Player p)
    {
        if (p.ClubId is not { } cid || !World.Clubs.TryGetValue(cid, out var club)) { p.ClubId = null; return; }
        club.PlayerIds.Remove(p.Id);
        club.YouthIds.Remove(p.Id);
        var slot = club.Tactic.Lineup.IndexOf(p.Id);
        if (slot >= 0) club.Tactic.Lineup[slot] = null;
        club.Tactic.Bench.Remove(p.Id);
        foreach (var saved in club.SavedTactics)
        {
            var s2 = saved.Lineup.IndexOf(p.Id);
            if (s2 >= 0) saved.Lineup[s2] = null;
            saved.Bench.Remove(p.Id);
        }
        if (club.Tactic.CaptainId == p.Id) club.Tactic.CaptainId = null;
        p.ClubId = null;
        p.Transfer = new TransferStatus();
    }

    /// <summary>Sicherheitsnetz: KI-Vereine füllen den Kader mit vereinslosen Spielern oder eigenen Talenten auf.</summary>
    private void FillSquad(Club club)
    {
        const int minimum = 22;
        var guard = 0;
        while (club.PlayerIds.Count < minimum && guard++ < 20)
        {
            if (IsHuman(club) && club.PlayerIds.Count >= 16) break;
            var need = NeededPosition(club);
            var youth = club.YouthIds.Select(id => World.Players[id]).Where(p => p.Age(World.Date) >= 17)
                .OrderByDescending(p => Ratings.PositionRating(p, p.MainPosition, Data.PositionProfiles)).FirstOrDefault();
            if (youth is not null && !IsHuman(club)) { PromoteYouthInternal(club, youth); continue; }
            if (!IsHuman(club) && SignBestFreeAgent(club, need)) continue;
            // Letzter Ausweg: Spieler aus dem Umfeld des Vereins (Amateurbereich) verpflichten.
            var level = PlayerGenerator.ClubLevel(club.Reputation) - 1.5;
            var p = PlayerGenerator.Create(World, Data, _rng, need, Math.Clamp(level + _rng.Normal(0, 1), 3, 18), _rng.Next(19, 28), club.CountryCode);
            p.ClubId = club.Id;
            p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
            World.Players[p.Id] = p;
            club.PlayerIds.Add(p.Id);
        }
    }

    /// <summary>Position mit dem größten Bedarf nach der Kaderschablone.</summary>
    public Position NeededPosition(Club club)
    {
        var squad = World.SquadOf(club).Where(p => p.Loan?.ParentClubId != club.Id).ToList();
        return PlayerGenerator.SquadTemplate
            .OrderByDescending(t => t.count - squad.Count(p => p.MainPosition == t.pos))
            .ThenBy(t => squad.Where(p => p.MainPosition == t.pos).Select(p => Ratings.PositionRating(p, t.pos, Data.PositionProfiles)).DefaultIfEmpty(0).Max())
            .First().pos;
    }

    /// <summary>Feste Termine im Kalender: Transferfenster, auslaufende Verträge.</summary>
    private void CalendarEvents()
    {
        var country = World.Country(HumanClub.CountryCode);
        var tw = country.TransferWindows;
        var start = country.Season.StartMonth;
        if (World.Date == ScheduleGenerator.ParseDate(tw.WinterStart, World.SeasonYear, start))
        {
            Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.windowOpen.subject"), TR("msg.windowOpen.winter"));
            var expiring = World.SquadOf(HumanClub).Where(p => p.Contract is { } c && c.Until <= new DateOnly(World.SeasonYear + 1, 6, 30) && p.Loan is null).ToList();
            if (expiring.Count > 0)
                Notify(MessageType.Contract, "msg.sender.assistant", TR("msg.expiring.subject"),
                    TR("msg.expiring.body", string.Join(", ", expiring.Select(p => p.Name))), MessageAction.ContractExpiring);
        }
        if (World.Date == ScheduleGenerator.ParseDate(tw.SummerEnd, World.SeasonYear, start).AddDays(1) ||
            World.Date == ScheduleGenerator.ParseDate(tw.WinterEnd, World.SeasonYear, start).AddDays(1))
            Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.windowClosed.subject"), TR("msg.windowClosed.body"));
    }

    public bool IsTransferWindowOpen(Club? club = null)
    {
        var country = World.Country((club ?? HumanClub).CountryCode);
        var tw = country.TransferWindows;
        var start = country.Season.StartMonth;
        bool In(string from, string to)
        {
            var a = ScheduleGenerator.ParseDate(from, World.SeasonYear, start);
            var b = ScheduleGenerator.ParseDate(to, World.SeasonYear, start);
            return World.Date >= a && World.Date <= b;
        }
        return In(tw.SummerStart, tw.SummerEnd) || In(tw.WinterStart, tw.WinterEnd);
    }
}
