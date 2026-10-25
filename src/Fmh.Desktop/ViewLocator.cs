using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Fmh.Desktop.ViewModels;

namespace Fmh.Desktop;

/// <summary>Ordnet jedem ViewModel die gleichnamige View zu (FooViewModel → FooView).</summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Type?> Cache = new();

    public Control? Build(object? data)
    {
        if (data is null) return null;
        var vmType = data.GetType();
        if (!Cache.TryGetValue(vmType, out var viewType))
        {
            var name = vmType.FullName!.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
            viewType = vmType.Assembly.GetType(name);
            Cache[vmType] = viewType;
        }
        return viewType is null
            ? new TextBlock { Text = "View fehlt: " + vmType.Name }
            : (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
