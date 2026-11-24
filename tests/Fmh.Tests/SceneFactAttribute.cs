using Fmh.SceneTree;

namespace Fmh.Tests;

/// <summary>
/// Test, der den Szenenbaum des Textmodus braucht. Die Dateien gehören nicht zum Repository; ohne bereitgestellten
/// Ordner (siehe <see cref="SceneTreeAsset.DirectoryVariable"/> und docs/textmodus.md) wird der Test übersprungen.
/// </summary>
public sealed class SceneFactAttribute : FactAttribute
{
    public SceneFactAttribute()
    {
        if (SceneTreeAsset.Locate() is null) Skip = "Textmodus-Dateien nicht bereitgestellt (FMH_TEXTMODE_DIR).";
    }
}
