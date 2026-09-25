using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenKustoExplorer.Kusto.Gateway.V1;

/// <summary>
/// Provides trimming-safe JSON metadata for the version-one gateway protocol.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayCopilotModelsResponse))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayCopilotRequest))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayCopilotResponse))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayDatabasesRequest))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayDatabasesResponse))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayErrorResponse))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayGraphFrame))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayQueryRequest))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewayQueryResponse))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewaySchemaRequest))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewaySchemaResponse))]
[JsonSerializable(typeof(KustoGatewayContracts.KustoGatewaySessionResponse))]
public sealed partial class KustoGatewayJsonContext : JsonSerializerContext
{
}
