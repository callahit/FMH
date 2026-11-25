using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fmh.Desktop.Services;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop.Views;

public partial class MainWindow : Window
{
    private IInputElement? _focusBeforeDialog;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.PropertyChanged += OnViewModelChanged;
        };
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        // Texteingaben behalten ihre Tasten.
        if (e.Source is TextBox && e.Key is not (Key.Escape or Key.Enter)) return;
        // Enter und Leertaste lösen die fokussierte Schaltfläche oder Auswahl aus, nicht das Kürzel des Bildschirms
        // (sonst lud Enter auf „Neues Spiel“ den ersten Spielstand).
        if (e.Key is Key.Enter or Key.Space && OwnsActivationKeys(FocusManager?.GetFocusedElement())) return;
        if (DataContext is MainWindowViewModel vm && vm.HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
    }

    /// <summary>Steuerelemente, die Enter/Leertaste selbst auswerten (Schaltflächen, Umschalter, Aufklapplisten, Menüs).</summary>
    public static bool OwnsActivationKeys(object? focused) =>
        focused is Button or ToggleButton or ComboBox or MenuItem or Slider;

    /// <summary>Dialog geöffnet: Fokus in den Dialog; geschlossen: zurück zum vorher fokussierten Element.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.Dialog) || sender is not MainWindowViewModel vm) return;
        if (vm.Dialog is not null)
        {
            _focusBeforeDialog ??= FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() =>
            {
                var first = DialogHost.GetVisualDescendants().OfType<InputElement>()
                    .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
                (first ?? DialogHost).Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Loaded);
        }
        else
        {
            var previous = _focusBeforeDialog;
            _focusBeforeDialog = null;
            if (previous is InputElement { IsEffectivelyVisible: true } p) Dispatcher.UIThread.Post(() => p.Focus(), DispatcherPriority.Loaded);
        }
    }

    /// <summary>Schließen mit ungespeichertem Fortschritt: erst nachfragen (speichern, verwerfen, weiterspielen).</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel || DataContext is not MainWindowViewModel vm) return;
        if (!AppStateHasUnsavedProgress()) return;
        e.Cancel = true;
        var t = AppState.Current.T;
        vm.ShowDialog(new ChoiceDialogViewModel(t["quit.title"], new[]
        {
            new ChoiceOption(t["quit.save"], t["quit.unsaved"]),
            new ChoiceOption(t["quit.discard"], ""),
            new ChoiceOption(t["quit.cancel"], ""),
        }, choice =>
        {
            if (choice == 2) return;
            if (choice == 0 && !TrySave(vm)) return;
            _closeConfirmed = true;
            Close();
        }));
    }

    private static bool AppStateHasUnsavedProgress()
    {
        try { return AppState.Current.HasUnsavedProgress; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Speichern vor dem Beenden; schlägt es fehl, bleibt das Fenster offen und zeigt den Fehler.</summary>
    private static bool TrySave(MainWindowViewModel vm)
    {
        try
        {
            AppState.Current.SaveCurrent();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            var t = AppState.Current.T;
            vm.ShowDialog(new MessageDialogViewModel(t["quit.title"], new[] { t.Format("ui.saveFailed", ex.Message) }));
            return false;
        }
    }
}
