using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Vtt.Edge.Api;
using Vtt.Edge.Application;

namespace Vtt.Edge.IntegrationTests;

public sealed class IdentityBffTests : IClassFixture<IdentityBffTests.EdgeFactory>
{
    private readonly EdgeFactory _factory;

    public IdentityBffTests(EdgeFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginStoresOpaqueSessionOnlyInHttpOnlyCookie()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = "player@example.test",
            password = "Correct-horse-42!",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-session-token", body, StringComparison.Ordinal);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, cookie => cookie.StartsWith("vtt.session=private-session-token", StringComparison.Ordinal) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cookies, cookie => cookie.StartsWith("vtt.csrf=csrf-token", StringComparison.Ordinal) && !cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MutationWithoutDoubleSubmitCsrfIsRejectedBeforeIdentityCall()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/logout");
        request.Headers.Add("Cookie", "vtt.session=private-session-token; vtt.csrf=csrf-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _factory.Gateway.LogoutCalls);
    }

    [Fact]
    public async Task UnknownJsonPropertyReturnsBadRequestInsteadOfInternalServerError()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/v1/auth/password-resets/complete", new
        {
            token = "unexpected-contract-field",
            newPassword = "Correct-horse-42!",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OidcStartCreatesStateNonceCorrelationAndPkceChallenge()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await LoginAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/v1/auth/oidc/start?returnUrl=https%3A%2F%2Fevil.example%2Fcallback");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("identity.test", location.Host);
        var query = ParseQuery(location.Query);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.False(string.IsNullOrWhiteSpace(query["nonce"]));
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.Contains("Correlation", StringComparison.Ordinal) &&
            value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OidcCallbackExchangesCodeValidatesNonceAndRejectsReplay()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await LoginAsync(client);

        using var startRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/v1/auth/oidc/start?returnUrl=%2F%5Cevil.example");
        using var start = await client.SendAsync(startRequest);
        var query = ParseQuery(Assert.IsType<Uri>(start.Headers.Location).Query);
        _factory.TokenEndpoint.Nonce = query["nonce"];
        var exchangesBefore = _factory.TokenEndpoint.ExchangeCalls;

        using var callback = await client.GetAsync(
            $"/signin-oidc?code=single-use-code&state={Uri.EscapeDataString(query["state"])}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/account", callback.Headers.Location?.OriginalString);
        Assert.Equal(exchangesBefore + 1, _factory.TokenEndpoint.ExchangeCalls);
        Assert.True(_factory.TokenEndpoint.CodeVerifierObserved);

        using var replay = await client.GetAsync(
            $"/signin-oidc?code=single-use-code&state={Uri.EscapeDataString(query["state"])}");
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal("/auth?oidcError=callback_failed", replay.Headers.Location?.OriginalString);
        Assert.Equal(exchangesBefore + 1, _factory.TokenEndpoint.ExchangeCalls);
    }

    [Fact]
    public async Task OidcCallbackRejectsTamperedStateBeforeCodeExchange()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await LoginAsync(client);
        using var start = await client.GetAsync("/v1/auth/oidc/start?returnUrl=%2Faccount");
        var query = ParseQuery(Assert.IsType<Uri>(start.Headers.Location).Query);
        var exchangesBefore = _factory.TokenEndpoint.ExchangeCalls;
        var tamperedState = query["state"][..^1] + (query["state"][^1] == 'A' ? "B" : "A");

