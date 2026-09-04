using System.Text.Json;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;
using static OpenKustoExplorer.Infrastructure.Storage.JsonElementReader;

namespace OpenKustoExplorer.Infrastructure.Dashboards;

/// <summary>
/// Reads and writes dashboard catalogs with explicit Native-AOT-safe JSON handling.
/// </summary>
internal static class KustoDashboardCatalogJson
{
    private const int CurrentVersion = 1;

    /// <summary>
    /// Reads a dashboard catalog from UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The readable JSON stream.</param>
    /// <returns>The dashboard catalog.</returns>
    internal static KustoDashboardCatalog Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int version = root.GetProperty("version").GetInt32();

        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported dashboard catalog version {version}.");
        }

        List<KustoDashboard> dashboards = [];
        foreach (JsonElement dashboardElement in root.GetProperty("dashboards").EnumerateArray())
        {
            dashboards.Add(ReadDashboard(dashboardElement));
        }

        return new KustoDashboardCatalog(dashboards);
    }

    /// <summary>
    /// Writes a dashboard catalog as UTF-8 JSON.
    /// </summary>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="catalog">The dashboard catalog.</param>
    internal static void Write(Stream stream, KustoDashboardCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(catalog);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);
        writer.WritePropertyName("dashboards");
        writer.WriteStartArray();

        foreach (KustoDashboard dashboard in catalog.Dashboards)
        {
            WriteDashboard(writer, dashboard);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static KustoDashboard ReadDashboard(JsonElement element)
    {
        List<KustoDashboardWidget> widgets = [];
        foreach (JsonElement widgetElement in element.GetProperty("widgets").EnumerateArray())
        {
            widgets.Add(ReadWidget(widgetElement));
        }

        return new KustoDashboard(
            element.GetProperty("id").GetGuid(),
            GetRequiredString(element, "title"),
            GetRequiredString(element, "backgroundColor"),
            widgets);
    }

    private static KustoDashboardWidget ReadWidget(JsonElement element)
    {
        string displayModeText = GetRequiredString(element, "displayMode");
        string visualizationKindText = GetRequiredString(element, "visualizationKind");

        if (!Enum.TryParse(displayModeText, true, out KustoDashboardWidgetDisplayMode displayMode)
            || !Enum.IsDefined(displayMode))
        {
            throw new InvalidDataException($"Unsupported dashboard widget display mode {displayModeText}.");
        }

        if (!Enum.TryParse(visualizationKindText, true, out KustoVisualizationKind visualizationKind)
            || !Enum.IsDefined(visualizationKind))
        {
            throw new InvalidDataException($"Unsupported dashboard visualization {visualizationKindText}.");
        }

        JsonElement layoutElement = element.GetProperty("layout");
        return new KustoDashboardWidget(
            element.GetProperty("id").GetGuid(),
            GetRequiredString(element, "title"),
            new Uri(GetRequiredString(element, "clusterUri"), UriKind.Absolute),
            GetRequiredString(element, "databaseName"),
            GetRequiredString(element, "queryText"),
            TimeSpan.FromSeconds(element.GetProperty("refreshIntervalSeconds").GetDouble()),
            displayMode,
            visualizationKind,
            new KustoDashboardWidgetLayout(
                layoutElement.GetProperty("column").GetInt32(),
                layoutElement.GetProperty("row").GetInt32(),
                layoutElement.GetProperty("columnSpan").GetInt32(),
                layoutElement.GetProperty("rowSpan").GetInt32()),
            GetRequiredString(element, "backgroundColor"),
            GetRequiredString(element, "foregroundColor"),
            GetRequiredString(element, "accentColor"));
    }

    private static void WriteDashboard(Utf8JsonWriter writer, KustoDashboard dashboard)
    {
        writer.WriteStartObject();
        writer.WriteString("id", dashboard.Id);
        writer.WriteString("title", dashboard.Title);
        writer.WriteString("backgroundColor", dashboard.BackgroundColor);
        writer.WritePropertyName("widgets");
        writer.WriteStartArray();

        foreach (KustoDashboardWidget widget in dashboard.Widgets)
        {
            WriteWidget(writer, widget);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteWidget(Utf8JsonWriter writer, KustoDashboardWidget widget)
    {
        writer.WriteStartObject();
        writer.WriteString("id", widget.Id);
        writer.WriteString("title", widget.Title);
        writer.WriteString("clusterUri", widget.ClusterUri.AbsoluteUri);
        writer.WriteString("databaseName", widget.DatabaseName);
        writer.WriteString("queryText", widget.QueryText);
        writer.WriteNumber("refreshIntervalSeconds", widget.RefreshInterval.TotalSeconds);
        writer.WriteString("displayMode", widget.DisplayMode.ToString());
        writer.WriteString("visualizationKind", widget.VisualizationKind.ToString());
        writer.WritePropertyName("layout");
        writer.WriteStartObject();
        writer.WriteNumber("column", widget.Layout.Column);
        writer.WriteNumber("row", widget.Layout.Row);
        writer.WriteNumber("columnSpan", widget.Layout.ColumnSpan);
        writer.WriteNumber("rowSpan", widget.Layout.RowSpan);
        writer.WriteEndObject();
        writer.WriteString("backgroundColor", widget.BackgroundColor);
        writer.WriteString("foregroundColor", widget.ForegroundColor);
        writer.WriteString("accentColor", widget.AccentColor);
        writer.WriteEndObject();
    }
}
