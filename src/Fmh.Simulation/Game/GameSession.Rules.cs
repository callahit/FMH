using Fmh.Core.Data;
using Fmh.Core.Logic;
using Fmh.Core.Model;

namespace Fmh.Core.Game;

/// <summary>Fassade: Regelfunktionen, die die Oberfläche direkt braucht.</summary>
public sealed partial class GameSession
{
    public static double Scale(Club club) => ClubRules.Scale(club);
    public static long LevelCost(BuildingDef def, int level) => ClubRules.LevelCost(def, level);
    public static long LandPrice(Club club) => ClubRules.LandPrice(club);
    public static (double cost, double days) ContractorFactors(Contractor c) => ClubRules.ContractorFactors(c);
    public static long StadiumUpkeep(Stadium st) => ClubRules.StadiumUpkeep(st);
    public static int ScoutWage(Scout s) => ClubRules.ScoutWage(s);
    public static Stadium CreateStadium(Club club, Rng rng) => StadiumBuilder.CreateStadium(club, rng);
    public static Stadium BuildNewStadium(Club club, StadiumDesign d) => StadiumBuilder.BuildNewStadium(club, d);
}
