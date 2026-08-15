using System.Diagnostics;
using System.Diagnostics.Metrics;
using global::Marten;
using global::Marten.Services;
using Npgsql;

namespace Vtt.Persistence.Marten;

/// <summary>
/// Records database failures without logging SQL, bound parameters, document JSON, or event data.
/// The request/worker boundary remains responsible for emitting a safe error code and correlation id.
/// </summary>
public sealed class RedactedMartenLogger : IMartenLogger, IMartenSessionLogger
{
    private static readonly Meter Meter = new("Vtt.Persistence");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "vtt.persistence.failures");

    public static RedactedMartenLogger Instance { get; } = new();

    private RedactedMartenLogger()
    {
    }

    public IMartenSessionLogger StartSession(IQuerySession session) => this;

    public void SchemaChange(string sql)
    {
        // DDL is intentionally not written by the runtime logger.
    }

    public void LogSuccess(NpgsqlCommand command)
    {
    }

    public void LogFailure(NpgsqlCommand command, Exception ex) =>
        RecordFailure(ex);

    public void LogSuccess(NpgsqlBatch batch)
    {
    }

    public void LogFailure(NpgsqlBatch batch, Exception ex) =>
        RecordFailure(ex);

    public void LogFailure(Exception ex, string message) =>
        RecordFailure(ex);

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {
    }

    public void OnBeforeExecute(NpgsqlBatch batch)
    {
    }

    private static void RecordFailure(Exception exception)
    {
        var errorCode = exception.GetType().Name;
        Failures.Add(
            1,
            new KeyValuePair<string, object?>("error_code", errorCode));
        Activity.Current?.AddEvent(
            new ActivityEvent(
                "persistence failure",
                tags: new ActivityTagsCollection
                {
                    ["error.type"] = errorCode,
                }));
    }
}
