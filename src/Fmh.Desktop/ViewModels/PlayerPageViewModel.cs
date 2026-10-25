using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Controls;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record AttrItem(string Name, int Value, IBrush Brush, string Symbol, string Stars, bool ShowNumber, bool ShowBar, bool ShowStars);
public sealed record AttrGroupVm(string Title, IReadOnlyList<AttrItem> Items);
public sealed record InfoLine(string Label, string Value, IBrush? Brush = null)
{
    public IBrush Fore => Brush ?? new SolidColorBrush(Palette.Current.Text);
}
public sealed record PositionChip(string Position, string Suitability, string Strength, IBrush Brush);
public sealed record SeasonLine(string Season, string Club, int Apps, int Minutes, int Goals, int Assists, int Yellow, int Red, string Grade);

/// <summary>Spielerprofil (Kapitel 4): Stammdaten, Attribute, Radar, Formkurve, Zustand, Statistik.</summary>
public sealed partial class PlayerPageViewModel : PageViewModel
{
    private readonly IReadOnlyList<Player> _players;

    public PlayerPageViewModel(ShellViewModel shell, IReadOnlyList<Player> players, int index) : base(shell)
    {
        _players = players;
        _index = Math.Clamp(index, 0, players.Count - 1);
        Load();
    }

    public override string Title => Player.Name;

    [ObservableProperty] private int _index;
    [ObservableProperty] private Player _player = null!;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _strength = "";
    [ObservableProperty] private IBrush _strengthBrush = Brushes.Gray;
    [ObservableProperty] private string _clubName = "";
    [ObservableProperty] private string _clubPrimary = "Gray";
    [ObservableProperty] private string _clubSecondary = "White";
    [ObservableProperty] private string _clubInitials = "";
    [ObservableProperty] private IReadOnlyList<AttrGroupVm> _groups = Array.Empty<AttrGroupVm>();
    [ObservableProperty] private bool _attributesHidden;
    [ObservableProperty] private IReadOnlyList<double> _radarValues = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<string> _radarLabels = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<Point> _formPoints = Array.Empty<Point>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _personal = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<InfoLine> _stateLines = Array.Empty<InfoLine>();
    [ObservableProperty] private IReadOnlyList<PositionChip> _positions = Array.Empty<PositionChip>();
    [ObservableProperty] private IReadOnlyList<SeasonLine> _seasons = Array.Empty<SeasonLine>();
    [ObservableProperty] private double _condition;
    [ObservableProperty] private double _practice;
    [ObservableProperty] private IBrush _conditionBrush = Brushes.Gray;
    [ObservableProperty] private string _navText = "";

