using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Match;
using Fmh.Core.Model;
using Fmh.Core.Modules;

namespace Fmh.Core.Game;

/// <summary>Fassade: öffentliche Aufrufe des Moduls „Transfers“.</summary>
public sealed partial class GameSession
{
    public long ValueOf(Player p) => _transfers.ValueOf(p);
    public IEnumerable<Player> FreeAgents => _transfers.FreeAgents;
    public IReadOnlyList<Negotiation> OpenNegotiations => _transfers.OpenNegotiations;
    public Negotiation? OpenNegotiationFor(Player p) => _transfers.OpenNegotiationFor(p);
    public (Negotiation? negotiation, string? errorKey) StartNegotiation(Player p, DealType type) => _transfers.StartNegotiation(p, type);
    public bool CanPreContract(Player p) => _transfers.CanPreContract(p);
    public NegotiationResponse SubmitClubOffer(Negotiation n, ClubOffer offer) => _transfers.SubmitClubOffer(n, offer);
    public NegotiationResponse SubmitContract(Negotiation n, ContractOffer offer) => _transfers.SubmitContract(n, offer);
    public void Withdraw(Negotiation n) => _transfers.Withdraw(n);
    public TextRef RespondIncoming(Negotiation n, ResponseKind answer, long counterFee = 0) => _transfers.RespondIncoming(n, answer, counterFee);
    public void DelegateDecisions() => _transfers.DelegateDecisions();
    public void SetTransferListed(Player p, bool listed, long? askingPrice = null) => _transfers.SetTransferListed(p, listed, askingPrice);
    public void SetLoanListed(Player p, bool listed) => _transfers.SetLoanListed(p, listed);
    public void ToggleShortlist(Player p) => _transfers.ToggleShortlist(p);
    public string? ExercisePurchaseOption(Player p) => _transfers.ExercisePurchaseOption(p);
    public string? RecallLoan(Player p) => _transfers.RecallLoan(p);
    public bool SignBestFreeAgent(Club club, Position pos, bool withinBudget = false) => _transfers.SignBestFreeAgent(club, pos, withinBudget);
}
