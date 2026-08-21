using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Vtt.Identity.Api;

namespace Vtt.Identity.IntegrationTests;

public sealed class ApiHealthSmokeTests(
    WebApplicationFactory<ApiAssemblyMarker> factory)
    : IClassFixture<WebApplicationFactory<ApiAssemblyMarker>>
{
    [Fact]
    public async Task LiveHealthEndpointReturnsSuccess()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OidcDiscoveryAdvertisesAuthorizationCodeWithPkce()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        Assert.Contains("code", root.GetProperty("response_types_supported").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("S256", root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(item => item.GetString()));
        Assert.EndsWith("/connect/token", root.GetProperty("token_endpoint").GetString(), StringComparison.Ordinal);
    }
}
