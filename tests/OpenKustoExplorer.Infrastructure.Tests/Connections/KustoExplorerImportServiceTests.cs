using System.Text.Json;
using System.Xml.Linq;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Infrastructure.Connections;

namespace OpenKustoExplorer.Infrastructure.Tests.Connections;

/// <summary>
/// Verifies safe import of Microsoft Kusto Explorer connection metadata.
/// </summary>
public sealed class KustoExplorerImportServiceTests
{
    /// <summary>
    /// Verifies nested legacy connection strings are validated, grouped, and deduplicated by authority.
    /// </summary>
    /// <returns>A task that completes after local XML parsing.</returns>
    [Fact]
    public async Task ImportConnectionsAsyncImportsSafeGroupedConnections()
    {
        string directoryPath = CreateTemporaryDirectory();
        string secondaryDirectoryPath = Path.Combine(directoryPath, "Connections");
        string nestedDirectoryPath = Path.Combine(directoryPath, "Profiles", "Team");
        Directory.CreateDirectory(secondaryDirectoryPath);
        Directory.CreateDirectory(nestedDirectoryPath);
        string primaryFilePath = Path.Combine(directoryPath, "UserConnections.xml");
        string secondaryFilePath = Path.Combine(secondaryDirectoryPath, "UserConnections.xml");
        string nestedFilePath = Path.Combine(nestedDirectoryPath, "UserConnections.xml");

        try
        {
            WriteConnections(
                primaryFilePath,
                ("Contoso", "https://adx.contoso.com"),
                ("Unsafe", "http://unsafe.contoso.com"));
            WriteConnections(
                secondaryFilePath,
                ("Duplicate", "https://ADX.CONTOSO.COM/path"),
                ("Fabrikam", "https://fabrikam.kusto.windows.net"));
            WriteDirectConnections(
                nestedFilePath,
                ("Northwind", "https://northwind.kusto.windows.net"));
            WriteGroups(
                directoryPath,
                ("Production", primaryFilePath),
                ("Shared", secondaryFilePath));
            KustoExplorerImportService service = new(directoryPath);

            KustoExplorerImportResult result = await service.ImportConnectionsAsync();

            Assert.True(result.SourceFound);
            Assert.Equal(3, result.Connections.Count);
            Assert.Equal(2, result.SkippedConnectionCount);
            KustoClusterConnection contoso = Assert.Single(
                result.Connections,
                connection => connection.DisplayName == "Contoso");
            Assert.Equal(new Uri("https://adx.contoso.com"), contoso.ClusterUri);
            Assert.Equal("Production", contoso.FolderName);
            Assert.Empty(contoso.Databases);
            KustoClusterConnection fabrikam = Assert.Single(
                result.Connections,
                connection => connection.DisplayName == "Fabrikam");
            Assert.Equal("Shared", fabrikam.FolderName);
            Assert.Contains(
                result.Connections,
                connection => connection.DisplayName == "Northwind");
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies a missing legacy profile is reported without throwing.
    /// </summary>
    /// <returns>A task that completes after checking the missing directory.</returns>
    [Fact]
    public async Task ImportConnectionsAsyncReportsMissingProfile()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"Missing-KustoExplorer-{Guid.NewGuid():N}");
        KustoExplorerImportService service = new(directoryPath);

        KustoExplorerImportResult result = await service.ImportConnectionsAsync();

        Assert.False(result.SourceFound);
        Assert.Empty(result.Connections);
        Assert.Equal(0, result.SkippedConnectionCount);
    }

