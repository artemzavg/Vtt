using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Vtt.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddVttServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        builder.Host.ConfigureHostOptions(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(15);
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.UseUtcTimestamp = true;
        });

        builder.Services.AddProblemDetails();
        builder.Services
            .AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy("Process is running."),
                tags: ["live"])
            .AddCheck<ConfiguredTcpDependenciesHealthCheck>(
                "configured-dependencies",
                tags: ["ready"]);

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint))
        {
            var resource = ResourceBuilder.CreateDefault().AddService(serviceName);

            builder.Logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resource);
                options.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
            });

            builder.Services
                .AddOpenTelemetry()
                .ConfigureResource(resourceBuilder => resourceBuilder.AddService(serviceName))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource(
                        "Vtt.Cqrs",
                        "Vtt.Outbox",
                        "Vtt.Inbox",
                        "Vtt.Projections",
                        "Vtt.Identity",
                        "Npgsql",
                        "NATS.Client")
                    .AddOtlpExporter(exporter => exporter.Endpoint = endpoint))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(
                        "Vtt.Cqrs",
                        "Vtt.EventStore",
                        "Vtt.Persistence",
                        "Vtt.Outbox",
                        "Vtt.Inbox",
                        "Vtt.Projections",
                        "Npgsql")
                    .AddOtlpExporter(exporter => exporter.Endpoint = endpoint));
        }

        return builder;
    }

    public static WebApplication MapVttServiceDefaults(
        this WebApplication app,
        string serviceName,
        bool mapFoundationOpenApi = true,
        bool mapRoot = true)
    {
        app.UseVttCorrelation();
        app.UseExceptionHandler();

        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live", StringComparer.Ordinal),
                ResponseWriter = WriteHealthResponseAsync,
            });

        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready", StringComparer.Ordinal),
                ResponseWriter = WriteHealthResponseAsync,
            });

        if (mapFoundationOpenApi)
        {
            app.MapGet(
                "/openapi/v1.json",
                () => Results.Json(
                    new
                    {
                        openapi = "3.1.1",
                        info = new
                        {
                            title = $"VTT {serviceName} API",
                            version = "0.0.0-foundation",
                        },
                        paths = new Dictionary<string, object>
                        {
                            ["/health/live"] = new { },
                            ["/health/ready"] = new { },
                        },
                    }));
        }

        if (mapRoot)
        {
            app.MapGet(
                "/",
                () => Results.Ok(
                    new
                    {
                        service = serviceName,
                        status = "foundation-only",
                        businessApi = "not-implemented",
                    }));
        }

        return app;
    }

    public static IApplicationBuilder UseVttCorrelation(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(
                    pair => pair.Key,
                    pair => new
                    {
                        status = pair.Value.Status.ToString(),
                        description = pair.Value.Description,
                    }),
            },
            cancellationToken: context.RequestAborted);
    }
}

internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(supplied)
            ? supplied
            : Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString("D");

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        using (logger.BeginScope(new Dictionary<string, object> { ["correlation_id"] = correlationId }))
        {
            await next(context);
        }
    }

    private static bool IsValid(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
}

internal sealed class ConfiguredTcpDependenciesHealthCheck(
    IConfiguration configuration) : IHealthCheck
{
    private readonly string[] _endpoints = configuration["VTT_READINESS_TCP_ENDPOINTS"]?
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? [];

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_endpoints.Length == 0)
        {
            return HealthCheckResult.Healthy("No mandatory foundation dependencies configured.");
        }

        var failures = new List<string>();

        foreach (var endpoint in _endpoints)
        {
            var separator = endpoint.LastIndexOf(':');
            if (separator <= 0 ||
                !int.TryParse(endpoint[(separator + 1)..], out var port))
            {
                failures.Add($"{endpoint}: invalid host:port value");
                continue;
            }

            var host = endpoint[..separator];

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(1));

                using var client = new TcpClient();
                await client.ConnectAsync(host, port, timeout.Token);
            }
            catch (Exception exception) when (
                exception is SocketException or OperationCanceledException)
            {
                failures.Add($"{endpoint}: unavailable");
            }
        }

        return failures.Count == 0
            ? HealthCheckResult.Healthy("All configured foundation dependencies are reachable.")
            : HealthCheckResult.Unhealthy(string.Join("; ", failures));
    }
}
