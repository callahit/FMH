using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Trainingswoche, Individualtraining, Umschulung, Taktikvertrautheit und Trainingslager (Kapitel 5).</summary>
public sealed partial class GameSession
{
    /// <summary>Vertrautheit eines Vereins mit einer Formation (0–100). Unbekannte Formationen starten bei 30.</summary>
    public double Familiarity(Club club, string formationId) =>
        club.Familiarity.TryGetValue(formationId, out var v) ? v : 30;

    public void AddFamiliarity(Club club, string formationId, double amount) =>
        club.Familiarity[formationId] = Math.Clamp(Familiarity(club, formationId) + amount, 0, 100);

    public static double IntensityFactor(TrainingIntensity i) => i switch
    {
        TrainingIntensity.Light => 0.7,
        TrainingIntensity.Hard => 1.3,
        _ => 1.0,
    };

    /// <summary>Konditionsverbrauch (positiv) bzw. Erholung (negativ) einer Einheit.</summary>
    public static double ConditionCost(TrainingBlock b, TrainingIntensity i) => b switch
    {
        TrainingBlock.Fitness => 5 * IntensityFactor(i),
        TrainingBlock.PracticeMatch => 6 * IntensityFactor(i),
        TrainingBlock.Technique or TrainingBlock.Finishing or TrainingBlock.Goalkeeping => 2.5 * IntensityFactor(i),
        TrainingBlock.Tactics or TrainingBlock.SetPieces => 1.5 * IntensityFactor(i),
        TrainingBlock.Recovery => -5,
        TrainingBlock.Rest => -2,
        _ => 0,
    };

    /// <summary>Ein Trainingstag für alle Vereine. Vereine mit Spiel trainieren heute nicht.</summary>
    private void RunTraining(HashSet<int> playing)
    {
        var monday = World.Date.DayOfWeek == DayOfWeek.Monday;
        foreach (var club in World.Clubs.Values)
        {
            if (monday) DecayFamiliarity(club);
            var camp = club.Camp is { } c && c.Start <= World.Date && c.End >= World.Date ? c : null;
            if (club.Camp is { } old && old.End < World.Date) club.Camp = null;
            if (playing.Contains(club.Id)) continue;

            var squad = World.SquadOf(club).Where(p => p.State.Injury is null).ToList();
            if (camp is not null)
            {
                RunCampDay(club, camp, squad);
                continue;
            }
            for (var session = 0; session < 2; session++)
            {
                var block = club.Training.At(World.Date.DayOfWeek, session);
                if (block == TrainingBlock.Rest) continue;
                RunSession(club, block, club.Training.Intensity, squad);
            }
        }
    }

    private void RunSession(Club club, TrainingBlock block, TrainingIntensity intensity, List<Player> squad)
    {
        var cost = ConditionCost(block, intensity);
        if (block == TrainingBlock.Tactics) AddFamiliarity(club, club.Tactic.FormationId, 0.8 * CoachFactor(club));
        if (block == TrainingBlock.PracticeMatch) AddFamiliarity(club, club.Tactic.FormationId, 0.4);

        foreach (var p in squad)
        {
            var gk = p.MainPosition == Position.GK;
            // Torhüter trainieren bei Technik und Abschluss mit dem Torwarttrainer.
            var effective = gk && block is TrainingBlock.Technique or TrainingBlock.Finishing ? TrainingBlock.Goalkeeping : block;
            var st = p.State;
            st.Condition = Math.Clamp(st.Condition - cost * (1.1 - p.Attributes[Attr.NaturalFitness] / 40.0), 35, 100);
            if (effective == TrainingBlock.PracticeMatch) st.MatchPractice = Math.Min(100, st.MatchPractice + 4);

            var gain = SessionGain(club, p, intensity);
            switch (effective)
            {
                case TrainingBlock.Fitness: p.Training.Physical += gain * 1.2; break;
                case TrainingBlock.Technique: p.Training.Technical += gain; break;
                case TrainingBlock.Tactics: p.Training.Mental += gain; break;
                case TrainingBlock.Finishing: p.Training.Technical += gain * 0.6; Raise(p, gain, Attr.Finishing, Attr.LongShots, Attr.Composure); break;
                case TrainingBlock.SetPieces: p.Training.Technical += gain * 0.4; Raise(p, gain, Attr.FreeKicks, Attr.Corners, Attr.Penalties); break;
                case TrainingBlock.Goalkeeping: if (gk) p.Training.Goalkeeping += gain * 1.2; break;
                case TrainingBlock.PracticeMatch: p.Training.Mental += gain * 0.5; p.Training.Technical += gain * 0.5; break;
            }
            if (p.Training.Focus is not null && effective is not (TrainingBlock.Recovery or TrainingBlock.Rest))
                p.Training.FocusProgress += gain * 0.35;

            ApplyProgress(p);
            Retrain(p);
            TrainingInjury(club, p, block, intensity);
        }
    }

