using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core.Data;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

/// <summary>
/// Bedienung (AP 12b): Tastenhilfe in der Statuszeile nach der aktuellen Belegung und Tutorial, das beim ersten Öffnen
/// einer Seite einen Hinweis aus rules/ui.json zeigt, bis er bestätigt ist.
/// </summary>
public sealed partial class ShellViewModel
{
    public string HelpText
    {
        get
        {
            var keys = KeyMap.Current;
            return T.Format("help.shell", keys.Display("continue", T), keys.Display("save", T), keys.Display("options", T));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTutorial), nameof(TutorialTitle), nameof(TutorialText))]
    private TutorialStep? _tutorial;

    public bool HasTutorial => Tutorial is not null;
    public string TutorialTitle => Tutorial is null ? "" : T["tutorial." + Tutorial.Id + ".title"];
    public string TutorialText => Tutorial is null ? "" : T["tutorial." + Tutorial.Id + ".text"];

    private void ShowTutorial(NavItem item)
    {
        var settings = State.Settings;
        Tutorial = settings.Tutorial
            ? Data.Ui.Tutorial.FirstOrDefault(s => s.Page == item.Id && !settings.TutorialSeen.Contains(s.Id))
            : null;
    }

    /// <summary>Hinweis gelesen: erscheint nicht wieder.</summary>
    [RelayCommand]
    private void TutorialDone()
    {
        if (Tutorial is null) return;
        if (!State.Settings.TutorialSeen.Contains(Tutorial.Id)) State.Settings.TutorialSeen.Add(Tutorial.Id);
        State.SaveSettings();
        Tutorial = null;
    }

    /// <summary>Tutorial ganz ausschalten (in den Einstellungen wieder einschaltbar).</summary>
    [RelayCommand]
    private void TutorialOff()
    {
        State.Settings.Tutorial = false;
        State.SaveSettings();
        Tutorial = null;
    }

    /// <summary>Nach Änderung der Tutorial-Einstellung: Hinweis der aktuellen Seite neu bestimmen.</summary>
    public void RefreshTutorial()
    {
        if (Nav.FirstOrDefault(n => n.IsActive) is { } active) ShowTutorial(active);
    }
}
