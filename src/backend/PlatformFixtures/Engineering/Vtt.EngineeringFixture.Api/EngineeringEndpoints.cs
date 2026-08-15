using System.Globalization;
using System.Text.Json;
using Vtt.Cqrs;
using Vtt.EngineeringFixture.Application;
using Vtt.EngineeringFixture.Contracts;
using Vtt.EngineeringFixture.Infrastructure;

namespace Vtt.EngineeringFixture.Api;

internal static class EngineeringEndpoints
{
    public static IEndpointRouteBuilder MapEngineeringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Ok(new
        {
            service = "engineering-fixture",
            status = "ready",
            purpose = "Step 02 platform guarantees; contains no product domain semantics.",
        })).ExcludeFromDescription();

        var group = endpoints.MapGroup("/platform")
            .WithTags("Engineering platform fixture");

        group.MapPost("/probes/{probeId:guid}/increments", IncrementProbeAsync)
            .WithName("incrementEngineeringProbe")
            .WithSummary("Append a version-checked event and transactional outbox message.")
            .Produces<IncrementProbeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/probes/{probeId:guid}/projection", GetProjectionAsync)
            .WithName("getEngineeringProbeProjection")
            .WithSummary("Read the eventually consistent service-local projection.")
            .Produces<ProbeProjectionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/projections/probes/rebuild", StartRebuildAsync)
            .WithName("rebuildEngineeringProbeProjection")
            .WithSummary("Start a side-effect-free projection rebuild.")
            .Produces<OperationResponse>(StatusCodes.Status202Accepted);
        group.MapGet("/operations/{operationId:guid}", GetOperationAsync)
            .WithName("getEngineeringOperation")
            .Produces<OperationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/operations/{operationId:guid}", CancelOperationAsync)
            .WithName("cancelEngineeringOperation")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/quarantine", ListQuarantineAsync)
            .WithName("listEngineeringQuarantine")
            .Produces<QuarantineResponse[]>();
        group.MapPost("/quarantine/{quarantineId:guid}/retry", RetryQuarantineAsync)
            .WithName("retryEngineeringQuarantine")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/outbox/{messageId:guid}/retry", RetryOutboxAsync)
            .WithName("retryEngineeringOutbox")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> IncrementProbeAsync(
        Guid probeId,
        IncrementProbeRequest request,
        HttpContext context,
        CommandPipeline<IncrementProbeCommand, IncrementProbeResult> pipeline,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ParseExpectedVersion(context.Request.Headers.IfMatch.ToString());
        var idempotencyKey = ParseIdempotencyKey(
            context.Request.Headers["Idempotency-Key"].ToString());
        var deadline = ParseDeadline(context.Request.Headers["X-Command-Deadline"].ToString());
        var commandId = ParseOptionalGuid(
            context.Request.Headers["X-Command-ID"].ToString()) ?? Guid.CreateVersion7();
        var requestHash = StableHash.Sha256(
            JsonSerializer.Serialize(new { probeId, request.Amount, expectedVersion }));
        var metadata = new CommandMetadata(
            commandId,
            PrincipalId: "engineering-fixture-principal",
            TenantId: "engineering-fixture-tenant",
            context.TraceIdentifier,
            context.Request.Headers["X-Causation-ID"].ToString() is { Length: > 0 } causation
                ? causation
                : null,
            deadline,
            idempotencyKey,
            expectedVersion);

        var result = await pipeline.ExecuteAsync(
            new IncrementProbeCommand(probeId, request.Amount, requestHash, metadata),
            cancellationToken);
        context.Response.Headers.ETag = $"\"{result.AggregateVersion}\"";
        context.Response.Headers["X-Projection-Pending"] = result.ProjectionPending
            ? "true"
            : "false";

        return Results.Ok(new IncrementProbeResponse(
            result.ProbeId,
            result.Value,
            result.AggregateVersion,
            result.EventId,
            result.ProjectionPending,
            result.IdempotencyReplayed,
            context.TraceIdentifier));
    }

    private static async Task<IResult> GetProjectionAsync(
        Guid probeId,
        HttpContext context,
        QueryPipeline<GetProbeProjectionQuery, ProbeProjectionResult?> pipeline,
        CancellationToken cancellationToken)
    {
        var result = await pipeline.ExecuteAsync(
            new GetProbeProjectionQuery(probeId),
            cancellationToken);
        return result is null
            ? EngineeringProblem.Result(
                context,
                StatusCodes.Status404NotFound,
                PlatformProblemCodes.ProjectionNotFound,
                "Projection not found.")
            : Results.Ok(new ProbeProjectionResponse(
                result.ProbeId,
                result.Value,
                result.ProjectionVersion,
                result.SourceAggregateVersion,
                result.AsOf,
                result.Checksum));
    }

    private static async Task<IResult> StartRebuildAsync(
        IProjectionOperations operations,
        CancellationToken cancellationToken)
    {
        var operation = await operations.StartRebuildAsync(cancellationToken);
        return Results.Accepted(
            $"/platform/operations/{operation.OperationId:D}",
            Map(operation));
    }

    private static async Task<IResult> GetOperationAsync(
        Guid operationId,
        HttpContext context,
        IProjectionOperations operations,
        CancellationToken cancellationToken)
    {
        var operation = await operations.FindAsync(operationId, cancellationToken);
        return operation is null
            ? EngineeringProblem.Result(
                context,
                StatusCodes.Status404NotFound,
                PlatformProblemCodes.OperationNotFound,
                "Operation not found.")
            : Results.Ok(Map(operation));
    }

    private static async Task<IResult> CancelOperationAsync(
        Guid operationId,
        HttpContext context,
        IProjectionOperations operations,
        CancellationToken cancellationToken) =>
        await operations.RequestCancellationAsync(operationId, cancellationToken)
            ? Results.Accepted($"/platform/operations/{operationId:D}")
            : EngineeringProblem.Result(
                context,
                StatusCodes.Status404NotFound,
                PlatformProblemCodes.OperationNotFound,
                "A cancellable operation was not found.");

    private static async Task<IResult> ListQuarantineAsync(
        IMessageOperations operations,
        CancellationToken cancellationToken)
    {
        var messages = await operations.ListQuarantineAsync(cancellationToken);
        return Results.Ok(messages.Select(message => new QuarantineResponse(
            message.QuarantineId,
            message.EventId,
            message.Consumer,
            message.Subject,
            message.ReasonCode,
            message.RetryCount,
            message.QuarantinedAt,
            message.RetriedAt)).ToArray());
    }

    private static async Task<IResult> RetryQuarantineAsync(
        Guid quarantineId,
        IMessageOperations operations,
        CancellationToken cancellationToken)
    {
        await operations.RetryQuarantinedAsync(quarantineId, cancellationToken);
        return Results.Accepted();
    }

    private static async Task<IResult> RetryOutboxAsync(
        Guid messageId,
        IMessageOperations operations,
        CancellationToken cancellationToken)
    {
        await operations.RetryOutboxAsync(messageId, cancellationToken);
        return Results.Accepted();
    }

    private static OperationResponse Map(PlatformOperationResult operation) => new(
        operation.OperationId,
        operation.Type,
        operation.Status.ToString().ToLowerInvariant(),
        operation.CreatedAt,
        operation.CompletedAt,
        operation.ResultChecksum,
        operation.ErrorCode,
        operation.CancellationRequested);

    private static long ParseExpectedVersion(string value)
    {
        if (value.Length >= 3 && value[0] == '"' && value[^1] == '"' &&
            long.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) &&
            version >= 0)
        {
            return version;
        }

        throw new RequestContractException(
            PlatformProblemCodes.InvalidRequest,
            "If-Match must contain a strong, non-negative aggregate version such as \"0\".");
    }

    private static string ParseIdempotencyKey(string value)
    {
        if (value.Length is > 0 and <= 128 &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':'))
        {
            return value;
        }

        throw new RequestContractException(
            PlatformProblemCodes.InvalidRequest,
            "Idempotency-Key is required and must be a safe value no longer than 128 characters.");
    }

    private static DateTimeOffset? ParseDeadline(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var deadline))
        {
            return deadline;
        }

        throw new RequestContractException(
            PlatformProblemCodes.InvalidRequest,
            "X-Command-Deadline must be an RFC 3339 timestamp.");
    }

    private static Guid? ParseOptionalGuid(string value) =>
        string.IsNullOrEmpty(value)
            ? null
            : Guid.TryParse(value, out var parsed)
                ? parsed
                : throw new RequestContractException(
                    PlatformProblemCodes.InvalidRequest,
                    "X-Command-ID must be a UUID.");
}
