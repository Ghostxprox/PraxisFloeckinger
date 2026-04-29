using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace PraxisFloeckinger.Api.Authentication;

public static class MfaPolicies
{
    public const string RequireMfaSession = "RequireMfaSession";
    public const string RequireFullAuth = "RequireFullAuth";

    public static AuthorizationOptions AddMfaPolicies(this AuthorizationOptions options)
    {
        // MFA-Session-Token: purpose=mfa Claim erforderlich
        options.AddPolicy(RequireMfaSession, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireClaim("purpose", "mfa"));

        // Normaler Access-Token: purpose=access oder kein purpose-Claim (Legacy-Kompatibilität)
        options.AddPolicy(RequireFullAuth, policy =>
            policy.RequireAuthenticatedUser()
                  .RequireAssertion(ctx =>
                      !ctx.User.HasClaim(c => c.Type == "purpose") ||
                      ctx.User.HasClaim("purpose", "access")));

        return options;
    }
}
