using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;
using Fmh.Core.Game;
using Fmh.Core.Simulation;

namespace Fmh.Core.Modules;

/// <summary>Vereinsgelände: Bauaufträge, Baustellen und Startausstattung.</summary>
public sealed partial class FacilitiesModule
{
    // ================================================================== Bauaufträge

    public static (double cost, double days) ContractorFactors(Contractor c) => c switch
    {
        Contractor.Cheap => (0.85, 1.4),
        Contractor.Premium => (1.3, 0.7),
        _ => (1.0, 1.0),
    };

    /// <summary>Was ein Stadionausbau kostet und wie lange er dauert (Standardunternehmen).</summary>
    public (long cost, int days)? StadiumProjectQuote(Club club, ProjectKind kind, StandSide? side)
    {
        var st = club.Stadium;
        var stand = side is { } s ? st.Stand(s) : null;
        return kind switch
        {
            ProjectKind.Standing => (450_000, 45),
            ProjectKind.Seating => (1_300_000, 75),
            ProjectKind.Roof when stand is { Roof: false } => (Math.Max(300_000, stand.Capacity * 220L), 90),
            ProjectKind.Boxes when stand is { Boxes: < 40 } => (800_000, 120),
            ProjectKind.Floodlight when st.Floodlight < 4 => (400_000L * (st.Floodlight + 1), 60),
            ProjectKind.Heating when !st.UndersoilHeating => (1_500_000, 30),
            ProjectKind.Scoreboard when st.Scoreboard < 3 => (300_000L * (st.Scoreboard + 1), 30),
            _ => null,
        };
    }

    /// <summary>Gibt einen Bauauftrag für das Stadion in Auftrag. Fehlertext-Schlüssel oder null.</summary>
    public string? OrderStadiumProject(ProjectKind kind, StandSide? side, Contractor contractor, bool financed = false)
    {
        var club = HumanClub;
        if (StadiumProjectQuote(club, kind, side) is not { } quote) return "build.error.notPossible";
        if (club.Grounds.Projects.Any(p => p.State is ProjectState.Building or ProjectState.AwaitingPermit or ProjectState.AwaitingBoard
                                           && p.Kind == kind && p.Side == side))
            return "build.error.running";
        return Order(club, new ConstructionProject { Kind = kind, Side = side, Amount = kind is ProjectKind.Standing or ProjectKind.Seating ? 1000 : kind == ProjectKind.Boxes ? 5 : 1 },
            quote.cost, quote.days, contractor, financed);
    }

    /// <summary>Neubau auf dem Gelände. Fehlertext-Schlüssel oder null.</summary>
    public string? OrderBuilding(string type, int x, int y, Contractor contractor, bool financed = false)
    {
        var club = HumanClub;
        if (Data.Building(type) is not { } def) return "build.error.unknown";
        if (!def.Multiple && club.Grounds.Buildings.Any(b => b.Type == type)) return "build.error.exists";
        if (MissingRequirement(club, def) is { } missing) return missing;
        if (!Fits(club, def, x, y)) return "build.error.space";
        if (def.Land)
        {
            Place(club, def, x, y, 1);
            return null;
        }
        return Order(club, new ConstructionProject { Kind = ProjectKind.NewBuilding, BuildingType = type, X = x, Y = y, Amount = 1 },
            def.Cost, def.BuildDays, contractor, financed);
    }

    public string? UpgradeBuilding(Building b, Contractor contractor, bool financed = false)
    {
        var club = HumanClub;
        if (Data.Building(b.Type) is not { } def || def.Land) return "build.error.unknown";
        if (b.Level >= def.MaxLevel) return "build.error.maxLevel";
        if (b.UnderConstruction) return "build.error.running";
        return Order(club, new ConstructionProject { Kind = ProjectKind.UpgradeBuilding, BuildingId = b.Id, BuildingType = b.Type, X = b.X, Y = b.Y, Amount = b.Level + 1 },
            LevelCost(def, b.Level + 1), Math.Max(10, def.BuildDays / 2), contractor, financed);
    }

    public string? MissingRequirement(Club club, BuildingDef def)
    {
        foreach (var req in def.Requires)
            if (BuildingLevel(club, req.Type) < req.Level) return "build.error.requires";
        return null;
    }

    public string? Demolish(Building b)
    {
        var club = HumanClub;
        if (b.UnderConstruction) return "build.error.running";
        club.Grounds.Buildings.Remove(b);
        InvalidateEffects(club);
        return null;
    }

    public void ToggleLease(Building b)
    {
        b.Leased = !b.Leased;
        InvalidateEffects(HumanClub);
    }

