using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vtt.Edge.Application;

namespace Vtt.Edge.Infrastructure;

public sealed class IdentityHttpGateway(HttpClient client) : IIdentityGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<GatewayResponse<string>> RegisterAsync(RegistrationInput input, CancellationToken cancellationToken) =>
        SendAsync<string>(HttpMethod.Post, "/api/v1/auth/registrations", input, null, null, null, cancellationToken);

    public Task<GatewayResponse<string>> VerifyEmailAsync(string token, CancellationToken cancellationToken) =>
        SendAsync<string>(HttpMethod.Post, "/api/v1/auth/email-verifications", new { challengeToken = token }, null, null, null, cancellationToken);

    public Task<GatewayResponse<string>> RequestResetAsync(string email, CancellationToken cancellationToken) =>
        SendAsync<string>(HttpMethod.Post, "/api/v1/auth/password-resets", new { email }, null, null, null, cancellationToken);

    public Task<GatewayResponse<string>> CompleteResetAsync(string token, string password, CancellationToken cancellationToken) =>
        SendAsync<string>(HttpMethod.Post, "/api/v1/auth/password-resets/complete", new { challengeToken = token, newPassword = password }, null, null, null, cancellationToken);

    public Task<GatewayResponse<GatewaySession>> LoginAsync(LoginInput input, CancellationToken cancellationToken) =>
        SendAsync<GatewaySession>(HttpMethod.Post, "/internal/v1/auth/sessions", input, null, null, null, cancellationToken);

    public Task<GatewayResponse<GatewaySession>> RefreshAsync(string sessionToken, CancellationToken cancellationToken) =>
        SendAsync<GatewaySession>(HttpMethod.Post, "/internal/v1/auth/sessions/refresh", null, sessionToken, null, null, cancellationToken);

    public Task<GatewayResponse<BffUser>> GetMeAsync(string sessionToken, CancellationToken cancellationToken) =>
        SendAsync<BffUser>(HttpMethod.Get, "/internal/v1/auth/me", null, sessionToken, null, null, cancellationToken);

    public Task<GatewayResponse<BffUser>> UpdateProfileAsync(string sessionToken, string csrfToken, long version, ProfileInput input, CancellationToken cancellationToken) =>
        SendAsync<BffUser>(HttpMethod.Put, "/internal/v1/auth/me", input, sessionToken, csrfToken, version, cancellationToken);

    public Task<GatewayResponse<IReadOnlyList<GatewayLoginSession>>> ListSessionsAsync(string sessionToken, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<GatewayLoginSession>>(HttpMethod.Get, "/internal/v1/auth/sessions", null, sessionToken, null, null, cancellationToken);

    public Task<GatewayResponse<string>> RevokeSessionAsync(string sessionToken, string csrfToken, Guid sessionId, CancellationToken cancellationToken) =>
        SendAsync<string>(HttpMethod.Delete, $"/internal/v1/auth/sessions/{sessionId:D}", null, sessionToken, csrfToken, null, cancellationToken);

    public Task<GatewayResponse<string>> RevokeOtherSessionsAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken) =>
        SendAsync<string>(HttpMethod.Delete, "/internal/v1/auth/sessions", null, sessionToken, csrfToken, null, cancellationToken);

    public Task<GatewayResponse<string>> LogoutAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken) =>
        SendAsync<string>(HttpMethod.Delete, "/internal/v1/auth/sessions/current", null, sessionToken, csrfToken, null, cancellationToken);

    private async Task<GatewayResponse<T>> SendAsync<T>(
        HttpMethod method,
        string uri,
        object? body,
        string? sessionToken,
        string? csrfToken,
        long? version,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        AddHeader(request, "X-Vtt-Session", sessionToken);
        AddHeader(request, "X-CSRF-Token", csrfToken);
        if (version.HasValue)
        {
            AddHeader(request, "If-Match", $"\"{version.Value}\"");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        T? value = default;
        if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength != 0)
        {
            if (typeof(T) == typeof(string))
            {
                value = (T)(object)await response.Content.ReadAsStringAsync(cancellationToken);
            }
            else
            {
                value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            }
        }

        string? problemCode = null;
        if (!response.IsSuccessStatusCode)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("code", out var code))
            {
                problemCode = code.GetString();
            }
        }

        return new GatewayResponse<T>((int)response.StatusCode, value, problemCode);
    }

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}

public static class EdgeInfrastructureExtensions
{
    public static IServiceCollection AddEdgeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Identity:BaseUrl"] ?? "http://localhost:5101";
        services.AddHttpClient<IIdentityGateway, IdentityHttpGateway>(client =>
        {
            client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        var campaignBaseUrl = configuration["Campaign:BaseUrl"] ?? "http://localhost:5102";
        services.AddHttpClient<ICampaignGateway, CampaignHttpGateway>(client =>
        {
            client.BaseAddress = new Uri(campaignBaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
