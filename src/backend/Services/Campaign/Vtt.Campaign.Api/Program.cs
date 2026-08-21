using Microsoft.EntityFrameworkCore;
using Vtt.Campaign.Api;
using Vtt.Campaign.Infrastructure;
using Vtt.ServiceDefaults;

const string ServiceName = "campaign";

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.AddVttServiceDefaults(ServiceName);
builder.Services.AddExceptionHandler<CampaignExceptionHandler>();
builder.Services.AddCampaignInfrastructure(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);

var app = builder.Build();
if (builder.Configuration.GetValue("VTT_APPLY_SCHEMA", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
    await database.Database.MigrateAsync();
}

app.MapVttServiceDefaults(ServiceName, mapFoundationOpenApi: false, mapRoot: false);
app.MapCampaignEndpoints();
app.MapGet("/", () => Results.Ok(new { service = ServiceName, status = "ready" }));

await app.RunAsync();

public partial class Program;
