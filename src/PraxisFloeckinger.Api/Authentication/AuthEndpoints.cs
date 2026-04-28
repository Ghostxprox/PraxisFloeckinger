using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PraxisFloeckinger.Api.Tenancy;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;

namespace PraxisFloeckinger.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithMetadata(new RequireTenantAttribute())
            .AddEndpointFilter<RequireTenantFilter>();

        // ─── Login ───────────────────────────────────────────────────────────
        group.MapPost("/login", async (
            LoginRequest req,
            HttpContext ctx,
            IPasswordHasher hasher,
            IJwtTokenService jwtService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.Problem(title: "Invalid credentials", statusCode: 401);

            var tenantCtx = (ITenantContext)ctx.Items["TenantContext"]!;
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var refreshService = ctx.RequestServices.GetRequiredService<IRefreshTokenService>();

            var user = await tenantDb.Users
                .FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant().Trim(), ct);

            if (user is null || !hasher.Verify(req.Password, user.PasswordHash))
                return Results.Problem(title: "Invalid credentials", statusCode: 401);

            // Rehash wenn Parameter veraltet
            if (hasher.NeedsRehash(user.PasswordHash))
            {
                user.PasswordHash = hasher.Hash(req.Password);
                await tenantDb.SaveChangesAsync(ct);
            }

            var accessToken = jwtService.IssueAccessToken(user, tenantCtx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var (rawRefresh, refreshEntity) = await refreshService.IssueAsync(
                user, req.RememberMe, ip, ua, ct);

            return Results.Ok(new LoginResponse(
                accessToken,
                rawRefresh,
                DateTimeOffset.UtcNow.Add(jwtService.AccessTokenLifetime),
                refreshEntity.ExpiresAt));
        })
        .RequireRateLimiting("login");

        // ─── Refresh ─────────────────────────────────────────────────────────
        group.MapPost("/refresh", async (
            RefreshRequest req,
            HttpContext ctx,
            IJwtTokenService jwtService,
            CancellationToken ct) =>
        {
            var tenantCtx = (ITenantContext)ctx.Items["TenantContext"]!;
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var refreshService = ctx.RequestServices.GetRequiredService<IRefreshTokenService>();

            var existing = await refreshService.ValidateAsync(req.RefreshToken, ct);
            if (existing is null)
                return Results.Problem(
                    title: "Invalid or expired refresh token", statusCode: 401);

            var user = await tenantDb.Users.FindAsync([existing.UserId], ct);
            if (user is null || user.IsDeleted)
            {
                await refreshService.RevokeAllForUserAsync(existing.UserId, "user-deleted", ct);
                return Results.Problem(title: "Invalid credentials", statusCode: 401);
            }

            // Token-Rotation: alten widerrufen, neuen ausgeben
            await refreshService.RevokeAsync(req.RefreshToken, "rotation", ct);
            var accessToken = jwtService.IssueAccessToken(user, tenantCtx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var (rawRefresh, refreshEntity) = await refreshService.IssueAsync(
                user, rememberMe: false, ip, ua, ct);

            return Results.Ok(new LoginResponse(
                accessToken,
                rawRefresh,
                DateTimeOffset.UtcNow.Add(jwtService.AccessTokenLifetime),
                refreshEntity.ExpiresAt));
        })
        .RequireRateLimiting("refresh");

        // ─── Logout ──────────────────────────────────────────────────────────
        group.MapPost("/logout", async (
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
            LogoutRequest? req,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var refreshService = ctx.RequestServices.GetRequiredService<IRefreshTokenService>();

            var userId = Guid.Parse(ctx.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);

            if (!string.IsNullOrWhiteSpace(req?.RefreshToken))
                await refreshService.RevokeAsync(req.RefreshToken, "logout", ct);
            else
                await refreshService.RevokeAllForUserAsync(userId, "logout", ct);

            return Results.NoContent();
        })
        .RequireAuthorization();

        return app;
    }
}
