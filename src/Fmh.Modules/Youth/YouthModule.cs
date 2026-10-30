using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Nachwuchsleistungszentrum (Kapitel 7): Altersstufen U15 (14–15), U17 (16–17) und U19 (18), vier
/// Qualitätskriterien, die einem jährlichen Budget und den Gebäuden folgen, jährlicher Jahrgang ab 14 Jahren
/// im März, Quereinsteiger, Aussortieren zum Saisonstart, Profiverträge und Absolventen.
/// </summary>
public sealed partial class YouthModule : GameModule, IYouthService
{
    public override string Id => "youth";
    public override IReadOnlyList<string> DependsOn => new[] { "scouting" };
    public override void OnDay(DayContext day) => YouthDaily();

    public const int ReleaseAge = 19;

    public IReadOnlyList<Player> YouthOf(Club club) => club.YouthIds.Select(id => World.Players[id]).ToList();

    /// <summary>Altersstufe nach dem Alter zum Saisonstart (1. Juli).</summary>
    public static YouthAgeGroup GroupOf(int age) => age <= 15 ? YouthAgeGroup.U15 : age <= 17 ? YouthAgeGroup.U17 : YouthAgeGroup.U19;

    private DateOnly SeasonStartDate => new(World.SeasonYear, World.Countries[0].Season.StartMonth, 1);

    public YouthAgeGroup AgeGroup(Player p) => GroupOf(p.Age(SeasonStartDate));

    public IReadOnlyList<Player> YouthGroup(Club club, YouthAgeGroup group) => YouthOf(club).Where(p => AgeGroup(p) == group).ToList();

    /// <summary>Plätze je Altersstufe: Grundwert plus Infrastruktur; die U19 umfasst nur einen Jahrgang.</summary>
    public int GroupCapacity(Club club, YouthAgeGroup group)
    {
        var size = World.Country(club.CountryCode).Youth.GroupSize + (int)(club.Academy.Facilities / 5);
        return group == YouthAgeGroup.U19 ? size * 2 / 3 : size;
    }

    public int YouthCapacity(Club club) => Enum.GetValues<YouthAgeGroup>().Sum(g => GroupCapacity(club, g));

    /// <summary>Jugendtrainer einer Altersstufe (aus dem Personal) oder null.</summary>
    public StaffMember? GroupCoach(Club club, YouthAgeGroup group) =>
        World.Staff.FirstOrDefault(s => s.ClubId == club.Id && s.Role == StaffRole.YouthCoach && s.AgeGroup == group);

    /// <summary>Jahresbudget, mit dem ein Leistungszentrum dieser Vereinsgröße "ordentlich" arbeitet.</summary>
    public long ReferenceAcademyBudget(Club club)
    {
        var level = World.FindLeague(club.LeagueId)?.Level ?? 3;
        var configured = Finance.HasFinances(club) ? Finance.DivisionFinance(club).AcademyBudgetFactor : 0;
        var factor = configured > 0 ? configured : level switch { 1 => 1.2, 2 => 0.8, _ => 0.25 };
        return MarketValue.Round(World.Country(club.CountryCode).Youth.ReferenceBudget * ClubRules.Scale(club) * factor);
    }

    // ================================================================== Ablauf

    private void YouthDaily()
    {
        if (World.Date.DayOfWeek == DayOfWeek.Monday) TrainYouth();
        if (World.Date.Day == 1)
            foreach (var club in World.Clubs.Values) AcademyMonthly(club);
        foreach (var country in World.Countries)
        {
            var y = country.Youth;
            if (World.Date.Month != y.IntakeMonth || World.Date.Day != 15) continue;
            // Amateurvereine (ohne eigene Finanzen) führen keine U15; ihr Jahrgang kommt mit 16 wie in der Startwelt (Review F-05).
            foreach (var club in World.Clubs.Values.Where(c => c.CountryCode == country.Code))
                YouthIntake(club, y.MinPlayers, y.MaxPlayers, fixedAge: Finance.HasFinances(club) ? null : Math.Max(y.IntakeAge, 16));
        }
    }

