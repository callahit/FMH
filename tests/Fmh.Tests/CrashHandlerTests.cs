using Fmh.Core.Data;
using Fmh.Core.Persistence;
using Fmh.Desktop.Services;
using Microsoft.Data.Sqlite;

namespace Fmh.Tests;

/// <summary>Unbehandelte Fehler: Bericht und Notfallspeicherung, ohne vorhandene Spielstände anzufassen.</summary>
public sealed class CrashHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-crash-" + Guid.NewGuid().ToString("N"));
    private string Saves => Path.Combine(_root, "saves");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static Exception Thrown()
    {
        try { throw new InvalidOperationException("Testfehler"); }
        catch (InvalidOperationException ex) { return ex; }
    }

    [Fact]
    public void WritesReportAndEmergencySaveThatLoadsAgain()
    {
        var s = TestData.NewSession(7);
        var report = CrashHandler.Handle(Thrown(), Saves, s.World, new DateTime(2026, 11, 25, 10, 30, 0));

        Assert.Equal(Path.Combine(Saves, "notfall-20261125-103000.fmh"), report.EmergencySave);
        Assert.Equal(s.World.Seed, SaveGame.Load(report.EmergencySave!).Seed);
        var text = File.ReadAllText(report.ReportPath!);
        Assert.Equal(Path.Combine(_root, "crash"), Path.GetDirectoryName(report.ReportPath));
        Assert.Contains("Testfehler", text);
        Assert.Contains("Seed 7", text);
        Assert.Contains("notfall-20261125-103000.fmh", text);
    }

    [Fact]
    public void IronmanAndNoGameWriteOnlyTheReport()
    {
        var s = TestData.NewSession(8);
        s.World.Rules.Ironman = true;
        var ironman = CrashHandler.Handle(Thrown(), Saves, s.World);
        Assert.Null(ironman.EmergencySave);
        Assert.True(File.Exists(ironman.ReportPath));
        Assert.False(Directory.Exists(Saves) && Directory.EnumerateFiles(Saves).Any());

        var none = CrashHandler.Handle(Thrown(), Saves, null, new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.Null(none.EmergencySave);
        Assert.Contains("Notfallspeicherung: keine", File.ReadAllText(none.ReportPath!));
    }
}

/// <summary>Doppelte Länderlisten in Datenpaketen sind ein klarer Datenfehler mit beiden Dateien.</summary>
public sealed class PackageDuplicateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fmh-dup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void DuplicateNameListNamesBothFiles()
    {
        var source = Path.Combine(PackageLoader.FindDataRoot(), "base");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root, "base", Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var names = Path.Combine(_root, "base", "names");
        var first = Directory.GetFiles(names, "*.json").OrderBy(f => f, StringComparer.Ordinal).First();
        File.Copy(first, Path.Combine(names, "zz-kopie.json"));

        var ex = Assert.Throws<InvalidDataException>(() => PackageLoader.Load(new[] { _root }, enabledPackages: new[] { "base" }));
        Assert.Contains(Path.GetFileName(first), ex.Message);
        Assert.Contains("zz-kopie.json", ex.Message);
    }
}
