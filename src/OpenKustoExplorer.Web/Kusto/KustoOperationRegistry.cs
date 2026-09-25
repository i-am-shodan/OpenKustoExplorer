using System.Collections.Concurrent;

namespace OpenKustoExplorer.Web.Kusto;

/// <summary>
/// Tracks cancellable Kusto operations for authenticated Web users.
/// </summary>
internal sealed class KustoOperationRegistry
{
    private readonly ConcurrentDictionary<Guid, RegisteredOperation> operations = new();

    /// <summary>
    /// Begins tracking one operation.
    /// </summary>
    /// <param name="operationId">The client-generated operation identifier.</param>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="requestAborted">The transport cancellation token.</param>
    /// <returns>A lease that completes and removes the operation.</returns>
    public OperationLease Begin(
        Guid operationId,
        string ownerId,
        CancellationToken requestAborted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        RegisteredOperation operation = new(ownerId, cancellation);
        if (!operations.TryAdd(operationId, operation))
        {
            cancellation.Dispose();
            throw new ArgumentException("The gateway operation identifier is already active.", nameof(operationId));
        }

        return new OperationLease(this, operationId, operation);
    }

    /// <summary>
    /// Cancels an operation only when it belongs to the requesting user.
    /// </summary>
    /// <param name="operationId">The operation identifier.</param>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <returns><see langword="true"/> when a matching operation was found.</returns>
    public bool TryCancel(Guid operationId, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (!operations.TryGetValue(operationId, out RegisteredOperation? operation)
            || !string.Equals(operation.OwnerId, ownerId, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            operation.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    private void Complete(Guid operationId, RegisteredOperation operation)
    {
        if (operations.TryRemove(operationId, out RegisteredOperation? removedOperation))
        {
            removedOperation.Cancellation.Dispose();
        }
        else
        {
            operation.Cancellation.Dispose();
        }
    }

    /// <summary>
    /// Associates an operation owner with its cancellation source.
    /// </summary>
    /// <param name="OwnerId">The authenticated owner identifier.</param>
    /// <param name="Cancellation">The operation cancellation source.</param>
    internal sealed record RegisteredOperation(
        string OwnerId,
        CancellationTokenSource Cancellation);

    /// <summary>
    /// Owns one active operation registration.
    /// </summary>
    internal sealed class OperationLease : IDisposable
    {
        private readonly KustoOperationRegistry registry;
        private readonly RegisteredOperation operation;
        private readonly Guid operationId;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="OperationLease"/> class.
        /// </summary>
        /// <param name="registry">The owning registry.</param>
        /// <param name="operationId">The operation identifier.</param>
        /// <param name="operation">The registered operation.</param>
        public OperationLease(
            KustoOperationRegistry registry,
            Guid operationId,
            RegisteredOperation operation)
        {
            this.registry = registry;
            this.operationId = operationId;
            this.operation = operation;
        }

        /// <summary>
        /// Gets the combined transport and explicit cancellation token.
        /// </summary>
        public CancellationToken CancellationToken => operation.Cancellation.Token;

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            registry.Complete(operationId, operation);
            disposed = true;
        }
    }
}
