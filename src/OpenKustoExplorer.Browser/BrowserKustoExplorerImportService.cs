using System.Text.Json;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Portable.Connections;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Imports explicitly selected Microsoft Kusto Explorer profile files.
/// </summary>
internal sealed class BrowserKustoExplorerImportService : IKustoExplorerImportService
{
    private const int MaximumFileCount = 2000;
    private const int MaximumTotalCharacterCount = 64 * 1024 * 1024;

    /// <inheritdoc />
    public async Task<KustoExplorerImportResult> ImportConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string selectionJson = await BrowserInterop.SelectKustoExplorerProfileAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(selectionJson))
        {
            return new KustoExplorerImportResult(false, [], 0);
        }

        using JsonDocument document = JsonDocument.Parse(selectionJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() > MaximumFileCount)
        {
            throw new InvalidDataException("The selected Kusto Explorer profile file list is invalid.");
        }

        List<KustoExplorerProfileFile> files = [];
        int totalCharacterCount = 0;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = GetRequiredString(element, "path");
            string content = GetRequiredString(element, "content");
            totalCharacterCount = checked(totalCharacterCount + content.Length);
            if (totalCharacterCount > MaximumTotalCharacterCount)
            {
                throw new InvalidDataException("The selected Kusto Explorer profile exceeds the import size limit.");
            }

            files.Add(new KustoExplorerProfileFile(path, content));
        }

        return KustoExplorerProfileParser.Parse(files);
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"The selected Kusto Explorer profile has no valid {propertyName} value.");
        }

        return value.GetString() ?? string.Empty;
    }
}
