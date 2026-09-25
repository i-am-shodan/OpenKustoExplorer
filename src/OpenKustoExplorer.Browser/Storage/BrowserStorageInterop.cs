using System.Runtime.InteropServices.JavaScript;

namespace OpenKustoExplorer.Browser.Storage;

/// <summary>
/// Exposes the minimal IndexedDB operations required by browser-local application stores.
/// </summary>
internal static partial class BrowserStorageInterop
{
    /// <summary>
    /// Opens and migrates browser-local storage.
    /// </summary>
    /// <returns>A task that completes when storage is ready.</returns>
    [JSImport("globalThis.openKustoExplorerStorageOpen")]
    internal static partial Task OpenAsync();

    /// <summary>
    /// Atomically archives an invalid record and removes it from the active catalog.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    /// <param name="name">The active catalog name.</param>
    /// <param name="json">The invalid JSON payload.</param>
    /// <returns>A task that completes when the recovery archive is durable.</returns>
    [JSImport("globalThis.openKustoExplorerStoragePreserveInvalid")]
    internal static partial Task PreserveInvalidAsync(string partition, string name, string json);

    /// <summary>
    /// Queues archival of invalid synchronous settings and removes them from active local storage.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    /// <param name="settingsJson">The invalid settings JSON.</param>
    [JSImport("globalThis.openKustoExplorerStoragePreserveInvalidSetting")]
    internal static partial void PreserveInvalidSetting(string partition, string settingsJson);

    /// <summary>
    /// Writes one catalog snapshot and waits until it is durable.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    /// <param name="name">The catalog name.</param>
    /// <param name="json">The versioned catalog JSON.</param>
    /// <returns>A task that completes when the write transaction commits.</returns>
    [JSImport("globalThis.openKustoExplorerStorageWrite")]
    internal static partial Task WriteAsync(string partition, string name, string json);

    /// <summary>
    /// Reads one catalog snapshot, or an empty string when none exists.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    /// <param name="name">The catalog name.</param>
    /// <returns>The stored JSON or an empty string.</returns>
    [JSImport("globalThis.openKustoExplorerStorageRead")]
    internal static partial Task<string> ReadAsync(string partition, string name);

    /// <summary>
    /// Reads partitioned appearance settings from local storage.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    /// <returns>The settings JSON or an empty string.</returns>
    [JSImport("globalThis.openKustoExplorerStorageReadSetting")]
    internal static partial string ReadSetting(string partition);

    /// <summary>
    /// Writes partitioned appearance settings to local storage.
    /// </summary>
    /// <param name="partition">The authenticated browser-storage partition.</param>
    /// <param name="settingsJson">The versioned settings JSON.</param>
    [JSImport("globalThis.openKustoExplorerStorageWriteSetting")]
    internal static partial void WriteSetting(string partition, string settingsJson);
}
