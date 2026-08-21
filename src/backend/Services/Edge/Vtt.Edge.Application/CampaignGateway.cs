namespace Vtt.Edge.Application;

public sealed record CampaignGatewayRequest(
    HttpMethod Method,
    string PathAndQuery,
    string? Content,
    string? ContentType,
    string? IfMatch);

public sealed record CampaignGatewayResponse(int StatusCode, string Content, string? ContentType);

public interface ICampaignGateway
{
    Task<CampaignGatewayResponse> SendAsync(
        Guid userId,
        CampaignGatewayRequest request,
        CancellationToken cancellationToken);
}
