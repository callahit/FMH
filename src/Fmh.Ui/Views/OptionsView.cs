using Fmh.Core.Model;
using Fmh.Ui.Rendering;

namespace Fmh.Ui.Views;

/// <summary>Persönliche Einstellungen des Managerprofils (Kapitel 2). Weltregeln sind hier nur lesbar.</summary>
public sealed class OptionsView : View
{
    private sealed record Option(string LabelKey, Func<ManagerProfile, string> Value, Action<ManagerProfile, int> Change);

    private readonly ListState _list = new();
    private readonly List<Option> _options;

    public OptionsView(App app) : base(app)
    {
        string Yes(bool b) => b ? T["ui.yes"] : T["ui.no"];
        static TEnum Next<TEnum>(TEnum v, int d) where TEnum : struct, Enum
        {
            var all = Enum.GetValues<TEnum>();
            return all[(Array.IndexOf(all, v) + all.Length + d) % all.Length];
        }
        _options = new List<Option>
        {
            new("options.wage", p => T["wage." + p.WageDisplay], (p, d) => p.WageDisplay = Next(p.WageDisplay, d)),
            new("options.attributes", p => T["attrDisplay." + p.AttributeDisplay], (p, d) => p.AttributeDisplay = Next(p.AttributeDisplay, d)),
            new("options.matchDisplay", p => T["matchDisplay." + p.MatchDisplay], (p, d) => p.MatchDisplay = Next(p.MatchDisplay, d)),
            new("options.matchSpeed", p => T["speed." + p.MatchSpeed], (p, d) => p.MatchSpeed = Math.Clamp(p.MatchSpeed + d, 1, 5)),
            new("options.pauseGoals", p => Yes(p.PauseOnGoals), (p, _) => p.PauseOnGoals = !p.PauseOnGoals),
            new("options.pauseRed", p => Yes(p.PauseOnRedCards), (p, _) => p.PauseOnRedCards = !p.PauseOnRedCards),
            new("options.theme", p => p.Theme, (p, d) =>
            {
                var all = Theme.All;
                var i = all.ToList().FindIndex(t => t.Name == p.Theme);
                p.Theme = all[(i + all.Count + d) % all.Count].Name;
            }),
        };
    }

    public override void Render(Canvas c)
    {
        var session = App.Session!;
        var profile = session.HumanManager.Profile;
        c.Clear(Th.Fg, Th.Bg);
        c.Fill(0, 0, c.Width, 1, ' ', Th.MenuFg, Th.MenuBg);
        c.Write(1, 0, T.Format("options.title", session.HumanManager.Name), Th.MenuFg, Th.MenuBg);

        c.Write(2, 2, T["options.personal"], Th.Accent, Th.Bg);
        for (var i = 0; i < _options.Count; i++)
        {
            var o = _options[i];
            var sel = i == _list.Index;
            var y = 4 + i;
            c.WriteField(2, y, 26, T[o.LabelKey], Th.Dim, Th.Bg);
            c.WriteField(29, y, 30, $"◄ {o.Value(profile)} ►", sel ? Th.SelFg : Th.Fg, sel ? Th.SelBg : Th.Bg);
        }

        var rules = session.World.Rules;
        var y2 = 6 + _options.Count;
        c.Write(2, y2++, T["options.world"], Th.Accent, Th.Bg);
        c.Write(2, y2++, $"{T["newGame.injuryRule"],-26} {rules.InjuryFrequency:0.0}x", Th.Fg, Th.Bg);
        c.Write(2, y2++, $"{T["newGame.ironman"],-26} {(rules.Ironman ? T["ui.yes"] : T["ui.no"])}", Th.Fg, Th.Bg);
        c.Write(2, y2++, $"{T["options.autosave"],-26} {rules.AutosaveSlots}", Th.Fg, Th.Bg);
        c.Write(2, y2 + 1, T["options.worldHint"], Th.Dim, Th.Bg, c.Width - 4);

        Draw.KeyHelp(c, c.Height - 1, Th, T["help.options"]);
    }

    public override void Handle(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key, _options.Count)) return;
        var profile = App.Session!.HumanManager.Profile;
        switch (key.Key)
        {
            case ConsoleKey.Escape: App.Pop(); break;
            case ConsoleKey.LeftArrow: _options[_list.Index].Change(profile, -1); break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.Enter: _options[_list.Index].Change(profile, +1); break;
        }
    }
}