    /// <summary>Gezielter Fortschritt in wenigen Attributen (Abschluss, Standards): Bruchteile wirken als Wahrscheinlichkeit.</summary>
    private void Raise(Player p, double gain, params Attr[] attrs)
    {
        if (!_rng.Chance(gain * 0.6)) return;
        var candidates = attrs.Where(a => p.Attributes[a] < AttrInfo.Max).ToList();
        if (candidates.Count == 0) return;
        p.Attributes[_rng.Pick(candidates)]++;
        var h = p.Hidden;
        h.CurrentAbility = Ratings.CurrentAbility(p, Data.PositionProfiles);
        if (h.PotentialAbility < h.CurrentAbility) h.PotentialAbility = h.CurrentAbility;
    }

    private double CoachFactor(Club club) => 0.7 + club.CoachQuality / 20.0 * 0.6;

    /// <summary>Fortschritt pro Einheit: Trainer, Intensität, Alter, Luft nach oben und Professionalität.</summary>
    private double SessionGain(Club club, Player p, TrainingIntensity intensity)
    {
        var h = p.Hidden;
        var headroom = Math.Max(0, h.PotentialAbility - h.CurrentAbility);
        if (headroom == 0) return 0;
        var age = p.Age(World.Date);
        var ageFactor = age switch { <= 19 => 1.3, <= 21 => 1.1, <= 24 => 0.8, <= 27 => 0.35, <= 30 => 0.12, _ => 0.04 };
        var pro = 0.6 + h.Professionalism / 20.0 * 0.8;
        var morale = 0.9 + (int)p.State.Morale * 0.033;
        return 0.014 * Math.Min(1.5, headroom / 35.0 + 0.15) * ageFactor * pro * morale * CoachFactor(club) * IntensityFactor(intensity);
    }

    /// <summary>Angesparter Fortschritt ab 1,0 hebt ein Attribut der Gruppe; Positionsgewichtung entscheidet welches.</summary>
    private void ApplyProgress(Player p)
    {
        var t = p.Training;
        var changed = false;
        changed |= Spend(p, () => t.Technical, v => t.Technical = v, AttrInfo.Technical);
        changed |= Spend(p, () => t.Mental, v => t.Mental = v, AttrInfo.Mental);
        changed |= Spend(p, () => t.Physical, v => t.Physical = v, AttrInfo.Physical);
        changed |= Spend(p, () => t.Goalkeeping, v => t.Goalkeeping = v, AttrInfo.Goalkeeping);
        if (t.Focus is { } focus && t.FocusProgress >= 1)
        {
            t.FocusProgress -= 1;
            if (p.Attributes[focus] < AttrInfo.Max) { p.Attributes[focus]++; changed = true; }
        }
        if (!changed) return;
        var h = p.Hidden;
        h.CurrentAbility = Ratings.CurrentAbility(p, Data.PositionProfiles);
        if (h.PotentialAbility < h.CurrentAbility) h.PotentialAbility = h.CurrentAbility;
    }

    private bool Spend(Player p, Func<double> get, Action<double> set, Attr[] group)
    {
        if (get() < 1) return false;
        set(get() - 1);
        var weights = Data.PositionProfiles.Weights.TryGetValue(p.MainPosition, out var w) ? w : new Dictionary<Attr, double>();
        var candidates = group.Where(a => p.Attributes[a] < AttrInfo.Max).ToList();
        if (candidates.Count == 0) return false;
        // Wichtige Attribute der Position steigen häufiger; niedrige Werte holen schneller auf.
        var scores = candidates.Select(a => (weights.TryGetValue(a, out var x) ? x : 0.2) + 0.15 + (20 - p.Attributes[a]) / 60.0).ToList();
        var pick = candidates[_rng.WeightedIndex(scores)];
        p.Attributes[pick]++;
        return true;
    }