    public string? BuyLand()
    {
        var club = HumanClub;
        var price = LandPrice(club) * club.Grounds.Height;
        if (price > club.Budget.Balance) return "neg.error.balance";
        if (club.Grounds.ExtraColumns >= 6) return "build.error.noLand";
        Finance.Book(club, TransactionType.Construction, -price, TR("tx.land"));
        club.Grounds.ExtraColumns++;
        return null;
    }

    public string? Order(Club club, ConstructionProject p, long baseCost, int baseDays, Contractor contractor, bool financed)
    {
        var (cf, df) = ContractorFactors(contractor);
        var fin = World.Country(club.CountryCode).Finance;
        p.Id = World.NextProjectId++;
        p.Cost = MarketValue.Round(baseCost * cf);
        p.Contractor = contractor;
        p.Ordered = World.Date;
        p.Financed = financed;
        p.End = null;
        var days = (int)Math.Round(baseDays * df);
        if (!financed && p.Cost > club.Budget.Balance) return "neg.error.balance";
        var stadiumLoan = p.Kind == ProjectKind.NewStadium;
        if (financed && p.Cost > (stadiumLoan ? StadiumPlans.StadiumLoanCapacity(club) : Finance.LoanCapacity(club))) return "fin.error.loanLimit";

        // Großprojekte: erst der Vorstand, dann ggf. die Baugenehmigung der Stadt.
        if (p.Cost > fin.BoardApprovalAbove)
        {
            var approve = club.Board.Trust >= 40 && (financed || club.Budget.Balance - p.Cost > -Finance.CreditLimit(club) / 2);
            if (!approve)
            {
                p.State = ProjectState.Rejected;
                club.Grounds.Projects.Add(p);
                if (IsHuman(club)) Notify(MessageType.Stadium, "msg.sender.board", TR("msg.build.boardNo.subject"), TR("msg.build.boardNo.body", ProjectName(p), TextArg.Money(p.Cost, Currency)));
                return "build.error.boardRejected";
            }
        }
        if (financed) Finance.TakeLoan(club, p.Cost, stadiumLoan ? 20 : 10, silent: true, stadiumLoan ? StadiumPlans.StadiumLoanCapacity(club) : null);
        Finance.Book(club, TransactionType.Construction, -p.Cost, TR("tx.construction", ProjectName(p)));
        if (p.Cost > fin.PermitAbove)
        {
            p.State = ProjectState.AwaitingPermit;
            var wait = Rng.Next(30, 150);
            p.PermitDate = World.Date.AddDays(wait);
            p.Start = p.PermitDate;
        }
        else
        {
            p.State = ProjectState.Building;
            p.Start = World.Date;
        }
        p.End = p.Start!.Value.AddDays(days);
        club.Grounds.Projects.Add(p);
        BeginWork(club, p);
        return null;
    }

    public TextArg ProjectName(ConstructionProject p) => p.Kind switch
    {
        ProjectKind.NewBuilding or ProjectKind.UpgradeBuilding => TextArg.Of(TR("project." + p.Kind, TR("building." + p.BuildingType), p.Amount)),
        ProjectKind.NewStadium => TextArg.Of(TR("project.NewStadium", p.Design?.Name ?? "", p.Amount)),
        _ => TextArg.Of(TR("project." + p.Kind, p.Side is { } s ? TR("stand." + s) : TR("stadium.whole"), p.Amount)),
    };

    /// <summary>Baustelle einrichten: Neubau belegt die Fläche, Tribünenumbau sperrt Plätze.</summary>
    private void BeginWork(Club club, ConstructionProject p)
    {
        if (p.Kind == ProjectKind.NewBuilding && Data.Building(p.BuildingType!) is { } def)
        {
            var b = Place(club, def, p.X, p.Y, 0);
            b.UnderConstruction = true;
            p.BuildingId = b.Id;
        }
        else if (p.Kind == ProjectKind.UpgradeBuilding && club.Grounds.Buildings.FirstOrDefault(b => b.Id == p.BuildingId) is { } existing)
            existing.UnderConstruction = true;
        if (p.Side is { } side && p.Kind is ProjectKind.Standing or ProjectKind.Seating or ProjectKind.Roof && p.State == ProjectState.Building)
        {
            var stand = club.Stadium.Stand(side);
            stand.Closed = stand.Capacity / 3;
        }
        InvalidateEffects(club);
    }

    private Building Place(Club club, BuildingDef def, int x, int y, int level)
    {
        var b = new Building { Id = World.NextBuildingId++, Type = def.Id, X = x, Y = y, Level = level };
        club.Grounds.Buildings.Add(b);
        InvalidateEffects(club);
        return b;
    }