        using var callback = await client.GetAsync(
            $"/signin-oidc?code=unused-code&state={Uri.EscapeDataString(tamperedState)}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/auth?oidcError=callback_failed", callback.Headers.Location?.OriginalString);
        Assert.Equal(exchangesBefore, _factory.TokenEndpoint.ExchangeCalls);
    }

    [Fact]
    public async Task OidcCallbackRejectsMissingCorrelationCookieBeforeCodeExchange()
    {
        using var initiatingClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await LoginAsync(initiatingClient);
        using var start = await initiatingClient.GetAsync("/v1/auth/oidc/start?returnUrl=%2Faccount");
        var query = ParseQuery(Assert.IsType<Uri>(start.Headers.Location).Query);
        var exchangesBefore = _factory.TokenEndpoint.ExchangeCalls;
        using var callbackClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var callback = await callbackClient.GetAsync(
            $"/signin-oidc?code=unused-code&state={Uri.EscapeDataString(query["state"])}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/auth?oidcError=callback_failed", callback.Headers.Location?.OriginalString);
        Assert.Equal(exchangesBefore, _factory.TokenEndpoint.ExchangeCalls);
    }

    [Fact]
    public async Task OidcCallbackRejectsIdTokenWithWrongNonce()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        await LoginAsync(client);
        using var start = await client.GetAsync("/v1/auth/oidc/start?returnUrl=%2Faccount");
        var query = ParseQuery(Assert.IsType<Uri>(start.Headers.Location).Query);
        _factory.TokenEndpoint.Nonce = "different-nonce";
        var exchangesBefore = _factory.TokenEndpoint.ExchangeCalls;

        using var callback = await client.GetAsync(
            $"/signin-oidc?code=nonce-mismatch-code&state={Uri.EscapeDataString(query["state"])}");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/auth?oidcError=callback_failed", callback.Headers.Location?.OriginalString);
        Assert.Equal(exchangesBefore + 1, _factory.TokenEndpoint.ExchangeCalls);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = "player@example.test",
            password = "Correct-horse-42!",
        });
        response.EnsureSuccessStatusCode();
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => Uri.UnescapeDataString(part.Length == 2 ? part[1] : string.Empty),
                StringComparer.Ordinal);

    public sealed class EdgeFactory : WebApplicationFactory<ApiAssemblyMarker>
    {
        public FakeGateway Gateway { get; } = new();
        public FakeTokenEndpoint TokenEndpoint { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IIdentityGateway>(Gateway);
                services.AddSingleton<IDataProtectionProvider>(
                    new EphemeralDataProtectionProvider());
                services.PostConfigure<OpenIdConnectOptions>("vtt.oidc", options =>
                {
                    options.Authority = "https://identity.test/";
                    options.RequireHttpsMetadata = false;
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = "https://identity.test/",
                        AuthorizationEndpoint = "https://identity.test/connect/authorize",
                        TokenEndpoint = "https://identity.test/connect/token",
                        SigningKeys = { TokenEndpoint.SigningKey },
                    };
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                    options.TokenValidationParameters.ValidIssuer = "https://identity.test/";
                    options.Backchannel = new HttpClient(TokenEndpoint)
                    {
                        BaseAddress = new Uri("https://identity.test/"),
                    };
                });
            });
    }

    public sealed class FakeTokenEndpoint : HttpMessageHandler
    {
        private readonly RsaSecurityKey _signingKey = new(System.Security.Cryptography.RSA.Create(2048))
        {
            KeyId = "acceptance-signing-key",
        };

        public SecurityKey SigningKey => _signingKey;
        public string Nonce { get; set; } = string.Empty;
        public int ExchangeCalls { get; private set; }
        public bool CodeVerifierObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("/connect/token", request.RequestUri?.AbsolutePath);
            ExchangeCalls++;
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            CodeVerifierObserved = form.Contains("code_verifier=", StringComparison.Ordinal);
            var now = DateTimeOffset.UtcNow;
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "https://identity.test/",
                Audience = "vtt-web-bff",
                Subject = new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim("sub", FakeGateway.UserId.ToString("D")),
                    new System.Security.Claims.Claim("name", "Player"),
                    new System.Security.Claims.Claim("nonce", Nonce),
                ]),
                IssuedAt = now.UtcDateTime,
                Expires = now.AddMinutes(5).UtcDateTime,
                SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
            });
            var payload = JsonSerializer.Serialize(new
            {
                access_token = "server-side-access-token",
                token_type = "Bearer",
                expires_in = 300,
                id_token = token,
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }

    public sealed class FakeGateway : IIdentityGateway
    {
        public static readonly Guid UserId = Guid.Parse("018f0000-0000-7000-8000-000000000001");
        public int LogoutCalls { get; private set; }
        public Task<GatewayResponse<GatewaySession>> LoginAsync(LoginInput input, CancellationToken cancellationToken) =>
            Task.FromResult(new GatewayResponse<GatewaySession>(200, new GatewaySession(
                "private-session-token", "csrf-token", DateTimeOffset.UtcNow.AddHours(1),
                new BffUser(UserId, "Player", "ru-RU", "Europe/Moscow", 1, 1))));
        public Task<GatewayResponse<string>> LogoutAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken)
        {
            LogoutCalls++;
            return Task.FromResult(new GatewayResponse<string>(204, null));
        }

        public Task<GatewayResponse<string>> RegisterAsync(RegistrationInput input, CancellationToken cancellationToken) => Throw<string>();
        public Task<GatewayResponse<string>> VerifyEmailAsync(string token, CancellationToken cancellationToken) => Throw<string>();
        public Task<GatewayResponse<string>> RequestResetAsync(string email, CancellationToken cancellationToken) => Throw<string>();
        public Task<GatewayResponse<string>> CompleteResetAsync(string token, string password, CancellationToken cancellationToken) => Throw<string>();
        public Task<GatewayResponse<GatewaySession>> RefreshAsync(string sessionToken, CancellationToken cancellationToken) => Throw<GatewaySession>();
        public Task<GatewayResponse<BffUser>> GetMeAsync(string sessionToken, CancellationToken cancellationToken) =>
            Task.FromResult(new GatewayResponse<BffUser>(200,
                new BffUser(UserId, "Player", "ru-RU", "Europe/Moscow", 1, 1)));
        public Task<GatewayResponse<BffUser>> UpdateProfileAsync(string sessionToken, string csrfToken, long version, ProfileInput input, CancellationToken cancellationToken) => Throw<BffUser>();
        public Task<GatewayResponse<IReadOnlyList<GatewayLoginSession>>> ListSessionsAsync(string sessionToken, CancellationToken cancellationToken) => Throw<IReadOnlyList<GatewayLoginSession>>();
        public Task<GatewayResponse<string>> RevokeSessionAsync(string sessionToken, string csrfToken, Guid sessionId, CancellationToken cancellationToken) => Throw<string>();
        public Task<GatewayResponse<string>> RevokeOtherSessionsAsync(string sessionToken, string csrfToken, CancellationToken cancellationToken) => Throw<string>();

        private static Task<GatewayResponse<T>> Throw<T>() => throw new NotSupportedException();
    }
}
