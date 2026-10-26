using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>
/// Presse und Medien (Kapitel 10): Pressekonferenzen vor und nach Spielen mit vier Antworttönen,
/// Schlagzeilen im Boulevard-Stil, Gerüchte, Spielerinterviews und Fehden mit gegnerischen Trainern.
/// </summary>
public sealed partial class GameSession
{
    public const int PressQuestionsPerConference = 3;

    private MediaStyle Style => HumanManager.Profile.Style;

    private bool StyleFits(List<MediaStyle> styles) => styles.Count == 0 || styles.Contains(Style);

    /// <summary>Spielkontext für die Fragenauswahl.</summary>
    private HashSet<string> PressContext(Fixture f, bool after)
    {
        var club = HumanClub;
        var opp = World.Clubs[f.HomeId == club.Id ? f.AwayId : f.HomeId];
        var ctx = new HashSet<string> { "any" };
        if (club.Rivals.Contains(opp.Id)) ctx.Add("derby");
        if (opp.Reputation >= club.Reputation + 3 || f.Kind != CompetitionKind.League) ctx.Add("bigGame");
        var recent = AllFixtures.Where(x => x.Involves(club.Id) && x.Result is not null && x != f).OrderByDescending(x => x.Date).Take(3).Select(x => Outcome(club, x)).ToList();
        if (recent.Count == 3 && recent.All(o => o > 0)) ctx.Add("streakWin");
        if (recent.Count == 3 && recent.All(o => o < 0)) ctx.Add("streakLoss");
        if (World.SquadOf(club).OrderByDescending(Rating).Take(5).Any(p => p.State.Injury is not null)) ctx.Add("injury");
        if (World.Transfers.Any(t => t.ToClubId == club.Id && t.Date >= World.Date.AddDays(-14))) ctx.Add("newSigning");
        if (club.Board.Trust < 35) ctx.Add("pressure");
        if (after && f.Result is not null)
        {
            var o = Outcome(club, f);
            ctx.Add(o > 0 ? "win" : o < 0 ? "loss" : "draw");
            var diff = Math.Abs(f.Result.HomeGoals - f.Result.AwayGoals);
            if (diff >= 3) ctx.Add(o > 0 ? "bigWin" : "bigLoss");
            if (f.Result.Cards.Any(c => c.Red && c.Home == (f.HomeId == club.Id))) ctx.Add("redCard");
        }
        return ctx;
    }

    private static int Outcome(Club club, Fixture f)
    {
        var r = f.Result!;
        var home = f.HomeId == club.Id;
        var diff = home ? r.HomeGoals - r.AwayGoals : r.AwayGoals - r.HomeGoals;
        if (diff == 0 && r.PenaltiesHome is { } ph && r.PenaltiesAway is { } pa) diff = home ? ph - pa : pa - ph;
        return Math.Sign(diff);
    }

    /// <summary>Pressekonferenz ansetzen; bei Delegation antwortet der Pressesprecher.</summary>
    public void SchedulePress(Fixture f, bool after)
    {
        var club = HumanClub;
        var ctx = PressContext(f, after);
        var when = after ? "after" : "before";
        var pool = Data.PressQuestions.Where(q => q.When == when && ctx.Contains(q.Context) && StyleFits(q.Styles)).ToList();
        if (pool.Count == 0) return;
        // Spezifische Fragen zuerst, allgemeine füllen auf.
        var specific = pool.Where(q => q.Context != "any").ToList();
        _rng.Shuffle(specific);
        var general = pool.Where(q => q.Context == "any").ToList();
        _rng.Shuffle(general);
        var picked = specific.Take(2).Concat(general).Take(PressQuestionsPerConference).ToList();
        var opp = World.Clubs[f.HomeId == club.Id ? f.AwayId : f.HomeId];
        var star = World.SquadOf(club).OrderByDescending(p => p.SeasonStats.Goals * 2 + Rating(p)).First();
        var injured = World.SquadOf(club).Where(p => p.State.Injury is not null).OrderByDescending(Rating).FirstOrDefault();
        var signing = World.Transfers.LastOrDefault(t => t.ToClubId == club.Id) is { } t ? World.Players[t.PlayerId] : star;
        var conf = new PressConference { FixtureId = f.Id, AfterMatch = after };
        foreach (var q in picked)
        {
            var player = q.Context switch { "injury" => injured ?? star, "newSigning" => signing, _ => star };
            conf.Questions.Add(new PressQuestion
            {
                Key = q.Key, Category = q.Category,
                Args = new List<TextArg> { TextArg.Literal(opp.Name), TextArg.Literal(player.Name), TextArg.Literal(HumanManager.Name), TextArg.Literal(club.Name), TextArg.Literal(f.Result is { } r ? $"{r.HomeGoals}:{r.AwayGoals}" : "") },
            });
        }
        if (HumanManager.Profile.DelegatePress)
        {
            World.PendingPress = conf;
            var officer = StaffAbility(club, StaffRole.PressOfficer);
            AnswerPress(conf.Questions.Select(_ => _rng.Chance(0.3 + officer / 40.0) ? PressAnswer.Modest : PressAnswer.Evasive).ToList(), delegated: true);
            return;
        }
        World.PendingPress = conf;
        Notify(MessageType.Press, "msg.sender.press", TR(after ? "msg.pressAfter.subject" : "msg.pressBefore.subject", opp.Name),
            TR("msg.press.body", opp.Name), MessageAction.PressConference, f.Id, required: true);
    }