    /// <summary>Monatlich: Budgetrate buchen, Kriterien gleiten zum Zielwert aus Budget, Personal und Gebäuden.</summary>
    private void AcademyMonthly(Club club)
    {
        var a = club.Academy;
        if (Finance.HasFinances(club) && a.Budget > 0) Finance.Book(club, TransactionType.Youth, -a.Budget / 12, TR("tx.academy"));
        var t = AcademyTargets(club);
        const double drift = 0.12;
        a.Coaching += (t.coaching - a.Coaching) * drift;
        a.Facilities += (t.facilities - a.Facilities) * drift;
        a.Recruitment += (t.recruitment - a.Recruitment) * drift;
        a.Education += (t.education - a.Education) * drift;
        ClubRules.SyncYouthValues(club);
    }

    /// <summary>Zielwerte der vier Kriterien (1–20) bei aktuellem Budget, Personal und Gebäuden.</summary>
    public (double coaching, double facilities, double recruitment, double education) AcademyTargets(Club club)
    {
        var a = club.Academy;
        var reference = Math.Max(1, ReferenceAcademyBudget(club));
        // Vereine ohne Finanzen (Amateurpools) arbeiten mit einem gedachten Budget von 40 %.
        var budget = Finance.HasFinances(club) ? a.Budget : reference * 0.4;
        double Funding(int share, int defaultShare) => 3 + 11 * Math.Sqrt(Math.Clamp(budget * share / (reference * (double)defaultShare), 0, 3));
        var coaches = Enum.GetValues<YouthAgeGroup>().Select(g => (double?)GroupCoach(club, g)?.Ability ?? 4).Average();
        var coaching = Funding(a.ShareCoaching, 50) * 0.7 + coaches * 0.3;
        var facilities = 2 + Facilities.BuildingLevel(club, "youthCentre") * 2.2 + Facilities.BuildingLevel(club, "youthPitches") * 1.2
                         + Facilities.BuildingLevel(club, "boardingSchool") * 0.8 + Facilities.BuildingLevel(club, "youthDressing") * 0.5
                         + Facilities.BuildingLevel(club, "youthCanteen") * 0.4 + Facilities.Effect(club, "youth.development") * 8;
        var recruitment = Funding(a.ShareRecruitment, 30) * 0.75 + Facilities.BuildingLevel(club, "footballSchool") * 0.8
                          + Facilities.BuildingLevel(club, "boardingSchool") * 0.6 + club.Reputation * 0.15 + a.Reputation / 25;
        var education = Funding(a.ShareEducation, 20) * 0.7 + Facilities.BuildingLevel(club, "partnerSchool") * 1.5 + Facilities.BuildingLevel(club, "boardingSchool") * 1.0;
        return (Math.Clamp(coaching, 1, 20), Math.Clamp(facilities, 1, 20), Math.Clamp(recruitment, 1, 20), Math.Clamp(education, 1, 20));
    }

    /// <summary>Startwerte nach Vereinsgröße (neue Welt oder älterer Spielstand).</summary>
    public void InitAcademy(Club club)
    {
        var a = club.Academy;
        if (a.Initialized) return;
        a.Initialized = true;
        a.Budget = Finance.HasFinances(club) ? ReferenceAcademyBudget(club) : 0;
        var t = AcademyTargets(club);
        double Start(double target) => Math.Clamp(target + Rng.Normal(0, 1.2), 1, 20);
        a.Coaching = Start(t.coaching);
        a.Facilities = Start(t.facilities);
        a.Recruitment = Start(t.recruitment);
        a.Education = Start(t.education);
        a.Reputation = Math.Clamp(15 + club.Reputation * 2.5 + Rng.Next(-8, 9), 5, 90);
        ClubRules.SyncYouthValues(club);
        AssignYouthCoaches(club);
    }

