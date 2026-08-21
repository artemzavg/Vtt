using Microsoft.AspNetCore.Diagnostics;
using Vtt.Campaign.Application;
using Vtt.Campaign.Domain;

namespace Vtt.Campaign.Api;

internal sealed class CampaignExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            CampaignNotFoundException => (StatusCodes.Status404NotFound, "campaign.not_found"),
            CampaignForbiddenException => (StatusCodes.Status403Forbidden, "campaign.forbidden"),
            CampaignInvalidInvitationException => (StatusCodes.Status400BadRequest, "campaign.invitation_invalid"),
            CampaignConcurrencyException => (StatusCodes.Status409Conflict, "campaign.version_conflict"),
            CampaignStoreConcurrencyException => (StatusCodes.Status409Conflict, "campaign.version_conflict"),
            CampaignDomainException domain => (StatusCodes.Status400BadRequest, domain.Code),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "campaign.malformed_request"),
            _ => (StatusCodes.Status500InternalServerError, "campaign.internal_error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            return false;
        }

        context.Response.StatusCode = status;
        await Results.Problem(statusCode: status, title: code,
            extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);
        return true;
    }
}
