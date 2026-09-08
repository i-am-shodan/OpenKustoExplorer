using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Assistance;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Coordinates the collapsible current-tab AI conversation.
/// </summary>
public sealed class KustoCopilotViewModel : ObservableObject
{
    private const int MaximumQueryRepairAttempts = 2;
    private readonly Action<string>? appendDocumentAction;
    private readonly Func<KustoCopilotContext?> contextProvider;
    private readonly Action<string>? createAutomationAction;
    private readonly Action<string> createDocumentAction;
    private readonly Action<string>? loadCypherAction;
    private readonly Action<string> replaceDocumentAction;
    private readonly Func<string, CancellationToken, Task>? runCypherAction;
    private readonly IKustoCopilotService service;
    private readonly bool supportsApplyToCurrentTab;
    private readonly bool supportsResultDataSharing;
    private readonly Func<KustoCopilotContext, string, CancellationToken, Task<string?>>? validateQueryAsync;
    private KustoCopilotModel defaultModel;
    private bool enableAzureMcp;
    private bool enableMicrosoftLearnMcp;
    private bool isBusy;
    private bool isLoadingModels;
    private bool isModelOverridden;
    private bool isOpen;
    private bool isSignedIn;
    private bool modelsLoaded;
    private string prompt = string.Empty;
    private string proposedCypher = string.Empty;
    private string proposedQuery = string.Empty;
    private KustoCopilotModel selectedModel;
    private bool shareResultData;
    private bool shareGraphData;
    private bool shareSchema = true;
    private bool shareTabContent = true;
    private string statusText = "Ready";

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotViewModel"/> class.
    /// </summary>
    /// <param name="service">The application-owned AI provider adapter.</param>
    /// <param name="contextProvider">Captures the active tab for each turn.</param>
    /// <param name="replaceDocumentAction">Replaces the active tab after user confirmation.</param>
    /// <param name="createDocumentAction">Creates a new tab after user confirmation.</param>
    /// <param name="loadCypherAction">Loads a proposed openCypher query without running it.</param>
    /// <param name="runCypherAction">Loads and explicitly runs a proposed openCypher query.</param>
    /// <param name="createAutomationAction">Opens automation setup for a proposed KQL query.</param>
    /// <param name="validateQueryAsync">Validates proposed KQL locally without executing it.</param>
    /// <param name="supportsResultDataSharing">Whether this scope supports result sharing and Azure MCP.</param>
    /// <param name="appendDocumentAction">Adds proposed KQL to the end of the active query tab.</param>
    /// <param name="supportsApplyToCurrentTab">Whether proposals may replace the active scope.</param>
    public KustoCopilotViewModel(
        IKustoCopilotService service,
        Func<KustoCopilotContext?> contextProvider,
        Action<string> replaceDocumentAction,
        Action<string> createDocumentAction,
        Action<string>? loadCypherAction = null,
        Func<string, CancellationToken, Task>? runCypherAction = null,
        Action<string>? createAutomationAction = null,
        Func<KustoCopilotContext, string, CancellationToken, Task<string?>>? validateQueryAsync = null,
        bool supportsResultDataSharing = true,
        Action<string>? appendDocumentAction = null,
        bool supportsApplyToCurrentTab = true)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(contextProvider);
        ArgumentNullException.ThrowIfNull(replaceDocumentAction);
        ArgumentNullException.ThrowIfNull(createDocumentAction);