    /// <summary>Tägliche Bauaufsicht: Genehmigungen, Verzögerungen, Pleiten, Fertigstellung.</summary>
    private void ConstructionDaily()
    {
        foreach (var club in World.Clubs.Values)
        {
            foreach (var p in club.Grounds.Projects.Where(p => p.State is ProjectState.AwaitingPermit or ProjectState.Building).ToList())
            {
                var human = IsHuman(club);
                if (p.State == ProjectState.AwaitingPermit && World.Date >= p.PermitDate)
                {
                    // Anwohnerprotest gegen Flutlicht und Parkhaus macht die Genehmigung schwieriger.
                    var risk = p.Kind == ProjectKind.Floodlight || p.BuildingType is "parkingGarage" or "hotel" ? 0.2 : 0.08;
                    if (Rng.Chance(risk))
                    {
                        p.State = ProjectState.Rejected;
                        Finance.Book(club, TransactionType.Construction, (long)(p.Cost * 0.9), TR("tx.constructionRefund", ProjectName(p)));
                        if (p.BuildingId is { } bid) club.Grounds.Buildings.RemoveAll(b => b.Id == bid && b.Level == 0);
                        foreach (var b in club.Grounds.Buildings.Where(b => b.Id == p.BuildingId)) b.UnderConstruction = false;
                        // Abgelehnter Neubau: das Grundstück steht weiter zur Verfügung.
                        if (p.Kind == ProjectKind.NewStadium && club.Grounds.Site is { } site) site.Used = false;
                        InvalidateEffects(club);
                        if (human) Notify(MessageType.Stadium, "msg.sender.city", TR("msg.build.permitNo.subject"), TR("msg.build.permitNo.body", ProjectName(p)));
                        continue;
                    }
                    p.State = ProjectState.Building;
                    BeginWork(club, p);
                    if (human) Notify(MessageType.Stadium, "msg.sender.city", TR("msg.build.permit.subject"), TR("msg.build.permit.body", ProjectName(p), TextArg.OfDate(p.End!.Value)));
                }
                if (p.State != ProjectState.Building) continue;
                // Wöchentliche Risiken je nach Bauunternehmen.
                if (World.Date.DayOfWeek == DayOfWeek.Monday)
                {
                    var delay = p.Contractor switch { Contractor.Cheap => 0.05, Contractor.Premium => 0.01, _ => 0.025 };
                    if (World.Date.Month is 12 or 1 or 2) delay *= 2;
                    if (Rng.Chance(delay))
                    {
                        var days = Rng.Next(7, 21);
                        p.End = p.End!.Value.AddDays(days);
                        if (human) Notify(MessageType.Stadium, "msg.sender.assistant", TR("msg.build.delay.subject"), TR("msg.build.delay.body", ProjectName(p), days, TextArg.OfDate(p.End.Value)));
                    }
                    if (p.Contractor == Contractor.Cheap && Rng.Chance(0.004))
                    {
                        p.End = p.End!.Value.AddDays(60);
                        var extra = MarketValue.Round(p.Cost * 0.1);
                        Finance.Book(club, TransactionType.Construction, -extra, TR("tx.construction", ProjectName(p)));
                        if (human) Notify(MessageType.Stadium, "msg.sender.assistant", TR("msg.build.bankrupt.subject"), TR("msg.build.bankrupt.body", ProjectName(p), TextArg.Money(extra, Currency)));
                    }
                }
                if (World.Date >= p.End) Complete(club, p);
            }
        }
    }

    private void Complete(Club club, ConstructionProject p)
    {
        p.State = ProjectState.Done;
        var st = club.Stadium;
        var stand = p.Side is { } side ? st.Stand(side) : null;
        switch (p.Kind)
        {
            case ProjectKind.Standing: stand!.Standing += p.Amount; break;
            case ProjectKind.Seating: stand!.Seating += p.Amount; break;
            case ProjectKind.Roof: stand!.Roof = true; break;
            case ProjectKind.Boxes: stand!.Boxes = Math.Min(40, stand.Boxes + p.Amount); break;
            case ProjectKind.Floodlight: st.Floodlight = Math.Min(4, st.Floodlight + 1); break;
            case ProjectKind.Heating: st.UndersoilHeating = true; break;
            case ProjectKind.Scoreboard: st.Scoreboard = Math.Min(3, st.Scoreboard + 1); break;
            case ProjectKind.NewStadium when p.Design is { } design:
                club.Stadium = StadiumPlansModule.BuildNewStadium(club, design);
                st = club.Stadium;
                club.StadiumName = design.Name;
                if (st.Capacity >= 50_000 && IsHuman(club)) Board.Achieve("bigStadium");
                break;
            case ProjectKind.NewBuilding or ProjectKind.UpgradeBuilding:
                if (club.Grounds.Buildings.FirstOrDefault(b => b.Id == p.BuildingId) is { } b)
                {
                    b.Level = p.Amount;
                    b.UnderConstruction = false;
                    b.Condition = 100;
                }
                break;
        }
        if (stand is not null) stand.Closed = 0;
        club.StadiumCapacity = st.Capacity;
        InvalidateEffects(club);
        if (IsHuman(club))
            Notify(MessageType.Stadium, "msg.sender.assistant", TR("msg.build.done.subject"), TR("msg.build.done.body", ProjectName(p)));
    }

