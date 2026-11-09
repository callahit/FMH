using Fmh.Core.Data;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Sperren je Wettbewerb (E4-S1). Liga und Relegation teilen einen Zähler, jeder Pokal und jeder Europapokal
/// hat einen eigenen. Schwellen stehen in den Länderdaten und im Europa-Format, nicht im Code.
/// </summary>
public static class DisciplineRules
{
    public readonly record struct MatchDiscipline(string Scope, ResolvedDiscipline Rules);

    public readonly record struct ResolvedDiscipline(
        int YellowCardsForSuspension,
        int YellowRepeat,
        int RedCardBanMin,
        int RedCardBanMax,
        int SecondYellowBan,
        bool ResetYellowsOnSeason,
        bool CarryYellowBansOnSeason,
        bool CarryRedBansOnSeason,
        int? ClearYellowsAfterRound,
        IReadOnlyList<YellowThreshold> Thresholds)
    {
        /// <summary>Zyklus „jede n-te Gelbe“ setzt den Zähler zurück. Leitern und UEFA-Folgeschwellen lassen ihn stehen.</summary>
        public bool ResetCountAfterBan => Thresholds.Count == 0 && YellowRepeat <= 0 && YellowCardsForSuspension > 0;

        public int YellowBanLength(int cards, int matchday)
        {
            if (Thresholds.Count > 0)
            {
                var ban = 0;
                foreach (var step in Thresholds)
                    if (step.Cards == cards && (step.UntilRound is null || matchday <= step.UntilRound))
                        ban = step.Matches;
                return ban;
            }
            if (YellowCardsForSuspension <= 0) return 0;
            if (YellowRepeat <= 0) return cards >= YellowCardsForSuspension ? 1 : 0;
            if (cards == YellowCardsForSuspension) return 1;
            if (cards > YellowCardsForSuspension && (cards - YellowCardsForSuspension) % YellowRepeat == 0) return 1;
            return 0;
        }
    }

    public readonly record struct CardOutcome(bool YellowBan, bool SendingOff, int BanMatches, int AtCards);

    /// <summary>Bereich, in dem Karten dieses Spiels zählen und Sperren abgesessen werden.</summary>
    public static string ScopeOf(Fixture fixture, World world)
    {
        var country = world.Country(world.Clubs[fixture.HomeId].CountryCode);
        if (country.Discipline.CompetitionScopes is { } map
            && map.TryGetValue(fixture.CompetitionId, out var named) && named.Length > 0)
            return named;
        return fixture.Kind switch
        {
            CompetitionKind.League or CompetitionKind.Playoff => PlayerState.LeagueScope,
            _ => fixture.CompetitionId,
        };
    }

    public static TextArg ScopeLabel(World world, string scope) => scope switch
    {
        PlayerState.LeagueScope => TextArg.Key("discipline.scope.league"),
        PlayerState.CupScope => TextArg.Key("discipline.scope.cup"),
        PlayerState.InternationalScope => TextArg.Key("discipline.scope.international"),
        _ => Messages.Competition(world, scope),
    };

    public static MatchDiscipline ForMatch(World world, GameData data, Fixture fixture, Club club)
    {
        var scope = ScopeOf(fixture, world);
        if (fixture.Kind == CompetitionKind.Europe)
        {
            var euro = data.Europe?.Discipline ?? world.Country(club.CountryCode).Discipline;
            return new MatchDiscipline(scope, Resolve(euro, fixture.CompetitionId, null));
        }
        var country = world.Country(club.CountryCode).Discipline;
        var template = fixture.Kind is CompetitionKind.League or CompetitionKind.Playoff
            ? PlayerState.LeagueScope
            : PlayerState.CupScope;
        return new MatchDiscipline(scope, Resolve(country, fixture.CompetitionId, template));
    }