    /// <summary>Jugendtrainer ohne Altersstufe übernehmen die erste freie (U19, U17, U15).</summary>
    public void AssignYouthCoaches(Club club)
    {
        foreach (var coach in World.Staff.Where(s => s.ClubId == club.Id && s.Role == StaffRole.YouthCoach && s.AgeGroup is null).OrderByDescending(s => s.Ability))
        {
            var free = new[] { YouthAgeGroup.U19, YouthAgeGroup.U17, YouthAgeGroup.U15 }.Where(g => GroupCoach(club, g) is null).ToList();
            if (free.Count == 0) break;
            coach.AgeGroup = free[0];
        }
    }

    /// <summary>Jahresbudget setzen (Mensch). Fehlertext-Schlüssel oder null.</summary>
    public string? SetAcademyBudget(long budget, int coaching, int recruitment, int education)
    {
        var a = HumanClub.Academy;
        if (budget < 0) return "fin.error.amount";
        if (coaching < 0 || recruitment < 0 || education < 0 || coaching + recruitment + education != 100) return "academy.error.shares";
        a.Budget = MarketValue.Round(budget);
        a.ShareCoaching = coaching;
        a.ShareRecruitment = recruitment;
        a.ShareEducation = education;
        a.BudgetSet = true;
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.Academy && !m.Resolved)) m.Resolved = true;
        return null;
    }

    /// <summary>Jugendtrainer einer Altersstufe zuordnen.</summary>
    public string? AssignYouthCoach(StaffMember coach, YouthAgeGroup group)
    {
        if (coach.ClubId != HumanClub.Id || coach.Role != StaffRole.YouthCoach) return "staff.error.notOwn";
        if (GroupCoach(HumanClub, group) is { } current && current != coach) current.AgeGroup = coach.AgeGroup;
        coach.AgeGroup = group;
        return null;
    }

    /// <summary>Saisonstart: Budget der KI nach Finanzlage; der Mensch bekommt einen Vorschlag in den Posteingang.</summary>
    private void AcademySeasonStart(Club club)
    {
        var a = club.Academy;
        InitAcademy(club);
        a.Reputation = Math.Clamp(a.Reputation * 0.97 + 20 * 0.03, 0, 100);
        // Absolventen, die sich bei den Profis durchgesetzt haben, stärken den Ruf der Akademie.
        foreach (var id in a.Graduates.Where(World.Players.ContainsKey))
            if (World.Players[id].History.LastOrDefault() is { Appearances: >= 15 } s && s.SeasonYear == World.SeasonYear - 1)
                a.Reputation = Math.Min(100, a.Reputation + 1.5);
        if (!Finance.HasFinances(club)) { a.Budget = 0; return; }
        var reference = ReferenceAcademyBudget(club);
        if (IsHuman(club))
        {
            a.BudgetSet = false;
            if (a.Budget <= 0) a.Budget = reference;
            Notify(MessageType.Youth, "msg.sender.youthCoach", TR("msg.academyBudget.subject"),
                TR("msg.academyBudget.body", TextArg.Money(a.Budget, Currency), TextArg.Money(reference, Currency), a.Stars, TextArg.Num(a.Rating, "0.0")),
                MessageAction.Academy);
            return;
        }
        var factor = club.Budget.Balance < 0 ? 0.6 : club.Reputation >= 14 ? 1.1 : 0.9;
        if (club.Board.Members.Any(m => m.Trait == BoardTrait.YouthFriendly)) factor += 0.15;
        // Zertifizierung unter der Lizenzvorgabe: mehr Geld ins Leistungszentrum.
        if (a.Stars < Finance.DivisionFinance(club).YouthCenterRequired) factor += 0.5;
        a.Budget = MarketValue.Round(reference * factor);
    }

    /// <summary>Wöchentlicher Fortschritt: Trainer, Infrastruktur und Jugendtrainer der Altersstufe, ältere lernen mehr.</summary>
    private void TrainYouth()
    {
        foreach (var club in World.Clubs.Values)
        {
            var a = club.Academy;
            var baseFactor = 0.5 + a.Coaching / 20 * 0.45 + a.Facilities / 20 * 0.35;
            var coachBonus = Enum.GetValues<YouthAgeGroup>().ToDictionary(g => g, g => (GroupCoach(club, g)?.Ability ?? 4) / 20.0 * 0.25);
            foreach (var p in YouthOf(club))
            {
                if (p.State.Injury is not null) continue;
                var group = AgeGroup(p);
                var ageFactor = group switch { YouthAgeGroup.U15 => 0.85, YouthAgeGroup.U17 => 1.0, _ => 1.1 };
                var g = Training.SessionGain(club, p, TrainingIntensity.Normal) * 7 * (baseFactor + coachBonus[group]) * ageFactor;
                var gk = p.MainPosition == Position.GK;
                p.Training.Technical += g * (gk ? 0.2 : 0.4);
                p.Training.Mental += g * 0.3;
                p.Training.Physical += g * (group == YouthAgeGroup.U15 ? 0.2 : 0.3);
                if (gk) p.Training.Goalkeeping += g * 0.4;
                Training.ApplyProgress(p);
            }
        }
    }

    /// <summary>
    /// Neuer Jahrgang (14-Jährige in die U15): Anzahl nach Sichtung und Platz, Talent nach Sichtung, Ruf der
    /// Akademie und Vereinsgröße, 2 % "goldene Generation"; gute Sichtung bringt zusätzlich Quereinsteiger.
    /// </summary>
    private void YouthIntake(Club club, int min, int max, bool initial = false, int? fixedAge = null)
    {
        InitAcademy(club);
        var a = club.Academy;
        var y = World.Country(club.CountryCode).Youth;
        var age = fixedAge ?? y.IntakeAge;
        var group = GroupOf(age);
        var count = Math.Clamp(min + (int)Math.Round((max - min) * a.Recruitment / 20.0) + Rng.Next(-2, 2), min, max + (int)Facilities.Effect(club, "youth.count"));
        if (!initial) count = Math.Min(count, GroupCapacity(club, group) - YouthGroup(club, group).Count + 4);
        if (count <= 0) return;
        var golden = !initial && Rng.Chance(0.02);
        var created = new List<Player>();
        for (var i = 0; i < count; i++) created.Add(CreateYouth(club, age, golden, 0));
        // Quereinsteiger: gute Sichtung holt Talente aus kleineren Vereinen.
        if (!initial && a.Recruitment >= 11 && Rng.Chance((a.Recruitment - 10) / 10))
            for (var i = Rng.Next(1, 3); i > 0; i--) created.Add(CreateYouth(club, Rng.Next(15, 17), false, 0.6));
        if (club.Id != HumanClub.Id || initial) return;
        var best = created.OrderByDescending(p => p.Hidden.PotentialAbility).Take(3).Select(p => (object?)TR("msg.youthIntake.line", p.Name, TR("pos." + p.MainPosition)));
        Notify(MessageType.Youth, "msg.sender.youthCoach", TR("msg.youthIntake.subject"),
            TR(golden ? "msg.youthIntake.golden" : "msg.youthIntake.body", created.Count, TextArg.Join(", ", best)),
            MessageAction.YouthIntake);
    }

    private Player CreateYouth(Club club, int age, bool golden, double extraLevel)
    {
        var a = club.Academy;
        var level = PlayerGenerator.ClubLevel(club.Reputation) - 5 - (16 - age) * 0.5 + a.Recruitment / 20.0 * 2 + a.Reputation / 100 + extraLevel;
        var pos = PlayerGenerator.SquadTemplate[Rng.WeightedIndex(PlayerGenerator.SquadTemplate.Select(t => (double)t.count).ToList())].pos;
        var p = PlayerGenerator.Create(World, Data, Rng, pos, Math.Clamp(level + Rng.Normal(0, 1.2), 1.5, 14), age, club.CountryCode);
        var bonus = (golden ? 25 : 0) + (int)(a.Recruitment / 20.0 * 10) + (int)(a.Reputation / 10) + Rng.Next(-6, 9) + (16 - age) * 3;
        // Im Mittel erreicht ein Talent etwa die Stärke des Vereins; mehr Spielraum ließe jede Generation stärker werden (Review F-02).
        p.Hidden.PotentialAbility = Math.Clamp(p.Hidden.CurrentAbility + Rng.Next(5, 35) + bonus, p.Hidden.CurrentAbility, 200);
        p.Hidden.Professionalism = Math.Clamp(p.Hidden.Professionalism + (int)Facilities.Effect(club, "youth.professionalism") + (int)Math.Round((a.Education - 10) / 3), 1, 20);
        p.IsYouth = true;
        p.Reputation = 1;
        p.ClubId = club.Id;
        Registration.InitFormative(p, World, club, Rng, youth: true);
        p.Contract = new Contract { WeeklyWage = age >= 16 ? 150 : 50, Start = World.Date, Until = new DateOnly(World.SeasonYear + 1, 6, 30), Youth = true, Status = SquadStatus.Prospect };
        p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
        World.Players[p.Id] = p;
        club.YouthIds.Add(p.Id);
        return p;
    }

    /// <summary>Neue Spielwelt: Jahrgänge 14 bis 18; ältere Jahrgänge sind durch Aussortieren schon kleiner.</summary>
    public void InitialYouth()
    {
        foreach (var club in World.Clubs.Values.Where(c => c.YouthIds.Count == 0))
        {
            InitAcademy(club);
            var y = World.Country(club.CountryCode).Youth;
            var full = Finance.HasFinances(club);
            for (var age = y.IntakeAge; age <= 18; age++)
            {
                if (!full && age < 16) continue;
                // Ältere Jahrgänge sind durch Abbrecher und Aussortieren kleiner (etwa wie im laufenden Spiel).
                var keep = age switch { <= 15 => 1.0, <= 16 => 0.9, <= 17 => 0.85, _ => 0.75 };
                YouthIntake(club, (int)(y.MinPlayers * keep), (int)Math.Max(1, y.MaxPlayers * keep), initial: true, fixedAge: age);
            }
        }
    }

    /// <summary>Saisonwechsel: Absolventen, Freigaben mit 19, Aussortieren bei vollen Altersstufen, Abbrecher.</summary>
    public void YouthSeasonStart(Club club)
    {
        AcademySeasonStart(club);
        var youth = YouthOf(club);
        var proAge = World.Country(club.CountryCode).Youth.ProContractAge;
        if (!IsHuman(club))
        {
            var level = PlayerGenerator.ClubLevel(club.Reputation);
            var promote = youth.Where(p => p.Age(World.Date) >= proAge)
                .Where(p => Transfers.Rating(p) >= level - 2.0 || p.Age(World.Date) >= 18 && p.Hidden.PotentialAbility >= p.Hidden.CurrentAbility + 40)
                .OrderByDescending(p => p.Hidden.PotentialAbility).Take(2).ToList();
            foreach (var p in promote) PromoteYouthInternal(club, p);
        }
        var human = club.Id == HumanClub.Id && !Board.AwaitingNewJob;
        // Mit 19 endet die Jugendzeit: wer bis dahin keinen Profivertrag hat, wird freigegeben.
        foreach (var p in YouthOf(club).Where(p => p.Age(World.Date) >= ReleaseAge).ToList())
        {
            ReleaseYouthInternal(p);
            if (human) Notify(MessageType.Youth, "msg.sender.youthCoach", TR("msg.youthReleased.subject", p.Name), TR("msg.youthReleased.body", p.Name, ReleaseAge));
        }
        // Abbrecher: Schule und Betreuung halten die Jugendlichen.
        var dropout = 0.05 * (1 - club.Academy.Education / 25);
        var left = YouthOf(club).Where(_ => Rng.Chance(dropout)).ToList();
        // Volle Altersstufen: die schwächsten Talente werden aussortiert.
        foreach (var group in Enum.GetValues<YouthAgeGroup>())
        {
            var members = YouthGroup(club, group).Except(left).ToList();
            var over = members.Count - GroupCapacity(club, group);
            if (over > 0) left.AddRange(members.OrderBy(p => p.Hidden.PotentialAbility + p.Hidden.CurrentAbility).Take(over));
        }
        foreach (var p in left.Distinct()) ReleaseYouthInternal(p);
        if (human && left.Count > 0)
            Notify(MessageType.Youth, "msg.sender.youthCoach", TR("msg.youthCut.subject", left.Count),
                TR("msg.youthCut.body", TextArg.Join(", ", left.Select(p => (object?)p.Name))));
    }

    public void PromoteYouthInternal(Club club, Player p)
    {
        club.YouthIds.Remove(p.Id);
        if (!club.PlayerIds.Contains(p.Id)) club.PlayerIds.Add(p.Id);
        p.IsYouth = false;
        p.Reputation = Math.Max(p.Reputation, 2);
        var wageLevel = ClubRules.DivisionFinance(World, club).WageLevel;
        var wage = (int)(Math.Round(MarketValue.FairWage(p, World, Data.PositionProfiles) * 0.6 * wageLevel / 100) * 100);
        Transfers.SetContract(p, Transfers.MakeContract(new ContractOffer { Years = 3, WeeklyWage = Math.Max((int)(500 * wageLevel), wage), Status = SquadStatus.Prospect }, World.Date, club));
        if (!club.Academy.Graduates.Contains(p.Id))
        {
            club.Academy.Graduates.Add(p.Id);
            club.Academy.Reputation = Math.Min(100, club.Academy.Reputation + 2);
        }
    }

    private void ReleaseYouthInternal(Player p)
    {
        Transfers.SetContract(p, null);
        Season.RemoveFromClub(p);
        p.IsYouth = false;
        // Wer es nicht in den Profibereich schafft, spielt meist im Amateurfußball weiter.
        if (Transfers.Rating(p) < PlayerGenerator.ClubLevel(1) - 2.5 && Rng.Chance(0.7)) p.Retired = true;
    }

    /// <summary>Profivertrag für ein Talent (ab dem Mindestalter). Fehlertext-Schlüssel oder null.</summary>
    public string? PromoteYouth(Player p)
    {
        var club = HumanClub;
        if (!club.YouthIds.Contains(p.Id)) return "youth.error.notYouth";
        if (p.Age(World.Date) < World.Country(club.CountryCode).Youth.PromoteAge) return "youth.error.tooYoung";
        if (club.PlayerIds.Count >= World.Country(club.CountryCode).Squad.MaxSquad) return "youth.error.squadFull";
        PromoteYouthInternal(club, p);
        Notify(MessageType.Youth, "msg.sender.youthCoach", TR("msg.youthPromoted.subject", p.Name), TR("msg.youthPromoted.body", p.Name));
        return null;
    }

    public string? ReleaseYouth(Player p)
    {
        if (!HumanClub.YouthIds.Contains(p.Id)) return "youth.error.notYouth";
        ReleaseYouthInternal(p);
        return null;
    }

    /// <summary>Profi zurück in die Jugend (nur bis 18).</summary>
    public string? DemoteToYouth(Player p)
    {
        var club = HumanClub;
        if (!club.PlayerIds.Contains(p.Id) || p.Loan is not null) return "youth.error.notOwn";
        if (p.Age(World.Date) > 18) return "youth.error.tooOld";
        Season.RemoveFromClub(p);
        p.ClubId = club.Id;
        p.IsYouth = true;
        club.YouthIds.Add(p.Id);
        return null;
    }
}
