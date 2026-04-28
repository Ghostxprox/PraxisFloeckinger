using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PraxisFloeckinger.Infrastructure.Identity;

namespace PraxisFloeckinger.Api.Authentication;

public static class JwtBearerConfiguration
{
    /// <summary>
    /// Konfiguriert JWT-Bearer-Validierung mit dem öffentlichen RSA-Key
    /// aus <see cref="JwtSigningKeyProvider"/>.
    /// ValidateIssuer + Audience + Lifetime = true, ClockSkew = 30s.
    /// </summary>
    public static AuthenticationBuilder AddPraxisJwtBearer(this AuthenticationBuilder builder)
    {
        builder.AddJwtBearer();

        // IOptions<JwtBearerOptions> wird nach DI-Aufbau konfiguriert,
        // damit JwtSigningKeyProvider (Singleton, Key-Datei) verfügbar ist.
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtSigningKeyProvider, IConfiguration>(
                (opts, keyProvider, config) =>
                {
                    opts.MapInboundClaims = false;
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = config["Jwt:Issuer"],
                        ValidateAudience = true,
                        ValidAudience = config["Jwt:Audience"],
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        IssuerSigningKey = keyProvider.ValidationKey,
                        ValidateIssuerSigningKey = true,
                        // "role"-Claim statt ClaimTypes.Role
                        RoleClaimType = "role",
                    };
                });

        return builder;
    }
}
