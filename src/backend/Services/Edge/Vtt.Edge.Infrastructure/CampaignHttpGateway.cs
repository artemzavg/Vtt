using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vtt.Edge.Application;

namespace Vtt.Edge.Infrastructure;

public sealed class CampaignHttpGateway(
    HttpClient client,
    IConfiguration configuration) : ICampaignGateway
{
    public async Task<CampaignGatewayResponse> SendAsync(
        Guid userId,
        CampaignGatewayRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request.Method, request.PathAndQuery);
        message.Headers.TryAddWithoutValidation("X-Vtt-User-Id", userId.ToString("D"));
        message.Headers.TryAddWithoutValidation(
            "X-Vtt-Internal-Key",
            configuration["Campaign:InternalApiKey"] ?? "local-campaign-internal-key-change-me");
        if (!string.IsNullOrWhiteSpace(request.IfMatch))
        {
            message.Headers.TryAddWithoutValidation("If-Match", request.IfMatch);
        }

        if (request.Content is not null)
        {
            message.Content = new StringContent(request.Content);
            if (MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType))
            {
                message.Content.Headers.ContentType = contentType;
            }
        }

        using var response = await client.SendAsync(message, cancellationToken);
        var content = response.Content.Headers.ContentLength == 0
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        return new CampaignGatewayResponse(
            (int)response.StatusCode,
            content,
            response.Content.Headers.ContentType?.ToString());
    }
}
