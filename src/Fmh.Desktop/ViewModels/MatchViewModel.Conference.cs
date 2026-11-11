using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;
using Fmh.MatchEngine;

namespace Fmh.Desktop.ViewModels;

/// <summary>Konferenz: Zwischenstände der anderen Partien, Torblitz und Live-Tabelle.</summary>
public sealed partial class MatchViewModel
{
    [ObservableProperty] private bool _isConference;
    [ObservableProperty] private IReadOnlyList<ConferenceLine> _conferenceGames = Array.Empty<ConferenceLine>();
    [ObservableProperty] private IReadOnlyList<LiveTableRow> _liveTable = Array.Empty<LiveTableRow>();
    public bool HasLiveTable => LiveTable.Count > 0;
    partial void OnLiveTableChanged(IReadOnlyList<LiveTableRow> value) => OnPropertyChanged(nameof(HasLiveTable));

    /// <summary>Konferenz: Zwischenstände der anderen Partien, Torblitz und Live-Tabelle zur aktuellen Spielminute.</summary>
    private void UpdateConference(IMatchSimulation sim)
    {
        var world = Session.World;
        var minute = sim.IsFinished ? 200 : sim.Minute;
        var others = world.AllFixtures.Where(f => f.CompetitionId == _fixture.CompetitionId && f.Date == _fixture.Date && f != _fixture && f.Result is not null).ToList();
        var partial = new Dictionary<Fixture, (int home, int away)>();
        var lines = new List<ConferenceLine>();
        foreach (var f in others)
        {
            var goals = f.Result!.Goals.Where(g => g.Minute <= minute).ToList();
            var score = (goals.Count(g => g.Home), goals.Count(g => !g.Home));
            partial[f] = score;
            var fresh = goals.Any(g => g.Minute > minute - 3);
            for (var i = 0; i < goals.Count; i++)
            {
                if (!_announced.Add((f.Id, i))) continue;
                var g = goals[i];
                var scorer = world.Players.TryGetValue(g.ScorerId, out var p) ? p.ShortName : "";
                Ticker.Insert(0, new TickerLine($"{g.Minute}'", T.Format("match.conferenceGoal", world.Clubs[f.HomeId].Name,
                        $"{goals.Take(i + 1).Count(x => x.Home)}:{goals.Take(i + 1).Count(x => !x.Home)}", world.Clubs[f.AwayId].Name, scorer),
                    new SolidColorBrush(P.Gold), new SolidColorBrush(P.Gold), FontWeight.SemiBold));
            }
            lines.Add(new ConferenceLine(world.Clubs[f.HomeId].Name, $"{score.Item1} : {score.Item2}", world.Clubs[f.AwayId].Name,
                new SolidColorBrush(fresh ? P.Gold : P.Text), fresh ? FontWeight.Bold : FontWeight.Normal));
        }
        ConferenceGames = lines;
        partial[_fixture] = (sim.Home.Goals, sim.Away.Goals);

        if (_fixture.Kind != CompetitionKind.League || world.FindLeague(_fixture.CompetitionId) is not { } league)
        {
            LiveTable = Array.Empty<LiveTableRow>();
            return;
        }
        var rows = league.ClubIds.ToDictionary(id => id, _ => (pts: 0, gf: 0, ga: 0));
        void Add(int home, int away, int hg, int ag)
        {
            var h = rows[home];
            var a = rows[away];
            rows[home] = (h.pts + (hg > ag ? 3 : hg == ag ? 1 : 0), h.gf + hg, h.ga + ag);
            rows[away] = (a.pts + (ag > hg ? 3 : hg == ag ? 1 : 0), a.gf + ag, a.ga + hg);
        }
        foreach (var f in league.Fixtures.Where(f => f.Result is not null && f.Date < _fixture.Date))
            Add(f.HomeId, f.AwayId, f.Result!.HomeGoals, f.Result.AwayGoals);
        foreach (var (f, score) in partial) Add(f.HomeId, f.AwayId, score.home, score.away);
        var own = Session.HumanClub.Id;
        LiveTable = rows.Select(kv => (id: kv.Key, pts: kv.Value.pts - league.Deductions.GetValueOrDefault(kv.Key), kv.Value.gf, kv.Value.ga))
            .OrderByDescending(r => r.pts).ThenByDescending(r => r.gf - r.ga).ThenByDescending(r => r.gf)
            .Select((r, i) => new LiveTableRow($"{i + 1}.", world.Clubs[r.id].ShortName, $"{r.gf}:{r.ga}", r.pts.ToString(),
                r.id == own ? FontWeight.Bold : FontWeight.Normal,
                new SolidColorBrush(r.id == own ? P.Gold : partial.Keys.Any(f => f.Involves(r.id)) ? P.Text : P.Muted)))
            .ToList();
    }
}
