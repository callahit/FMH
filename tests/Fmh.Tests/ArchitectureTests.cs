using System.Reflection;
using System.Text.RegularExpressions;
using Fmh.Core.Simulation;

namespace Fmh.Tests;

/// <summary>Architekturregeln aus Umsetzungskonzept 4.1 und 4.2: erlaubte Projektverweise, Dateigrößen, Module ohne Direktzugriffe.</summary>
public class ArchitectureTests
{
    /// <summary>Projekt → Projekte, die es referenzieren darf (nur Fmh.*).</summary>
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["Fmh.Domain"] = Array.Empty<string>(),
        ["Fmh.Data"] = new[] { "Fmh.Domain" },
        ["Fmh.Simulation.Abstractions"] = new[] { "Fmh.Domain" },
        ["Fmh.Modules"] = new[] { "Fmh.Domain", "Fmh.Data", "Fmh.Simulation.Abstractions" },
        ["Fmh.Simulation"] = new[] { "Fmh.Domain", "Fmh.Data", "Fmh.Simulation.Abstractions", "Fmh.Modules" },
        ["Fmh.Persistence"] = new[] { "Fmh.Domain", "Fmh.Simulation.Abstractions" },
        ["Fmh.MatchEngine"] = new[] { "Fmh.Domain" },
    };

    private static string Src => Path.Combine(TestData.RepoRoot(), "src");

    [Fact]
    public void ProjectsReferenceOnlyAllowedProjects()
    {
        foreach (var (name, allowed) in Allowed)
        {
            var asm = Assembly.Load(name);
            var refs = asm.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("Fmh") || n.StartsWith("fmh")).ToList();
            var forbidden = refs.Except(allowed).ToList();
            Assert.True(forbidden.Count == 0, $"{name} referenziert unerlaubt: {string.Join(", ", forbidden)}");
        }
    }

    [Fact]
    public void InterfacesDoNotReferenceEachOther()
    {
        foreach (var project in new[] { "Fmh.Desktop", "Fmh.Calibrate" })
        {
            var csproj = File.ReadAllText(Path.Combine(Src, project, project + ".csproj"));
            foreach (var other in new[] { "Fmh.Desktop", "Fmh.Calibrate", "Fmh.Editor" }.Where(o => o != project))
                Assert.DoesNotContain(other + ".csproj", csproj);
        }
    }

    [Theory]
    [InlineData("Fmh.Simulation")]
    [InlineData("Fmh.Modules")]
    public void NoFileExceedsFourHundredLines(string project)
    {
        var tooLong = Directory.EnumerateFiles(Path.Combine(Src, project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (file: Path.GetFileName(f), lines: File.ReadLines(f).Count()))
            .Where(x => x.lines > 400).ToList();
        Assert.True(tooLong.Count == 0, string.Join(", ", tooLong.Select(x => $"{x.file}: {x.lines}")));
    }

    /// <summary>Module kennen einander nur über Dienst-Schnittstellen und Ereignisse, nie über die Klasse eines anderen Moduls.</summary>
    [Fact]
    public void ModulesDoNotUseOtherModuleClasses()
    {
        var dir = Path.Combine(Src, "Fmh.Modules");
        var modules = Directory.GetDirectories(dir).Select(Path.GetFileName).Where(d => d is not ("bin" or "obj")).ToList();
        foreach (var module in modules)
            foreach (var file in Directory.EnumerateFiles(Path.Combine(dir, module!), "*.cs"))
            {
                var text = File.ReadAllText(file);
                foreach (var other in modules.Where(o => o != module))
                    Assert.False(Regex.IsMatch(text, $@"\b{other}Module\b"), $"{Path.GetFileName(file)} verwendet {other}Module direkt.");
            }
    }

    /// <summary>Alle Dienst-Schnittstellen sind angemeldet, die Modulkennungen eindeutig, die Reihenfolge folgt DependsOn.</summary>
    [Fact]
    public void ModulesRegisterAllServicesInDependencyOrder()
    {
        var s = TestData.NewSession();
        var ids = s.Modules.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        for (var i = 0; i < s.Modules.Count; i++)
            foreach (var d in s.Modules[i].DependsOn)
                Assert.True(ids.IndexOf(d) < i, $"{s.Modules[i].Id} läuft vor {d}.");
        var services = typeof(IGameModule).Assembly.GetTypes().Where(t => t.IsInterface && t.Name.EndsWith("Service"));
        foreach (var service in services)
            Assert.Contains(s.Modules, m => service.IsInstanceOfType(m));
    }
}
