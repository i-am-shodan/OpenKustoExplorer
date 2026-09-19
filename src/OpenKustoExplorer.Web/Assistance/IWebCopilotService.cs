using OpenKustoExplorer.Application.Assistance;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Web.Assistance;

/// <summary>
/// Executes stateless Copilot turns through the server-configured provider.
/// </summary>
internal interface IWebCopilotService
{
    /// <summary>Gets the configured model after validating provider settings.</summary>
    /// <returns>The configured model.</returns>
    public KustoCopilotModel GetModel();

    /// <summary>Executes one turn using bounded browser-local history.</summary>
    /// <param name="context">The validated current context.</param>
    /// <param name="request">The current natural-language request.</param>
    /// <param name="modelId">The requested configured model.</param>
    /// <param name="history">Prior browser-local turns.</param>
    /// <param name="cancellationToken">Cancels the completion.</param>
    /// <returns>The assistant reply.</returns>
    public Task<KustoCopilotReply> SendAsync(
        KustoCopilotContext context,
        string request,
        string modelId,
        IReadOnlyList<KustoGatewayCopilotTurn> history,
        CancellationToken cancellationToken = default);
}
