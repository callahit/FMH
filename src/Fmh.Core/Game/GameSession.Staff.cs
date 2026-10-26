using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Personal (Kapitel 8): Trainerstab, Medizin, Psychologie, Presse, Platzwart mit Attributen und Verträgen.</summary>
public sealed partial class GameSession
{
    /// <summary>Planstellen je Rolle ohne Ausbau der Geschäftsstelle.</summary>
    public static int BaseSlots(StaffRole role) => role == StaffRole.Physio ? 2 : 1;

    public int StaffSlots(Club club, StaffRole role) =>
        BaseSlots(role) + (role is StaffRole.Physio or StaffRole.AssistantManager or StaffRole.FitnessCoach ? (int)Effect(club, "staff.slots") / 2 : 0);

    public IReadOnlyList<StaffMember> StaffOf(Club club) => World.Staff.Where(s => s.ClubId == club.Id).OrderBy(s => s.Role).ToList();

    public IEnumerable<StaffMember> StaffMarket => World.Staff.Where(s => s.ClubId is null).OrderByDescending(s => s.Ability);

    /// <summary>Beste Fähigkeit einer Rolle im Verein (0 = unbesetzt).</summary>
    public int StaffAbility(Club club, StaffRole role) =>
        World.Staff.Where(s => s.ClubId == club.Id && s.Role == role).Select(s => s.Ability).DefaultIfEmpty(0).Max();

    public static int StaffWage(int ability) => (int)(Math.Round(320 * Math.Pow(1.22, ability) / 100) * 100);

    private StaffMember NewStaff(StaffRole role, int ability, string country)
    {
        var names = Data.Names.TryGetValue(country, out var n) ? n : Data.Names.Values.First();
        return new StaffMember
        {
            Id = World.NextStaffId++, Role = role, Name = $"{_rng.Pick(names.FirstNames)} {_rng.Pick(names.LastNames)}",
            Age = _rng.Next(30, 64), Ability = Math.Clamp(ability, 1, 20), Motivation = Math.Clamp(ability + _rng.Next(-4, 4), 1, 20),
            WeeklyWage = StaffWage(ability), ContractUntil = new DateOnly(World.SeasonYear + _rng.Next(1, 3), 6, 30),
        };
    }

