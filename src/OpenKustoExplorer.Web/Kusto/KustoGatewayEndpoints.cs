using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Antiforgery;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Kusto.Execution;
using OpenKustoExplorer.Kusto.Gateway.V1;
using OpenKustoExplorer.Web.Assistance;
using OpenKustoExplorer.Web.Authentication;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Web.Kusto;

/// <summary>
/// Maps the authenticated version-one Kusto gateway.
/// </summary>
internal static class KustoGatewayEndpoints
{
    /// <summary>
    /// Maps the same-origin Kusto gateway endpoints.
    /// </summary>
    /// <param name="endpoints">The Web endpoint route builder.</param>
    /// <returns>The supplied route builder.</returns>
    public static IEndpointRouteBuilder MapKustoGateway(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(KustoGatewayRoutes.Session, GetSession)
            .RequireAuthorization();
        endpoints.MapPost(KustoGatewayRoutes.SignOut, SignOutAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapPost(KustoGatewayRoutes.Query, ExecuteQueryAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapPost(KustoGatewayRoutes.Graph, ExecuteGraphAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapGet(KustoGatewayRoutes.CopilotModels, GetCopilotModels)
            .RequireAuthorization();
        endpoints.MapPost(KustoGatewayRoutes.Copilot, ExecuteCopilotAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapPost(KustoGatewayRoutes.Databases, GetDatabasesAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapPost(KustoGatewayRoutes.Schema, GetSchemaAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        endpoints.MapDelete(
            $"{KustoGatewayRoutes.Operations}/{{operationId:guid}}",
            CancelOperationAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        return endpoints;
    }

    private static async Task<IResult> CancelOperationAsync(
        Guid operationId,
        HttpContext context,
        IAntiforgery antiforgery,
        ClaimsPrincipal user,
        KustoOperationRegistry operationRegistry)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException exception)
        {
            return CreateError("invalid_antiforgery_token", exception.Message, StatusCodes.Status400BadRequest);
        }

        string ownerId = GetAccountId(user);
        return operationRegistry.TryCancel(operationId, ownerId)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static IResult CreateError(string code, string message, int statusCode)
    {
        KustoGatewayErrorResponse response = new()
        {
            Code = code,
            Message = message,
            Version = KustoGatewayRoutes.Version,
        };
        return Results.Json(
            response,
            KustoGatewayJsonContext.Default.KustoGatewayErrorResponse,
            statusCode: statusCode);
    }

    private static IResult CreateResponse<T>(T response, JsonTypeInfo<T> typeInfo)
    {
        byte[] responseBytes = JsonSerializer.SerializeToUtf8Bytes(response, typeInfo);
        return responseBytes.Length <= KustoGatewayRoutes.MaximumResponseByteCount
            ? Results.Json(response, typeInfo)
            : CreateError(
                "response_too_large",
                "The Kusto result exceeds the Web response size limit.",
                StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> ExecuteGatewayRequestAsync<TRequest>(
        HttpContext context,
        IAntiforgery antiforgery,
        KustoOperationRegistry operationRegistry,
        JsonTypeInfo<TRequest> typeInfo,
        Func<TRequest, Guid> getOperationId,
        Func<TRequest, CancellationToken, Task<IResult>> executeAsync)
        where TRequest : class
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            TRequest request = await ReadRequestAsync(context.Request, typeInfo, context.RequestAborted)
                .ConfigureAwait(false);
            Guid operationId = getOperationId(request);
            string ownerId = GetAccountId(context.User);
            using KustoOperationRegistry.OperationLease operation = operationRegistry.Begin(
                operationId,
                ownerId,
                context.RequestAborted);
            return await executeAsync(request, operation.CancellationToken).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException exception)
        {
            return CreateError("invalid_antiforgery_token", exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
        {
            return CreateError("invalid_request", exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CreateError("authentication_required", exception.Message, StatusCodes.Status401Unauthorized);
        }
        catch (WebCopilotUnavailableException exception)
        {
            return CreateError("copilot_unavailable", exception.Message, StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException exception)
        {
            return CreateError("adx_request_failed", exception.Message, StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException exception)
        {
            return CreateError("invalid_destination", exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            return CreateError("operation_cancelled", "The Kusto operation was cancelled.", 499);
        }
    }

    private static async Task<IResult> ExecuteQueryAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        KustoExecutionService executionService,
        KustoOperationRegistry operationRegistry)
    {
        return await ExecuteGatewayRequestAsync(
            context,
            antiforgery,
            operationRegistry,
            KustoGatewayJsonContext.Default.KustoGatewayQueryRequest,
            request => request.OperationId,
            async (request, cancellationToken) =>
            {
                KustoQueryResult result = await executionService
                    .ExecuteAsync(KustoGatewayMapper.ToDomainRequest(request), cancellationToken)
                    .ConfigureAwait(false);
                KustoGatewayQueryResponse response = KustoGatewayMapper.ToGatewayQueryResponse(result);
                return CreateResponse(
                    response,
                    KustoGatewayJsonContext.Default.KustoGatewayQueryResponse);
            }).ConfigureAwait(false);
    }

    private static async Task<IResult> ExecuteCopilotAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IWebCopilotService copilotService,
        KustoOperationRegistry operationRegistry)
    {
        return await ExecuteGatewayRequestAsync(
            context,
            antiforgery,
            operationRegistry,
            KustoGatewayJsonContext.Default.KustoGatewayCopilotRequest,
            request => request.OperationId,
            async (request, cancellationToken) =>
            {
                KustoCopilotContext copilotContext = KustoGatewayMapper.ToDomainCopilotContext(request);
                KustoCopilotReply reply = await copilotService.SendAsync(
                    copilotContext,
                    request.Request,
                    request.ModelId,
                    request.History,
                    cancellationToken).ConfigureAwait(false);
                KustoGatewayCopilotResponse response = KustoGatewayMapper.ToGatewayCopilotResponse(reply);
                return CreateResponse(
                    response,
                    KustoGatewayJsonContext.Default.KustoGatewayCopilotResponse);
            }).ConfigureAwait(false);
    }

    private static async Task<IResult> ExecuteGraphAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        KustoExecutionService executionService,
        IKustoLanguageService languageService,
        KustoOperationRegistry operationRegistry)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            KustoGatewayQueryRequest request = await ReadRequestAsync(
                context.Request,
                KustoGatewayJsonContext.Default.KustoGatewayQueryRequest,
                context.RequestAborted).ConfigureAwait(false);
            KustoQueryRequest query = KustoGatewayMapper.ToDomainRequest(request);
            KustoGraphQueryPlan plan = languageService.GetGraphQueryPlanAtPosition(
                query.QueryText,
                query.QueryText.Length)
                ?? throw new InvalidDataException("The selected query does not produce a supported graph.");
            string ownerId = GetAccountId(context.User);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Append("X-Accel-Buffering", "no");
            return Results.Stream(
                stream => StreamGraphAsync(
                    stream,
                    query,
                    plan,
                    request.OperationId,
                    ownerId,
                    executionService,
                    operationRegistry,
                    context.RequestAborted),
                "application/json; charset=utf-8");
        }
        catch (AntiforgeryValidationException exception)
        {
            return CreateError("invalid_antiforgery_token", exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
        {
            return CreateError("invalid_request", exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CreateError("authentication_required", exception.Message, StatusCodes.Status401Unauthorized);
        }
    }

    private static string GetAccountId(ClaimsPrincipal user)
    {
        string? objectId = user.FindFirstValue("oid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new UnauthorizedAccessException("The authenticated account has no stable identifier.");
        }

        string? tenantId = user.FindFirstValue("tid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        return string.IsNullOrWhiteSpace(tenantId)
            ? objectId
            : $"{objectId}.{tenantId}";
    }

    private static async Task<IResult> GetDatabasesAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        KustoExecutionService executionService,
        KustoOperationRegistry operationRegistry)
    {
        return await ExecuteGatewayRequestAsync(
            context,
            antiforgery,
            operationRegistry,
            KustoGatewayJsonContext.Default.KustoGatewayDatabasesRequest,
            request => request.OperationId,
            async (request, cancellationToken) =>
            {
                IReadOnlyList<KustoDatabaseInfo> databases = await executionService
                    .GetDatabasesAsync(
                        KustoGatewayMapper.ToDomainClusterUri(request),
                        cancellationToken)
                    .ConfigureAwait(false);
                KustoGatewayDatabasesResponse response = KustoGatewayMapper
                    .ToGatewayDatabasesResponse(databases);
                return CreateResponse(
                    response,
                    KustoGatewayJsonContext.Default.KustoGatewayDatabasesResponse);
            }).ConfigureAwait(false);
    }

    private static IResult GetSession(HttpContext context, IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        string accountId = GetAccountId(context.User);
        string accountName = context.User.FindFirstValue("preferred_username")
            ?? context.User.FindFirstValue(ClaimTypes.Upn)
            ?? context.User.FindFirstValue(ClaimTypes.Email)
            ?? context.User.Identity?.Name
            ?? accountId;
        string displayName = context.User.FindFirstValue("name")
            ?? context.User.FindFirstValue(ClaimTypes.Name)
            ?? accountName;
        KustoGatewaySessionResponse response = new()
        {
            AccountId = accountId,
            AccountName = accountName,
            AntiforgeryToken = tokens.RequestToken
                ?? throw new InvalidOperationException("Antiforgery did not issue a request token."),
            DisplayName = displayName,
            StoragePartition = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(accountId))),
            Version = KustoGatewayRoutes.Version,
        };
        return Results.Json(
            response,
            KustoGatewayJsonContext.Default.KustoGatewaySessionResponse);
    }

    private static IResult GetCopilotModels(IWebCopilotService copilotService)
    {
        try
        {
            KustoCopilotModel model = copilotService.GetModel();
            KustoGatewayCopilotModelsResponse response = new()
            {
                Models =
                [
                    new KustoGatewayCopilotModel
                    {
                        DisplayName = model.Name,
                        Id = model.Id,
                    },
                ],
                ProviderDisplayName = "Azure OpenAI",
                ProviderKind = (int)KustoAIProviderKind.AzureOpenAI,
                Version = KustoGatewayRoutes.Version,
            };
            return Results.Json(
                response,
                KustoGatewayJsonContext.Default.KustoGatewayCopilotModelsResponse);
        }
        catch (WebCopilotUnavailableException exception)
        {
            return CreateError("copilot_unavailable", exception.Message, StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetSchemaAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        KustoExecutionService executionService,
        KustoOperationRegistry operationRegistry)
    {
        return await ExecuteGatewayRequestAsync(
            context,
            antiforgery,
            operationRegistry,
            KustoGatewayJsonContext.Default.KustoGatewaySchemaRequest,
            request => request.OperationId,
            async (request, cancellationToken) =>
            {
                (Uri clusterUri, string databaseName) = KustoGatewayMapper.ToDomainSchemaTarget(request);
                KustoDatabaseSchema schema = await executionService
                    .GetDatabaseSchemaAsync(clusterUri, databaseName, cancellationToken)
                    .ConfigureAwait(false);
                KustoGatewaySchemaResponse response = KustoGatewayMapper.ToGatewaySchemaResponse(schema);
                return CreateResponse(
                    response,
                    KustoGatewayJsonContext.Default.KustoGatewaySchemaResponse);
            }).ConfigureAwait(false);
    }

    private static async Task<T> ReadRequestAsync<T>(
        HttpRequest request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        if (request.ContentLength > KustoGatewayRoutes.MaximumRequestByteCount)
        {
            throw new InvalidDataException("The gateway request exceeds the configured size limit.");
        }

        using MemoryStream buffer = new();
        byte[] readBuffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await request.Body
            .ReadAsync(readBuffer.AsMemory(), cancellationToken)
            .ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + bytesRead > KustoGatewayRoutes.MaximumRequestByteCount)
            {
                throw new InvalidDataException("The gateway request exceeds the configured size limit.");
            }

            await buffer.WriteAsync(
                readBuffer.AsMemory(0, bytesRead),
                cancellationToken).ConfigureAwait(false);
        }

        return JsonSerializer.Deserialize(buffer.ToArray(), typeInfo)
            ?? throw new InvalidDataException("The gateway request body is empty.");
    }

    private static async Task StreamGraphAsync(
        Stream stream,
        KustoQueryRequest query,
        KustoGraphQueryPlan plan,
        Guid operationId,
        string ownerId,
        KustoExecutionService executionService,
        KustoOperationRegistry operationRegistry,
        CancellationToken requestCancellationToken)
    {
        GraphResponseSink sink = new(stream);
        await sink.BeginAsync(requestCancellationToken).ConfigureAwait(false);

        try
        {
            using KustoOperationRegistry.OperationLease operation = operationRegistry.Begin(
                operationId,
                ownerId,
                requestCancellationToken);
            KustoGraphExportSummary summary = await executionService.ExecuteGraphAsync(
                query,
                plan,
                sink,
                operation.CancellationToken).ConfigureAwait(false);
            await sink.CompleteAsync(summary, requestCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await sink.CompleteWithErrorAsync(
                CreateGraphErrorFrame(exception),
                requestCancellationToken).ConfigureAwait(false);
        }
    }

    private static KustoGatewayGraphFrame CreateGraphErrorFrame(Exception exception)
    {
        (string code, string message) = exception switch
        {
            ArgumentException or InvalidDataException or JsonException => ("invalid_request", exception.Message),
            UnauthorizedAccessException => ("authentication_required", exception.Message),
            HttpRequestException => ("adx_request_failed", exception.Message),
            InvalidOperationException => ("invalid_destination", exception.Message),
            OperationCanceledException => ("operation_cancelled", "The Kusto operation was cancelled."),
            _ => ("internal_error", "The graph export failed unexpectedly."),
        };
        return new KustoGatewayGraphFrame
        {
            ErrorCode = code,
            ErrorMessage = message,
            FrameKind = KustoGatewayGraphFrameKind.Error,
            Version = KustoGatewayRoutes.Version,
        };
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IWebSignOutService signOutService)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException exception)
        {
            return CreateError("invalid_antiforgery_token", exception.Message, StatusCodes.Status400BadRequest);
        }

        return await signOutService.SignOutAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private sealed class GraphResponseSink : IKustoGraphExportSink
    {
        private static readonly byte[] ArrayEnd = "]"u8.ToArray();
        private static readonly byte[] ArrayStart = "["u8.ToArray();
        private static readonly byte[] ItemSeparator = ","u8.ToArray();
        private readonly Stream stream;
        private bool completed;
        private bool hasFrame;

        internal GraphResponseSink(Stream stream)
        {
            this.stream = stream;
        }

        public ValueTask WriteBatchAsync(
            KustoGraphExportBatch batch,
            CancellationToken cancellationToken = default)
        {
            return WriteFrameAsync(
                KustoGatewayMapper.ToGatewayGraphBatchFrame(batch),
                cancellationToken);
        }

        internal async ValueTask BeginAsync(CancellationToken cancellationToken)
        {
            await stream.WriteAsync(ArrayStart, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        internal async ValueTask CompleteAsync(
            KustoGraphExportSummary summary,
            CancellationToken cancellationToken)
        {
            await WriteFrameAsync(
                KustoGatewayMapper.ToGatewayGraphSummaryFrame(summary),
                cancellationToken).ConfigureAwait(false);
            await CompleteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        internal async ValueTask CompleteWithErrorAsync(
            KustoGatewayGraphFrame error,
            CancellationToken cancellationToken)
        {
            await WriteFrameAsync(error, cancellationToken).ConfigureAwait(false);
            await CompleteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask CompleteArrayAsync(CancellationToken cancellationToken)
        {
            await stream.WriteAsync(ArrayEnd, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            completed = true;
        }

        private async ValueTask WriteFrameAsync(
            KustoGatewayGraphFrame frame,
            CancellationToken cancellationToken)
        {
            if (completed)
            {
                throw new InvalidOperationException("The graph response stream is already complete.");
            }

            if (hasFrame)
            {
                await stream.WriteAsync(ItemSeparator, cancellationToken).ConfigureAwait(false);
            }

            await JsonSerializer.SerializeAsync(
                stream,
                frame,
                KustoGatewayJsonContext.Default.KustoGatewayGraphFrame,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            hasFrame = true;
        }
    }
}
