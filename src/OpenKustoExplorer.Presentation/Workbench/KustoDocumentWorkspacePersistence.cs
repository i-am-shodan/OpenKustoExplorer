using OpenKustoExplorer.Application.Documents;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Serializes immediate and debounced document workspace persistence.
/// </summary>
internal sealed class KustoDocumentWorkspacePersistence : IDisposable
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(400);
    private readonly object autosaveLock = new();
    private readonly object storeLock = new();
    private readonly IKustoDocumentStore store;
    private CancellationTokenSource? autosaveCancellationSource;
    private KustoDocumentWorkspace? latestWorkspace;
    private string? saveError;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDocumentWorkspacePersistence"/> class.
    /// </summary>
    /// <param name="store">The durable workspace store.</param>
    internal KustoDocumentWorkspacePersistence(IKustoDocumentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>
    /// Occurs when the latest save failure changes or a retry succeeds.
    /// </summary>
    internal event Action<string?>? SaveErrorChanged;

    /// <inheritdoc />
    public void Dispose()
    {
        lock (autosaveLock)
        {
            if (!isDisposed)
            {
                isDisposed = true;
                autosaveCancellationSource?.Cancel();
                autosaveCancellationSource = null;
            }
        }
    }

    /// <summary>
    /// Loads the initial durable workspace.
    /// </summary>
    /// <returns>The restored workspace.</returns>
    internal KustoDocumentWorkspace Load() => store.Load();

    /// <summary>
    /// Replaces any pending autosave with the latest immutable workspace snapshot.
    /// </summary>
    /// <param name="workspace">The latest workspace snapshot.</param>
    internal void Schedule(KustoDocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        CancellationTokenSource cancellationSource = new();

        lock (autosaveLock)
        {
            if (isDisposed)
            {
                cancellationSource.Dispose();
                return;
            }

            latestWorkspace = workspace;
            autosaveCancellationSource?.Cancel();
            autosaveCancellationSource = cancellationSource;
        }

        _ = SaveAfterDelayAsync(workspace, cancellationSource);
    }

    /// <summary>
    /// Cancels pending debounce work and immediately persists the supplied snapshot.
    /// </summary>
    /// <param name="workspace">The final workspace snapshot.</param>
    internal void Flush(KustoDocumentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        lock (autosaveLock)
        {
            latestWorkspace = workspace;
            autosaveCancellationSource?.Cancel();
            autosaveCancellationSource = null;
        }

        Save(workspace);
    }

    /// <summary>
    /// Immediately retries the latest failed workspace snapshot.
    /// </summary>
    internal void Retry()
    {
        KustoDocumentWorkspace? workspace;
        lock (autosaveLock)
        {
            workspace = latestWorkspace;
        }

        if (workspace is not null)
        {
            Save(workspace);
        }
    }

    private async Task SaveAfterDelayAsync(
        KustoDocumentWorkspace workspace,
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await Task.Delay(AutosaveDelay, cancellationSource.Token);
            Save(workspace);
        }
        catch (OperationCanceledException)
        {
            // A newer workspace snapshot superseded this autosave.
        }
        finally
        {
            lock (autosaveLock)
            {
                if (ReferenceEquals(autosaveCancellationSource, cancellationSource))
                {
                    autosaveCancellationSource = null;
                }
            }

            cancellationSource.Dispose();
        }
    }

    private void Save(KustoDocumentWorkspace workspace)
    {
        try
        {
            lock (storeLock)
            {
                store.Save(workspace);
            }

            SetSaveError(null);
        }
        catch (Exception exception)
        {
            SetSaveError($"Queries are not saved. {exception.Message}");
        }
    }

    private void SetSaveError(string? value)
    {
        if (!string.Equals(saveError, value, StringComparison.Ordinal))
        {
            saveError = value;
            SaveErrorChanged?.Invoke(value);
        }
    }
}