    /// <summary>Startpersonal nach Vereinsgröße; kleine Vereine besetzen nicht jede Stelle.</summary>
    public void CreateStaff(Club club)
    {
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            var needed = role switch
            {
                StaffRole.Psychologist => club.Reputation >= 12,
                StaffRole.PressOfficer => club.Reputation >= 7,
                StaffRole.Doctor => club.Reputation >= 5,
                StaffRole.GoalkeepingCoach => club.Reputation >= 5,
                StaffRole.Groundsman => club.Reputation >= 4,
                _ => true,
            };
            if (!needed) continue;
            for (var i = 0; i < (role == StaffRole.Physio && club.Reputation >= 10 ? 2 : 1); i++)
            {
                var s = NewStaff(role, (int)Math.Round(3 + club.Reputation * 0.75 + _rng.Normal(0, 2)), club.CountryCode);
                s.ClubId = club.Id;
                World.Staff.Add(s);
            }
        }
        RefreshStaffEffects(club);
    }

    /// <summary>Arbeitsmarkt für Personal: jedes Jahr frische Kandidaten.</summary>
    private void RefreshStaffMarket()
    {
        World.Staff.RemoveAll(s => s.ClubId is null && _rng.Chance(0.5));
        var country = World.Countries[0].Code;
        foreach (var role in Enum.GetValues<StaffRole>())
            for (var i = 0; i < 6; i++)
                World.Staff.Add(NewStaff(role, (int)Math.Round(_rng.Range(4, 19)), country));
    }

    public string? HireStaff(StaffMember s, int years = 2)
    {
        var club = HumanClub;
        if (s.ClubId is not null) return "staff.error.taken";
        if (World.Staff.Count(x => x.ClubId == club.Id && x.Role == s.Role) >= StaffSlots(club, s.Role)) return "staff.error.slots";
        if (Budgets.WageBill(club, World) + s.WeeklyWage > club.Budget.WageBudget * 1.1) return "neg.error.wageBudget";
        s.ClubId = club.Id;
        s.ContractUntil = new DateOnly(World.SeasonYear + Math.Clamp(years, 1, 4), 6, 30);
        RefreshStaffEffects(club);
        Notify(MessageType.Info, "msg.sender.assistant", TR("msg.staffHired.subject", s.Name), TR("msg.staffHired.body", s.Name, TR("staff." + s.Role)));
        return null;
    }

    /// <summary>Entlassung: Abfindung bis zu 26 Wochengehälter.</summary>
    public string? FireStaff(StaffMember s)
    {
        var club = HumanClub;
        if (s.ClubId != club.Id) return "staff.error.notOwn";
        var weeks = Math.Min(26, Math.Max(0, (s.ContractUntil.DayNumber - World.Date.DayNumber) / 7));
        Book(club, TransactionType.StaffWages, -(long)weeks * s.WeeklyWage, TR("tx.severance", s.Name));
        s.ClubId = null;
        RefreshStaffEffects(club);
        return null;
    }

    public static long Severance(StaffMember s, DateOnly today) =>
        (long)Math.Min(26, Math.Max(0, (s.ContractUntil.DayNumber - today.DayNumber) / 7)) * s.WeeklyWage;

    /// <summary>Überträgt Personal und Anlagen auf die Kennwerte, die Training und Jugend verwenden.</summary>
    public void RefreshStaffEffects(Club club)
    {
        var assistant = StaffAbility(club, StaffRole.AssistantManager);
        var fitness = StaffAbility(club, StaffRole.FitnessCoach);
        var gk = StaffAbility(club, StaffRole.GoalkeepingCoach);
        var license = IsHuman(club) ? (int)HumanManager.Career.License * 0.5 : 1;
        club.CoachQuality = (int)Math.Clamp(Math.Round(assistant * 0.55 + fitness * 0.25 + gk * 0.1 + license + Effect(club, "training.quality") * 20), 1, 20);
        var youthCoach = StaffAbility(club, StaffRole.YouthCoach);
        club.YouthFacilities = (int)Math.Clamp(Math.Round(2 + BuildingLevel(club, "youthCentre") * 2.2 + youthCoach * 0.3 + Effect(club, "youth.development") * 20), 1, 20);
        club.YouthRecruitment = (int)Math.Clamp(Math.Round(3 + Effect(club, "youth.count") * 3 + Effect(club, "youth.quality") * 1.5 + youthCoach * 0.25), 1, 20);
    }

    /// <summary>Saisonwechsel: auslaufende Personalverträge. KI verlängert gute Leute, ersetzt schwache.</summary>
    private void StaffSeasonStart()
    {
        foreach (var s in World.Staff.Where(s => s.ClubId is { } c && s.ContractUntil < World.Date).ToList())
        {
            var club = World.Clubs[s.ClubId!.Value];
            if (IsHuman(club))
            {
                s.ClubId = null;
                Notify(MessageType.Info, "msg.sender.assistant", TR("msg.staffLeft.subject", s.Name), TR("msg.staffLeft.body", s.Name, TR("staff." + s.Role)));
                continue;
            }
            if (s.Ability >= 3 + club.Reputation * 0.6 || _rng.Chance(0.5)) s.ContractUntil = new DateOnly(World.SeasonYear + _rng.Next(1, 3), 6, 30);
            else
            {
                s.ClubId = null;
                var replacement = StaffMarket.FirstOrDefault(x => x.Role == s.Role && x.Ability <= 5 + club.Reputation * 0.8);
                if (replacement is not null) { replacement.ClubId = club.Id; replacement.ContractUntil = new DateOnly(World.SeasonYear + 2, 6, 30); }
            }
        }
        RefreshStaffMarket();
        foreach (var club in World.Clubs.Values) RefreshStaffEffects(club);
    }
}