    /// <summary>Antworten auswerten: Moral, Gegnermotivation, Vorstandsvertrauen, Medienruf, Fehden, Schlagzeile.</summary>
    public void AnswerPress(IReadOnlyList<PressAnswer> answers, bool delegated = false)
    {
        if (World.PendingPress is not { } conf) return;
        var club = HumanClub;
        var career = HumanManager.Career;
        var f = AllFixtures.FirstOrDefault(x => x.Id == conf.FixtureId);
        var opp = f is null ? null : World.Clubs[f.HomeId == club.Id ? f.AwayId : f.HomeId];
        var officer = StaffAbility(club, StaffRole.PressOfficer) + Effect(club, "media") * 10;
        conf.Answers = answers.ToList();
        for (var i = 0; i < conf.Questions.Count && i < answers.Count; i++)
        {
            var q = conf.Questions[i];
            var a = answers[i];
            var mediaSwing = Style switch { MediaStyle.Satirical => 1.3, MediaStyle.Serious => 0.7, _ => 1.0 };
            switch (a)
            {
                case PressAnswer.Confident:
                    career.Media += 1.0 * mediaSwing;
                    ChangeTrust(club, 0.3);
                    TeamMorale(club, 0.4);
                    if (opp is not null && !conf.AfterMatch) TeamMorale(opp, 0.25);
                    break;
                case PressAnswer.Aggressive:
                    career.Media += (Style == MediaStyle.Serious ? -1.0 : 1.5) * mediaSwing;
                    ChangeTrust(club, -0.6);
                    if (q.Category is "player" or "team") TeamMorale(club, 0.4);
                    if (q.Category is "opponent" or "referee" && opp is not null)
                    {
                        TeamMorale(opp, 0.5);
                        if (!career.Feuds.Contains(opp.Id)) career.Feuds.Add(opp.Id);
                    }
                    if (q.Category == "referee" && _rng.Chance(0.35 - Effect(club, "fines") * 0.2))
                    {
                        var fine = MarketValue.Round(15_000 * Scale(club));
                        Book(club, TransactionType.Fines, -fine, TR("tx.pressFine"));
                        Notify(MessageType.Discipline, "msg.sender.association", TR("msg.pressFine.subject"), TR("msg.pressFine.body", TextArg.Money(fine, Currency)));
                    }
                    break;
                case PressAnswer.Modest:
                    ChangeTrust(club, 0.5);
                    career.Media += 0.2;
                    if (opp is not null) TeamMorale(opp, -0.15);
                    break;
                case PressAnswer.Evasive:
                    career.Media -= 1.2 * mediaSwing * (1 - officer / 40.0);
                    break;
            }
        }
        career.Media = Math.Clamp(career.Media, 0, 100);
        World.PendingPress = null;
        foreach (var m in World.Messages.Where(m => m.Action == MessageAction.PressConference && !m.Resolved)) m.Resolved = true;
        if (!delegated || _rng.Chance(0.3)) PressHeadline(answers, opp);
    }

    /// <summary>Moral eines Teams: Bruchteile wirken als Wahrscheinlichkeit für eine Stufe.</summary>
    private void TeamMorale(Club club, double steps)
    {
        foreach (var p in World.SquadOf(club))
        {
            if (!_rng.Chance(Math.Abs(steps))) continue;
            p.State.Morale = (Morale)Math.Clamp((int)p.State.Morale + Math.Sign(steps), 0, 6);
        }
    }

    private string StyleKey => Style switch { MediaStyle.Satirical => "satirical", MediaStyle.Serious => "serious", _ => "balanced" };

