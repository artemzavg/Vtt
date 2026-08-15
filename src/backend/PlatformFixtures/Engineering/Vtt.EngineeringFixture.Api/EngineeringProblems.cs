using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Vtt.Cqrs;
using Vtt.EngineeringFixture.Application;
using Vtt.EngineeringFixture.Contracts;
using Vtt.EngineeringFixture.Domain;
using Vtt.EventSourcing;

namespace Vtt.EngineeringFixture.Api;

internal sealed class RequestContractException(string code, string safeDetail)
    : Exception(safeDetail)
{
    public string Code { get; } = code;

    public string SafeDetail { get; } = safeDetail;
}

internal sealed class EngineeringExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<EngineeringExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, code, title, detail, extensions) = Map(exception);
        if (status >= 500)
        {
            EngineeringApiLog.UnexpectedRequestFailure(
                logger,
                exception.GetType().Name,
                httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = EngineeringProblem.Create(
                httpContext,
                status,
                code,
                title,
                detail,
                extensions),
        });
    }

    private static (int Status, string Code, string Title, string Detail,
        IReadOnlyDictionary<string, object?>? Extensions) Map(Exception exception) => exception switch
        {
            RequestContractException request => (
                StatusCodes.Status400BadRequest,
                request.Code,
                "Invalid request",
                request.SafeDetail,
                null),
            CommandValidationException validation => (
                StatusCodes.Status400BadRequest,
                PlatformProblemCodes.InvalidRequest,
                "Invalid request",
                "One or more command fields are invalid.",
                new Dictionary<string, object?> { ["errors"] = validation.Errors }),
            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                PlatformProblemCodes.MalformedRequest,
                "Malformed request",
                "The request body or route value is malformed.",
                null),
            JsonException => (
                StatusCodes.Status400BadRequest,
                PlatformProblemCodes.MalformedRequest,
                "Malformed request",
                "The request JSON is malformed.",
                null),
            IdempotencyPayloadMismatchException => (
                StatusCodes.Status409Conflict,
                PlatformProblemCodes.IdempotencyPayloadMismatch,
                "Idempotency conflict",
                "The key was already used for a different request.",
                null),
            AggregateConflictException conflict => (
                StatusCodes.Status409Conflict,
                PlatformProblemCodes.AggregateConflict,
                "Aggregate version conflict",
                "The resource changed after the caller read it.",
                new Dictionary<string, object?>
                {
                    ["aggregateId"] = conflict.AggregateId,
                    ["expectedVersion"] = conflict.ExpectedVersion,
                    ["currentVersion"] = conflict.CurrentVersion,
                }),
            ProbeInvariantException invariant => (
                StatusCodes.Status422UnprocessableEntity,
                invariant.Code,
                "Domain invariant rejected the command",
                "The requested increment violates the engineering fixture invariant.",
                null),
            CommandDeadlineExceededException deadline => (
                StatusCodes.Status408RequestTimeout,
                PlatformProblemCodes.CommandDeadlineExceeded,
                "Command deadline exceeded",
                "The command deadline elapsed before processing began.",
                new Dictionary<string, object?> { ["deadline"] = deadline.Deadline }),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                PlatformProblemCodes.OperationNotFound,
                "Resource not found",
                "The requested operator resource does not exist.",
                null),
            _ => (
                StatusCodes.Status500InternalServerError,
                PlatformProblemCodes.UnexpectedError,
                "Unexpected error",
                "The request could not be completed.",
                null),
        };
}

internal static partial class EngineeringApiLog
{
    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Error,
        Message = "Engineering request failed with {ErrorCode}; correlation {CorrelationId}.")]
    public static partial void UnexpectedRequestFailure(
        ILogger logger,
        string errorCode,
        string correlationId);
}

internal static class EngineeringProblem
{
    public static ProblemDetails Create(
        HttpContext context,
        int status,
        string code,
        string title,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Type = $"urn:vtt:problem:{code}",
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString();
        if (extensions is not null)
        {
            foreach (var extension in extensions)
            {
                problem.Extensions[extension.Key] = extension.Value;
            }
        }

        return problem;
    }

    public static IResult Result(HttpContext context, int status, string code, string detail) =>
        Results.Json(
            Create(
                context,
                status,
                code,
                status == StatusCodes.Status404NotFound
                    ? "Resource not found"
                    : "Request failed",
                detail),
            statusCode: status,
            contentType: "application/problem+json");
}

internal sealed class RequestSizeLimitMiddleware(RequestDelegate next)
{
    private const long MaximumBodySize = 65_536;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > MaximumBodySize)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                EngineeringProblem.Create(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    PlatformProblemCodes.MalformedRequest,
                    "Payload too large",
                    "Request bodies are limited to 64 KiB."),
                cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }
}
