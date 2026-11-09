using Avalonia.Controls;
using Avalonia.Data;
using Fmh.Editor.ViewModels;

namespace Fmh.Editor.Views;

public sealed partial class LanguagesSectionView : UserControl
{
    public LanguagesSectionView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildColumns();
    }

    /// <summary>Je Sprache eine bearbeitbare Spalte (Anzahl der Sprachen steht erst zur Laufzeit fest).</summary>
    private void BuildColumns()
    {
        if (DataContext is not LanguagesSectionViewModel vm) return;
        Grid.Columns.Clear();
        Grid.Columns.Add(new DataGridTextColumn { Header = Services.EditorState.T["editor.languages.key"], Binding = new Binding(nameof(LanguageRow.Key)), IsReadOnly = true, Width = new DataGridLength(260) });
        foreach (var lang in vm.Languages)
            Grid.Columns.Add(new DataGridTextColumn { Header = lang, Binding = new Binding($"[{lang}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.Columns.Add(new DataGridTextColumn { Header = "", Binding = new Binding(nameof(LanguageRow.ProblemText)), IsReadOnly = true, Width = new DataGridLength(110) });
    }
}
