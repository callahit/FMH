using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Privatleben des Managers (AP 13b, Anstoß-Element <c>privateLife</c>). Monatlich Gehalt aufs Privatkonto und
/// laufende Kosten ab, Prestige stärkt den Medienruf. Stress steigt nach Niederlagen, sinkt täglich und durch
/// Erholung; ab der Grenze kostet er wöchentlich Vorstandsvertrauen. Ohne Paket oder bei ausgeschaltetem Element
/// geschieht nichts.
/// </summary>
public sealed class PrivateLifeModule : GameModule
{
    public override string Id => "privateLife";
    public override IReadOnlyList<string> DependsOn => new[] { "board" };

    private bool On => Data.PrivateLife is not null && Anstoss.IsOn(World, HumanManager.Id, PrivateLives.Feature);

    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchPlayed>(e =>
        {
            if (!e.HumanInvolved || !On || Board.AwaitingNewJob || e.Fixture.Result is null) return;
            if (!e.Fixture.Involves(HumanClub.Id) || Life() is not { } life) return;
            var outcome = ClubRules.Outcome(HumanClub, e.Fixture);
            var stress = Data.PrivateLife!.Stress;
            AddStress(life, outcome > 0 ? stress.Win : outcome < 0 ? stress.Loss : stress.Draw);
        });
    }

    /// <summary>Privatleben anlegen, sobald das Element an ist (Spielstart, Laden, Einschalten).</summary>
    public void Ensure()
    {
        if (On) Life();
    }

    /// <summary>Privatleben des Menschen; wird beim ersten Bedarf mit eingeschaltetem Element angelegt.</summary>
    private PrivateLife? Life()
    {
        if (World.PrivateLives.TryGetValue(HumanManager.Id, out var life)) return life;
        if (Board.AwaitingNewJob) return null;
        life = PrivateLives.Create(Data.PrivateLife!, HumanClub.Board.ManagerWeeklyWage);
        World.PrivateLives[HumanManager.Id] = life;
        return life;
    }

    private static void AddStress(PrivateLife life, double delta) => life.Stress = Math.Clamp(life.Stress + delta, 0, 100);

    public override void OnDay(DayContext day)
    {
        if (!On || Life() is not { } life) return;
        var rules = Data.PrivateLife!;
        AddStress(life, -(rules.Stress.DailyDecay + PrivateLives.Relief(rules, life)));
        var employed = !Board.AwaitingNewJob;
        if (World.Date.DayOfWeek == DayOfWeek.Monday) Weekly(life, employed);
        if (World.Date.Day == 1) Monthly(life, employed);
    }

    private void Weekly(PrivateLife life, bool employed)
    {
        var rules = Data.PrivateLife!.Stress;
        if (life.Stress < rules.High)
        {
            if (life.Stress < rules.High - 10) life.StressWarned = false;
            return;
        }
        if (employed) Board.ChangeTrust(HumanClub, -rules.BoardPenaltyPerWeek);
        if (life.StressWarned) return;
        life.StressWarned = true;
        Notify(MessageType.Career, "msg.sender.private", TR("private.stress.subject"), TR("private.stress.body", (int)Math.Round(life.Stress)));
    }

    private void Monthly(PrivateLife life, bool employed)
    {
        var rules = Data.PrivateLife!;
        var wage = employed ? (long)Math.Round(HumanClub.Board.ManagerWeeklyWage * 52 / 12.0) : 0;
        var before = life.Savings;
        life.Savings += wage - PrivateLives.MonthlyCost(rules, life);
        var career = HumanManager.Career;
        career.Media = Math.Clamp(career.Media + PrivateLives.Prestige(rules, life), 0, 100);
        if (life.Savings >= 0) return;
        AddStress(life, rules.Stress.Debt);
        if (before >= 0)
            Notify(MessageType.Career, "msg.sender.private", TR("private.debt.subject"), TR("private.debt.body", TextArg.Money(-life.Savings, Currency)));
    }
}