    private void Headline(string situation, params object?[] args)
    {
        var variant = _rng.Next(1, 2);
        Notify(MessageType.Press, "msg.sender.newspaper", TR("headline." + situation + "." + StyleKey + "." + variant, args), TR("headline.body", TR("headline." + situation + "." + StyleKey + "." + variant, args)));
    }

    private void PressHeadline(IReadOnlyList<PressAnswer> answers, Club? opp)
    {
        var dominant = answers.GroupBy(a => a).OrderByDescending(g => g.Count()).First().Key;
        Headline("press" + dominant, LastName(HumanManager.Name), HumanClub.Name, opp?.Name ?? "");
    }

    private static string LastName(string full) => full.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? full;

    /// <summary>Schlagzeile nach einem eigenen Spiel (nicht nach jedem, sonst nutzt sie sich ab).</summary>
    private void MatchHeadline(Fixture f)
    {
        var club = HumanClub;
        var opp = World.Clubs[f.HomeId == club.Id ? f.AwayId : f.HomeId];
        var o = Outcome(club, f);
        var r = f.Result!;
        var diff = Math.Abs(r.HomeGoals - r.AwayGoals);
        var derby = club.Rivals.Contains(opp.Id);
        var recent = AllFixtures.Where(x => x.Involves(club.Id) && x.Result is not null).OrderByDescending(x => x.Date).Take(4).Select(x => Outcome(club, x)).ToList();
        string? situation = derby && o != 0 ? (o > 0 ? "derbyWin" : "derbyLoss")
            : recent.Count == 4 && recent.All(x => x > 0) ? "streakWin"
            : recent.Count == 4 && recent.All(x => x < 0) ? "streakLoss"
            : diff >= 3 ? (o > 0 ? "bigWin" : "bigLoss")
            : _rng.Chance(0.25) ? (o > 0 ? "win" : o < 0 ? "loss" : "draw") : null;
        if (situation is null) return;
        var scorer = r.Goals.Where(g => g.Home == (f.HomeId == club.Id)).GroupBy(g => g.ScorerId).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        var hero = scorer is { } id && World.Players.TryGetValue(id, out var p) ? p.ShortName : LastName(HumanManager.Name);
        var score = f.HomeId == club.Id ? $"{r.HomeGoals}:{r.AwayGoals}" : $"{r.AwayGoals}:{r.HomeGoals}";
        Headline(situation, LastName(HumanManager.Name), club.Name, opp.Name, score, hero);
        HumanManager.Career.Media = Math.Clamp(HumanManager.Career.Media + o * 0.5, 0, 100);
    }

    /// <summary>Wöchentlich: Gerüchte und Spielerinterviews.</summary>
    private void PressWeekly()
    {
        var club = HumanClub;
        var squad = World.SquadOf(club).ToList();
        if (_rng.Chance(0.08) && squad.Count > 0)
        {
            var star = squad.OrderByDescending(Rating).Take(3).ToList();
            var player = _rng.Pick(star);
            var suitor = World.Clubs.Values.Where(c => c.Reputation > club.Reputation && World.FindLeague(c.LeagueId) is not null).ToList();
            if (suitor.Count > 0)
            {
                var s = _rng.Pick(suitor);
                Notify(MessageType.Press, "msg.sender.newspaper", TR("rumour.subject", player.Name, s.Name), TR("rumour." + _rng.Next(1, 4), player.Name, s.Name, club.Name));
                if (_rng.Chance(0.3)) player.State.Morale = (Morale)Math.Max(0, (int)player.State.Morale - 1);
            }
        }
        if (_rng.Chance(0.05))
        {
            var benched = squad.Where(p => p.Contract?.Status is SquadStatus.Star or SquadStatus.Regular && p.SeasonStats.Appearances * 3 < Math.Max(3, World.LeagueOf(club).CurrentMatchday)).ToList();
            if (benched.Count > 0)
            {
                var p = _rng.Pick(benched);
                p.State.Morale = (Morale)Math.Max(0, (int)p.State.Morale - 1);
                Notify(MessageType.Press, "msg.sender.newspaper", TR("interview.unhappy.subject", p.Name), TR("interview.unhappy.body", p.Name, club.Name));
            }
            else if (squad.Count > 0)
            {
                var p = squad.OrderByDescending(x => x.SeasonStats.Goals + x.SeasonStats.Assists).First();
                Notify(MessageType.Press, "msg.sender.newspaper", TR("interview.happy.subject", p.Name), TR("interview.happy.body", p.Name, club.Name, LastName(HumanManager.Name)));
            }
        }
    }
}
