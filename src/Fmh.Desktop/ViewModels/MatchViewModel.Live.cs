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

/// <summary>Live-Spiel: Takt, Phasen, Ereignisse, Ticker und Statistiken.</summary>
public sealed partial class MatchViewModel
{
    private void StartTimer()
    {
        _wait = TimeSpan.Zero;
        Paused = false;
        if (!UseTimer || _timer is not null) return;
        _lastTick = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) =>
        {
            var now = DateTime.UtcNow;
            Tick(now - _lastTick);
            _lastTick = now;
        });
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>Spielt so viele Phasen, wie im gewählten Tempo in die vergangene Zeit passen.</summary>
    public void Tick(TimeSpan elapsed)
    {
        if (Stage != MatchStage.Live || Paused || _sim is null) return;
        _wait -= elapsed;
        var perStep = TimeSpan.FromSeconds(SecondsPerMatch[Math.Clamp(Profile.MatchSpeed, 1, 5) - 1] / PhasesPerMatch);
        var guard = 0;
        while (_wait <= TimeSpan.Zero && Stage == MatchStage.Live && !Paused && guard++ < 60)
        {
            StepOnce();
            _wait += perStep;
        }
        if (_wait < -perStep) _wait = TimeSpan.Zero;
    }

    private void StepOnce()
    {
        var sim = _sim!;
        sim.Step();
        ProcessEvents();
        if (sim.IsHalfTime)
        {
            Stage = MatchStage.HalfTime;
            Paused = false;
            OnPropertyChanged(nameof(IsTalk));
        }
        else if (sim.IsFinished) Finish();
        UpdateLive();
    }

    private void ProcessEvents()
    {
        var sim = _sim!;
        var ticker = Profile.MatchDisplay == MatchDisplayMode.Ticker;
        for (; _processedEvents < sim.Events.Count; _processedEvents++)
        {
            var e = sim.Events[_processedEvents];
            if (e.Type == MatchEventType.Shot || e.Type == MatchEventType.Goal)
            {
                var list = e.Side == Side.Home ? _xgHome : _xgAway;
                list.Add(new Point(Math.Min(90, e.Minute), list[^1].Y + e.Xg));
            }
            if (e.Type == MatchEventType.Goal) (e.Side == Side.Home ? _goalsHome : _goalsAway).Add(Math.Min(90, e.Minute));

            var marker = e.Type switch
            {
                MatchEventType.Goal => P.Gold,
                MatchEventType.YellowCard => Color.Parse("#F4D03F"),
                MatchEventType.RedCard or MatchEventType.SecondYellow => P.Bad,
                MatchEventType.Injury => P.Bad,
                MatchEventType.Substitution => P.Accent,
                _ => Colors.Transparent,
            };
            if (!string.IsNullOrEmpty(e.Narrative))
            {
                var goal = e.Type == MatchEventType.Goal;
                Ticker.Insert(0, new TickerLine(e.MinuteLabel, e.Narrative,
                    new SolidColorBrush(goal ? P.Gold : e.Key ? P.Text : P.Muted),
                    new SolidColorBrush(marker), goal ? FontWeight.Bold : FontWeight.Normal));
            }
            else if (_commentary!.Describe(e, ticker) is { } line)
            {
                Ticker.Insert(0, new TickerLine(line.Minute, line.Text,
                    new SolidColorBrush(line.Highlight ? P.Gold : e.Key ? P.Text : P.Muted),
                    new SolidColorBrush(marker), line.Highlight ? FontWeight.Bold : FontWeight.Normal));
            }

            var ours = e.Side == _side;
            if (e.Type == MatchEventType.Goal && Profile.PauseOnGoals) PauseWith(T.Format("pause.goal", e.HomeScore, e.AwayScore));
            if (e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow && Profile.PauseOnRedCards) PauseWith(T["pause.red"]);
            if (e.Type == MatchEventType.Injury && ours && e.PlayerId is { } pid) PauseWith(T.Format("pause.injury", Session.World.Players[pid].Name));
        }
    }

    private void PauseWith(string reason)
    {
        Paused = true;
        Status = reason;
    }

    private void UpdateLive()
    {
        var sim = _sim;
        if (sim is null) return;
        Score = $"{sim.Home.Goals} : {sim.Away.Goals}";
        if (sim.PenaltiesHome is { } ph && sim.PenaltiesAway is { } pa) Score += "  " + T.Format("score.penalties", ph, pa);
        else if (sim.IsExtraTime) Score += "  " + T["score.extraTime"];
        Clock = sim.IsFinished ? T["match.final"] : sim.StoppageMinute > 0 ? $"{sim.Minute}+{sim.StoppageMinute}'" : $"{sim.Minute}'";
        var h = sim.Home.Stats;
        var a = sim.Away.Stats;
        var total = h.PossessionSeconds + a.PossessionSeconds;
        var poss = total <= 0 ? 50 : Math.Round(100 * h.PossessionSeconds / total);
        Stats = new List<StatLine>
        {
            new(T["stat.possession"], $"{poss}%", $"{100 - poss}%", poss, 100 - poss),
            new(T["stat.shots"], h.Shots.ToString(), a.Shots.ToString(), h.Shots, a.Shots),
            new(T["stat.onTarget"], h.ShotsOnTarget.ToString(), a.ShotsOnTarget.ToString(), h.ShotsOnTarget, a.ShotsOnTarget),
            new(T["stat.xg"], Fmt.Num(h.Xg, "0.00"), Fmt.Num(a.Xg, "0.00"), h.Xg, a.Xg),
            new(T["stat.passes"], $"{h.Passes} ({h.PassAccuracy}%)", $"{a.Passes} ({a.PassAccuracy}%)", h.Passes, a.Passes),
            new(T["stat.duels"], $"{h.DuelRate}%", $"{a.DuelRate}%", h.DuelRate, a.DuelRate),
            new(T["stat.corners"], h.Corners.ToString(), a.Corners.ToString(), h.Corners, a.Corners),
            new(T["stat.fouls"], h.Fouls.ToString(), a.Fouls.ToString(), h.Fouls, a.Fouls),
            new(T["stat.cards"], $"{h.YellowCards}/{h.RedCards}", $"{a.YellowCards}/{a.RedCards}", h.YellowCards + 3 * h.RedCards, a.YellowCards + 3 * a.RedCards),
        };
        if (sim.IsFinished) Stats = Stats.Append(new StatLine(T["stat.distance"], Fmt.Num(h.DistanceKm, "0.0"), Fmt.Num(a.DistanceKm, "0.0"), h.DistanceKm, a.DistanceKm)).ToList();

        var own = sim.Team(_side);
        Players = own.OnPitch.OrderBy(p => p.SlotIndex).Select(mp => new LivePlayer(
            UiText.Position(mp.Position), mp.Player.LastName, mp.Condition, new SolidColorBrush(P.ConditionColor(mp.Condition)),
            UiText.Grade(mp.Grade), new SolidColorBrush(P.GradeColor(mp.Grade)),
            (mp.Goals > 0 ? new string('●', mp.Goals) + " " : "") + (mp.Yellow ? "▮" : "") + (mp.Injured ? " ✚" : ""))).ToList();
        SubsLeft = T.Format("match.subsLeftValue", sim.MaxSubstitutions - own.SubstitutionsUsed);
        XgSeriesHome = _xgHome.Append(new Point(Math.Min(90, sim.Minute), _xgHome[^1].Y)).ToList();
        XgSeriesAway = _xgAway.Append(new Point(Math.Min(90, sim.Minute), _xgAway[^1].Y)).ToList();
        GoalMarksHome = _goalsHome.ToList();
        GoalMarksAway = _goalsAway.ToList();
        if (!Paused && Stage == MatchStage.Live && own.Shout != Shout.None) Status = T.Format("match.shoutActive", T["shout." + own.Shout]);
        if (_conference) UpdateConference(sim);
    }
}
