using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.SceneTree;

/// <summary>
/// Besetzte Rollen einer Mannschaft. Mehrere Spieler derselben Rolle liegen in Slot-Reihenfolge;
/// das Suffix 1-ST1 meint den zweiten. Spezialrollen (Elfmeter, Freistoß, Ecke) fallen auf den besten Mann zurück.
/// </summary>
public sealed class RoleBook
{
    private readonly Dictionary<int, List<MatchPlayer>> _roles = new();

    public static RoleBook Build(MatchTeamState team)
    {
        var book = new RoleBook();
        var onPitch = team.OnPitch.Where(p => p.SlotIndex >= 0 && p.SlotIndex < team.Formation.Slots.Count).ToList();
        foreach (var p in onPitch.OrderBy(p => team.Formation.Slots[p.SlotIndex].Col).ThenBy(p => p.SlotIndex))
        {
            if (p.Injured && p.Position != Position.GK) continue;
            var slot = team.Formation.Slots[p.SlotIndex];
            switch (p.Position)
            {
                case Position.GK: book.Add(FmRole.Gk, p); break;
                case Position.DR:
                    book.Add(slot.Row >= 2 ? FmRole.Rwb : FmRole.Rb, p);
                    if (slot.Row >= 2) book.Add(FmRole.Rb, p);
                    break;
                case Position.DL:
                    book.Add(slot.Row >= 2 ? FmRole.Lwb : FmRole.Lb, p);
                    if (slot.Row >= 2) book.Add(FmRole.Lb, p);
                    break;
                case Position.DC: break;
                case Position.DM: book.Add(FmRole.Dm, p); break;
                case Position.MC: book.Add(FmRole.Cm, p); break;
                case Position.MR:
                    book.Add(slot.Row >= 4 ? FmRole.Rw : FmRole.Rm, p);
                    if (slot.Row >= 4) book.Add(FmRole.Rm, p);
                    break;
                case Position.ML:
                    book.Add(slot.Row >= 4 ? FmRole.Lw : FmRole.Lm, p);
                    if (slot.Row >= 4) book.Add(FmRole.Lm, p);
                    break;
                case Position.AMR: book.Add(FmRole.Rw, p); book.Add(FmRole.Rm, p); break;
                case Position.AML: book.Add(FmRole.Lw, p); book.Add(FmRole.Lm, p); break;
                case Position.AMC: book.Add(FmRole.Am, p); book.Add(FmRole.Cf, p); break;
                case Position.ST: book.Add(FmRole.St, p); break;
            }
        }

        var dcs = onPitch.Where(p => p.Position == Position.DC && !p.Injured)
            .OrderBy(p => team.Formation.Slots[p.SlotIndex].Col).ToList();
        foreach (var p in dcs) book.Add(FmRole.Cd, p);
        if (dcs.Count > 0) book.Add(FmRole.Sw, dcs[dcs.Count / 2]);

        var dms = onPitch.Where(p => p.Position == Position.DM && !p.Injured)
            .OrderBy(p => team.Formation.Slots[p.SlotIndex].Row).ToList();
        if (dms.Count > 0) book.Add(FmRole.Anc, dms[0]);

        var strikers = onPitch.Where(p => p.Position == Position.ST && !p.Injured).ToList();
        if (strikers.Count >= 2) book.Add(FmRole.Cf, strikers[1]);
        else if (strikers.Count == 1) book.Add(FmRole.Cf, strikers[0]);
        if (!book.Occupied((int)FmRole.Am) && strikers.Count > 0) book.Add(FmRole.Am, strikers[0]);

        book.AddSpecialists(team, onPitch);
        return book;
    }

    public bool Occupied(int code) => _roles.TryGetValue(code, out var list) && list.Count > 0;

    public MatchPlayer? Find(int code, int index)
    {
        if (!_roles.TryGetValue(code, out var list) || list.Count == 0) return null;
        return index >= 0 && index < list.Count ? list[index] : list[^1];
    }

    public MatchPlayer? Find(RoleRef role, int index) => role.IsEmpty ? null : Find(role.Code, index);

    private void Add(FmRole role, MatchPlayer player)
    {
        if (!_roles.TryGetValue((int)role, out var list))
        {
            list = new List<MatchPlayer>();
            _roles[(int)role] = list;
        }
        if (!list.Contains(player)) list.Add(player);
    }

    private void AddSpecialists(MatchTeamState team, List<MatchPlayer> onPitch)
    {
        var outfield = onPitch.Where(p => p.Position != Position.GK && !p.Injured).ToList();
        if (outfield.Count == 0) return;
        MatchPlayer Best(Attr attr) => outfield.MaxBy(p => p.Player.Attributes[attr])!;
        MatchPlayer Pick(int? id, Attr attr) =>
            onPitch.FirstOrDefault(p => p.Player.Id == id && !p.Injured && p.Position != Position.GK) ?? Best(attr);

        Add(FmRole.Pen, Pick(team.Tactic.PenaltyTakerId, Attr.Penalties));
        var fk = Pick(team.Tactic.FreeKickTakerId, Attr.FreeKicks);
        Add(FmRole.Fkc, fk);
        int Col(MatchPlayer p) => team.Formation.Slots[p.SlotIndex].Col;
        Add(FmRole.Fkr, outfield.Where(p => Col(p) >= 3).MaxBy(p => p.Player.Attributes[Attr.Crossing]) ?? fk);
        Add(FmRole.Fkl, outfield.Where(p => Col(p) <= 1).MaxBy(p => p.Player.Attributes[Attr.Crossing]) ?? fk);
        var ck = Pick(team.Tactic.CornerTakerId, Attr.Corners);
        Add(FmRole.Ckr, outfield.Where(p => Col(p) >= 3).MaxBy(p => p.Player.Attributes[Attr.Corners]) ?? ck);
        Add(FmRole.Ckl, outfield.Where(p => Col(p) <= 1).MaxBy(p => p.Player.Attributes[Attr.Corners]) ?? ck);
    }

    public static string FallbackName(int code) => code switch
    {
        1 => "der Torwart",
        2 or 3 or 6 or 7 => "der Außenverteidiger",
        4 or 5 => "der Innenverteidiger",
        8 or 9 or 12 => "der Mittelfeldspieler",
        10 or 11 or 13 or 14 => "der Flügelspieler",
        15 or 16 or 17 => "der Stürmer",
        18 => "der Elfmeterschütze",
        >= 19 and <= 23 => "der Standardschütze",
        _ => "ein Spieler",
    };
}
