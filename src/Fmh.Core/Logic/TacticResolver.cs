using Fmh.Core.Data;
using Fmh.Core.Match;
using Fmh.Core.Model;

namespace Fmh.Core.Logic;

/// <summary>
/// Löst eine Taktik in die gespielte Formation auf: Formationsvorlage plus freie Positionierung,
/// Aufgaben, Rollen und Spieleranweisungen je Slot (Kapitel 5).
/// </summary>
public static class TacticResolver
{
    /// <summary>Position, die ein Platz auf dem 5x7-Raster bei freier Positionierung bekommt.</summary>
    public static Position PositionAt(int col, int row) => row switch
    {
        <= 0 => Position.GK,
        1 => col == 0 ? Position.DL : col == 4 ? Position.DR : Position.DC,
        2 => col == 0 ? Position.DL : col == 4 ? Position.DR : Position.DM,
        3 => col == 0 ? Position.ML : col == 4 ? Position.MR : Position.MC,
        4 => col == 0 ? Position.AML : col == 4 ? Position.AMR : Position.AMC,
        _ => Position.ST,
    };

    public static SlotOrder OrderFor(Tactic tactic, int slot)
    {
        while (tactic.Orders.Count <= slot) tactic.Orders.Add(new SlotOrder());
        return tactic.Orders[slot];
    }

    /// <summary>Gespielte Formation inklusive verschobener Plätze und gewählter Aufgaben.</summary>
    public static Formation Resolve(Tactic tactic, GameData data)
    {
        var template = data.Formation(tactic.FormationId);
        var slots = new List<FormationSlot>();
        for (var i = 0; i < template.Slots.Count; i++)
        {
            var baseSlot = template.Slots[i];
            var order = i < tactic.Orders.Count ? tactic.Orders[i] : null;
            var col = order?.Col ?? baseSlot.Col;
            var row = order?.Row ?? baseSlot.Row;
            var moved = order?.Col is not null || order?.Row is not null;
            var pos = moved && baseSlot.Position != Position.GK ? PositionAt(col, row) : baseSlot.Position;
            var role = RoleOf(order, pos, data);
            var duty = order?.Duty is { } d && (role is null || role.Duties.Contains(d)) ? d
                : role is not null && (order?.Role is { Length: > 0 }) ? role.DefaultDuty
                : baseSlot.Duty;
            slots.Add(new FormationSlot { Position = pos, Col = col, Row = row, Duty = duty });
        }
        var custom = tactic.Orders.Any(o => o.Col is not null || o.Row is not null);
        return new Formation { Id = template.Id, Name = custom ? template.Name + " *" : template.Name, Slots = slots };
    }

    public static RoleDef? RoleOf(SlotOrder? order, Position pos, GameData data)
    {
        if (order?.Role is { Length: > 0 } id && data.Role(id) is { } r && r.Positions.Contains(pos)) return r;
        return data.DefaultRole(pos);
    }

    public static List<ResolvedOrder> Orders(Tactic tactic, Formation resolved, GameData data) =>
        resolved.Slots.Select((slot, i) =>
        {
            var o = i < tactic.Orders.Count ? tactic.Orders[i] : null;
            return new ResolvedOrder(RoleOf(o, slot.Position, data), o?.MoreShots ?? false, o?.TightMarking ?? false,
                o?.CrossEarly ?? false, o?.HoldPosition ?? false);
        }).ToList();

    /// <summary>Verschiebt einen Platz auf ein anderes Rasterfeld (freie Positionierung).</summary>
    public static bool MoveSlot(Tactic tactic, GameData data, int slot, int col, int row)
    {
        var resolved = Resolve(tactic, data);
        if (slot < 0 || slot >= resolved.Slots.Count) return false;
        if (resolved.Slots[slot].Position == Position.GK) return false;
        col = Math.Clamp(col, 0, Grid.Cols - 1);
        row = Math.Clamp(row, 1, Grid.Rows - 2);
        if (resolved.Slots.Where((_, i) => i != slot).Any(s => s.Col == col && s.Row == row)) return false;
        var order = OrderFor(tactic, slot);
        order.Col = col;
        order.Row = row;
        // Eine nicht mehr passende Rolle fällt auf die Standardrolle der neuen Position zurück.
        if (data.Role(order.Role) is { } r && !r.Positions.Contains(PositionAt(col, row))) order.Role = "";
        return true;
    }

    /// <summary>Formationsvorlage wechseln: freie Positionierung und Rollen werden zurückgesetzt.</summary>
    public static void ChangeFormation(Tactic tactic, string formationId)
    {
        tactic.FormationId = formationId;
        tactic.Orders.Clear();
    }
}
