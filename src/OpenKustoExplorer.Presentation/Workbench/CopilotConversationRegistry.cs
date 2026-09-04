using System.ComponentModel;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Owns the lifetime of scoped GitHub Copilot conversations and disposes their SDK sessions when scopes close.
/// </summary>
internal sealed class CopilotConversationRegistry
{
    private readonly IKustoCopilotService service;
    private readonly Func<KustoCopilotViewModel> createQueryConversation;
    private readonly Func<KustoCopilotViewModel> createAutomationConversation;
    private readonly Func<KustoCopilotViewModel> createGraphConversation;
    private readonly Dictionary<Guid, KustoCopilotViewModel> documentConversations = [];
    private readonly Dictionary<Guid, KustoCopilotViewModel> automationConversations = [];
    private readonly Dictionary<GraphSnapshot, KustoCopilotViewModel> graphConversations = [];
    private readonly HashSet<KustoCopilotViewModel> trackedConversations = [];
    private KustoCopilotDefaults defaults = KustoCopilotDefaults.Standard;
    private KustoCopilotViewModel? standaloneQueryConversation;
    private KustoCopilotViewModel? unboundAutomationConversation;
    private KustoCopilotViewModel? unboundGraphConversation;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotConversationRegistry"/> class.
    /// </summary>
    /// <param name="service">The Copilot adapter whose per-scope sessions are released on removal.</param>
    /// <param name="createQueryConversation">Creates a query-scope conversation.</param>
    /// <param name="createAutomationConversation">Creates an automation-scope conversation.</param>
    /// <param name="createGraphConversation">Creates a graph-scope conversation.</param>
    public CopilotConversationRegistry(
        IKustoCopilotService service,
        Func<KustoCopilotViewModel> createQueryConversation,
        Func<KustoCopilotViewModel> createAutomationConversation,
        Func<KustoCopilotViewModel> createGraphConversation)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(createQueryConversation);
        ArgumentNullException.ThrowIfNull(createAutomationConversation);
        ArgumentNullException.ThrowIfNull(createGraphConversation);
        this.service = service;
        this.createQueryConversation = createQueryConversation;
        this.createAutomationConversation = createAutomationConversation;
        this.createGraphConversation = createGraphConversation;
    }

    /// <summary>
    /// Occurs when any tracked conversation starts or finishes a turn, or when the tracked set changes.
    /// </summary>
    public event EventHandler? WorkingChanged;

    /// <summary>
    /// Gets a value indicating whether any tracked conversation currently has an active turn.
    /// </summary>
    public bool IsWorking => trackedConversations.Any(conversation => conversation.IsBusy);

    /// <summary>
    /// Gets the shared query conversation used before a specific document tab is active.
    /// </summary>
    public KustoCopilotViewModel StandaloneQueryConversation =>
        standaloneQueryConversation ??= Track(createQueryConversation());

    /// <summary>
    /// Gets or creates the conversation bound to a specific query document tab.
    /// </summary>
    /// <param name="documentId">The stable document identifier.</param>
    /// <returns>The document's conversation.</returns>
    public KustoCopilotViewModel GetOrCreateDocumentConversation(Guid documentId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        return GetOrCreate(documentConversations, documentId, createQueryConversation);
    }

    /// <summary>
    /// Gets or creates the conversation for the selected automation, or the shared unbound conversation.
    /// </summary>
    /// <param name="automationId">The selected automation identifier, or <see langword="null"/>.</param>
    /// <returns>The automation conversation.</returns>
    public KustoCopilotViewModel GetOrCreateAutomationConversation(Guid? automationId)
    {
        return automationId is Guid identifier
            ? GetOrCreate(automationConversations, identifier, createAutomationConversation)
            : unboundAutomationConversation ??= Track(createAutomationConversation());
    }

    /// <summary>
    /// Gets or creates the conversation for a graph generation, or the shared unbound conversation.
    /// </summary>
    /// <param name="snapshot">The active graph generation, or <see langword="null"/>.</param>
    /// <returns>The graph conversation.</returns>
    public KustoCopilotViewModel GetOrCreateGraphConversation(GraphSnapshot? snapshot)
    {
        if (snapshot is not GraphSnapshot generation)
        {
            return unboundGraphConversation ??= Track(createGraphConversation());
        }

        PruneSupersededGraphConversations(generation);
        return GetOrCreate(graphConversations, generation, createGraphConversation);
    }

    /// <summary>
    /// Applies application defaults to current scopes and scopes created later.
    /// </summary>
    /// <param name="newDefaults">The current application Copilot defaults.</param>
    public void ApplyDefaults(KustoCopilotDefaults newDefaults)
    {
        ArgumentNullException.ThrowIfNull(newDefaults);
        defaults = newDefaults;

        foreach (KustoCopilotViewModel conversation in trackedConversations)
        {
            conversation.ApplyDefaults(defaults);
        }
    }

    /// <summary>
    /// Removes a document conversation and releases its SDK session.
    /// </summary>
    /// <param name="documentId">The closed document identifier.</param>
    public void RemoveDocumentConversation(Guid documentId)
    {
        RemoveAndReset(documentConversations, documentId, documentId);
    }

    /// <summary>
    /// Removes an automation conversation and releases its SDK session.
    /// </summary>
    /// <param name="automationId">The deleted automation identifier.</param>
    public void RemoveAutomationConversation(Guid automationId)
    {
        RemoveAndReset(automationConversations, automationId, automationId);
    }

    /// <summary>
    /// Stops observing every tracked conversation. Intended for owner disposal.
    /// </summary>
    public void DetachAll()
    {
        foreach (KustoCopilotViewModel conversation in trackedConversations)
        {
            conversation.PropertyChanged -= OnConversationPropertyChanged;
        }

        trackedConversations.Clear();
    }

    private void PruneSupersededGraphConversations(GraphSnapshot current)
    {
        List<GraphSnapshot> supersededGenerations = graphConversations.Keys
            .Where(key => key.GraphId == current.GraphId && key.GenerationId != current.GenerationId)
            .ToList();

        foreach (GraphSnapshot generation in supersededGenerations)
        {
            RemoveAndReset(graphConversations, generation, generation.GenerationId);
        }
    }

    private KustoCopilotViewModel GetOrCreate<TKey>(
        Dictionary<TKey, KustoCopilotViewModel> conversations,
        TKey key,
        Func<KustoCopilotViewModel> create)
        where TKey : notnull
    {
        if (!conversations.TryGetValue(key, out KustoCopilotViewModel? conversation))
        {
            conversation = Track(create());
            conversations.Add(key, conversation);
        }

        return conversation;
    }

    private void RemoveAndReset<TKey>(
        Dictionary<TKey, KustoCopilotViewModel> conversations,
        TKey key,
        Guid sessionId)
        where TKey : notnull
    {
        if (conversations.Remove(key, out KustoCopilotViewModel? conversation))
        {
            Untrack(conversation);
            _ = ResetSessionAsync(sessionId);
        }
    }

    private KustoCopilotViewModel Track(KustoCopilotViewModel conversation)
    {
        conversation.ApplyDefaults(defaults);
        conversation.PropertyChanged += OnConversationPropertyChanged;
        trackedConversations.Add(conversation);
        return conversation;
    }

    private void Untrack(KustoCopilotViewModel conversation)
    {
        conversation.PropertyChanged -= OnConversationPropertyChanged;
        trackedConversations.Remove(conversation);
        WorkingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnConversationPropertyChanged(object? sender, PropertyChangedEventArgs eventArguments)
    {
        _ = sender;

        if (eventArguments.PropertyName == nameof(KustoCopilotViewModel.IsBusy))
        {
            WorkingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ResetSessionAsync(Guid sessionId)
    {
        try
        {
            await service.ResetConversationAsync(sessionId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Releasing a closed scope's SDK session is best effort and must never crash the workbench.
        }
    }
}
