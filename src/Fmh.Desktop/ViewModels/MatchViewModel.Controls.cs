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

/// <summary>Eingriffe während des Spiels: Pause, Tempo, Ticker, Wechsel, Taktik und Zurufe.</summary>
public sealed partial class MatchViewModel
{
    [RelayCommand]
    public void TogglePause()
    {
        if (Stage != MatchStage.Live) return;
        Paused = !Paused;
        if (!Paused) Status = "";
        else Status = T["match.paused"];
    }

    [RelayCommand]
    private void SetSpeed(string? value)
    {
        if (int.TryParse(value, out var s)) Speed = s;
    }

    [RelayCommand]
    private void ToggleTicker()
    {
        if (Profile.MatchDisplay == MatchDisplayMode.Scene)
        {
            TickerMode = true;
            return;
        }
        TickerMode = !TickerMode;
        if (!_conference) Profile.MatchDisplay = TickerMode ? MatchDisplayMode.Ticker : MatchDisplayMode.Commentary;
    }

    [RelayCommand]
    public void FinishNow()
    {
        if (_sim is null || _sim.IsFinished) return;
        while (!_sim.IsFinished)
        {
            _sim.Step();
            if (_sim.IsHalfTime) _sim.Step();
        }
        ProcessEvents();
        Finish();
        UpdateLive();
    }

    [RelayCommand]
    public void Substitute()
    {
        if (_sim is null || _sim.IsFinished) return;
        var sim = _sim;
        var team = sim.Team(_side);
        var wasPaused = Paused;
        Paused = true;
        if (team.SubstitutionsUsed >= sim.MaxSubstitutions)
        {
            _main.ShowDialog(new MessageDialogViewModel(T["match.substitution"], new[] { T["sub.noneLeft"] }));
            return;
        }
        var onPitch = team.OnPitch.OrderBy(p => p.SlotIndex).ToList();
        var options = onPitch.Select(p => new ChoiceOption($"{UiText.Position(p.Position)}  {p.Player.Name}",
            $"{p.Condition:0} %  ·  {T["col.grade"]} {UiText.Grade(p.Grade)}{(p.Injured ? "  ✚" : "")}{(p.Yellow ? "  ▮" : "")}")).ToList();
        _main.ShowDialog(new ChoiceDialogViewModel(T["match.subOut"], options, i =>
        {
            var outMp = onPitch[i];
            var bench = team.Bench.ToList();
            if (bench.Count == 0)
            {
                _main.ShowDialog(new MessageDialogViewModel(T["match.substitution"], new[] { T["sub.emptyBench"] }));
                return;
            }
            var benchOptions = bench.Select(b =>
            {
                var rating = Ratings.EffectiveRating(b.Player, outMp.Position, Data.PositionProfiles);
                return new ChoiceOption($"{UiText.Position(b.Player.MainPosition)}  {b.Player.Name}",
                    $"{UiText.Strength(Ratings.StrengthFromRating(rating))} {T["match.asPosition"]} {UiText.Position(outMp.Position)}");
            }).ToList();
            _main.ShowDialog(new ChoiceDialogViewModel(T.Format("match.subIn", outMp.Player.Name), benchOptions, j =>
            {
                if (!sim.Substitute(_side, outMp.Player.Id, bench[j].Player.Id, out var error))
                    _main.ShowDialog(new MessageDialogViewModel(T["match.substitution"], new[] { T[error ?? "sub.failed"] }));
                ProcessEvents();
                UpdateLive();
                Paused = wasPaused;
            }));
        }));
    }

    [RelayCommand]
    public void ChangeTactics()
    {
        if (_sim is null || _sim.IsFinished) return;
        Paused = true;
        _main.ShowDialog(new MatchTacticsViewModel(_sim, _side, () => UpdateLive()));
    }

    [RelayCommand]
    public void ShoutOut()
    {
        if (_sim is null || _sim.IsFinished) return;
        var shouts = Enum.GetValues<Shout>();
        var options = shouts.Select(s => new ChoiceOption(T["shout." + s], T["shoutHint." + s])).ToList();
        _main.ShowDialog(new ChoiceDialogViewModel(T["match.shout"], options, i =>
        {
            _sim.SetShout(_side, shouts[i]);
            UpdateLive();
        }, (int)_sim.Team(_side).Shout));
    }
}
