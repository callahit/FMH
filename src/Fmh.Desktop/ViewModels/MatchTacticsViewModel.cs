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

/// <summary>Taktikänderung während des Spiels: Formation, Mentalität und Mannschaftsanweisungen.</summary>
public sealed partial class MatchTacticsViewModel : ViewModelBase
{
    private readonly IMatchSimulation _sim;
    private readonly Side _side;
    private readonly Tactic _draft;
    private readonly Action _onApply;

    public MatchTacticsViewModel(IMatchSimulation sim, Side side, Action onApply)
    {
        _sim = sim;
        _side = side;
        _onApply = onApply;
        _draft = sim.Team(side).Tactic.Clone();
        var t = _draft;
        var ins = t.Instructions;
        string[] Names<TEnum>(string prefix) where TEnum : struct, Enum => Enum.GetValues<TEnum>().Select(v => T[prefix + v]).ToArray();
        int Idx<TEnum>(TEnum v) where TEnum : struct, Enum => Array.IndexOf(Enum.GetValues<TEnum>(), v);
        TEnum At<TEnum>(int i) where TEnum : struct, Enum => Enum.GetValues<TEnum>()[i];
        var yesNo = new[] { T["ui.no"], T["ui.yes"] };
        Settings = new List<ChoiceSetting>
        {
            new(T["tactic.formation"], Data.Formations.Select(f => f.Name).ToList(), Data.Formations.FindIndex(f => f.Id == t.FormationId), i => t.FormationId = Data.Formations[i].Id),
            new(T["tactic.mentality"], Names<Mentality>("mentality."), Idx(t.Mentality), i => t.Mentality = At<Mentality>(i)),
            new(T["tactic.passing"], Names<PassingLength>("passing."), Idx(ins.PassingLength), i => ins.PassingLength = At<PassingLength>(i)),
            new(T["tactic.tempo"], Names<Tempo>("tempo."), Idx(ins.Tempo), i => ins.Tempo = At<Tempo>(i)),
            new(T["tactic.width"], Names<Width>("width."), Idx(ins.Width), i => ins.Width = At<Width>(i)),
            new(T["tactic.counter"], yesNo, ins.Counter ? 1 : 0, i => ins.Counter = i == 1),
            new(T["tactic.pressing"], Names<PressingHeight>("pressing."), Idx(ins.Pressing), i => ins.Pressing = At<PressingHeight>(i)),
            new(T["tactic.line"], Names<DefensiveLine>("line."), Idx(ins.Line), i => ins.Line = At<DefensiveLine>(i)),
            new(T["tactic.tackling"], Names<TacklingStyle>("tackling."), Idx(ins.Tackling), i => ins.Tackling = At<TacklingStyle>(i)),
        };
    }

    public IReadOnlyList<ChoiceSetting> Settings { get; }

    [RelayCommand]
    private void Apply()
    {
        var team = _sim.Team(_side);
        if (_draft.FormationId != team.Tactic.FormationId) _sim.ChangeFormation(_side, Data.Formation(_draft.FormationId));
        if (_draft.Mentality != team.Tactic.Mentality) _sim.ChangeMentality(_side, _draft.Mentality);
        _sim.ChangeInstructions(_side, _draft.Instructions);
        MainWindowViewModel.Instance?.CloseDialog();
        _onApply();
    }

    [RelayCommand]
    private void Cancel() => MainWindowViewModel.Instance?.CloseDialog();
}