    /// <summary>Positionsumschulung: je Eignungsstufe 60–180 Trainingstage, abhängig von der Anpassungsfähigkeit.</summary>
    private void Retrain(Player p)
    {
        if (p.Training.Retrain is not { } target) return;
        var current = p.SuitabilityFor(target);
        if (current == Suitability.Natural) { p.Training.Retrain = null; p.Training.RetrainProgress = 0; return; }
        var days = 180 - p.Hidden.Adaptability * 6;
        p.Training.RetrainProgress += 0.5 / days;
        if (p.Training.RetrainProgress < 1) return;
        p.Training.RetrainProgress = 0;
        p.Positions[target] = current + 1;
        if (p.ClubId == HumanClub.Id)
            Notify(MessageType.Training, "msg.sender.assistant", T.Format("msg.retrain.subject", p.Name),
                T.Format("msg.retrain.body", p.Name, T["pos." + target], T["suit." + (current + 1)]));
        if (current + 1 == Suitability.Natural) p.Training.Retrain = null;
    }

    private void TrainingInjury(Club club, Player p, TrainingBlock block, TrainingIntensity intensity)
    {
        if (block is TrainingBlock.Recovery or TrainingBlock.Rest or TrainingBlock.Tactics or TrainingBlock.SetPieces) return;
        var risk = 0.00035 * IntensityFactor(intensity) * IntensityFactor(intensity) * (0.5 + p.Hidden.InjuryProneness / 20.0) * World.Rules.InjuryFrequency;
        if (p.State.Condition < 70) risk *= 2;
        if (!_rng.Chance(risk)) return;
        var light = Data.Injuries.OrderBy(i => i.MaxDays).Take(Math.Max(1, Data.Injuries.Count / 2)).ToList();
        var type = light[_rng.WeightedIndex(light.Select(i => i.Weight).ToList())];
        InjurePlayer(p, type.Key, _rng.Next(type.MinDays, type.MaxDays), club.Id == HumanClub.Id && !p.IsYouth);
    }

    private void DecayFamiliarity(Club club)
    {
        foreach (var id in club.Familiarity.Keys.ToList())
            if (id != club.Tactic.FormationId && club.Familiarity[id] > 40)
                club.Familiarity[id] = Math.Max(40, club.Familiarity[id] - 0.5);
    }

    // ------------------------------------------------------------------ Trainingslager

    private void RunCampDay(Club club, TrainingCamp camp, List<Player> squad)
    {
        switch (camp.Goal)
        {
            case CampGoal.Recovery:
                foreach (var p in squad) p.State.Condition = Math.Min(100, p.State.Condition + 6);
                break;
            case CampGoal.TeamBuilding:
                AddFamiliarity(club, club.Tactic.FormationId, 1.2);
                if (World.Date == camp.End)
                    foreach (var p in squad) p.State.Morale = (Morale)Math.Min(6, (int)p.State.Morale + 1);
                break;
            case CampGoal.Fitness:
                RunSession(club, TrainingBlock.Fitness, TrainingIntensity.Hard, squad);
                RunSession(club, TrainingBlock.Recovery, TrainingIntensity.Normal, squad);
                break;
        }
    }

    public static long CampCost(Club club, int days) => MarketValue.Round(days * 12_000 * Math.Pow(1.2, club.Reputation - 5));

    /// <summary>Bucht ein Trainingslager (3–10 Tage, ohne Pflichtspiel im Zeitraum). Fehlertext-Schlüssel oder null.</summary>
    public string? BookCamp(CampGoal goal, DateOnly start, int days, string location)
    {
        var club = HumanClub;
        days = Math.Clamp(days, 3, 10);
        var end = start.AddDays(days - 1);
        if (start <= World.Date) return "camp.error.date";
        if (club.Camp is not null) return "camp.error.booked";
        if (AllFixtures.Any(f => f.Involves(club.Id) && f.Date >= start && f.Date <= end)) return "camp.error.fixture";
        var cost = CampCost(club, days);
        if (cost > club.Budget.Balance) return "neg.error.balance";
        club.Camp = new TrainingCamp { Goal = goal, Start = start, End = end, Cost = cost, Location = location };
        Book(club, TransactionType.Other, -cost, T.Format("tx.camp", location));
        return null;
    }

    public void CancelCamp()
    {
        if (HumanClub.Camp is not { } camp || camp.Start <= World.Date) return;
        Book(HumanClub, TransactionType.Other, camp.Cost / 2, T.Format("tx.campRefund", camp.Location));
        HumanClub.Camp = null;
    }
}
