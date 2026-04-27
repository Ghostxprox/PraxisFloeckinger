using Microsoft.AspNetCore.Http;

namespace PraxisFloeckinger.Api.Tenancy;

/// <summary>
/// Endpoint-Filter der sicherstellt, dass die Middleware einen Tenant aufgelöst hat.
/// Gibt 400 zurück wenn <c>TenantContext</c> nicht in <c>HttpContext.Items</c> liegt.
/// </summary>
public sealed class RequireTenantFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Items.ContainsKey("TenantContext"))
        {
            return Results.Problem(
                title: "Tenant required for this endpoint",
                detail: "Provide a valid tenant subdomain via Host header or X-Tenant-Subdomain (dev only).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}
