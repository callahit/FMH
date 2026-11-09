using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Fmh.Editor.ViewModels;

namespace Fmh.Editor.Views;

public sealed partial class ClubsSectionView : UserControl
{
    public ClubsSectionView() => InitializeComponent();

    private ClubsSectionViewModel Vm => (ClubsSectionViewModel)DataContext!;

    private static readonly FilePickerFileType Csv = new("CSV") { Patterns = new[] { "*.csv" } };

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = new[] { Csv } });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        Vm.ImportCsv(await reader.ReadToEndAsync());
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions { DefaultExtension = "csv", FileTypeChoices = new[] { Csv }, SuggestedFileName = "vereine.csv" });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true));
        await writer.WriteAsync(Vm.ExportCsv());
    }
}
