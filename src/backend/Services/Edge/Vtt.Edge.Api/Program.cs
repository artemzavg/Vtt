using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Vtt.Edge.Api;
using Vtt.Edge.Infrastructure;
using Vtt.ServiceDefaults;

const string ServiceName = "edge";

var builder = WebApplication.CreateBuilder(args);
builder.AddVttServiceDefaults(ServiceName);
builder.Services.AddExceptionHandler<EdgeExceptionHandler>();
builder.Services.AddEdgeInfrastructure(builder.Configuration);
builder.Services.AddEdgeOidc(builder.Configuration, builder.Environment);
var edgeDataProtection = builder.Services.AddDataProtection().SetApplicationName("Vtt.Edge");
var edgeDataProtectionPath = builder.Configuration["Edge:DataProtectionPath"];
if (string.IsNullOrWhiteSpace(edgeDataProtectionPath))
{
    edgeDataProtection.UseEphemeralDataProtectionProvider();
}
else
{
    edgeDataProtection.PersistKeysToFileSystem(new DirectoryInfo(edgeDataProtectionPath));
}
builder.Services.AddHealthChecks().AddCheck<EdgeDataProtectionHealthCheck>(
    "edge-data-protection",
    tags: ["ready"]);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();
app.UseRateLimiter();
app.UseAuthentication();
app.MapVttServiceDefaults(ServiceName, mapFoundationOpenApi: false, mapRoot: false);
app.MapIdentityBff();
app.MapCampaignBff();
app.MapGet("/", () => Results.Ok(new { service = ServiceName, status = "ready" }));

await app.RunAsync();

public partial class Program;
