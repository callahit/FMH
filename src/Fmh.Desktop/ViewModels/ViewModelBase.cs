using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Fmh.Core.Data;
using Fmh.Core.Game;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected static AppState State => AppState.Current;
    protected static Texts T => AppState.Current.T;
    protected static GameData Data => AppState.Current.Data;
    protected static GameSession Session => AppState.Current.Session ?? throw new InvalidOperationException("Kein Spiel geladen.");
    protected static Palette P => Palette.Current;

    /// <summary>Tastenkürzel. Gibt true zurück, wenn die Taste verbraucht wurde.</summary>
    public virtual bool HandleKey(Key key, KeyModifiers modifiers) => false;
}
