using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>
/// Scouting (Kapitel 7): Kenntnisstand je Spieler (0–100 %) bestimmt, wie genau Attribute angezeigt werden.
/// Scouts beobachten Spieler, durchsuchen Ligen oder analysieren den nächsten Gegner.
/// </summary>
public sealed partial class GameSession
{
    public const int WatchDays = 7;
    public const int SearchDays = 21;
    public const int OpponentDays = 3;

    public IReadOnlyList<Scout> HumanScouts => World.Scouts.Where(s => s.ClubId == HumanClub.Id).ToList();

    public ScoutAssignment? AssignmentOf(Scout s) => World.ScoutAssignments.FirstOrDefault(a => a.ScoutId == s.Id);

    /// <summary>Kenntnisstand des menschlichen Managers über einen Spieler.</summary>
    public int Knowledge(Player p)
    {
        if (p.ClubId == HumanClub.Id) return 100;
        var baseline = 10;
        if (p.ClubId is { } cid && World.Clubs.TryGetValue(cid, out var club))
            baseline = club.LeagueId == HumanClub.LeagueId ? 25 : 10;
        if (p.Reputation >= 15) baseline += 15;
        return World.Knowledge.TryGetValue(p.Id, out var k) ? Math.Max(k, baseline) : baseline;
    }

    private void AddKnowledge(Player p, int amount) =>
        World.Knowledge[p.Id] = Math.Clamp(Knowledge(p) + amount, 0, 100);

    /// <summary>
    /// Sichtbarer Bereich eines Attributs. Bei 100 % Kenntnis exakt; sonst eine stabile Spanne,
    /// die den wahren Wert enthält (Breite bis 8 Punkte).
    /// </summary>
    public (int min, int max) AttributeRange(Player p, Attr a)
    {
        var value = p.Attributes[a];
        var k = Knowledge(p);
        if (k >= 100) return (value, value);
        var width = (int)Math.Round((100 - k) / 100.0 * 8);
        if (width == 0) return (value, value);
        var bucket = (ulong)(k / 10);
        var h = Rng.Mix(Rng.Mix(World.Seed, (ulong)p.Id * 64 + (ulong)a), bucket);
        var below = (int)(h % (ulong)(width + 1));
        var lo = Math.Max(AttrInfo.Min, value - below);
        var hi = Math.Min(AttrInfo.Max, value + (width - below));
        return (lo, hi);
    }

    /// <summary>Ab 70 % Kenntnis ist die Stärke sichtbar, ab 50 % als Spanne.</summary>
    public bool StrengthKnown(Player p) => Knowledge(p) >= 70;

    // ------------------------------------------------------------------ Aufträge

    public string? AssignScout(Scout scout, ScoutTask task, int? playerId = null, string? leagueId = null, Position? position = null, int maxAge = 40)
    {
        if (scout.ClubId != HumanClub.Id) return "scout.error.notOwn";
        if (task == ScoutTask.WatchPlayer && (playerId is null || !World.Players.ContainsKey(playerId.Value))) return "scout.error.player";
        if (task == ScoutTask.NextOpponent && NextFixture(HumanClub) is null) return "scout.error.noFixture";
        World.ScoutAssignments.RemoveAll(a => a.ScoutId == scout.Id);
        var days = task switch { ScoutTask.WatchPlayer => WatchDays, ScoutTask.SearchRegion => SearchDays, _ => OpponentDays };
        World.ScoutAssignments.Add(new ScoutAssignment
        {
            Id = World.NextAssignmentId++, ScoutId = scout.Id, Task = task, PlayerId = playerId,
            LeagueId = leagueId, Position = position, MaxAge = maxAge,
            Start = World.Date, End = World.Date.AddDays(days),
        });
        return null;
    }

    public void CancelAssignment(Scout scout) => World.ScoutAssignments.RemoveAll(a => a.ScoutId == scout.Id);

