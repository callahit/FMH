using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Fmh.Editor.ViewModels;

namespace Fmh.Editor;

/// <summary>Findet zu jedem ViewModel die View gleichen Namens (…ViewModel → …View).</summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Type?> Cache = new();

    public Control? Build(object? data)
    {
        if (data is null) return null;
        var vmType = data.GetType();
        if (!Cache.TryGetValue(vmType, out var viewType))
        {
            viewType = vmType.Assembly.GetType(vmType.FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View"));
            Cache[vmType] = viewType;
        }
        return viewType is null ? new TextBlock { Text = "View fehlt: " + vmType.Name } : (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
