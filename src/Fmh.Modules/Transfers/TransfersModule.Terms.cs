using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>
/// Transfers: Bedingungen, die nach dem Abschluss wirken – Zusatzzahlungen an den abgebenden Verein, Einsatzgarantie bei
/// Leihen und die Verlängerungsoption des Vereins.
/// </summary>
public sealed partial class TransfersModule
{
    public override void Register(ModuleContext ctx)
    {
        base.Register(ctx);
        ctx.Bus.Subscribe<MatchPlayed>(e =>
        {
            if (e.Participants is not { } played) return;
            PayTransferConditions(e.Fixture, played);
            CountLoanPlayingTime(e.Fixture, played);
        });
    }

    // ================================================================== Zusatzzahlungen an den Verkäufer

    /// <summary>Legt die erfolgsabhängigen Zahlungen eines Transfers an (bis zum Ende des neuen Vertrags).</summary>
    private void AddTransferConditions(Player p, Club buyer, Club seller, ClubOffer offer)
    {
        if (p.Contract is not { } contract) return;
        void Add(TransferConditionKind kind, long amount)
        {
            if (amount <= 0) return;
            World.TransferConditions.Add(new TransferCondition
            {
                PlayerId = p.Id, PayerId = buyer.Id, PayeeId = seller.Id, Kind = kind, Amount = amount, Until = contract.Until,
            });
        }
        Add(TransferConditionKind.Appearance, offer.AppearanceBonus);
        Add(TransferConditionKind.Goal, offer.GoalBonus);
    }

    /// <summary>Nach einem Pflichtspiel: Einsätze und Tore für den Käufer lösen die vereinbarten Zahlungen aus.</summary>
    private void PayTransferConditions(Fixture f, IReadOnlySet<int> played)
    {
        foreach (var c in World.TransferConditions)
        {
            if (World.Date > c.Until || !f.Involves(c.PayerId) || !played.Contains(c.PlayerId)) continue;
            if (!World.Players.TryGetValue(c.PlayerId, out var p) || p.ClubId != c.PayerId) continue;
            if (!World.Clubs.TryGetValue(c.PayerId, out var payer) || !World.Clubs.TryGetValue(c.PayeeId, out var payee)) continue;
            var home = f.HomeId == c.PayerId;
            var units = c.Kind == TransferConditionKind.Appearance ? 1
                : f.Result?.Goals.Count(g => g.ScorerId == p.Id && g.Home == home && !g.OwnGoal) ?? 0;
            if (units == 0) continue;
            var amount = c.Amount * units;
            Finance.Book(payer, TransactionType.TransferIn, -amount, TR("tx.addOnPaid", p.Name));
            Finance.Book(payee, TransactionType.TransferOut, amount, TR("tx.addOnReceived", p.Name));
            // Wie bei der Ablöse: Käufer trägt den vollen Betrag, der Verkäufer bekommt die Hälfte ins Transferbudget.
            Budgeted(payer).Budget.TransferBudget -= amount;
            Budgeted(payee).Budget.TransferBudget += amount / 2;
            c.Count += units;
            c.Paid += amount;
        }
    }

    /// <summary>Abgelaufene Bedingungen und solche für Spieler, die den Käufer verlassen haben, entfallen.</summary>
    private void DropFinishedTransferConditions()
    {
        if (World.TransferConditions.Count == 0) return;
        World.TransferConditions.RemoveAll(c => c.Until < World.Date
            || !World.Players.TryGetValue(c.PlayerId, out var p) || p.Retired
            || (p.Loan?.ParentClubId ?? p.ClubId) != c.PayerId);
    }

    // ================================================================== Einsatzgarantie (Leihe)

    /// <summary>Zählt für Leihspieler mit Einsatzgarantie die Pflichtspiele des ausleihenden Vereins und ihre Einsätze.</summary>
    private void CountLoanPlayingTime(Fixture f, IReadOnlySet<int> played)
    {
        foreach (var clubId in new[] { f.HomeId, f.AwayId })
        {
            if (!World.Clubs.TryGetValue(clubId, out var club)) continue;
            foreach (var p in World.SquadOf(club))
            {
                if (p.Loan is not { PlayingTimeGuarantee: true } loan) continue;
                loan.ClubMatches++;
                if (played.Contains(p.Id)) loan.Appearances++;
            }
        }
    }

    /// <summary>Winterfenster des Landes eines Vereins (Rückholungen).</summary>
    private bool InWinterWindow(Club club)
    {
        var country = World.Country(club.CountryCode);
        var tw = country.TransferWindows;
        var winterStart = ScheduleGenerator.ParseDate(tw.WinterStart, World.SeasonYear, country.Season.StartMonth);
        var winterEnd = ScheduleGenerator.ParseDate(tw.WinterEnd, World.SeasonYear, country.Season.StartMonth);
        return World.Date >= winterStart && World.Date <= winterEnd;
    }

