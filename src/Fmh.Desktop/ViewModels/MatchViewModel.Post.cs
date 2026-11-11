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

/// <summary>Nachbericht: Noten, Spieler des Spiels, weitere Ergebnisse und Tabellenstand.</summary>
public sealed partial class MatchViewModel
{
    // ================================================================== Nachbericht

    private void Finish()
    {
        StopTimer();
        var sim = _sim!;
        ProcessEvents();
        Session.FinishMatch(_fixture, sim);
        Session.PlayOtherFixturesToday();

        GradeLine Grade(MatchPlayer mp)
        {
            var notes = new List<string>();
            if (mp.Goals > 0) notes.Add(T.Format("match.goalsShort", mp.Goals));
            if (mp.Assists > 0) notes.Add(T.Format("match.assistsShort", mp.Assists));
            if (mp.Red) notes.Add(T["match.redShort"]);
            else if (mp.Yellow) notes.Add(T["match.yellowShort"]);
            if (mp.Injured) notes.Add("✚");
            var name = mp.Started ? mp.Player.Name : $"↑ {mp.MinuteOn}'  {mp.Player.Name}";
            return new GradeLine(UiText.Position(mp.Position), name, UiText.Grade(mp.Grade), new SolidColorBrush(P.GradeColor(mp.Grade)), string.Join(" ", notes));
        }
        IEnumerable<MatchPlayer> Order(MatchTeamState t) => t.Participants.OrderBy(p => p.Started ? 0 : 1).ThenBy(p => p.MinuteOn).ThenBy(p => p.SlotIndex);
        HomeGrades = Order(sim.Home).Select(Grade).ToList();
        AwayGrades = Order(sim.Away).Select(Grade).ToList();
        var result = _fixture.Result!;
        if (sim.PlayerOfTheMatchId is { } potm && Session.World.Players.TryGetValue(potm, out var star))
            PlayerOfTheMatch = T.Format("match.potm", star.Name, Session.World.Clubs[star.ClubId ?? Home.Id].Name);
        var referee = Session.World.Referees.FirstOrDefault(r => r.Id == _fixture.RefereeId);
        PostInfo = T.Format("match.postInfo", Fmt.Int(result.Attendance), T["weather." + result.Weather], referee?.Name ?? "–")
                   + " · " + T.Format("match.halfTimeScore", result.HalfTimeHome, result.HalfTimeAway);
        if (result.NoteKeys.Count > 0)
            PostInfo += " · " + string.Join(" · ", result.NoteKeys.Select(k => T[k]));
        Heat = sim.Team(_side).Stats.Heat.ToList();

        var league = Session.World.LeagueOf(Session.HumanClub);
        OtherResults = Session.World.AllFixtures.Where(f => f.CompetitionId == _fixture.CompetitionId && f.Date == _fixture.Date).Select(f => new ResultLine(
            Session.World.Clubs[f.HomeId].Name, f.Result is { } r ? UiText.Score(r) : "– : –",
            Session.World.Clubs[f.AwayId].Name, f.Involves(Session.HumanClub.Id) ? FontWeight.Bold : FontWeight.Normal)).ToList();
        var row = LeagueTable.Compute(league).First(r => r.ClubId == Session.HumanClub.Id);
        TableInfo = T.Format("match.ownTable", row.Position, row.Points, row.GoalsFor, row.GoalsAgainst);
        Stage = MatchStage.PostMatch;
    }
}