    /// <summary>Monatlich: Zustand der Gebäude, Rasenpflege.</summary>
    public void FacilitiesMonthly(Club club)
    {
        StadiumPlans.SiteMonthly(club);
        foreach (var b in club.Grounds.Buildings.Where(b => b.Level > 0))
            b.Condition = club.Grounds.Maintenance ? Math.Min(100, b.Condition + 1.5) : Math.Max(0, b.Condition - 4);
        var pitchCare = Effect(club, "pitch") + Staff.StaffAbility(club, StaffRole.Groundsman) * 0.25;
        club.Stadium.Pitch = Math.Clamp(club.Stadium.Pitch + pitchCare - 3 + (club.Stadium.UndersoilHeating ? 1 : 0), 30, 100);
        InvalidateEffects(club);
    }

    // ================================================================== Startausstattung

    /// <summary>Stadion aus der Kapazität ableiten: vier Tribünen, Stehplatzanteil nach Vereinsgröße.</summary>
    public static Stadium CreateStadium(Club club, Rng rng)
    {
        var cap = Math.Max(1_000, club.StadiumCapacity);
        var standingShare = Math.Clamp(0.55 - club.Reputation * 0.02, 0.15, 0.55);
        var shares = new[] { (StandSide.North, 0.22), (StandSide.South, 0.22), (StandSide.East, 0.28), (StandSide.West, 0.28) };
        var st = new Stadium
        {
            Floodlight = club.Reputation >= 9 ? 3 + (club.Reputation >= 15 ? 1 : 0) : club.Reputation >= 5 ? 2 : 1,
            UndersoilHeating = club.Reputation >= 11,
            Scoreboard = Math.Clamp(club.Reputation / 6, 0, 3),
            Pitch = 75 + club.Reputation,
        };
        foreach (var (side, share) in shares)
        {
            var c = (int)(cap * share);
            // Stehplätze vor allem in den Kurven (Nord/Süd).
            var standing = side is StandSide.North or StandSide.South ? (int)(c * Math.Min(0.95, standingShare * 1.8)) : (int)(c * standingShare * 0.2);
            st.Stands.Add(new Stand
            {
                Side = side, Standing = standing, Seating = c - standing,
                Roof = club.Reputation >= 8 || side == StandSide.West,
                Boxes = side == StandSide.West ? Math.Clamp((club.Reputation - 4) * 3, 0, 40) : side == StandSide.East ? Math.Clamp((club.Reputation - 12) * 2, 0, 20) : 0,
            });
        }
        club.StadiumCapacity = st.Capacity;
        return st;
    }

    /// <summary>Startgebäude nach Vereinsgröße.</summary>
    public void CreateGrounds(Club club)
    {
        var r = club.Reputation;
        club.Grounds = new Grounds { Width = r >= 10 ? 14 : r >= 5 ? 12 : 10, Height = r >= 10 ? 9 : 8 };
        var g = club.Grounds;
        g.StadiumX = g.Width / 2 - 2;
        var start = new (string type, int level, int count)[]
        {
            ("dressingRooms", 1 + r / 5, 1), ("trainingPitch", Math.Max(1, r / 4), r >= 12 ? 3 : r >= 6 ? 2 : 1), ("gym", r / 5, 1),
            ("physio", 1 + r / 6, 1), ("youthCentre", r / 4, 1), ("youthPitches", r / 7, 1), ("fanshop", 1 + r / 5, 1),
            ("snackBar", 1 + r / 8, 1 + r / 6), ("beerGarden", r / 8, 1), ("parking", 1 + r / 5, 1), ("office", 1 + r / 6, 1),
            ("scoutingDept", r / 6, 1), ("videoRoom", r / 8, 1), ("goalkeeperArea", r / 8, 1), ("clubhouse", 1, 1),
            ("museum", r / 9, 1), ("businessClub", r / 8, 1), ("medicalCentre", 0, 0), ("clinic", r / 12, 1), ("restaurant", r / 10, 1),
        };
        foreach (var (type, level, count) in start)
        {
            if (level <= 0 || Data.Building(type) is not { } def) continue;
            for (var i = 0; i < count; i++)
            {
                if (FreeSpot(club, def) is not { } spot) break;
                Place(club, def, spot.x, spot.y, Math.Min(def.MaxLevel, level));
            }
        }
    }
}