    /// <summary>
    /// Verifies every open recovery tab is imported in source order with exact contents and metadata.
    /// </summary>
    /// <returns>A task that completes after recovery JSON parsing.</returns>
    [Fact]
    public async Task ImportConnectionsAsyncImportsAllOpenRecoveryTabs()
    {
        string directoryPath = CreateTemporaryDirectory();
        string recoveryDirectoryPath = Path.Combine(directoryPath, "Recovery");
        Directory.CreateDirectory(recoveryDirectoryPath);

        try
        {
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            const string ExactText = "Events\r\n| take 10\r\n";
            WriteRecoveryTab(
                Path.Combine(recoveryDirectoryPath, "later-name.kebak"),
                firstId,
                string.Empty,
                ExactText,
                99,
                0,
                "#FFFF0000");
            WriteRecoveryTab(
                Path.Combine(recoveryDirectoryPath, "earlier-name.kebak"),
                secondId,
                "Investigate events",
                "Events | count",
                4,
                1,
                "#FF8064A2");
            await File.WriteAllTextAsync(
                Path.Combine(recoveryDirectoryPath, "malformed.kebak"),
                "not json");
            KustoExplorerImportService service = new(directoryPath);

            KustoExplorerImportResult result = await service.ImportConnectionsAsync();

            Assert.Equal(2, result.Tabs.Count);
            Assert.Equal(1, result.SkippedTabCount);
            KustoExplorerImportedTab first = result.Tabs[0];
            Assert.Equal(firstId, first.Id);
            Assert.Equal("Imported tab 1", first.Title);
            Assert.Equal(ExactText, first.Text);
            Assert.Equal(ExactText.Length, first.CaretPosition);
            Assert.Equal(KustoDocumentTabColor.Red, first.TabColor);
            Assert.Equal(new Uri("https://adx.contoso.com"), first.ClusterUri);
            Assert.Equal("Telemetry", first.DatabaseName);
            Assert.Equal(secondId, result.Tabs[1].Id);
            Assert.Equal("Investigate events", result.Tabs[1].Title);
            Assert.Equal(KustoDocumentTabColor.Purple, result.Tabs[1].TabColor);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"KustoExplorerImport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void WriteConnections(
        string filePath,
        params (string Name, string ClusterAddress)[] connections)
    {
        XElement[] entries = connections.Select(connection =>
        {
            XElement serializedConnection = new(
                "ConnectionString",
                $"Data Source={connection.ClusterAddress};AAD Federated Security=True");
            return new XElement(
                "ServerDescriptionBase",
                new XElement("Name", connection.Name),
                new XElement("Details", connection.Name),
                new XElement(
                    "ConnectionString",
                    serializedConnection.ToString(SaveOptions.DisableFormatting)));
        }).ToArray();
        XDocument document = new(new XElement("ArrayOfServerDescriptionBase", entries));
        document.Save(filePath);
    }

    private static void WriteDirectConnections(
        string filePath,
        params (string Name, string ClusterAddress)[] connections)
    {
        XElement[] entries = connections.Select(connection => new XElement(
            "ServerDescriptionBase",
            new XElement("Name", connection.Name),
            new XElement("Details", connection.Name),
            new XElement(
                "ConnectionString",
                $"Data Source={connection.ClusterAddress};AAD Federated Security=True")))
            .ToArray();
        XDocument document = new(new XElement("ArrayOfServerDescriptionBase", entries));
        document.Save(filePath);
    }

    private static void WriteGroups(
        string directoryPath,
        params (string Name, string FilePath)[] groups)
    {
        XElement[] entries = groups.Select(group => new XElement(
            "ServerGroupDescription",
            new XElement("Name", group.Name),
            new XElement("Details", group.FilePath))).ToArray();
        XDocument document = new(new XElement("ArrayOfServerGroupDescription", entries));
        document.Save(Path.Combine(directoryPath, "UserConnectionGroups.xml"));
    }

    private static void WriteRecoveryTab(
        string filePath,
        Guid id,
        string title,
        string text,
        int caretPosition,
        int order,
        string colorTag)
    {
        using FileStream stream = File.Create(filePath);
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();
        writer.WriteString("Id", id);
        writer.WriteString("CustomTitle", title);
        writer.WriteString("FullPath", string.Empty);
        writer.WriteString("QueryText", text);
        writer.WriteNumber("Position", caretPosition);
        writer.WriteNumber("TabOrdinal", order);
        writer.WriteString("ColorTag", colorTag);
        writer.WriteString(
            "ConnectionString",
            "Data Source=https://adx.contoso.com;Initial Catalog=Telemetry");
        writer.WriteBoolean("IsOpen", true);
        writer.WriteEndObject();
    }
}
