using System.Text.Json.Serialization;
using Vtt.EngineeringFixture.Api;
using Vtt.EngineeringFixture.Infrastructure;
using Vtt.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddVttServiceDefaults("engineering-fixture");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65_536);
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddExceptionHandler<EngineeringExceptionHandler>();
builder.Services.AddEngineeringFixture(builder.Configuration);

var app = builder.Build();
if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    await app.Services.GetRequiredService<EngineeringSchemaMigrator>()
        .ApplyAsync(CancellationToken.None);
    return;
}

app.UseMiddleware<RequestSizeLimitMiddleware>();
app.MapVttServiceDefaults(
    "engineering-fixture",
    mapFoundationOpenApi: false,
    mapRoot: false);
app.MapOpenApi();
app.MapEngineeringEndpoints();

await app.RunAsync();

public partial class Program;
