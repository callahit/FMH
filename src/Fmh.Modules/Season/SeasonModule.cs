using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Saisonablauf (Kapitel 3): Saisonende, Auf- und Abstieg, Vertragsende, Entwicklung, neue Saison.</summary>
public sealed partial class SeasonModule : GameModule, ISeasonService
{
    public override string Id => "season";
    public override IReadOnlyList<string> DependsOn => new[] { "economy", "cups" };

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
                // Rangfolge (Sieger, Finalist, Halbfinalisten …) für Startplätze in der nächsten Saison.
                ChampionId = winner.Id, ChampionName = winner.Name, FinalOrder = CupEntries.Ranking(World, cup),
            });
        }

        var own = HumanClub;
        var ownLeague = World.LeagueOf(own);
        var ownTable = LeagueTable.Compute(ownLeague);
        var row = ownTable.First(r => r.ClubId == own.Id);
        var champ = World.Clubs[ownTable[0].ClubId];
        var playoff = World.Cups.FirstOrDefault(c => c.Kind == CompetitionKind.Playoff && c.ParticipantIds.Contains(own.Id));
        var upperPlayoff = playoff?.UpperDivision is { } ud && Pyramid.Matching(World, ud).Any(l => l.Id == own.LeagueId);
        var zone = Pyramid.DirectZone(ownLeague, row.Position, World.SeasonYear);
        var verdictKey = playoff is not null
            ? (playoff.WinnerId == own.Id ? (upperPlayoff ? "msg.seasonEnd.stayedUp" : "msg.seasonEnd.promoted") : (upperPlayoff ? "msg.seasonEnd.relegated" : "msg.seasonEnd.neutral"))
            : zone == MovementDirection.Up ? "msg.seasonEnd.promoted"
            : zone == MovementDirection.Down ? "msg.seasonEnd.relegated"
            : row.Position == 1 ? "msg.seasonEnd.champion" : "msg.seasonEnd.neutral";
        Notify(MessageType.Season, "msg.sender.league",
            TR("msg.seasonEnd.subject", Comp(ownLeague.Id), SeasonLabel(World.SeasonYear)),
            TR("msg.seasonEnd.body", champ.Name, row.Position, row.Points, TR(verdictKey, own.Name)));

        // Phase 3: TV-Leistungsanteil, Vorstand, Sponsorenboni, Karriere, Lizenzierung.
        Finance.SeasonEndFinances();
        foreach (var club in World.Clubs.Values.Where(c => Finance.HasFinances(c) && !IsHuman(c)))
        {
            var table = LeagueTable.Compute(World.LeagueOf(club));
            Sponsors.SponsorBonuses(club, table.First(r => r.ClubId == club.Id).Position <= Board.GoalPosition(club, club.Board.Goal));
        }
        if (!Board.AwaitingNewJob)
        {
            var reached = Board.EvaluateSeason(own, row.Position);
            Sponsors.SponsorBonuses(own, reached);
            if (!Board.AwaitingNewJob)
                Board.CareerSeasonEnd(own, row.Position, reached, verdictKey is "msg.seasonEnd.promoted", row.Position == 1 && ownLeague.Level == 1);
        }
        License.AllocateEuropeanPlaces();
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
        // Neue Formatversion eines Landes (z. B. Ligareform): Ligen und Staffeln werden umgebaut.
        Pyramid.SwitchFormat(World, Data, Rng);

        Transfers.ExpireContractsAndLoans();
        foreach (var club in World.Clubs.Values) RenewSquad(club);
        Transfers.RetireFreeAgents();
        MarketValue.UpdateMarketLevel(World, Data.PositionProfiles);

        foreach (var league in World.Leagues)
            ScheduleGenerator.CreateFixtures(World, league, World.Country(league.CountryCode), Rng);
        World.Cups.Clear();
        Cups.CreateCups();

        // Punktabzüge aus der Lizenzierung gelten in der Liga der neuen Saison.
        foreach (var league in World.Leagues) league.Deductions.Clear();
        foreach (var club in World.Clubs.Values.Where(c => c.Finance.PendingDeduction > 0))
        {
            if (World.FindLeague(club.LeagueId) is { } league) league.Deductions[club.Id] = club.Finance.PendingDeduction;
            club.Finance.PendingDeduction = 0;
        }
        Finance.RollSeasonFinances();
        License.FfpAssessment();
        License.EuropeSeasonStart();
        Staff.StaffSeasonStart();
        foreach (var club in World.Clubs.Values)
        {
            FillSquad(club);
            club.Board.Goal = Board.ExpectedGoal(club);
            Finance.SetSeasonBudget(club);
            Board.UpdateFavourites(club);
            // KI-Vereine übernehmen nach Auf- oder Abstieg die üblichen Eintrittspreise ihrer Spielklasse.
            if (!IsHuman(club) && Finance.HasFinances(club))
            {
                var div = Finance.DivisionFinance(club);
                (club.Finance.Prices.Standing, club.Finance.Prices.Seating, club.Finance.Prices.Box) = (div.Standing, div.Seating, div.Box);
            }
            if (!IsHuman(club)) club.Tactic.FormationId = AiTactics.BestFormation(club, World, Data).Id;
            LineupSelector.Repair(club, World, Data, BenchSize(club));
        }

        Sponsors.SponsorSeasonStart();
        var b = HumanClub.Budget;
        var currency = Currency;
        Notify(MessageType.Board, "msg.sender.board",
            TR("msg.newSeason.subject", SeasonLabel(World.SeasonYear)),
            TR("msg.newSeason.body", HumanClub.Name, Comp(HumanClub.LeagueId),
                TextArg.Money(b.TransferBudget, currency), TextArg.Money(b.WageBudget, currency)));
        Board.OfferSeasonGoal();
        return true;
    }

    /// <summary>Auf- und Abstieg in allen Ländern nach den Bewegungsregeln der Ligapyramide.</summary>
    private void PromoteAndRelegate()
    {
        var before = World.Clubs.Values.ToDictionary(c => c.Id, c => (level: Level(c), div: ClubRules.DivisionFinance(World, c)));
        foreach (var country in World.Countries)
            Pyramid.PromoteAndRelegate(World, country, Rng, _ => { });
        foreach (var club in World.Clubs.Values)
        {
            var (level, div) = before[club.Id];
            if (Level(club) is var now && now != level && level > 0 && now > 0) ApplyClauses(club, now - level, div);
        }
    }

    /// <summary>Ebene der Liga bzw. des Pools (Pools unter der untersten Liga); 0 = ohne Ligabetrieb im Spiel.</summary>
    private int Level(Club club) =>
        World.FindLeague(club.LeagueId)?.Level ?? (World.Country(club.CountryCode).Pools.Any(p => p.Id == club.LeagueId)
            ? World.Leagues.Where(l => l.CountryCode == club.CountryCode).Select(l => l.Level).DefaultIfEmpty(0).Max() + 1 : 0);

    /// <summary>
    /// Auf- und Abstiegsklauseln (Kapitel 7): Spielergehälter nach der Klausel im Vertrag, Personal und Manager nach der üblichen
    /// Klausel der bisherigen Spielklasse, Sponsoren nach der Klausel im Sponsorenvertrag. Verträge aus älteren Spielständen ohne Klausel
    /// folgen wie bisher dem Gehaltsniveau der neuen Spielklasse.
    /// </summary>
    private void ApplyClauses(Club club, int steps, DivisionFinance old)
    {
        var now = ClubRules.DivisionFinance(World, club);
        var max = now.MaxWeeklyWage;
        long wagesBefore = 0, wagesAfter = 0;
        int Adjust(int wage, double factor) => Math.Min(max, Math.Max(100, (int)(Math.Round(wage * factor / 100) * 100)));
        // Leihspieler bleiben außen vor: Ihren Vertrag trägt der abgebende Verein.
        foreach (var p in World.SquadOf(club).Where(p => p.Loan is null))
        {
            if (p.Contract is not { Youth: false } c) continue;
            var factor = c.RelegationPercent is { } rel
                ? ContractClauses.Factor(steps, rel, c.PromotionPercent ?? 0)
                : (old.WageLevel > 0 ? now.WageLevel / old.WageLevel : 1.0) * (steps > 0 && c.RelegationClause ? 0.7 : 1.0);
            if (steps > 0) c.RelegationClause = false;
            wagesBefore += c.WeeklyWage;
            c.WeeklyWage = Adjust(c.WeeklyWage, factor);
            wagesAfter += c.WeeklyWage;
        }
        var staffFactor = ContractClauses.Factor(steps, old.RelegationWagePercent, old.PromotionWagePercent);
        foreach (var s in World.Staff.Where(s => s.ClubId == club.Id)) s.WeeklyWage = Math.Max(100, (int)(Math.Round(s.WeeklyWage * staffFactor / 100) * 100));
        // Managervertrag mit derselben üblichen Klausel.
        if (club.Board.ManagerWeeklyWage > 0)
            club.Board.ManagerWeeklyWage = Math.Max(100, (int)(Math.Round(club.Board.ManagerWeeklyWage * staffFactor / 100) * 100));
        long sponsorsBefore = 0, sponsorsAfter = 0;
        // Nur Verträge, die in die neue Saison hineinlaufen.
        foreach (var deal in club.Sponsors.Where(d => d.UntilSeason > World.SeasonYear))
        {
            var factor = ContractClauses.Factor(steps, deal.RelegationPercent, deal.PromotionPercent);
            sponsorsBefore += deal.PerSeason;
            deal.PerSeason = MarketValue.Round(deal.PerSeason * factor);
            deal.Bonus = MarketValue.Round(deal.Bonus * factor);
            sponsorsAfter += deal.PerSeason;
        }
        if (IsHuman(club))
            Notify(MessageType.Finance, "msg.sender.assistant", TR(steps > 0 ? "msg.clauses.relegation.subject" : "msg.clauses.promotion.subject"),
                TR("msg.clauses.body", TextArg.Money((wagesAfter - wagesBefore) * 52, Currency), TextArg.Money(sponsorsAfter - sponsorsBefore, Currency)));
    }

    /// <summary>Saisonwechsel je Verein: Entwicklung, Statistik, Karriereenden.</summary>
    private void RenewSquad(Club club)
    {
        var today = World.Date;
        var squad = World.SquadOf(club).ToList();
        foreach (var p in squad.Concat(club.YouthIds.Select(id => World.Players[id])))
        {
            Development.ApplySeason(p, today, Data.PositionProfiles, Rng);
            if (p.SeasonStats.Appearances > 0) p.History.Add(p.SeasonStats);
            p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
            p.State.YellowCardsAccumulated = 0;
            p.State.Condition = Math.Max(p.State.Condition, 92);
            p.State.Morale = Morale.Okay;
            if (p.Contract is { YearlyRaisePercent: > 0 } c) c.WeeklyWage = (int)(c.WeeklyWage * (1 + c.YearlyRaisePercent / 100.0));
        }

        foreach (var p in squad.Where(p => Rng.Chance(Development.RetirementChance(p, today))).ToList())
        {
            RemoveFromClub(p);
            p.Retired = true;
            p.Contract = null;
            if (club.Id == HumanClub.Id)
                Notify(MessageType.Info, "msg.sender.assistant",
                    TR("msg.retired.subject", p.Name), TR("msg.retired.body", p.Name, p.Age(today)));
        }
        Youth.YouthSeasonStart(club);
    }

    /// <summary>Verein verlassen: Kader, Jugend, Aufstellung, Bank und Beobachtungslisten.</summary>
    public void RemoveFromClub(Player p)
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
        // KI füllt bis drei unter die übliche Kadergröße der Spielklasse auf (Vereinslose zuerst), Notverpflichtungen nur unter 22.
        const int minimum = 22;
        var target = Math.Max(minimum, Finance.DivisionFinance(club).SquadSize - 3);
        var guard = 0;
        while (club.PlayerIds.Count < target && guard++ < 20)
        {
            if (IsHuman(club) && club.PlayerIds.Count >= 16) break;
            if (!IsHuman(club) && Transfers.SignBestFreeAgent(club, NeededPosition(club))) continue;
            if (club.PlayerIds.Count >= minimum) break;
            AddEmergencyPlayer(club);
        }
    }

    /// <summary>Kader auffüllen: bestes Talent ab 17 aus der eigenen Jugend, sonst ein Spieler aus dem Amateurbereich.</summary>
    public void AddEmergencyPlayer(Club club)
    {
        var need = NeededPosition(club);
        var youth = club.YouthIds.Select(id => World.Players[id]).Where(p => p.Age(World.Date) >= 17)
            .OrderByDescending(p => Ratings.PositionRating(p, p.MainPosition, Data.PositionProfiles)).FirstOrDefault();
        if (youth is not null && !IsHuman(club)) { Youth.PromoteYouthInternal(club, youth); return; }
        var level = PlayerGenerator.ClubLevel(club.Reputation) - 1.5;
        var p = PlayerGenerator.Create(World, Data, Rng, need, Math.Clamp(level + Rng.Normal(0, 1), 3, 18), Rng.Next(19, 28), club.CountryCode);
        p.ClubId = club.Id;
        p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
        // Vertrag wie ein ablösefreier Neuzugang, damit der Spieler nicht sofort wieder geht.
        var contract = Negotiations.SuggestContract(p, club, World, Data);
        p.Contract = Transfers.MakeContract(contract, World.Date, club);
        World.Players[p.Id] = p;
        club.PlayerIds.Add(p.Id);
    }

    /// <summary>Position mit dem größten Bedarf nach der Kaderschablone.</summary>
    public Position NeededPosition(Club club)
    {
        var squad = World.SquadOf(club).Where(p => p.Loan?.ParentClubId != club.Id).ToList();
        return PlayerGenerator.Template(Finance.DivisionFinance(club).SquadSize)
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
