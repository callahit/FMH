using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Lizenzierung (Kapitel 8) mit festen Stichtagen: Antrag (15.03.), Entscheidung mit Auflagen (30.04.),
/// Nachweis der Auflagen (31.05.). Verweigerte Lizenz bedeutet Zwangsabstieg. Dazu die Europa-Lizenz,
/// Financial Fair Play (Break-even über drei Spielzeiten, Kaderkostenquote) und die Vergabe der Europapokalplätze
/// mit Nachrücken. Die europäischen Wettbewerbe laufen vereinfacht (Rundenergebnisse nach Stärke, Prämien).
/// </summary>
public sealed partial class LicenseModule : GameModule, ILicenseService
{
    public override string Id => "license";
    public override IReadOnlyList<string> DependsOn => new[] { "finance" };
    public override void OnDay(DayContext day) => LicenseDaily();

    /// <summary>Stichtag (MM-dd) der laufenden Saison; Monate vor dem Saisonstart liegen im Folgejahr.</summary>
    private DateOnly SeasonDate(string monthDay)
    {
        var parts = monthDay.Split('-');
        var month = int.Parse(parts[0]);
        var day = int.Parse(parts[1]);
        var year = month < World.Countries[0].Season.StartMonth ? World.SeasonYear + 1 : World.SeasonYear;
        return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    private void LicenseDaily()
    {
        foreach (var country in World.Countries)
        {
            var fin = country.Finance;
            if (World.Date == SeasonDate(fin.LicenseApplication)) LicenseApplication(country);
            if (World.Date == SeasonDate(fin.LicenseDecision)) LicenseDecision(country);
            if (World.Date == SeasonDate(fin.LicenseDeadline)) LicenseDeadline(country);
            if (country.Europe is { } europe) EuropeDaily(europe);
        }
    }

    private IEnumerable<Club> LicenseClubs(CountryConfig country) =>
        World.Clubs.Values.Where(c => c.CountryCode == country.Code && Finance.HasFinances(c)).OrderBy(c => c.Id);

    /// <summary>Liga, für die die Lizenz beantragt wird: bei Aufstiegschance auch die höhere.</summary>
    private DivisionFinance TargetDivision(Club club, out bool upper)
    {
        upper = false;
        var league = World.FindLeague(club.LeagueId);
        if (league is null) return Finance.DivisionFinance(club);
        var country = World.Country(club.CountryCode);
        var higher = World.Leagues.FirstOrDefault(l => l.CountryCode == league.CountryCode && l.Level == league.Level - 1);
        if (higher is null) return Finance.DivisionFinance(club);
        var table = LeagueTable.Compute(league);
        var pos = table.FindIndex(r => r.ClubId == club.Id) + 1;
        // Aufstiegs- und Relegationsplätze plus ein Platz Puffer; dazu Teilnehmer einer Relegation in die höhere Ebene.
        var playoff = country.Playoffs.Any(p => p.Entrants.Any(e => (e.Division == league.Id || e.Division == league.Format.Id) && e.Position == pos)
                                                && Pyramid.Matching(World, p.UpperDivision).Any(l => l.Level == league.Level - 1));
        if (pos > 0 && (pos <= league.Format.Promotion + 2 || playoff)
            && (country.Finance.Divisions.TryGetValue(higher.Id, out var div) || country.Finance.Divisions.TryGetValue(higher.Format.Id, out div)))
        {
            upper = true;
            return div;
        }
        return Finance.DivisionFinance(club);
    }

    /// <summary>Offene Lizenzkriterien als Textschlüssel (Eigenkapital, Flutlicht, Nachwuchsleistungszentrum).</summary>
    private List<string> LicenseGaps(Club club, DivisionFinance div)
    {
        var gaps = new List<string>();
        if (Finance.Equity(club) < div.MinEquity) gaps.Add("license.gap.equity");
        if (club.Stadium.Floodlight < div.FloodlightRequired) gaps.Add("license.gap.floodlight");
        if (club.Academy.Stars < div.YouthCenterRequired) gaps.Add("license.gap.academy");
        return gaps;
    }

    private TextArg GapList(IEnumerable<string> gaps, DivisionFinance div) =>
        TextArg.Join("\n", gaps.Select(g => (object?)TR(g, TextArg.Money(div.MinEquity, Currency), div.FloodlightRequired, div.YouthCenterRequired)));

    /// <summary>15.03.: Alle Vereine reichen ihre Unterlagen ein; der Mensch bekommt die Checkliste.</summary>
    private void LicenseApplication(CountryConfig country)
    {
        foreach (var club in LicenseClubs(country))
        {
            club.License.Stage = LicenseStage.Applied;
            club.License.Conditions.Clear();
            club.License.UpperConditions.Clear();
            club.License.ForcedRelegation = false;
            club.License.PromotionAllowed = true;
            if (club.Id != HumanClub.Id || Board.AwaitingNewJob) continue;
            var div = TargetDivision(club, out var upper);
            var gaps = LicenseGaps(club, div);
            Notify(MessageType.Finance, "msg.sender.league", TR("msg.licenseApplied.subject", SeasonLabel(World.SeasonYear + 1)),
                TR("msg.licenseApplied.body", TextArg.OfDate(SeasonDate(country.Finance.LicenseDecision)), TextArg.OfDate(SeasonDate(country.Finance.LicenseDeadline)),
                    TextArg.Money(Finance.Equity(club), Currency), TextArg.Money(div.MinEquity, Currency), club.Stadium.Floodlight, div.FloodlightRequired,
                    club.Academy.Stars, div.YouthCenterRequired,
                    gaps.Count == 0 ? TR("license.allMet") : GapList(gaps, div),
                    upper ? TR("license.upperLeague") : TextRef.Literal("")),
                MessageAction.License);
        }
    }

    /// <summary>30.04.: Entscheidung mit Auflagen, Punktabzug oder Verweigerung; dazu die Europa-Lizenz.</summary>
    private void LicenseDecision(CountryConfig country)
    {
        foreach (var club in LicenseClubs(country))
        {
            var lic = club.License;
            var div = TargetDivision(club, out var upper);
            var current = Finance.DivisionFinance(club);
            var equity = Finance.Equity(club);
            // Die wirtschaftliche Prüfung gilt der aktuellen Liga; für die höhere Liga entscheidet sie nur über den Aufstieg.
            lic.PromotionAllowed = !upper || equity >= div.MinEquity * 1.5;
            var lowest = World.FindLeague(club.LeagueId) is { } league && !World.Leagues.Any(l => l.CountryCode == league.CountryCode && l.Level > league.Level);
            var outcome = LicenseOutcome.Granted;
            var deduction = 0;
            if (equity < current.MinEquity * 3 && current.MinEquity < 0)
            {
                // Verweigerung bedeutet Zwangsabstieg; in der untersten Liga stattdessen neun Punkte Abzug.
                if (lowest) { outcome = LicenseOutcome.PointDeduction; deduction = 9; }
                else { outcome = LicenseOutcome.Refused; lic.ForcedRelegation = true; }
            }
            else if (equity < current.MinEquity * 1.5 && current.MinEquity < 0) { outcome = LicenseOutcome.PointDeduction; deduction = 6; }
            var gaps = LicenseGaps(club, current);
            lic.UpperConditions = upper ? LicenseGaps(club, div).Where(g => g != "license.gap.equity" && !gaps.Contains(g)).ToList() : new List<string>();
            if (outcome == LicenseOutcome.Granted && gaps.Count > 0) outcome = LicenseOutcome.Conditions;
            lic.Conditions = outcome == LicenseOutcome.Conditions ? gaps : new List<string>();
            if (club.Finance.Insolvent && outcome == LicenseOutcome.Granted) { outcome = LicenseOutcome.Conditions; lic.Conditions.Add("license.gap.equity"); }
            lic.Outcome = outcome;
            lic.Stage = LicenseStage.Decided;
            club.Finance.LastLicense = outcome;
            club.Finance.Insolvent = false;
            if (deduction > 0) club.Finance.PendingDeduction = Math.Max(club.Finance.PendingDeduction, deduction);
            UefaLicenseDecision(club, country);
            if (club.Id != HumanClub.Id || Board.AwaitingNewJob) continue;
            Notify(MessageType.Finance, "msg.sender.league", TR("msg.license.subject"),
                TR("msg.licenseDecision.body",
                    TR("msg.license." + outcome, TextArg.Money(equity, Currency), TextArg.Money(div.MinEquity, Currency), deduction, TextRef.Literal("")),
                    lic.Conditions.Count + lic.UpperConditions.Count == 0 ? TextRef.Literal("")
                        : TR("msg.licenseConditions", GapList(lic.Conditions.Concat(lic.UpperConditions), div), TextArg.OfDate(SeasonDate(country.Finance.LicenseDeadline))),
                    lic.PromotionAllowed ? TextRef.Literal("") : TR("msg.license.noPromotion"),
                    lic.UefaLicense ? TR("msg.uefa.granted") : TR("msg.uefa.refused", TextArg.Join(", ", lic.UefaReasons.Select(r => (object?)TR(r))))),
                MessageAction.License);
        }
    }

    /// <summary>31.05.: Nachweis der Auflagen; nicht erfüllte Auflagen kosten Punkte und eine Geldstrafe.</summary>
    private void LicenseDeadline(CountryConfig country)
    {
        foreach (var club in LicenseClubs(country))
        {
            var lic = club.License;
            if (lic.Stage != LicenseStage.Decided) continue;
            lic.Stage = LicenseStage.Final;
            var div = TargetDivision(club, out _);
            var human = club.Id == HumanClub.Id && !Board.AwaitingNewJob;
            // Auflagen nur für die höhere Liga: Geldstrafe, der Aufstieg bleibt möglich.
            var upperOpen = LicenseGaps(club, div).Intersect(lic.UpperConditions).ToList();
            if (upperOpen.Count > 0)
            {
                Finance.Book(club, TransactionType.Fines, -MarketValue.Round(100_000 * ClubRules.Scale(club)), TR("tx.licenseFine"));
                if (human) Notify(MessageType.Finance, "msg.sender.league", TR("msg.licenseFinal.subject"), TR("msg.licenseFinal.upper", GapList(upperOpen, div)), MessageAction.License);
            }
            if (lic.Outcome != LicenseOutcome.Conditions) continue;
            var open = LicenseGaps(club, Finance.DivisionFinance(club)).Intersect(lic.Conditions).ToList();
            // KI: Eine Bürgschaft (Sponsor, Stadt, Gönner) deckt die Liquiditätslücke häufig ab.
            if (!IsHuman(club) && open.Remove("license.gap.equity") && !Rng.Chance(0.6)) open.Add("license.gap.equity");
            if (open.Count == 0)
            {
                if (human && upperOpen.Count == 0) Notify(MessageType.Finance, "msg.sender.league", TR("msg.licenseFinal.subject"), TR("msg.licenseFinal.met"), MessageAction.License);
                continue;
            }
            var penalty = country.Finance.ConditionPenalty;
            club.Finance.PendingDeduction += penalty;
            club.Finance.LastLicense = LicenseOutcome.PointDeduction;
            lic.Outcome = LicenseOutcome.PointDeduction;
            Finance.Book(club, TransactionType.Fines, -MarketValue.Round(50_000 * ClubRules.Scale(club)), TR("tx.licenseFine"));
            if (human)
                Notify(MessageType.Finance, "msg.sender.league", TR("msg.licenseFinal.subject"),
                    TR("msg.licenseFinal.missed", GapList(open, Finance.DivisionFinance(club)), penalty), MessageAction.License);
        }
    }

    /// <summary>Europa-Lizenz: nationale Lizenz, FFP, Stadion (Sitzplätze, Flutlicht), Nachwuchszentrum, keine Überziehung.</summary>
    private void UefaLicenseDecision(Club club, CountryConfig country)
    {
        var lic = club.License;
        lic.UefaReasons.Clear();
        if (country.Europe is not { } europe) { lic.UefaLicense = false; return; }
        var rules = europe.License;
        if (lic.Outcome is LicenseOutcome.Refused or LicenseOutcome.PointDeduction) lic.UefaReasons.Add("uefa.reason.national");
        if (lic.ExcludedUntilSeason > World.SeasonYear) lic.UefaReasons.Add("uefa.reason.ffp");
        if (club.Stadium.SeatingTotal < rules.MinSeats) lic.UefaReasons.Add("uefa.reason.seats");
        if (club.Stadium.Floodlight < rules.Floodlight) lic.UefaReasons.Add("uefa.reason.floodlight");
        if (club.Academy.Stars < rules.AcademyStars) lic.UefaReasons.Add("uefa.reason.academy");
        if (club.Budget.Balance < -Finance.CreditLimit(club)) lic.UefaReasons.Add("uefa.reason.overdue");
        lic.UefaLicense = lic.UefaReasons.Count == 0;
    }

    // ================================================================== Financial Fair Play

    /// <summary>Saisonabschluss für FFP und Lizenz festhalten (beim Saisonwechsel).</summary>
    public void RecordSeasonFinance(Club club)
    {
        var s = club.Finance.Season;
        long Get(TransactionType t) => s.GetValueOrDefault(t);
        var excluded = new[] { TransactionType.Construction, TransactionType.Youth, TransactionType.LoanPayout, TransactionType.LoanRepayment, TransactionType.Deposit };
        var relevant = s.Where(kv => !excluded.Contains(kv.Key)).ToList();
        var revenue = relevant.Where(kv => kv.Value > 0).Sum(kv => kv.Value);
        var expenses = relevant.Where(kv => kv.Value < 0).Sum(kv => kv.Value);
        var squadCost = -(Get(TransactionType.Wages) + Get(TransactionType.Bonuses) + Get(TransactionType.AgentFee) + Get(TransactionType.SigningFee))
                        - (Get(TransactionType.TransferIn) + Get(TransactionType.Installment)) / 3.0;
        club.Finance.Seasons.Add(new SeasonFinance
        {
            SeasonYear = World.SeasonYear - 1, Revenue = revenue, Expenses = expenses, BreakEven = revenue + expenses,
            SquadCostRatio = revenue <= 0 ? 0 : Math.Max(0, squadCost) / revenue, EquityAtEnd = Finance.Equity(club),
        });
        if (club.Finance.Seasons.Count > 5) club.Finance.Seasons.RemoveAt(0);
    }

    /// <summary>
    /// FFP-Prüfung zum Saisonstart über die letzten drei Spielzeiten (nur Vereine der Europa-Liga):
    /// Defizit bis zur Toleranz ist erlaubt, darüber Verwarnung, im Wiederholungsfall Geldstrafe, bei großem Defizit
    /// Ausschluss aus den europäischen Wettbewerben der übernächsten Vergabe. Die Kaderkostenquote wird zusätzlich geprüft.
    /// </summary>
    public void FfpAssessment()
    {
        foreach (var country in World.Countries)
        {
            if (country.Europe is not { } europe) continue;
            var rules = europe.Ffp;
            foreach (var club in LicenseClubs(country))
            {
                var lic = club.License;
                var seasons = club.Finance.Seasons.TakeLast(3).ToList();
                if (seasons.Count == 0) continue;
                var inEurope = club.LeagueId == europe.League || World.EuropeanEntries.Any(e => e.ClubId == club.Id && e.SeasonYear == World.SeasonYear);
                var previous = lic.Ffp;
                var breakEven = seasons.Sum(x => x.BreakEven);
                var ratio = seasons[^1].SquadCostRatio;
                lic.FfpBreakEven = breakEven;
                lic.FfpSquadCost = ratio;
                if (!inEurope) { lic.Ffp = FfpStatus.Compliant; continue; }
                var deficit = -breakEven;
                var status = FfpStatus.Compliant;
                if (deficit > rules.SevereDeviation) status = FfpStatus.Excluded;
                else if (deficit > rules.AcceptableDeviation || ratio > rules.SquadCostRatio)
                    status = previous is FfpStatus.Warning or FfpStatus.Fined || deficit > rules.AcceptableDeviation * 2 || ratio > rules.SquadCostRatio + 0.1
                        ? FfpStatus.Fined : FfpStatus.Warning;
                lic.Ffp = status;
                long fine = 0;
                if (status is FfpStatus.Fined or FfpStatus.Excluded)
                {
                    fine = MarketValue.Round(seasons[^1].Revenue * rules.FinePercent / 100);
                    if (fine > 0) Finance.Book(club, TransactionType.Fines, -fine, TR("tx.ffpFine"));
                }
                if (status == FfpStatus.Excluded) lic.ExcludedUntilSeason = World.SeasonYear + 1;
                if (club.Id != HumanClub.Id || Board.AwaitingNewJob) continue;
                Notify(MessageType.Finance, "msg.sender.uefa", TR("msg.ffp.subject"),
                    TR("msg.ffp." + status, TextArg.Money(breakEven, Currency), TextArg.Money(-rules.AcceptableDeviation, Currency),
                        TextArg.Num(ratio * 100, "0"), TextArg.Num(rules.SquadCostRatio * 100, "0"), TextArg.Money(fine, Currency), seasons.Count),
                    MessageAction.License);
            }
        }
    }

    // ================================================================== Europapokalplätze

    /// <summary>Saisonende: Plätze nach Abschlusstabelle und Pokalsieger; Vereine ohne Zulassung werden übersprungen.</summary>
    public void AllocateEuropeanPlaces()
    {
        var next = World.SeasonYear + 1;
        World.EuropeanEntries.RemoveAll(e => e.SeasonYear < World.SeasonYear);
        foreach (var country in World.Countries)
        {
            if (country.Europe is not { } europe || World.FindLeague(europe.League) is not { } league) continue;
            bool Eligible(Club c) => c.License.UefaLicense && c.License.ExcludedUntilSeason < next && !c.License.ForcedRelegation;
            var table = LeagueTable.Compute(league);
            var cupWinner = World.Cups.FirstOrDefault(c => c.Id == europe.Cup && c.SeasonYear == World.SeasonYear)?.WinnerId is { } wid ? World.Clubs[wid] : null;
            var slots = europe.Places.OrderBy(p => p.From).SelectMany(p => Enumerable.Repeat(p.Competition, p.To - p.From + 1)).ToList();
            var cupOk = cupWinner is not null && Eligible(cupWinner) && europe.CupWinner.Length > 0;
            var cupViaLeague = cupOk && table.Select(r => World.Clubs[r.ClubId]).Where(Eligible).Take(slots.Count).Contains(cupWinner!);
            // Pokalsieger schon über die Liga dabei oder ohne Zulassung: der Platz geht an den nächsten Ligaplatz.
            if (europe.CupWinner.Length > 0 && (!cupOk || cupViaLeague))
            {
                var idx = slots.FindLastIndex(s => s == europe.CupWinner);
                slots.Insert(idx < 0 ? slots.Count : idx + 1, europe.CupWinner);
            }
            var skipped = new List<Club>();
            var allocated = new List<EuropeanEntry>();
            foreach (var row in table)
            {
                if (allocated.Count >= slots.Count) break;
                var club = World.Clubs[row.ClubId];
                if (cupOk && !cupViaLeague && club == cupWinner) continue;
                if (!Eligible(club)) { skipped.Add(club); continue; }
                allocated.Add(new EuropeanEntry { ClubId = club.Id, CompetitionId = slots[allocated.Count], SeasonYear = next, Replacement = skipped.Count > 0 });
            }
            if (cupOk && !cupViaLeague) allocated.Add(new EuropeanEntry { ClubId = cupWinner!.Id, CompetitionId = europe.CupWinner, SeasonYear = next });
            World.EuropeanEntries.AddRange(allocated);

            var human = HumanClub;
            if (Board.AwaitingNewJob) continue;
            if (allocated.FirstOrDefault(e => e.ClubId == human.Id) is { } own)
                Notify(MessageType.Season, "msg.sender.uefa", TR("msg.europeQualified.subject", TR("europe." + own.CompetitionId)),
                    TR(own.Replacement ? "msg.europeQualified.replacement" : "msg.europeQualified.body", human.Name, TR("europe." + own.CompetitionId), SeasonLabel(next)));
            else if (skipped.Contains(human))
                Notify(MessageType.Season, "msg.sender.uefa", TR("msg.europeDenied.subject"),
                    TR("msg.europeDenied.body", human.Name, TextArg.Join(", ", human.License.UefaReasons.Select(r => (object?)TR(r)))));
            var lines = allocated.Select(e => (object?)TR("msg.europeList.line", World.Clubs[e.ClubId].Name, TR("europe." + e.CompetitionId))).ToList();
            Notify(MessageType.Season, "msg.sender.uefa", TR("msg.europeList.subject", SeasonLabel(next)),
                TR("msg.europeList.body", TextArg.Join("\n", lines), skipped.Count == 0 ? TextRef.Literal("") :
                    TR("msg.europeList.skipped", TextArg.Join(", ", skipped.Select(c => (object?)c.Name)))));
        }
    }

    public IReadOnlyList<EuropeanEntry> EuropeanEntriesOf(int seasonYear) => World.EuropeanEntries.Where(e => e.SeasonYear == seasonYear).ToList();

    public static EuropeCompetition? EuropeCompetitionOf(CountryConfig country, string id) => country.Europe?.Competitions.FirstOrDefault(c => c.Id == id);

    /// <summary>Saisonstart: Startprämie der Teilnehmer.</summary>
    public void EuropeSeasonStart()
    {
        foreach (var e in EuropeanEntriesOf(World.SeasonYear))
        {
            var club = World.Clubs[e.ClubId];
            if (EuropeCompetitionOf(World.Country(club.CountryCode), e.CompetitionId) is not { } comp) continue;
            Finance.Book(club, TransactionType.European, comp.EntryPrize, TR("tx.europeEntry", TR("europe." + comp.Id)));
            if (club.Id == HumanClub.Id && !Board.AwaitingNewJob)
                Notify(MessageType.Season, "msg.sender.uefa", TR("msg.europeStart.subject", TR("europe." + comp.Id)),
                    TR("msg.europeStart.body", club.Name, TR("europe." + comp.Id), TextArg.Money(comp.EntryPrize, Currency)));
        }
    }

    /// <summary>An den Rundenterminen entscheidet die Stärke gegen das Teilnehmerfeld über das Weiterkommen.</summary>
    private void EuropeDaily(EuropeConfig europe)
    {
        foreach (var comp in europe.Competitions)
            for (var i = 0; i < comp.Stages.Count; i++)
            {
                if (World.Date != SeasonDate(comp.Stages[i].Date)) continue;
                var stage = comp.Stages[i];
                foreach (var e in EuropeanEntriesOf(World.SeasonYear).Where(e => e.CompetitionId == comp.Id && !e.Eliminated && e.Stage == i - 1))
                {
                    var club = World.Clubs[e.ClubId];
                    var strength = AiTactics.TeamStrength(club, World, Data);
                    var chance = 1 / (1 + Math.Exp(-(1.2 + (strength - comp.FieldStrength) * 1.4 - i * 0.5)));
                    var human = club.Id == HumanClub.Id && !Board.AwaitingNewJob;
                    if (Rng.Chance(chance))
                    {
                        e.Stage = i;
                        Finance.Book(club, TransactionType.European, stage.Prize, TR("tx.europePrize", TR("europe." + comp.Id)));
                        club.Fans.Mood = Math.Clamp(club.Fans.Mood + 3, 0, 100);
                        if (i == comp.Stages.Count - 1)
                        {
                            e.Eliminated = true;
                            if (Rng.Chance(0.5)) club.Reputation = Math.Min(20, club.Reputation + 1);
                            World.History.Add(new SeasonRecord { SeasonYear = World.SeasonYear, LeagueId = comp.Id, LeagueName = comp.Id, ChampionId = club.Id, ChampionName = club.Name, FinalOrder = new List<int> { club.Id } });
                        }
                        if (human)
                        {
                            Board.ChangeTrust(club, 3);
                            Notify(MessageType.Season, "msg.sender.uefa", TR("msg.europeStage.subject", TR("europe." + comp.Id), TR(stage.Key)),
                                TR("msg.europeStage.body", club.Name, TR(stage.Key), TextArg.Money(stage.Prize, Currency)));
                        }
                    }
                    else
                    {
                        e.Eliminated = true;
                        if (human)
                            Notify(MessageType.Season, "msg.sender.uefa", TR("msg.europeOut.subject", TR("europe." + comp.Id)),
                                TR("msg.europeOut.body", club.Name, TR(stage.Key)));
                    }
                }
            }
    }
}