    public static ResolvedDiscipline Resolve(DisciplineConfig config, string competitionId, string? template)
    {
        var yellow = config.YellowCardsForSuspension;
        var repeat = config.YellowRepeat;
        var redMin = config.RedCardBanMin;
        var redMax = config.RedCardBanMax;
        var second = config.SecondYellowBan;
        var resetYellows = config.ResetYellowsOnSeason;
        var carryYellow = config.CarryYellowBansOnSeason;
        var carryRed = config.CarryRedBansOnSeason;
        int? clear = config.ClearYellowsAfterRound;
        List<YellowThreshold>? steps = config.Thresholds;

        void Overlay(DisciplineScopeConfig? scope)
        {
            if (scope is null) return;
            if (scope.YellowCardsForSuspension.HasValue) yellow = scope.YellowCardsForSuspension.Value;
            if (scope.YellowRepeat.HasValue) repeat = scope.YellowRepeat.Value;
            if (scope.RedCardBanMin.HasValue) redMin = scope.RedCardBanMin.Value;
            if (scope.RedCardBanMax.HasValue) redMax = scope.RedCardBanMax.Value;
            if (scope.SecondYellowBan.HasValue) second = scope.SecondYellowBan.Value;
            if (scope.ResetYellowsOnSeason.HasValue) resetYellows = scope.ResetYellowsOnSeason.Value;
            if (scope.CarryYellowBansOnSeason.HasValue) carryYellow = scope.CarryYellowBansOnSeason.Value;
            if (scope.CarryRedBansOnSeason.HasValue) carryRed = scope.CarryRedBansOnSeason.Value;
            if (scope.ClearYellowsAfterRound.HasValue) clear = scope.ClearYellowsAfterRound.Value;
            if (scope.Thresholds is { Count: > 0 }) steps = scope.Thresholds;
        }

        if (config.Scopes is not null)
        {
            if (template is not null && config.Scopes.TryGetValue(template, out var fromTemplate)) Overlay(fromTemplate);
            if (config.Scopes.TryGetValue(competitionId, out var fromId) && competitionId != template) Overlay(fromId);
        }

        return new ResolvedDiscipline(yellow, repeat, redMin, redMax, second, resetYellows, carryYellow, carryRed, clear,
            steps ?? (IReadOnlyList<YellowThreshold>)Array.Empty<YellowThreshold>());
    }

    public static ResolvedDiscipline RulesForScope(DisciplineConfig country, EuropeFormat? europe, string scope)
    {
        if (scope == PlayerState.LeagueScope)
            return Resolve(country, scope, PlayerState.LeagueScope);
        if (europe?.Competition(scope) is not null)
            return Resolve(europe.Discipline ?? country, scope, null);
        return Resolve(country, scope, PlayerState.CupScope);
    }

    /// <summary>Baut eine bestehende Sperre um ein Spiel ab. Wahr, wenn der Spieler in diesem Bereich wieder spielen darf.</summary>
    public static bool Serve(PlayerState state, string scope)
    {
        var blocked = state.PersonalBan > 0 || state.Ban(scope) > 0;
        var servedScope = false;
        if (state.YellowBan(scope) > 0)
        {
            state.AddYellowBan(scope, -1);
            servedScope = true;
        }
        else if (state.RedBan(scope) > 0)
        {
            state.AddRedBan(scope, -1);
            servedScope = true;
        }
        if (!servedScope && state.PersonalBan > 0) state.PersonalBan--;
        return blocked && state.PersonalBan == 0 && state.Ban(scope) == 0;
    }

    public static CardOutcome ApplyCard(PlayerState state, MatchDiscipline match, bool yellow, bool red, int matchday, Func<int, int, int> roll)
    {
        var scope = match.Scope;
        var rules = match.Rules;
        if (red)
        {
            var secondYellow = yellow;
            var length = secondYellow
                ? rules.SecondYellowBan
                : roll(Math.Min(rules.RedCardBanMin, rules.RedCardBanMax), Math.Max(rules.RedCardBanMin, rules.RedCardBanMax));
            if (length > 0) state.AddRedBan(scope, length);
            return new CardOutcome(false, true, length, 0);
        }
        if (!yellow) return default;
        var cards = state.AddYellow(scope);
        var ban = rules.YellowBanLength(cards, matchday);
        if (ban > 0)
        {
            state.AddYellowBan(scope, ban);
            if (rules.ResetCountAfterBan) state.SetYellows(scope, 0);
        }
        return new CardOutcome(ban > 0, false, ban, cards);
    }

    /// <summary>Nach dem Spieltag: Gelbe löschen, wenn das Reglement diesen Spieltag als Schnitt setzt. Sperren bleiben.</summary>
    public static void ClearYellowsIfRoundEnds(PlayerState state, MatchDiscipline match, int matchday)
    {
        if (match.Rules.ClearYellowsAfterRound == matchday)
            state.SetYellows(match.Scope, 0);
    }

    public static void ApplySeason(PlayerState state, DisciplineConfig country, EuropeFormat? europe)
    {
        foreach (var scope in state.ScopeKeys())
        {
            var rules = RulesForScope(country, europe, scope);
            var yellows = rules.ResetYellowsOnSeason ? 0 : state.Yellows(scope);
            var yellowBan = rules.CarryYellowBansOnSeason ? state.YellowBan(scope) : 0;
            var redBan = rules.CarryRedBansOnSeason ? state.RedBan(scope) : 0;
            state.SetDiscipline(scope, yellows, yellowBan, redBan);
        }
    }
}
