namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Contains the opaque accent and translucent highlight for one recorded value.
/// </summary>
/// <param name="AccentHex">The opaque RGB accent.</param>
/// <param name="HighlightHex">The translucent ARGB highlight.</param>
internal readonly record struct KustoRecordedValueColor(string AccentHex, string HighlightHex);
