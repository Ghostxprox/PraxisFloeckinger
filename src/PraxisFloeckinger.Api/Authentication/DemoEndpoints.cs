#if DEBUG
using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using PraxisFloeckinger.Api.Tenancy;

namespace PraxisFloeckinger.Api.Authentication;

/// <summary>
/// Nur in DEBUG-Builds verfügbare Test-Endpoints um Authorization-Policies manuell zu verifizieren.
/// </summary>
public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/demo")
            .WithMetadata(new RequireTenantAttribute())
            .AddEndpointFilter<RequireTenantFilter>();

        group.MapGet("/patient-only", (System.Security.Claims.ClaimsPrincipal user) =>
            Results.Ok(new { message = "Patient-Bereich", role = user.FindFirstValue("role") }))
            .RequireAuthorization(RolePolicies.OnlyPatient);

        group.MapGet("/therapist-only", (System.Security.Claims.ClaimsPrincipal user) =>
            Results.Ok(new { message = "Therapeut-Bereich", role = user.FindFirstValue("role") }))
            .RequireAuthorization(RolePolicies.OnlyTherapist);

        group.MapGet("/clinical", (System.Security.Claims.ClaimsPrincipal user) =>
            Results.Ok(new { message = "Klinischer Bereich", role = user.FindFirstValue("role") }))
            .RequireAuthorization(RolePolicies.TherapyClinicalAccess);

        group.MapGet("/admin", (System.Security.Claims.ClaimsPrincipal user) =>
            Results.Ok(new { message = "Admin-Bereich", role = user.FindFirstValue("role") }))
            .RequireAuthorization(RolePolicies.AdminOrSupervisor);

        return app;
    }
}
#endif
