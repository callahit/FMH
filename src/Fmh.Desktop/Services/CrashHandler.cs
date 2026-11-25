using System.Globalization;
using System.Text;
using Avalonia.Threading;
using Fmh.Core.Model;
using Fmh.Core.Persistence;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop.Services;

/// <summary>Ergebnis einer Absturzbehandlung: Pfad des Berichts und (falls möglich) der Notfallspeicherung.</summary>
public sealed record CrashReport(string? ReportPath, string? EmergencySave, string Message);

/// <summary>
/// Unbehandelte Fehler: Bericht nach <c>FMH/crash</c> schreiben und das laufende Spiel als eigene Notfalldatei sichern
/// (<c>notfall-&lt;Zeitpunkt&gt;.fmh</c>), ohne vorhandene Spielstände zu überschreiben. Im Ironman-Modus gibt es keine
/// Notfallkopie; dort bleibt der letzte automatische Spielstand.
/// </summary>
public static class CrashHandler
{
    private static int _handling;

    /// <summary>Fehler im UI-Thread werden gemeldet und das Spiel läuft weiter; Fehler anderer Threads beenden es.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Handle(ex, AppStateOrNull());
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Handle(e.Exception, AppStateOrNull(), emergencySave: false);
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            var report = Handle(e.Exception, AppStateOrNull());
            e.Handled = true;
            Show(report);
        };
    }

    private static AppState? AppStateOrNull()
    {
        try { return AppState.Current; }
        catch (InvalidOperationException) { return null; }
    }

    private static CrashReport Handle(Exception ex, AppState? state, bool emergencySave = true) =>
        Handle(ex, state?.SaveDirectory ?? SaveGame.DefaultDirectory(), emergencySave ? state?.Session?.World : null);

    /// <summary>Bericht schreiben und (mit <paramref name="world"/>) Notfallspeicherung versuchen. Wirft selbst nie.</summary>
    public static CrashReport Handle(Exception ex, string saveDir, World? world, DateTime? now = null)
    {
        // Ein Fehler während der Behandlung (z. B. beim Speichern) darf keine Endlosschleife auslösen.
        if (Interlocked.Exchange(ref _handling, 1) == 1) return new CrashReport(null, null, ex.Message);
        try
        {
            var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string? save = null;
            string? saveError = null;
            if (world is not null && !world.Rules.Ironman)
            {
                try
                {
                    Directory.CreateDirectory(saveDir);
                    save = Path.Combine(saveDir, $"notfall-{stamp}{SaveGame.Extension}");
                    SaveGame.Save(world, save);
                }
                catch (Exception saveEx)
                {
                    saveError = saveEx.Message;
                    save = null;
                }
            }
            string? report = null;
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(saveDir)) ?? saveDir, "crash");
                Directory.CreateDirectory(dir);
                report = Path.Combine(dir, $"crash-{stamp}.txt");
                File.WriteAllText(report, Describe(ex, world, save, saveError), Encoding.UTF8);
            }
            catch (IOException) { report = null; }
            catch (UnauthorizedAccessException) { report = null; }
            return new CrashReport(report, save, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _handling, 0);
        }
    }

    private static string Describe(Exception ex, World? world, string? save, string? saveError)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FMH Absturzbericht");
        sb.AppendLine("Zeit: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        sb.AppendLine("Version: " + typeof(CrashHandler).Assembly.GetName().Version);
        sb.AppendLine("System: " + Environment.OSVersion + ", .NET " + Environment.Version);
        if (world is not null)
            sb.AppendLine($"Spiel: Datum {world.Date:yyyy-MM-dd}, Saison {world.SeasonYear}, Seed {world.Seed}");
        sb.AppendLine("Notfallspeicherung: " + (save ?? (saveError is null ? "keine" : "fehlgeschlagen: " + saveError)));
        sb.AppendLine();
        sb.AppendLine(ex.ToString());
        return sb.ToString();
    }

    private static void Show(CrashReport report)
    {
        if (MainWindowViewModel.Instance is not { } main) return;
        var t = AppStateOrNull()?.T;
        if (t is null) return;
        var lines = new List<string> { t.Format("crash.body", report.Message) };
        if (report.EmergencySave is { } save) lines.Add(t.Format("crash.saved", Path.GetFileName(save)));
        if (report.ReportPath is { } path) lines.Add(t.Format("crash.report", path));
        main.ShowDialog(new MessageDialogViewModel(t["crash.title"], lines));
    }
}
