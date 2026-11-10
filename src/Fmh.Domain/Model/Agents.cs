namespace Fmh.Core.Model;

/// <summary>Spielerberater mit Mandanten (AP 7): beeinflusst Wechselwünsche, Gehaltsforderungen und Beraterhonorar.</summary>
public sealed class Agent
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Ländercode des Büros.</summary>
    public string Country { get; set; } = "";
    /// <summary>Ruf 1–20: große Berater betreuen die besseren Spieler.</summary>
    public int Reputation { get; set; } = 10;
    /// <summary>Gier 1–20: höheres Honorar und höhere Gehaltsforderungen.</summary>
    public int Greed { get; set; } = 10;
    /// <summary>Einfluss 1–20: wie stark der Berater Wechselwünsche seiner Mandanten anstößt.</summary>
    public int Influence { get; set; } = 10;
}