    private void ScoutingDaily()
    {
        foreach (var a in World.ScoutAssignments.ToList())
        {
            var scout = World.Scouts.FirstOrDefault(s => s.Id == a.ScoutId);
            if (scout is null) { World.ScoutAssignments.Remove(a); continue; }
            var human = scout.ClubId == HumanClub.Id;
            if (a.Task == ScoutTask.WatchPlayer && a.PlayerId is { } pid && World.Players.TryGetValue(pid, out var watched) && human)
                AddKnowledge(watched, 6 + scout.JudgingAbility / 4);
            if (World.Date < a.End) continue;
            World.ScoutAssignments.Remove(a);
            if (!human) continue;
            switch (a.Task)
            {
                case ScoutTask.WatchPlayer when a.PlayerId is { } id && World.Players.TryGetValue(id, out var p):
                    var report = MakeReport(scout, p);
                    Notify(MessageType.Scouting, "msg.sender.scout", TR("msg.scoutReport.subject", p.Name),
                        TR("msg.scoutReport.body", scout.Name, p.Name, report.Recommendation.ToString(), report.SummaryRef),
                        MessageAction.ScoutReport, p.Id);
                    break;
                case ScoutTask.SearchRegion:
                    SearchRegion(scout, a);
                    break;
                case ScoutTask.NextOpponent:
                    OpponentReport(scout);
                    break;
            }
        }
    }

    private void SearchRegion(Scout scout, ScoutAssignment a)
    {
        var level = PlayerGenerator.ClubLevel(HumanClub.Reputation);
        var found = World.Players.Values
            .Where(p => !p.Retired && !p.IsYouth && p.ClubId != HumanClub.Id)
            .Where(p => a.LeagueId is null || (p.ClubId is { } c && World.Clubs[c].LeagueId == a.LeagueId) || (a.LeagueId == "" && p.ClubId is null))
            .Where(p => a.Position is null || p.MainPosition == a.Position || p.SuitabilityFor(a.Position.Value) >= Suitability.Good)
            .Where(p => p.Age(World.Date) <= a.MaxAge)
            .Select(p => (p, score: PerceivedRating(scout, p) + Math.Max(0, PerceivedPotential(scout, p) - PerceivedRating(scout, p)) * 0.4))
            .Where(x => x.score >= level - 2)
            .OrderByDescending(x => x.score)
            .Take(5)
            .Select(x => x.p)
            .ToList();
        if (found.Count == 0)
        {
            Notify(MessageType.Scouting, "msg.sender.scout", TR("msg.scoutSearch.subject"), TR("msg.scoutSearch.none", scout.Name));
            return;
        }
        var lines = new List<TextRef>();
        foreach (var p in found)
        {
            AddKnowledge(p, 30 + scout.JudgingAbility);
            var r = MakeReport(scout, p);
            lines.Add(TR("msg.scoutSearch.line", r.Recommendation.ToString(), p.Name, TR("pos." + p.MainPosition), p.Age(World.Date), ClubName(p)));
            if (!HumanClub.Shortlist.Contains(p.Id) && r.Recommendation is 'A' or 'B') HumanClub.Shortlist.Add(p.Id);
        }
        Notify(MessageType.Scouting, "msg.sender.scout", TR("msg.scoutSearch.subject"),
            TR("msg.scoutSearch.body", scout.Name, TextArg.Join("\n", lines)));
    }

    private TextArg ClubName(Player p) => p.ClubId is { } c ? TextArg.Literal(World.Clubs[c].Name) : TextArg.Key("transfer.freeAgent");

    private void OpponentReport(Scout scout)
    {
        if (NextFixture(HumanClub) is not { } f) return;
        var opp = World.Clubs[f.HomeId == HumanClub.Id ? f.AwayId : f.HomeId];
        var squad = World.SquadOf(opp).ToList();
        foreach (var p in squad) AddKnowledge(p, 15);
        var formation = Data.Formation(opp.Tactic.FormationId).Name;
        var best = squad.Where(p => p.IsAvailable).OrderByDescending(Rating).Take(3).Select(p => p.Name);
        var absent = squad.Where(p => !p.IsAvailable).OrderByDescending(Rating).Take(3).Select(p => p.Name).ToList();
        Notify(MessageType.Scouting, "msg.sender.scout", TR("msg.opponentReport.subject", opp.Name),
            TR("msg.opponentReport.body", scout.Name, opp.Name, formation, TR("mentality." + opp.Tactic.Mentality),
                string.Join(", ", best), absent.Count == 0 ? TR("ui.none") : TextRef.Literal(string.Join(", ", absent))));
    }

