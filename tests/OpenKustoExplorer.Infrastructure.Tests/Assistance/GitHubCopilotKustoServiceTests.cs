using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Infrastructure.Assistance;

namespace OpenKustoExplorer.Infrastructure.Tests.Assistance;

/// <summary>
/// Verifies the Copilot MCP permission boundary independently of a live runtime.
/// </summary>
#pragma warning disable GHCP001
public sealed class GitHubCopilotKustoServiceTests
{
    /// <summary>
    /// Verifies only enabled, allowlisted, read-only MCP tools receive one-call approval.
    /// </summary>
    [Fact]
    public void PermissionDecisionAllowsOnlyConfiguredReadOnlyMcpTools()
    {
        KustoCopilotOptions learnOptions = new("auto", true, false);
        KustoCopilotOptions azureOptions = new("auto", false, true);
        string approvedKind = PermissionDecision.ApproveOnce().Kind;
        string rejectedKind = PermissionDecision.Reject("test").Kind;
        PermissionDecision learnSearchDecision = GitHubCopilotKustoService.GetPermissionDecision(
            CreateMcpRequest("microsoft-learn", "microsoft_docs_search", readOnly: true),
            learnOptions);
        PermissionDecision azureQueryDecision = GitHubCopilotKustoService.GetPermissionDecision(
            CreateMcpRequest("azure-mcp", "kusto_query", readOnly: true),
            azureOptions);
        PermissionDecision unrelatedAzureDecision = GitHubCopilotKustoService.GetPermissionDecision(
            CreateMcpRequest("azure-mcp", "storage_account_list", readOnly: true),
            azureOptions);
        PermissionDecision nonReadOnlyDecision = GitHubCopilotKustoService.GetPermissionDecision(
            CreateMcpRequest("azure-mcp", "kusto_query", readOnly: false),
            azureOptions);
        PermissionRequestCustomTool customToolRequest = new()
        {
            Kind = "custom-tool",
            ToolCallId = "call-5",
            ToolDescription = "Untrusted custom tool",
            ToolName = "custom_tool",
        };
        PermissionDecision customToolDecision = GitHubCopilotKustoService.GetPermissionDecision(
            customToolRequest,
            azureOptions);
        PermissionRequestCustomTool graphToolRequest = new()
        {
            Kind = "custom-tool",
            ToolCallId = "call-graph",
            ToolDescription = "Read selected graph schema",
            ToolName = CopilotGraphTools.GetSchemaToolName,
        };
        KustoCopilotOptions graphOptions = new("auto", false, false, true);
        PermissionDecision graphToolDecision = GitHubCopilotKustoService.GetPermissionDecision(
            graphToolRequest,
            graphOptions,
            KustoCopilotScopeKind.Graph);
        PermissionRequestCustomTool routeToolRequest = new()
        {
            Kind = "custom-tool",
            ToolCallId = "call-routes",
            ToolDescription = "Find routes in the selected graph",
            ToolName = CopilotGraphTools.RouteToolName,
        };
        PermissionDecision routeToolDecision = GitHubCopilotKustoService.GetPermissionDecision(
            routeToolRequest,
            graphOptions,
            KustoCopilotScopeKind.Graph);
        PermissionDecision wrongScopeGraphToolDecision = GitHubCopilotKustoService.GetPermissionDecision(
            graphToolRequest,
            graphOptions,
            KustoCopilotScopeKind.Query);
        PermissionRequestCustomTool sessionToolRequest = new()
        {
            Kind = "custom-tool",
            ToolCallId = "call-recorded-search",
            ToolDescription = "Search the selected recorded session",
            ToolName = CopilotRecordedSessionTools.SearchResultsToolName,
        };
        KustoCopilotOptions sessionOptions = new("auto", false, false, false, true);
        PermissionDecision sessionToolDecision = GitHubCopilotKustoService.GetPermissionDecision(
            sessionToolRequest,
            sessionOptions,
            KustoCopilotScopeKind.RecordedSession);
        PermissionDecision sessionToolWithoutConsentDecision = GitHubCopilotKustoService.GetPermissionDecision(
            sessionToolRequest,
            new KustoCopilotOptions("auto", false, false),
            KustoCopilotScopeKind.RecordedSession);
        PermissionDecision wrongScopeSessionToolDecision = GitHubCopilotKustoService.GetPermissionDecision(
            sessionToolRequest,
            sessionOptions,
            KustoCopilotScopeKind.Query);

        Assert.Equal(approvedKind, learnSearchDecision.Kind);
        Assert.Equal(approvedKind, azureQueryDecision.Kind);
        Assert.Equal(rejectedKind, unrelatedAzureDecision.Kind);
        Assert.Equal(rejectedKind, nonReadOnlyDecision.Kind);
        Assert.Equal(rejectedKind, customToolDecision.Kind);
        Assert.Equal(approvedKind, graphToolDecision.Kind);
        Assert.Equal(approvedKind, routeToolDecision.Kind);
        Assert.Equal(rejectedKind, wrongScopeGraphToolDecision.Kind);
        Assert.Equal(approvedKind, sessionToolDecision.Kind);
        Assert.Equal(rejectedKind, sessionToolWithoutConsentDecision.Kind);
        Assert.Equal(rejectedKind, wrongScopeSessionToolDecision.Kind);
    }

    private static PermissionRequestMcp CreateMcpRequest(
        string serverName,
        string toolName,
        bool readOnly)
    {
        return new PermissionRequestMcp
        {
            Kind = "mcp",
            ReadOnly = readOnly,
            ServerName = serverName,
            ToolCallId = $"{serverName}-{toolName}",
            ToolName = toolName,
            ToolTitle = toolName,
        };
    }
}
#pragma warning restore GHCP001
