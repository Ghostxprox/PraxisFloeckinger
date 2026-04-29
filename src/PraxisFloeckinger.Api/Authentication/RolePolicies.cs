using Microsoft.AspNetCore.Authorization;
using PraxisFloeckinger.Core.Identity;

namespace PraxisFloeckinger.Api.Authentication;

public static class RolePolicies
{
    public const string OnlyPatient           = "OnlyPatient";
    public const string OnlyTherapist         = "OnlyTherapist";
    public const string OnlySupervisor        = "OnlySupervisor";
    public const string OnlySekretariat       = "OnlySekretariat";
    public const string OnlyPraxisAdmin       = "OnlyPraxisAdmin";
    public const string StaffOnly             = "StaffOnly";
    public const string TherapyClinicalAccess = "TherapyClinicalAccess";
    public const string AdminOrSupervisor     = "AdminOrSupervisor";

    private static string R(UserRole role) => role.ToString();

    public static AuthorizationOptions AddRolePolicies(this AuthorizationOptions options)
    {
        // Alle Policies erfordern einen vollständigen Access-Token (purpose=access).
        options.AddPolicy(OnlyPatient, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireClaim("role", R(UserRole.Patient)));

        options.AddPolicy(OnlyTherapist, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireClaim("role", R(UserRole.Therapeut)));

        options.AddPolicy(OnlySupervisor, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireClaim("role", R(UserRole.TherapeutSupervisor)));

        options.AddPolicy(OnlySekretariat, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireClaim("role", R(UserRole.Sekretaerin)));

        options.AddPolicy(OnlyPraxisAdmin, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireClaim("role", R(UserRole.PraxisAdmin)));

        options.AddPolicy(StaffOnly, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireAssertion(ctx =>
                ctx.User.HasClaim("role", R(UserRole.Sekretaerin))
             || ctx.User.HasClaim("role", R(UserRole.Therapeut))
             || ctx.User.HasClaim("role", R(UserRole.TherapeutSupervisor))
             || ctx.User.HasClaim("role", R(UserRole.PraxisAdmin))));

        options.AddPolicy(TherapyClinicalAccess, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireAssertion(ctx =>
                ctx.User.HasClaim("role", R(UserRole.Therapeut))
             || ctx.User.HasClaim("role", R(UserRole.TherapeutSupervisor))));

        options.AddPolicy(AdminOrSupervisor, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("purpose", "access")
            .RequireAssertion(ctx =>
                ctx.User.HasClaim("role", R(UserRole.PraxisAdmin))
             || ctx.User.HasClaim("role", R(UserRole.TherapeutSupervisor))));

        return options;
    }
}
