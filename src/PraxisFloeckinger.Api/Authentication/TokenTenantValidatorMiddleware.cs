using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Api.Authentication;

/// <summary>
/// Prüft nach UseAuthentication ob ein authentifizierter User seinen Token
/// bei einem anderen Tenant als dem aktuellen einlösen will.
///
/// Schutz gegen: Token der für tenant-A ausgestellt wurde,
/// wird gegen tenant-B genutzt → 403.
/// </summary>
public sealed class TokenTenantValidatorMiddleware
{
    private readonly RequestDelegate _next;

    public TokenTenantValidatorMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tokenTenantId = context.User.FindFirstValue("tenant_id");
            if (tokenTenantId is not null
                && context.Items["TenantContext"] is ITenantContext tenantContext
                && !string.Equals(tokenTenantId, tenantContext.TenantId.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                    title = "Token issued for different tenant",
                    status = 403,
                });
                return;
            }
        }

        await _next(context);
    }
}