        this.service = service;
        this.contextProvider = contextProvider;
        this.replaceDocumentAction = replaceDocumentAction;
        this.createDocumentAction = createDocumentAction;
        this.loadCypherAction = loadCypherAction;
        this.runCypherAction = runCypherAction;
        this.createAutomationAction = createAutomationAction;
        this.validateQueryAsync = validateQueryAsync;
        this.supportsResultDataSharing = supportsResultDataSharing;
        this.appendDocumentAction = appendDocumentAction;
        this.supportsApplyToCurrentTab = supportsApplyToCurrentTab;
        Messages = new ObservableCollection<KustoCopilotMessageViewModel>();
        KustoCopilotModel automaticModel = new("auto", "Automatic");
        Models = new ObservableCollection<KustoCopilotModel> { automaticModel };
        selectedModel = Models[0];
        defaultModel = automaticModel;
        ToggleCommand = new RelayCommand(Toggle);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
        SignInCommand = new AsyncRelayCommand(SignInAsync, () => service.SupportsInteractiveSignIn && !IsBusy && !IsLoadingModels);
        RefreshModelsCommand = new AsyncRelayCommand(LoadModelsAsync, () => !IsBusy && !IsLoadingModels);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
        ApplyToCurrentTabCommand = new RelayCommand(ApplyToCurrentTab, () => HasApplyProposal);
        AppendToCurrentTabCommand = new RelayCommand(AppendToCurrentTab, () => HasAppendProposal);
        CreateNewTabCommand = new RelayCommand(CreateNewTab, () => HasProposal);
        CreateAutomationCommand = new RelayCommand(
            CreateAutomation,
            () => HasAutomationProposal);
        LoadCypherCommand = new RelayCommand(LoadCypher, () => HasCypherProposal && this.loadCypherAction is not null);
        RunCypherProposalCommand = new AsyncRelayCommand(
            RunCypherProposalAsync,
            () => HasCypherProposal && this.runCypherAction is not null && !IsBusy);
        ClearCommand = new AsyncRelayCommand(ClearAsync, () => Messages.Count > 0 || HasProposal);
    }

    /// <summary>
    /// Gets the conversation messages in display order.
    /// </summary>
    public ObservableCollection<KustoCopilotMessageViewModel> Messages { get; }

    /// <summary>
    /// Gets models available from the active AI provider.
    /// </summary>
    public ObservableCollection<KustoCopilotModel> Models { get; }

    /// <summary>Gets the active provider display name.</summary>
    public string ProviderDisplayName => service.ProviderDisplayName;

    /// <summary>Gets a value indicating whether the active provider supports MCP servers.</summary>
    public bool SupportsMcp => service.SupportsMcp;

    /// <summary>Gets a value indicating whether interactive provider sign-in should be offered.</summary>
    public bool CanSignIn => service.SupportsInteractiveSignIn
        && !IsSignedIn
        && !IsBusy
        && !IsLoadingModels;

    /// <summary>
    /// Gets the command that opens or closes the sidebar.
    /// </summary>
    public IRelayCommand ToggleCommand { get; }

    /// <summary>
    /// Gets the command that closes the sidebar.
    /// </summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>
    /// Gets the command that sends the active prompt.
    /// </summary>
    public IAsyncRelayCommand SendCommand { get; }

    /// <summary>
    /// Gets the command that opens the active provider's interactive sign-in flow.
    /// </summary>
    public IAsyncRelayCommand SignInCommand { get; }

    /// <summary>
    /// Gets the command that refreshes models available to the signed-in account.
    /// </summary>
    public IAsyncRelayCommand RefreshModelsCommand { get; }

    /// <summary>
    /// Gets the command that cancels the active turn.
    /// </summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// Gets the command that replaces the active tab with the proposed KQL.
    /// </summary>
    public IRelayCommand ApplyToCurrentTabCommand { get; }

    /// <summary>
    /// Gets the command that adds the proposed KQL to the end of the active query tab.
    /// </summary>
    public IRelayCommand AppendToCurrentTabCommand { get; }

    /// <summary>
    /// Gets the command that creates a new tab with the proposed KQL.
    /// </summary>
    public IRelayCommand CreateNewTabCommand { get; }

    /// <summary>
    /// Gets the command that opens automation setup with the proposed KQL.
    /// </summary>
    public IRelayCommand CreateAutomationCommand { get; }

    /// <summary>
    /// Gets the command that loads an openCypher proposal into the Graph editor.
    /// </summary>
    public IRelayCommand LoadCypherCommand { get; }

    /// <summary>
    /// Gets the command that explicitly loads and runs an openCypher proposal.
    /// </summary>
    public IAsyncRelayCommand RunCypherProposalCommand { get; }

    /// <summary>
    /// Gets the command that clears the visible conversation and proposal.
    /// </summary>
    public IAsyncRelayCommand ClearCommand { get; }

    /// <summary>
    /// Gets or sets the model used for subsequent turns in this tab.
    /// </summary>
    public KustoCopilotModel SelectedModel
    {
        get => selectedModel;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (SetProperty(ref selectedModel, value))
            {
                isModelOverridden = true;
                StatusText = "Model selected for the next turn";
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether bounded current result data may be shared.
    /// </summary>
    public bool ShareResultData
    {
        get => shareResultData;
        set
        {
            bool allowedValue = value && supportsResultDataSharing;

            if (SetProperty(ref shareResultData, allowedValue))
            {
                OnPropertyChanged(nameof(CanEnableAzureMcp));

                if (!allowedValue && EnableAzureMcp)
                {
                    EnableAzureMcp = false;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether session-only consent permits bounded read-only graph tools.
    /// </summary>
    public bool ShareGraphData
    {
        get => shareGraphData;
        set
        {
            if (SetProperty(ref shareGraphData, value))
            {
                StatusText = "Graph sharing applies to the next turn";
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the active scope's schema and cluster/database identity are shared.
    /// </summary>
    public bool ShareSchema
    {
        get => shareSchema;
        set
        {
            if (SetProperty(ref shareSchema, value))
            {
                StatusText = value
                    ? "Schema sharing applies to the next turn"
                    : "Schema and database are hidden from the next turn";
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the complete query tab content is shared.
    /// </summary>
    public bool ShareTabContent
    {
        get => shareTabContent;
        set
        {
            if (SetProperty(ref shareTabContent, value))
            {
                StatusText = value
                    ? "Tab content sharing applies to the next turn"
                    : "Tab content is hidden from the next turn";
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the public Microsoft Learn MCP server is enabled.
    /// </summary>
    public bool EnableMicrosoftLearnMcp
    {
        get => enableMicrosoftLearnMcp;
        set
        {
            if (SetProperty(ref enableMicrosoftLearnMcp, value))
            {
                StatusText = "MCP settings apply to the next turn";
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether read-only Azure MCP access is enabled.
    /// </summary>
    public bool EnableAzureMcp
    {
        get => enableAzureMcp;
        set
        {
            bool allowedValue = value && CanEnableAzureMcp;
            if (SetProperty(ref enableAzureMcp, allowedValue))
            {
                StatusText = "MCP settings apply to the next turn";
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether explicit data-sharing consent allows Azure MCP access.
    /// </summary>
    public bool CanEnableAzureMcp => supportsResultDataSharing && ShareResultData;

    /// <summary>
    /// Gets or sets the natural-language prompt.
    /// </summary>
    public string Prompt
    {
        get => prompt;
        set
        {
            if (SetProperty(ref prompt, value))
            {
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(CanSignIn));
                OnPropertyChanged(nameof(CanCancel));
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the sidebar is open.
    /// </summary>
    public bool IsOpen
    {
        get => isOpen;
        private set => SetProperty(ref isOpen, value);
    }

    /// <summary>
    /// Gets a value indicating whether a Copilot turn is active.
    /// </summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanSend));
                SendCommand.NotifyCanExecuteChanged();
                SignInCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                RunCypherProposalCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether model discovery is active.
    /// </summary>
    public bool IsLoadingModels
    {
        get => isLoadingModels;
        private set
        {
            if (SetProperty(ref isLoadingModels, value))
            {
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(CanSignIn));
                OnPropertyChanged(nameof(CanCancel));
                SendCommand.NotifyCanExecuteChanged();
                SignInCommand.NotifyCanExecuteChanged();
                RefreshModelsCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether GitHub Copilot authentication has been confirmed.
    /// </summary>
    public bool IsSignedIn
    {
        get => isSignedIn;
        private set
        {
            if (SetProperty(ref isSignedIn, value))
            {
                OnPropertyChanged(nameof(CanSignIn));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether any conversation messages are visible.
    /// </summary>
    public bool HasMessages => Messages.Count > 0;

    /// <summary>
    /// Gets the current complete KQL proposal.
    /// </summary>
    public string ProposedQuery
    {
        get => proposedQuery;
        private set
        {
            if (SetProperty(ref proposedQuery, value))
            {
                OnPropertyChanged(nameof(HasProposal));
                OnPropertyChanged(nameof(HasApplyProposal));
                OnPropertyChanged(nameof(HasAppendProposal));
                OnPropertyChanged(nameof(HasAutomationProposal));
                OnPropertyChanged(nameof(HasAnyProposal));
                OnPropertyChanged(nameof(ProposalTitle));
                OnPropertyChanged(nameof(ProposalText));
                ApplyToCurrentTabCommand.NotifyCanExecuteChanged();
                AppendToCurrentTabCommand.NotifyCanExecuteChanged();
                CreateNewTabCommand.NotifyCanExecuteChanged();
                CreateAutomationCommand.NotifyCanExecuteChanged();
                ClearCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a complete KQL proposal is available.
    /// </summary>
    public bool HasProposal => !string.IsNullOrWhiteSpace(ProposedQuery);

    /// <summary>
    /// Gets a value indicating whether a KQL proposal can replace the active scope.
    /// </summary>
    public bool HasApplyProposal => HasProposal && supportsApplyToCurrentTab;

    /// <summary>
    /// Gets a value indicating whether the KQL proposal can be added to the active query tab.
    /// </summary>
    public bool HasAppendProposal => HasProposal && appendDocumentAction is not null;

    /// <summary>
    /// Gets a value indicating whether the KQL proposal can open automation setup.
    /// </summary>
    public bool HasAutomationProposal => HasProposal && createAutomationAction is not null;

    /// <summary>
    /// Gets the current complete read-only openCypher proposal.
    /// </summary>
    public string ProposedCypher
    {
        get => proposedCypher;
        private set
        {
            if (SetProperty(ref proposedCypher, value))
            {
                OnPropertyChanged(nameof(HasCypherProposal));
                OnPropertyChanged(nameof(HasAnyProposal));
                OnPropertyChanged(nameof(ProposalTitle));
                OnPropertyChanged(nameof(ProposalText));
                LoadCypherCommand.NotifyCanExecuteChanged();
                RunCypherProposalCommand.NotifyCanExecuteChanged();
                ClearCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a complete openCypher proposal is available.
    /// </summary>
    public bool HasCypherProposal => !string.IsNullOrWhiteSpace(ProposedCypher);

    /// <summary>
    /// Gets a value indicating whether any query proposal is available.
    /// </summary>
    public bool HasAnyProposal => HasProposal || HasCypherProposal;

    /// <summary>
    /// Gets the current proposal heading.
    /// </summary>
    public string ProposalTitle => HasCypherProposal ? "OpenCypher proposal ready" : "KQL proposal ready";

    /// <summary>
    /// Gets the current complete proposal text.
    /// </summary>
    public string ProposalText => HasCypherProposal ? ProposedCypher : ProposedQuery;

    /// <summary>
    /// Gets the concise Copilot connection or turn status.
    /// </summary>
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>
    /// Gets a value indicating whether the current prompt can be sent.
    /// </summary>
    public bool CanSend => !IsBusy && !IsLoadingModels && !string.IsNullOrWhiteSpace(Prompt);

    /// <summary>
    /// Gets a value indicating whether an active assistant operation can be canceled.
    /// </summary>
    public bool CanCancel => IsBusy || IsLoadingModels;

    /// <summary>
    /// Updates sidebar visibility while preserving this tab's conversation.
    /// </summary>
    /// <param name="value">Whether the sidebar is open.</param>
    internal void SetOpen(bool value)
    {
        bool wasOpen = IsOpen;
        IsOpen = value;

        if (value && !wasOpen && !modelsLoaded && !IsLoadingModels)
        {
            _ = LoadModelsAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Applies application defaults to this scoped conversation.
    /// </summary>
    /// <param name="defaults">The current application Copilot defaults.</param>
    internal void ApplyDefaults(KustoCopilotDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        string previousStatus = StatusText;
        ShareTabContent = defaults.ShareTabContent;
        ShareSchema = defaults.ShareSchema;
        ShareResultData = defaults.ShareResultData;
        EnableMicrosoftLearnMcp = defaults.EnableMicrosoftLearnMcp;
        EnableAzureMcp = defaults.EnableAzureMcp;
        defaultModel = defaults.DefaultModel;

        if (!isModelOverridden)
        {
            KustoCopilotModel model = FindModel(defaultModel.Id) ?? AddModel(defaultModel);
            SetSelectedModelInternally(model);
        }

        StatusText = previousStatus;
    }

    /// <summary>
    /// Clears provider-specific state after application provider settings change.
    /// </summary>
    internal void RefreshProvider()
    {
        modelsLoaded = false;
        isModelOverridden = false;
        IsSignedIn = false;
        Models.Clear();
        KustoCopilotModel automaticModel = new("auto", "Automatic");
        Models.Add(automaticModel);
        SetSelectedModelInternally(automaticModel);
        Messages.Clear();
        ProposedQuery = string.Empty;
        ProposedCypher = string.Empty;
        StatusText = $"Using {service.ProviderDisplayName}";
        OnPropertyChanged(nameof(ProviderDisplayName));
        OnPropertyChanged(nameof(SupportsMcp));
        OnPropertyChanged(nameof(CanSignIn));
        OnPropertyChanged(nameof(HasMessages));
        SignInCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        _ = ResetForProviderChangeAsync();

        if (IsOpen && !IsLoadingModels)
        {
            _ = LoadModelsAsync(CancellationToken.None);
        }
    }

    private static string CreateAssistantMessage(KustoCopilotReply reply)
    {
        string message = reply.Message;

        if (reply.ProposedQuery is not null)
        {
            message = $"{message}\n\nProposed KQL\n{reply.ProposedQuery}";
        }
        else if (reply.ProposedCypher is not null)
        {
            message = $"{message}\n\nProposed openCypher\n{reply.ProposedCypher}";
        }

        return message;
    }

    private static string CreateRepairRequest(string validationErrors)
    {
        return $"""
            Your previous KQL proposal failed local syntax or semantic validation and was not shown to the user.
            Correct the KQL using the validation diagnostics below. Do not execute the query.
            Return the corrected complete KQL in the structured query field. Do not repeat the invalid draft.

            <local_kql_validation_errors>
            {validationErrors}
            </local_kql_validation_errors>
            """;
    }

    private static KustoCopilotReply CreateValidationFailureReply()
    {
        return new KustoCopilotReply(
            "Copilot could not produce valid KQL after local validation. Try refining the request.",
            null);
    }

    private void ApplyToCurrentTab()
    {
        if (HasProposal)
        {
            replaceDocumentAction(ProposedQuery);
            ProposedQuery = string.Empty;
            StatusText = "Applied to current tab";
        }
    }

    private void AppendToCurrentTab()
    {
        if (HasAppendProposal)
        {
            appendDocumentAction!(ProposedQuery);
            ProposedQuery = string.Empty;
            StatusText = "Added to end of current tab";
        }
    }

    private void Cancel()
    {
        SendCommand.Cancel();
        SignInCommand.Cancel();
        RefreshModelsCommand.Cancel();
    }

    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        KustoCopilotContext? context = contextProvider();
        Messages.Clear();
        ProposedQuery = string.Empty;
        ProposedCypher = string.Empty;
        StatusText = "Ready";
        OnPropertyChanged(nameof(HasMessages));
        ClearCommand.NotifyCanExecuteChanged();

        if (context is not null)
        {
            await service.ResetConversationAsync(context.DocumentId, cancellationToken);
        }
    }

    private void CreateNewTab()
    {
        if (HasProposal)
        {
            createDocumentAction(ProposedQuery);
            ProposedQuery = string.Empty;
            StatusText = "Created a new query tab";
        }
    }

    private void CreateAutomation()
    {
        if (HasAutomationProposal)
        {
            createAutomationAction!(ProposedQuery);
            ProposedQuery = string.Empty;
            StatusText = "Opened automation setup";
        }
    }

    private void LoadCypher()
    {
        if (HasCypherProposal && loadCypherAction is not null)
        {
            loadCypherAction(ProposedCypher);
            ProposedCypher = string.Empty;
            StatusText = "Loaded into the Graph editor";
        }
    }

    private void Toggle()
    {
        SetOpen(!IsOpen);
    }

    private async Task SendAsync(CancellationToken cancellationToken)
    {
        KustoCopilotContext context = contextProvider()
            ?? throw new InvalidOperationException("Select a query tab before using the AI assistant.");
        string request = Prompt.Trim();
        Messages.Add(new KustoCopilotMessageViewModel("You", request, isUser: true));
        Prompt = string.Empty;
        ProposedQuery = string.Empty;
        ProposedCypher = string.Empty;
        IsBusy = true;
        StatusText = "Thinking";
        OnPropertyChanged(nameof(HasMessages));
        ClearCommand.NotifyCanExecuteChanged();

        try
        {
            KustoCopilotOptions options = new(
                SelectedModel.Id,
                service.SupportsMcp && EnableMicrosoftLearnMcp,
                service.SupportsMcp && EnableAzureMcp,
                ShareGraphData,
                context.ScopeKind == KustoCopilotScopeKind.RecordedSession && ShareResultData);
            KustoCopilotReply reply = await SendValidatedAsync(
                context,
                request,
                options,
                cancellationToken);
            string assistantMessage = CreateAssistantMessage(reply);
            Messages.Add(new KustoCopilotMessageViewModel(
                service.ProviderDisplayName,
                assistantMessage,
                isUser: false));
            ProposedQuery = reply.ProposedQuery ?? string.Empty;
            ProposedCypher = reply.ProposedCypher ?? string.Empty;
            StatusText = HasAnyProposal ? "Query proposal ready" : "Ready";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            Messages.Add(new KustoCopilotMessageViewModel(
                service.ProviderDisplayName,
                $"Could not complete the request: {exception.Message}",
                isUser: false));
            StatusText = "Unavailable";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasMessages));
            ClearCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task<KustoCopilotReply> SendValidatedAsync(
        KustoCopilotContext context,
        string request,
        KustoCopilotOptions options,
        CancellationToken cancellationToken)
    {
        KustoCopilotReply reply = await service.SendAsync(
            context,
            request,
            options,
            cancellationToken);
        bool repairStarted = false;
        int repairAttempt = 0;

        while (reply.ProposedQuery is not null && validateQueryAsync is not null)
        {
            StatusText = repairStarted ? "Checking repaired KQL" : "Validating proposed KQL";
            string? validationErrors = await validateQueryAsync(
                context,
                reply.ProposedQuery,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(validationErrors))
            {
                return reply;
            }

            if (repairAttempt >= MaximumQueryRepairAttempts)
            {
                return CreateValidationFailureReply();
            }

            repairStarted = true;
            repairAttempt++;
            StatusText = $"Asking {service.ProviderDisplayName} to repair KQL";
            reply = await service.SendAsync(
                context,
                CreateRepairRequest(validationErrors),
                options,
                cancellationToken);
        }

        return repairStarted && reply.ProposedQuery is null
            ? CreateValidationFailureReply()
            : reply;
    }

    private async Task RunCypherProposalAsync(CancellationToken cancellationToken)
    {
        if (HasCypherProposal && runCypherAction is not null)
        {
            string query = ProposedCypher;
            await runCypherAction(query, cancellationToken);
            ProposedCypher = string.Empty;
            StatusText = "Ran in the Graph workspace";
        }
    }

    private async Task ResetForProviderChangeAsync()
    {
        try
        {
            KustoCopilotContext? context = contextProvider();
            if (context is not null)
            {
                await service.ResetConversationAsync(context.DocumentId, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Provider reconfiguration remains isolated from the rest of the application.
        }
    }

    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusText = $"Waiting for {service.ProviderDisplayName} sign in";

        try
        {
            await service.SignInAsync(cancellationToken);
            IsSignedIn = true;
            StatusText = "Signed in";
            await LoadModelsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            StatusText = $"Sign in failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadModelsAsync(CancellationToken cancellationToken)
    {
        IsLoadingModels = true;
        KustoCopilotModel previousSelection = SelectedModel;

        try
        {
            IReadOnlyList<KustoCopilotModel> availableModels = await service.GetModelsAsync(cancellationToken);
            Models.Clear();
            Models.Add(new KustoCopilotModel("auto", "Automatic"));

            foreach (KustoCopilotModel model in availableModels.Where(model => !string.Equals(
                model.Id,
                "auto",
                StringComparison.OrdinalIgnoreCase)))
            {
                Models.Add(model);
            }

            PreserveModel(previousSelection);
            if (service.ProviderKind == KustoAIProviderKind.GitHubCopilot)
            {
                PreserveModel(defaultModel);
            }

            KustoCopilotModel? configuredDefault = !isModelOverridden
                && service.ProviderKind == KustoAIProviderKind.GitHubCopilot
                    ? FindModel(defaultModel.Id)
                    : null;
            KustoCopilotModel selected = configuredDefault
                ?? FindModel(previousSelection.Id)
                ?? Models[0];
            SetSelectedModelInternally(selected);
            modelsLoaded = true;
            IsSignedIn = true;
            StatusText = "Models refreshed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Canceled";
        }
        catch (Exception exception)
        {
            StatusText = $"Models unavailable: {exception.Message}";
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    private KustoCopilotModel AddModel(KustoCopilotModel model)
    {
        Models.Add(model);
        return model;
    }

    private KustoCopilotModel? FindModel(string modelId)
    {
        return Models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal));
    }

    private void PreserveModel(KustoCopilotModel model)
    {
        if (FindModel(model.Id) is null)
        {
            Models.Add(model);
        }
    }

    private void SetSelectedModelInternally(KustoCopilotModel model)
    {
        SetProperty(ref selectedModel, model, nameof(SelectedModel));
    }
}
