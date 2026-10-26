using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Jugendarbeit (Kapitel 7): jährlicher Jahrgang im März, Förderung, Profiverträge, Freigaben.</summary>
public sealed partial class GameSession
{
    public const int MaxYouthSquad = 30;
    public const int ReleaseAge = 19;

    public IReadOnlyList<Player> YouthOf(Club club) => club.YouthIds.Select(id => World.Players[id]).ToList();

    private void YouthDaily()
    {
        if (World.Date.DayOfWeek == DayOfWeek.Monday) TrainYouth();
        foreach (var country in World.Countries)
        {
            var y = country.Youth;
            if (World.Date.Month != y.IntakeMonth || World.Date.Day != 15) continue;
            foreach (var club in World.Clubs.Values.Where(c => c.CountryCode == country.Code))
                YouthIntake(club, y.MinPlayers, y.MaxPlayers);
        }
    }

    /// <summary>Wöchentlicher Fortschritt im Nachwuchsleistungszentrum: Qualität der Einrichtungen statt Trainingsplan.</summary>
    private void TrainYouth()
    {
        foreach (var club in World.Clubs.Values)
        {
            var facilities = 0.6 + club.YouthFacilities / 20.0 * 0.8;
            foreach (var p in YouthOf(club))
            {
                if (p.State.Injury is not null) continue;
                var g = SessionGain(club, p, TrainingIntensity.Normal) * 7 * facilities;
                var gk = p.MainPosition == Position.GK;
                p.Training.Technical += g * (gk ? 0.2 : 0.4);
                p.Training.Mental += g * 0.3;
                p.Training.Physical += g * 0.3;
                if (gk) p.Training.Goalkeeping += g * 0.4;
                ApplyProgress(p);
            }
        }
    }

    /// <summary>Neuer Jahrgang: Anzahl nach Rekrutierung, Qualität nach Einrichtungen und Reputation; 2 % "goldene Generation".</summary>
    private void YouthIntake(Club club, int min, int max, bool initial = false)
    {
        var count = Math.Clamp(min + (int)Math.Round((max - min) * club.YouthRecruitment / 20.0) + _rng.Next(-2, 2), min, max + (int)Effect(club, "youth.count"));
        count = Math.Min(count, MaxYouthSquad - club.YouthIds.Count);
        if (count <= 0) return;
        var golden = _rng.Chance(0.02);
        var level = PlayerGenerator.ClubLevel(club.Reputation) - 4.5 + club.YouthFacilities / 20.0 * 1.5;
        var created = new List<Player>();
        for (var i = 0; i < count; i++)
        {
            var pos = PlayerGenerator.SquadTemplate[_rng.WeightedIndex(PlayerGenerator.SquadTemplate.Select(t => (double)t.count).ToList())].pos;
            var age = initial ? _rng.Next(15, 18) : _rng.Next(15, 16);
            var p = PlayerGenerator.Create(World, Data, _rng, pos, Math.Clamp(level + _rng.Normal(0, 1.2), 2, 14), age, club.CountryCode);
            var bonus = (golden ? 25 : 0) + (int)(club.YouthRecruitment / 20.0 * 8) + _rng.Next(-5, 10);
            p.Hidden.PotentialAbility = Math.Clamp(p.Hidden.CurrentAbility + _rng.Next(15, 45) + bonus, p.Hidden.CurrentAbility, 200);
            p.Hidden.Professionalism = Math.Clamp(p.Hidden.Professionalism + (int)Effect(club, "youth.professionalism"), 1, 20);
            p.IsYouth = true;
            p.Reputation = 1;
            p.ClubId = club.Id;
            p.Contract = new Contract { WeeklyWage = 100, Start = World.Date, Until = new DateOnly(World.SeasonYear + 1, 6, 30), Youth = true, Status = SquadStatus.Prospect };
            p.SeasonStats = new PlayerSeasonStats { SeasonYear = World.SeasonYear, ClubId = club.Id };
            World.Players[p.Id] = p;
            club.YouthIds.Add(p.Id);
            created.Add(p);
        }
        if (club.Id != HumanClub.Id || initial) return;
        var best = created.OrderByDescending(p => p.Hidden.PotentialAbility).Take(3).Select(p => (object?)TR("msg.youthIntake.line", p.Name, TR("pos." + p.MainPosition)));
        Notify(MessageType.Youth, "msg.sender.youthCoach", TR("msg.youthIntake.subject"),
            TR(golden ? "msg.youthIntake.golden" : "msg.youthIntake.body", created.Count, TextArg.Join(", ", best)),
            MessageAction.YouthIntake);
    }

    /// <summary>Neue Spielwelt: jeder Verein startet mit einer Jugendmannschaft.</summary>
    private void InitialYouth()
    {
        foreach (var club in World.Clubs.Values.Where(c => c.YouthIds.Count == 0))
        {
            var y = World.Country(club.CountryCode).Youth;
            YouthIntake(club, y.MinPlayers * 2, y.MaxPlayers * 2, initial: true);
        }
    }

    /// <summary>Saisonwechsel: KI übernimmt Talente, zu alte Jugendspieler werden freigegeben.</summary>
    private void YouthSeasonStart(Club club)
    {
        var youth = YouthOf(club);
        var proAge = World.Country(club.CountryCode).Youth.ProContractAge;
        if (!IsHuman(club))
        {
            var level = PlayerGenerator.ClubLevel(club.Reputation);
            var promote = youth.Where(p => p.Age(World.Date) >= proAge)
                .Where(p => Rating(p) >= level - 2.0 || p.Age(World.Date) >= 18 && p.Hidden.PotentialAbility >= p.Hidden.CurrentAbility + 40)
                .OrderByDescending(p => p.Hidden.PotentialAbility).Take(2).ToList();
            foreach (var p in promote) PromoteYouthInternal(club, p);
        }
        // KI-Vereine geben nicht übernommene 18-Jährige frei, der Mensch entscheidet bis 19 selbst.
        var releaseAge = IsHuman(club) ? ReleaseAge : ReleaseAge - 1;
        foreach (var p in YouthOf(club).Where(p => p.Age(World.Date) >= releaseAge).ToList())
        {
            ReleaseYouthInternal(p);
            if (club.Id == HumanClub.Id)
                Notify(MessageType.Youth, "msg.sender.youthCoach", TR("msg.youthReleased.subject", p.Name), TR("msg.youthReleased.body", p.Name, ReleaseAge));
        }
    }

    private void PromoteYouthInternal(Club club, Player p)
    {
        club.YouthIds.Remove(p.Id);
        if (!club.PlayerIds.Contains(p.Id)) club.PlayerIds.Add(p.Id);
        p.IsYouth = false;
        p.Reputation = Math.Max(p.Reputation, 2);
        var wage = (int)(Math.Round(MarketValue.FairWage(p, Data.PositionProfiles) * 0.6 / 100) * 100);
        p.Contract = MakeContract(new ContractOffer { Years = 3, WeeklyWage = Math.Max(500, wage), Status = SquadStatus.Prospect }, World.Date);
    }

    private void ReleaseYouthInternal(Player p)
    {
        RemoveFromClub(p);
        p.IsYouth = false;
        p.Contract = null;
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
        RemoveFromClub(p);
        p.ClubId = club.Id;
        p.IsYouth = true;
        club.YouthIds.Add(p.Id);
        return null;
    }
}