    /// <summary>
    /// Im Winterfenster des Stammvereins: Eine verfehlte Einsatzgarantie berechtigt zur Rückholung. KI-Vereine holen den Spieler
    /// sofort zurück, der menschliche Manager wird informiert und kann ihn zurückholen.
    /// </summary>
    private void CheckLoanGuarantees()
    {
        foreach (var p in World.Players.Values.Where(p => p.Loan is { PlayingTimeGuarantee: true, GuaranteeBreachNotified: false }).ToList())
        {
            var loan = p.Loan!;
            if (!TransferTerms.GuaranteeBreached(loan) || !World.Clubs.TryGetValue(loan.ParentClubId, out var parent) || !InWinterWindow(parent)) continue;
            loan.GuaranteeBreachNotified = true;
            var borrower = p.ClubId is { } cid ? World.Clubs[cid] : null;
            if (IsHuman(parent))
            {
                Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.guaranteeBreached.subject", p.Name),
                    TR("msg.guaranteeBreached.body", p.Name, borrower?.Name ?? "", loan.Appearances, loan.ClubMatches));
                continue;
            }
            if (borrower is not null && borrower.Id == HumanClub.Id)
                Notify(MessageType.Transfer, "msg.sender.assistant", TR("msg.guaranteeRecall.subject", p.Name),
                    TR("msg.guaranteeRecall.body", p.Name, parent.Name, loan.Appearances, loan.ClubMatches));
            ReturnLoan(p, notify: false);
        }
    }

    // ================================================================== Verlängerungsoption

    /// <summary>So viele Tage vor dem Stichtag erinnert der Assistent an offene Verlängerungsoptionen.</summary>
    private const int OptionReminderDays = 28;

    public bool CanExerciseExtensionOption(Player p) =>
        TransferTerms.ContractingClubId(p) == HumanClub.Id && TransferTerms.CanExerciseOption(p, World);

    /// <summary>Verlängerungsoption des Vereins ziehen: Vertrag läuft zu gleichen Bedingungen weiter, die Option ist verbraucht.</summary>
    public string? ExerciseExtensionOption(Player p)
    {
        if (!CanExerciseExtensionOption(p)) return "neg.error.noExtensionOption";
        ExtendByOption(p);
        Notify(MessageType.Contract, "msg.sender.assistant", TR("msg.optionExercised.subject", p.Name),
            TR("msg.optionExercised.body", p.Name, TextArg.OfDate(p.Contract!.Until)));
        return null;
    }

    private static void ExtendByOption(Player p)
    {
        var c = p.Contract!;
        c.Until = c.Until.AddYears(Math.Clamp(c.ExtensionOptionYears, 0, TransferTerms.MaxExtensionOptionYears));
        c.ExtensionOptionYears = 0;
    }

    /// <summary>
    /// Täglich: Am Stichtag ziehen KI-Vereine die Option für Spieler, die sie behalten wollen; vier Wochen vorher erinnert der
    /// Assistent den menschlichen Manager an offene Optionen.
    /// </summary>
    private void CheckExtensionOptions()
    {
        var deadlines = World.Countries.ToDictionary(c => c.Code, c => ScheduleGenerator.ParseDate(
            c.TransferWindows.PreContractFrom, World.SeasonYear, c.Season.StartMonth).AddDays(-1));
        if (!deadlines.Values.Any(d => d == World.Date || d.AddDays(-OptionReminderDays) == World.Date)) return;
        var reminders = new List<object?>();
        foreach (var p in World.Players.Values.Where(p => p.Contract is { ExtensionOptionYears: > 0 }).OrderBy(p => p.Id).ToList())
        {
            if (TransferTerms.ContractingClubId(p) is not { } clubId || !World.Clubs.TryGetValue(clubId, out var club)) continue;
            if (!TransferTerms.CanExerciseOption(p, World)) continue;
            var deadline = deadlines[club.CountryCode];
            if (IsHuman(club))
            {
                if (club.Id == HumanClub.Id && deadline.AddDays(-OptionReminderDays) == World.Date) reminders.Add(p.Name);
            }
            else if (deadline == World.Date && ShouldAiRenew(club, p)) ExtendByOption(p);
        }
        if (reminders.Count > 0)
            Notify(MessageType.Contract, "msg.sender.assistant", TR("msg.optionReminder.subject"),
                TR("msg.optionReminder.body", TextArg.Join(", ", reminders), TextArg.OfDate(deadlines[HumanClub.CountryCode])));
    }
}
