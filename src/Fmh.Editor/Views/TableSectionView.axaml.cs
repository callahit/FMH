using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Fmh.Editor.ViewModels;

namespace Fmh.Editor.Views;

/// <summary>Tabelle mit Spalten aus der gewählten Art (Spieler, Personal …); Spaltenkopf zeigt die Schemabeschreibung.</summary>
public sealed partial class TableSectionView : UserControl
{
    private TableSectionViewModel? _vm;

    public TableSectionView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TableSectionViewModel);
    }

    private void Attach(TableSectionViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (vm is null) return;
        vm.PropertyChanged += OnVmChanged;
        BuildColumns();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableSectionViewModel.Columns)) BuildColumns();
    }

    private void BuildColumns()
    {
        var grid = this.FindControl<DataGrid>("Grid")!;
        grid.Columns.Clear();
        if (_vm is null) return;
        foreach (var col in _vm.Columns)
        {
            var header = new TextBlock { Text = col };
            if (_vm.Describe(col) is { } text) ToolTip.SetTip(header, text);
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{col}]") { Mode = BindingMode.TwoWay },
                Width = new DataGridLength(col is "key" or "firstName" or "lastName" or "name" or "club" ? 150 : 100),
            });
        }
    }

    private static readonly FilePickerFileType Csv = new("CSV") { Patterns = new[] { "*.csv" } };

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = new[] { Csv } });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        _vm.ImportCsv(await reader.ReadToEndAsync());
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "csv", FileTypeChoices = new[] { Csv }, SuggestedFileName = _vm.Kind.Folder + ".csv",
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true));
        await writer.WriteAsync(_vm.ExportCsv());
    }
}
