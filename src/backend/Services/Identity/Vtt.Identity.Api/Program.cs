using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Vtt.Identity.Api;
using Vtt.Identity.Infrastructure;
using Vtt.ServiceDefaults;

const string ServiceName = "identity";

var builder = WebApplication.CreateBuilder(args);
builder.AddVttServiceDefaults(ServiceName);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<IdentityDataProtectionHealthCheck>(
    "identity-data-protection",
    tags: ["ready"]);
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore()
        .UseDbContext<IdentityDbContext>()
        .ReplaceDefaultEntities<Guid>())
    .AddServer(options =>
    {
        var issuer = builder.Configuration["Identity:Oidc:Issuer"];
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            options.SetIssuer(new Uri(issuer, UriKind.Absolute));
        }
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetRevocationEndpointUris("connect/revocation")
            .SetUserInfoEndpointUris("connect/userinfo");
        options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
        options.RequireProofKeyForCodeExchange();
        options.RegisterScopes(
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.OfflineAccess);
        if (builder.Environment.IsDevelopment())
        {
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }
        else
        {
            var certificatePath = builder.Configuration["Identity:Certificates:Path"];
            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                throw new InvalidOperationException("Identity:Certificates:Path is required outside Development.");
            }

            var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                builder.Configuration["Identity:Certificates:Password"]);
            options.AddEncryptionCertificate(certificate).AddSigningCertificate(certificate);
        }
        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough();
        if (builder.Environment.IsDevelopment())
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapVttServiceDefaults(ServiceName, mapFoundationOpenApi: false, mapRoot: false);
app.MapIdentityEndpoints();
app.MapVttOidcEndpoints();
app.MapGet("/", () => Results.Ok(new { service = ServiceName, status = "ready" }));

if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentitySchemaMigrator>()
        .ApplyAsync(CancellationToken.None);
    await OpenIddictBootstrapper.EnsureWebClientAsync(scope.ServiceProvider, builder.Configuration, CancellationToken.None);
    return;
}

if (builder.Configuration.GetValue("VTT_APPLY_SCHEMA", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentitySchemaMigrator>()
        .ApplyAsync(CancellationToken.None);
    await OpenIddictBootstrapper.EnsureWebClientAsync(scope.ServiceProvider, builder.Configuration, CancellationToken.None);
}

await app.RunAsync();

public partial class Program;