    private void Load()
    {
        var p = _players[Index];
        Player = p;
        var world = Session.World;
        var profiles = Data.PositionProfiles;
        var club = p.ClubId is { } cid ? world.Clubs[cid] : null;
        ClubName = club?.Name ?? T["player.noClub"];
        ClubPrimary = club?.PrimaryColor ?? "Gray";
        ClubSecondary = club?.SecondaryColor ?? "White";
        ClubInitials = club is null ? "" : Crest.InitialsOf(club.Name);
        Subtitle = $"{UiText.Position(p.MainPosition)} · {T.Format("player.ageValue", p.Age(world.Date), Fmt.Date(p.BirthDate))} · {string.Join(", ", p.Nationalities)}";
        var rating = Ratings.PositionRating(p, p.MainPosition, profiles);
        Strength = UiText.Strength(Ratings.Strength(p, profiles));
        StrengthBrush = new SolidColorBrush(P.AttributeColor((int)Math.Round(rating)));
        NavText = $"{Index + 1} / {_players.Count}";

        var mode = Session.HumanManager.Profile.AttributeDisplay;
        AttributesHidden = mode == AttributeDisplay.StrengthOnly;
        var groups = p.MainPosition == Position.GK
            ? new[] { ("group.goalkeeping", AttrInfo.Goalkeeping), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) }
            : new[] { ("group.technical", AttrInfo.Technical), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) };
        Groups = groups.Select(g => new AttrGroupVm(T[g.Item1], g.Item2.Select(a =>
        {
            var v = p.Attributes[a];
            return new AttrItem(T[AttrInfo.TextKey(a)], v, new SolidColorBrush(P.AttributeColor(v)), UiText.AttributeSymbol(v),
                UiText.Stars(v / 4.0), mode != AttributeDisplay.Stars, mode == AttributeDisplay.Bars || mode == AttributeDisplay.Numbers, mode == AttributeDisplay.Stars);
        }).ToList())).ToList();

        double Avg(params Attr[] attrs) => attrs.Average(a => p.Attributes[a]);
        if (p.MainPosition == Position.GK)
        {
            RadarLabels = new[] { T["radar.reflexes"], T["radar.handling"], T["radar.area"], T["radar.oneOnOne"], T["radar.distribution"], T["radar.mental"] };
            RadarValues = new[]
            {
                Avg(Attr.Reflexes), Avg(Attr.Handling), Avg(Attr.CommandOfArea, Attr.RushingOut), Avg(Attr.OneOnOnes),
                Avg(Attr.Kicking, Attr.Throwing), AttrInfo.Mental.Average(a => p.Attributes[a]),
            };
        }
        else
        {
            RadarLabels = new[] { T["radar.attack"], T["radar.technique"], T["radar.pace"], T["radar.physical"], T["radar.defence"], T["radar.mental"] };
            RadarValues = new[]
            {
                Avg(Attr.Finishing, Attr.OffTheBall, Attr.Composure, Attr.LongShots), Avg(Attr.FirstTouch, Attr.Passing, Attr.Technique, Attr.Dribbling),
                Avg(Attr.Pace, Attr.Acceleration), Avg(Attr.Strength, Attr.Stamina, Attr.Agility, Attr.JumpingReach),
                Avg(Attr.Tackling, Attr.Marking, Attr.Positioning, Attr.Anticipation), AttrInfo.Mental.Average(a => p.Attributes[a]),
            };
        }
        FormPoints = p.State.RecentGrades.Select((g, i) => new Point(i, g)).ToList();

        var currency = club is null ? "EUR" : UiText.Currency(club);
        var personal = new List<InfoLine>
        {
            new(T["player.body"], $"{p.Height} cm, {p.Weight} kg"),
            new(T["player.foot"], $"{T["foot." + p.Foot]} (L {p.LeftFoot}, R {p.RightFoot})"),
            new(T["player.reputation"], $"{p.Reputation}/20"),
        };
        if (p.Contract is { } contract)
        {
            personal.Add(new(T["player.contract"], Fmt.Date(contract.Until)));
            personal.Add(new(T["player.wage"], UiText.Wage(contract.WeeklyWage, Session.HumanManager.Profile, currency)));
        }
        Personal = personal;

        Condition = p.State.Condition;
        Practice = p.State.MatchPractice;
        ConditionBrush = new SolidColorBrush(P.ConditionColor(p.State.Condition));
        var state = new List<InfoLine>
        {
            new(T["player.condition"], $"{p.State.Condition:0} %", ConditionBrush),
            new(T["player.morale"], UiText.Morale(p.State.Morale)),
            new(T["player.form"], $"{p.State.Form}/10"),
            new(T["player.practice"], $"{p.State.MatchPractice:0} %"),
        };
        if (p.State.Injury is { } inj)
            state.Add(new(T["player.injury"], T.Format("player.injuryValue", T["injury." + inj.Type], inj.DaysRemaining), new SolidColorBrush(P.Bad)));
        if (p.State.SuspendedMatches > 0)
            state.Add(new(T["player.suspension"], T.Format("status.suspended", p.State.SuspendedMatches), new SolidColorBrush(P.Bad)));
        StateLines = state;

        Positions = p.Positions.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv =>
        {
            var r = Ratings.EffectiveRating(p, kv.Key, profiles);
            return new PositionChip(UiText.Position(kv.Key), T["suit." + kv.Value], UiText.Strength(Ratings.StrengthFromRating(r)),
                new SolidColorBrush(P.AttributeColor((int)Math.Round(r))));
        }).ToList();

        Seasons = new[] { p.SeasonStats }.Concat(p.History.AsEnumerable().Reverse()).Where(s => s.SeasonYear > 0)
            .Select(s => new SeasonLine(GameSession.SeasonLabel(s.SeasonYear),
                s.ClubId is { } id && world.Clubs.TryGetValue(id, out var c) ? c.ShortName : "–",
                s.Appearances, s.Minutes, s.Goals, s.Assists, s.YellowCards, s.RedCards, UiText.Grade(s.AverageGrade)))
            .ToList();
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private void Previous()
    {
        Index = (Index + _players.Count - 1) % _players.Count;
        Load();
    }

    [RelayCommand]
    private void Next()
    {
        Index = (Index + 1) % _players.Count;
        Load();
    }

    [RelayCommand]
    private void Back() => Shell.Back();

    public override bool HandleKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        switch (key)
        {
            case Avalonia.Input.Key.Left: Previous(); return true;
            case Avalonia.Input.Key.Right: Next(); return true;
        }
        return false;
    }
}
