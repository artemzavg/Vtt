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
}
