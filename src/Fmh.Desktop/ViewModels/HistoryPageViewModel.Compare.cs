using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record PlayerChoice(int Id, string Name);

/// <summary>Zeile des Spielervergleichs; der bessere Wert ist fett, wenn beide Werte genau bekannt sind.</summary>
public sealed record CompareLine(string Label, string A, string B, FontWeight WeightA, FontWeight WeightB, bool Header = false);

/// <summary>
/// Datenzentrale (AP 11c), Reiter Spielervergleich: zwei Spieler beliebiger Vereine nebeneinander. Fremde Spieler
/// erscheinen wie auf der Spielerseite nur so genau, wie der Kenntnisstand des Scoutings reicht.
/// </summary>
public sealed partial class HistoryPageViewModel
{
    [ObservableProperty] private HistoryClubChoice? _compareClubA;
    [ObservableProperty] private HistoryClubChoice? _compareClubB;
    [ObservableProperty] private IReadOnlyList<PlayerChoice> _comparePlayersA = Array.Empty<PlayerChoice>();
    [ObservableProperty] private IReadOnlyList<PlayerChoice> _comparePlayersB = Array.Empty<PlayerChoice>();
    [ObservableProperty] private PlayerChoice? _compareA;
    [ObservableProperty] private PlayerChoice? _compareB;
    [ObservableProperty] private IReadOnlyList<CompareLine> _compareLines = Array.Empty<CompareLine>();

    partial void OnCompareClubAChanged(HistoryClubChoice? value)
    {
        ComparePlayersA = SquadChoices(value);
        CompareA = ComparePlayersA.FirstOrDefault();
    }

    partial void OnCompareClubBChanged(HistoryClubChoice? value)
    {
        ComparePlayersB = SquadChoices(value);
        CompareB = ComparePlayersB.FirstOrDefault();
    }

    partial void OnCompareAChanged(PlayerChoice? value) => RefreshCompare();
    partial void OnCompareBChanged(PlayerChoice? value) => RefreshCompare();

    private IReadOnlyList<PlayerChoice> SquadChoices(HistoryClubChoice? club)
    {
        var w = Session.World;
        if (club is null || !w.Clubs.TryGetValue(club.Id, out var c)) return Array.Empty<PlayerChoice>();
        var profiles = Data.PositionProfiles;
        return w.SquadOf(c).OrderBy(p => p.MainPosition).ThenByDescending(p => Ratings.Strength(p, profiles))
            .Select(p => new PlayerChoice(p.Id, $"{p.Name} ({UiText.Position(p.MainPosition)})")).ToList();
    }

    private void InitCompare()
    {
        var w = Session.World;
        var own = Clubs.FirstOrDefault(c => c.Id == Session.HumanClub.Id);
        var next = DataCenter.NextOpponent(w, Session.HumanClub.Id) is { } id ? Clubs.FirstOrDefault(c => c.Id == id) : null;
        CompareClubA = own;
        CompareClubB = next ?? Clubs.FirstOrDefault(c => c.Id != Session.HumanClub.Id);
    }

    private void RefreshCompare()
    {
        var w = Session.World;
        if (CompareA is null || CompareB is null || !w.Players.TryGetValue(CompareA.Id, out var a) || !w.Players.TryGetValue(CompareB.Id, out var b))
        {
            CompareLines = Array.Empty<CompareLine>();
            return;
        }
        var profiles = Data.PositionProfiles;
        var lines = new List<CompareLine>();
        void Text(string label, string va, string vb) => lines.Add(new(label, va, vb, FontWeight.Normal, FontWeight.Normal));
        // Höher ist besser, außer bei Noten (niedriger besser).
        void Number(string label, double? va, double? vb, string format = "0", bool lowerIsBetter = false)
        {
            var better = va is { } x && vb is { } y && x != y ? ((x > y) ^ lowerIsBetter ? 1 : 2) : 0;
            lines.Add(new(label, va is { } v1 ? Fmt.Num(v1, format) : "–", vb is { } v2 ? Fmt.Num(v2, format) : "–",
                better == 1 ? FontWeight.Bold : FontWeight.Normal, better == 2 ? FontWeight.Bold : FontWeight.Normal));
        }
        void Header(string label) => lines.Add(new(label, "", "", FontWeight.Bold, FontWeight.Bold, true));

        Text(T["data.club"], a.ClubId is { } ca ? ClubName(ca) : "–", b.ClubId is { } cb ? ClubName(cb) : "–");
        Text(T["col.pos"], UiText.Position(a.MainPosition), UiText.Position(b.MainPosition));
        Text(T["col.age"], a.Age(w.Date).ToString(), b.Age(w.Date).ToString());
        double? Strength(Player p) => Session.StrengthKnown(p) ? Ratings.Strength(p, profiles) : null;
        Number(T["col.strength"], Strength(a), Strength(b), "0.0");

        Header(T.Format("data.season", GameSession.SeasonLabel(w.SeasonYear)));
        Number(T["col.apps"], a.SeasonStats.Appearances, b.SeasonStats.Appearances);
        Number(T["col.goals"], a.SeasonStats.Goals, b.SeasonStats.Goals);
        Number(T["col.assists"], a.SeasonStats.Assists, b.SeasonStats.Assists);
        Number(T["col.grade"], a.SeasonStats.AverageGrade, b.SeasonStats.AverageGrade, "0.0", lowerIsBetter: true);
        Header(T["player.career"]);
        Number(T["col.apps"], RecordBook.Seasons(a).Sum(s => s.Appearances), RecordBook.Seasons(b).Sum(s => s.Appearances));
        Number(T["col.goals"], RecordBook.Seasons(a).Sum(s => s.Goals), RecordBook.Seasons(b).Sum(s => s.Goals));

        var groups = a.MainPosition == Position.GK || b.MainPosition == Position.GK
            ? new[] { ("group.goalkeeping", AttrInfo.Goalkeeping), ("group.technical", AttrInfo.Technical), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) }
            : new[] { ("group.technical", AttrInfo.Technical), ("group.mental", AttrInfo.Mental), ("group.physical", AttrInfo.Physical) };
        foreach (var (key, attrs) in groups)
        {
            Header(T[key]);
            foreach (var attr in attrs)
            {
                var (loA, hiA) = Session.AttributeRange(a, attr);
                var (loB, hiB) = Session.AttributeRange(b, attr);
                var exact = loA == hiA && loB == hiB && loA != loB;
                lines.Add(new(T[AttrInfo.TextKey(attr)], Range(loA, hiA), Range(loB, hiB),
                    exact && loA > loB ? FontWeight.Bold : FontWeight.Normal, exact && loB > loA ? FontWeight.Bold : FontWeight.Normal));
            }
        }
        CompareLines = lines;

        static string Range(int lo, int hi) => lo == hi ? lo.ToString() : $"{lo}–{hi}";
    }
}
