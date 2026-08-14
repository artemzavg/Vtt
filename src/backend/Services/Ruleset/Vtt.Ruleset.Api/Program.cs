using Vtt.ServiceDefaults;

const string ServiceName = "ruleset";

var builder = WebApplication.CreateBuilder(args);
builder.AddVttServiceDefaults(ServiceName);

var app = builder.Build();
app.MapVttServiceDefaults(ServiceName);

await app.RunAsync();

public partial class Program;

