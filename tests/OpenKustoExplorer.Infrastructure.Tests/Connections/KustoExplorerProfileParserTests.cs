using System.Text.Json;
using System.Xml.Linq;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Portable.Connections;

namespace OpenKustoExplorer.Infrastructure.Tests.Connections;

/// <summary>
/// Verifies platform-neutral parsing of explicitly selected Kusto Explorer profile files.
/// </summary>
public sealed class KustoExplorerProfileParserTests
{
    /// <summary>
    /// Verifies group paths, connection validation, deduplication, and recovery tabs match desktop import behavior.
    /// </summary>
    [Fact]
    public void ParseImportsSelectedProfileFolder()
    {
        Guid tabId = Guid.NewGuid();
        KustoExplorerProfileFile[] files =
        [
            new(
                "Kusto.Explorer/UserConnectionGroups.xml",
                CreateGroups((
                    "Shared",
                    @"C:\Users\Ada\AppData\Local\Kusto.Explorer\Connections\UserConnections.xml"))),
            new(
                "Kusto.Explorer/Connections/UserConnections.xml",
                CreateConnections(("Fabrikam", "https://fabrikam.kusto.windows.net"))),
            new(
                "Kusto.Explorer/UserConnections.xml",
                CreateConnections(
                    ("Duplicate", "https://FABRIKAM.kusto.windows.net/path"),
                    ("Unsafe", "http://unsafe.example.com"),
                    ("Contoso", "https://contoso.kusto.windows.net"))),
            new(
                "Kusto.Explorer/Recovery/active.kebak",
                CreateRecoveryTab(tabId)),
            new("Kusto.Explorer/Recovery/malformed.kebak", "not json"),
        ];

        KustoExplorerImportResult result = KustoExplorerProfileParser.Parse(files);

        Assert.True(result.SourceFound);
        Assert.Equal(2, result.Connections.Count);
        Assert.Equal(2, result.SkippedConnectionCount);
        KustoClusterConnection fabrikam = Assert.Single(
            result.Connections,
            connection => connection.DisplayName == "Fabrikam");
        Assert.Equal("Shared", fabrikam.FolderName);
        Assert.Contains(result.Connections, connection => connection.DisplayName == "Contoso");
        KustoExplorerImportedTab tab = Assert.Single(result.Tabs);
        Assert.Equal(tabId, tab.Id);
        Assert.Equal("Selected investigation", tab.Title);
        Assert.Equal("Events | take 10", tab.Text);
        Assert.Equal(5, tab.CaretPosition);
        Assert.Equal(KustoDocumentTabColor.Blue, tab.TabColor);
        Assert.Equal(new Uri("https://fabrikam.kusto.windows.net"), tab.ClusterUri);
        Assert.Equal("Telemetry", tab.DatabaseName);
        Assert.Equal(1, result.SkippedTabCount);
    }

    private static string CreateConnections(params (string Name, string ClusterAddress)[] connections)
    {
        XElement[] entries = connections.Select(connection => new XElement(
            "ServerDescriptionBase",
            new XElement("Name", connection.Name),
            new XElement(
                "ConnectionString",
                $"Data Source={connection.ClusterAddress};AAD Federated Security=True")))
            .ToArray();
        return new XDocument(new XElement("ArrayOfServerDescriptionBase", entries))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static string CreateGroups(params (string Name, string FilePath)[] groups)
    {
        XElement[] entries = groups.Select(group => new XElement(
            "ServerGroupDescription",
            new XElement("Name", group.Name),
            new XElement("Details", group.FilePath)))
            .ToArray();
        return new XDocument(new XElement("ArrayOfServerGroupDescription", entries))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static string CreateRecoveryTab(Guid tabId)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Id"] = tabId,
            ["CustomTitle"] = "Selected investigation",
            ["FullPath"] = string.Empty,
            ["QueryText"] = "Events | take 10",
            ["Position"] = 5,
            ["TabOrdinal"] = 0,
            ["ColorTag"] = "#FF9ACCFF",
            ["ConnectionString"] = "Data Source=https://fabrikam.kusto.windows.net;Initial Catalog=Telemetry",
            ["IsOpen"] = true,
        });
    }
}
