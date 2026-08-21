using Vtt.Edge.Application;

namespace Vtt.Edge.Api;

internal static class CampaignBffEndpoints
{
    private static readonly string[] Methods =
    [
        HttpMethods.Get,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    ];

    public static IEndpointRouteBuilder MapCampaignBff(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/v1/campaigns", Methods, ForwardCampaignAsync);
        endpoints.MapMethods("/v1/campaigns/{**rest}", Methods, ForwardCampaignAsync);
        endpoints.MapPost("/v1/invitations:accept", ForwardInvitationAsync);
        return endpoints;
    }

    private static Task<IResult> ForwardCampaignAsync(
        HttpContext context,
        IIdentityGateway identity,
        ICampaignGateway campaign,
        CancellationToken cancellationToken) =>
        ForwardAsync("/api" + context.Request.Path + context.Request.QueryString, context, identity, campaign, cancellationToken);

    private static Task<IResult> ForwardInvitationAsync(
        HttpContext context,
        IIdentityGateway identity,
        ICampaignGateway campaign,
        CancellationToken cancellationToken) =>
        ForwardAsync("/api/v1/invitations:accept", context, identity, campaign, cancellationToken);

    private static async Task<IResult> ForwardAsync(
        string target,
        HttpContext context,
        IIdentityGateway identity,
        ICampaignGateway campaign,
        CancellationToken cancellationToken)
    {
        var session = context.Request.Cookies[IdentityBffEndpoints.SessionCookie];
        if (string.IsNullOrWhiteSpace(session))
        {
            return Problem(StatusCodes.Status401Unauthorized, "edge.session_missing");
        }

        if (!HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method) &&
            !IdentityBffEndpoints.HasValidCsrf(context))
        {
            return Problem(StatusCodes.Status403Forbidden, "edge.csrf_invalid");
        }

        var principal = await identity.GetMeAsync(session, cancellationToken);
        if (!principal.IsSuccess || principal.Value is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "edge.session_invalid");
        }

        string? body = null;
        if (context.Request.ContentLength is > 0)
        {
            using var reader = new StreamReader(context.Request.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var response = await campaign.SendAsync(
            principal.Value.UserId,
            new CampaignGatewayRequest(
                new HttpMethod(context.Request.Method),
                target,
                body,
                context.Request.ContentType,
                context.Request.Headers.IfMatch.ToString()),
            cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return response.StatusCode == StatusCodes.Status204NoContent
            ? Results.NoContent()
            : Results.Content(response.Content, response.ContentType ?? "application/problem+json", statusCode: response.StatusCode);
    }

    private static IResult Problem(int status, string code) =>
        Results.Problem(statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