    // ------------------------------------------------------------------ Berichte

    /// <summary>Wahrgenommene Positionsstärke: Fehler hängt von der Urteilskraft des Scouts ab, stabil je Scout und Spieler.</summary>
    private double PerceivedRating(Scout scout, Player p)
    {
        var rng = new Rng(Rng.Mix(Rng.Mix(World.Seed, (ulong)scout.Id), (ulong)p.Id));
        return Rating(p) + rng.Normal(0, (20 - scout.JudgingAbility) / 20.0 * 1.6);
    }

    private double PerceivedPotential(Scout scout, Player p)
    {
        var rng = new Rng(Rng.Mix(Rng.Mix(World.Seed, (ulong)scout.Id + 1000), (ulong)p.Id));
        var potentialRating = 1 + p.Hidden.PotentialAbility * 19.0 / 200.0;
        return Math.Max(PerceivedRating(scout, p), potentialRating + rng.Normal(0, (20 - scout.JudgingPotential) / 20.0 * 2.2));
    }

    /// <summary>Sterne relativ zum eigenen Verein: 3 Sterne = Stammspielerniveau, 5 = Leistungsträger.</summary>
    private double Stars(double rating)
    {
        var level = PlayerGenerator.ClubLevel(HumanClub.Reputation);
        return Math.Clamp(Math.Round((3 + (rating - level) / 1.2) * 2) / 2, 0.5, 5);
    }

    public ScoutReport MakeReport(Scout scout, Player p)
    {
        var strength = Stars(PerceivedRating(scout, p));
        var potential = Math.Max(strength, Stars(PerceivedPotential(scout, p)));
        var age = p.Age(World.Date);
        var score = age <= 23 ? strength * 0.5 + potential * 0.5 : strength;
        var rec = score >= 4 ? 'A' : score >= 3 ? 'B' : score >= 2 ? 'C' : 'D';
        var summaryKey = rec switch
        {
            'A' => "scout.summary.A",
            'B' => potential - strength >= 1.5 ? "scout.summary.B.talent" : "scout.summary.B",
            'C' => "scout.summary.C",
            _ => "scout.summary.D",
        };
        var report = new ScoutReport
        {
            PlayerId = p.Id, Date = World.Date, StrengthStars = strength, PotentialStars = potential,
            Recommendation = rec, SummaryRef = TR(summaryKey, p.ShortName),
        };
        World.ScoutReports.RemoveAll(r => r.PlayerId == p.Id);
        World.ScoutReports.Add(report);
        return report;
    }

    public ScoutReport? ReportFor(Player p) => World.ScoutReports.FirstOrDefault(r => r.PlayerId == p.Id);

    /// <summary>Neue Scouts für einen Verein (Anzahl je Reputation).</summary>
    public static void CreateScouts(World world, Club club, Data.GameData data, Rng rng, int count)
    {
        var names = data.Names.TryGetValue(club.CountryCode, out var n) ? n : data.Names.Values.First();
        for (var i = 0; i < count; i++)
        {
            var quality = Math.Clamp((int)Math.Round(6 + club.Reputation * 0.5 + rng.Normal(0, 2)), 3, 20);
            world.Scouts.Add(new Scout
            {
                Id = world.NextScoutId++, ClubId = club.Id,
                Name = $"{rng.Pick(names.FirstNames)} {rng.Pick(names.LastNames)}",
                JudgingAbility = quality, JudgingPotential = Math.Clamp(quality + rng.Next(-3, 3), 3, 20),
                RegionKnowledge = Math.Clamp(quality + rng.Next(-2, 4), 3, 20), Region = club.CountryCode,
            });
        }
    }
}
