using Microsoft.AspNetCore.Mvc.Testing;
using Vtt.Search.Api;

namespace Vtt.Search.IntegrationTests;

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
