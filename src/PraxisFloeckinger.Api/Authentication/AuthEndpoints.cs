using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PraxisFloeckinger.Api.Tenancy;
using PraxisFloeckinger.Core.Cryptography;
using PraxisFloeckinger.Core.Identity;
using PraxisFloeckinger.Core.Tenancy;
using PraxisFloeckinger.Infrastructure.Persistence.Tenant;

namespace PraxisFloeckinger.Api.Authentication;

public static class AuthEndpoints
{
    // Pflicht-MFA-Rollen
    private static readonly HashSet<UserRole> MandatoryMfaRoles =
    [
        UserRole.Sekretaerin,
        UserRole.Therapeut,
        UserRole.TherapeutSupervisor,
        UserRole.PraxisAdmin,
    ];

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithMetadata(new RequireTenantAttribute())
            .AddEndpointFilter<RequireTenantFilter>();

        // ─── Login (zweistufig) ──────────────────────────────────────────────
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

            if (hasher.NeedsRehash(user.PasswordHash))
            {
                user.PasswordHash = hasher.Hash(req.Password);
                await tenantDb.SaveChangesAsync(ct);
            }

            // 2FA-Check
            if (user.TotpEnabled)
            {
                var mfaToken = jwtService.IssueAccessToken(user, tenantCtx, purpose: "mfa");
                return Results.Ok(new LoginResponse
                {
                    Status = "mfa_required",
                    MfaSessionToken = mfaToken,
                });
            }

            if (MandatoryMfaRoles.Contains(user.Role))
            {
                var mfaToken = jwtService.IssueAccessToken(user, tenantCtx, purpose: "mfa");
                return Results.Ok(new LoginResponse
                {
                    Status = "mfa_setup_required",
                    MfaSessionToken = mfaToken,
                });
            }

            // Patient (optional 2FA, noch nicht aktiviert) → direkt einloggen
            var accessToken = jwtService.IssueAccessToken(user, tenantCtx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var (rawRefresh, refreshEntity) = await refreshService.IssueAsync(
                user, req.RememberMe, ip, ua, ct);

            return Results.Ok(new LoginResponse
            {
                Status = "ok",
                AccessToken = accessToken,
                RefreshToken = rawRefresh,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.Add(jwtService.AccessTokenLifetime),
                RefreshTokenExpiresAt = refreshEntity.ExpiresAt,
            });
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

            await refreshService.RevokeAsync(req.RefreshToken, "rotation", ct);
            var accessToken = jwtService.IssueAccessToken(user, tenantCtx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var (rawRefresh, refreshEntity) = await refreshService.IssueAsync(
                user, rememberMe: false, ip, ua, ct);

            return Results.Ok(new LoginResponse
            {
                Status = "ok",
                AccessToken = accessToken,
                RefreshToken = rawRefresh,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.Add(jwtService.AccessTokenLifetime),
                RefreshTokenExpiresAt = refreshEntity.ExpiresAt,
            });
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
            var userId = Guid.Parse(ctx.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);

            if (!string.IsNullOrWhiteSpace(req?.RefreshToken))
                await refreshService.RevokeAsync(req.RefreshToken, "logout", ct);
            else
                await refreshService.RevokeAllForUserAsync(userId, "logout", ct);

            return Results.NoContent();
        })
        .RequireAuthorization(MfaPolicies.RequireFullAuth);

        // ─── MFA: Verify (TOTP-Code nach Login) ──────────────────────────────
        group.MapPost("/mfa/verify", async (
            MfaVerifyRequest req,
            HttpContext ctx,
            IJwtTokenService jwtService,
            CancellationToken ct) =>
        {
            var tenantCtx = (ITenantContext)ctx.Items["TenantContext"]!;
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var refreshService = ctx.RequestServices.GetRequiredService<IRefreshTokenService>();
            var totpService = ctx.RequestServices.GetRequiredService<ITotpService>();
            var encryptor = ctx.RequestServices.GetRequiredService<IFieldEncryptor>();

            var userId = Guid.Parse(ctx.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var user = await tenantDb.Users.FindAsync([userId], ct);
            if (user is null || !user.TotpEnabled || user.TotpSecret is null)
                return Results.Problem(title: "MFA not configured", statusCode: 400);

            var plaintextSecret = encryptor.Decrypt(user.TotpSecret);
            if (!totpService.VerifyCode(plaintextSecret, req.Code))
                return Results.Problem(title: "Invalid MFA code", statusCode: 401);

            var accessToken = jwtService.IssueAccessToken(user, tenantCtx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var (rawRefresh, refreshEntity) = await refreshService.IssueAsync(
                user, rememberMe: false, ip, ua, ct);

            return Results.Ok(new LoginResponse
            {
                Status = "ok",
                AccessToken = accessToken,
                RefreshToken = rawRefresh,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.Add(jwtService.AccessTokenLifetime),
                RefreshTokenExpiresAt = refreshEntity.ExpiresAt,
            });
        })
        .RequireAuthorization(MfaPolicies.RequireMfaSession);

        // ─── MFA: Recovery-Code einlösen ─────────────────────────────────────
        group.MapPost("/mfa/recover", async (
            MfaRecoverRequest req,
            HttpContext ctx,
            IJwtTokenService jwtService,
            CancellationToken ct) =>
        {
            var tenantCtx = (ITenantContext)ctx.Items["TenantContext"]!;
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var refreshService = ctx.RequestServices.GetRequiredService<IRefreshTokenService>();
            var recoveryService = ctx.RequestServices.GetRequiredService<ITwoFactorRecoveryService>();

            var userId = Guid.Parse(ctx.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var user = await tenantDb.Users.FindAsync([userId], ct);
            if (user is null)
                return Results.Problem(title: "Invalid credentials", statusCode: 401);

            if (!await recoveryService.ConsumeAsync(userId, req.RecoveryCode, ct))
                return Results.Problem(title: "Invalid or already used recovery code", statusCode: 401);

            // User muss beim nächsten Login neue Recovery-Codes generieren
            user.MustRotateRecoveryCodes = true;
            await tenantDb.SaveChangesAsync(ct);

            var accessToken = jwtService.IssueAccessToken(user, tenantCtx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var (rawRefresh, refreshEntity) = await refreshService.IssueAsync(
                user, rememberMe: false, ip, ua, ct);

            return Results.Ok(new LoginResponse
            {
                Status = "ok",
                AccessToken = accessToken,
                RefreshToken = rawRefresh,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.Add(jwtService.AccessTokenLifetime),
                RefreshTokenExpiresAt = refreshEntity.ExpiresAt,
            });
        })
        .RequireAuthorization(MfaPolicies.RequireMfaSession);

        // ─── MFA: Setup (QR-Code generieren) ─────────────────────────────────
        group.MapPost("/mfa/setup", async (
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantCtx = (ITenantContext)ctx.Items["TenantContext"]!;
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var totpService = ctx.RequestServices.GetRequiredService<ITotpService>();
            var encryptor = ctx.RequestServices.GetRequiredService<IFieldEncryptor>();

            var userId = Guid.Parse(ctx.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var user = await tenantDb.Users.FindAsync([userId], ct);
            if (user is null)
                return Results.Problem(title: "User not found", statusCode: 404);

            if (user.TotpEnabled)
                return Results.Problem(title: "MFA already enabled", statusCode: 400);

            // Secret generieren und verschlüsselt speichern (noch nicht aktiviert)
            var plainSecret = totpService.GenerateSecret();
            user.TotpSecret = encryptor.Encrypt(plainSecret);
            await tenantDb.SaveChangesAsync(ct);

            var otpauthUri = totpService.BuildOtpAuthUri(user.Email, tenantCtx.Subdomain, plainSecret);
            var qrCodeBase64 = totpService.GenerateQrCodePngBase64(otpauthUri);

            return Results.Ok(new
            {
                qrCodeBase64,
                otpauthUri,
                secret = plainSecret,
            });
        })
        .RequireAuthorization(MfaPolicies.RequireMfaSession);

        // ─── MFA: Setup bestätigen ────────────────────────────────────────────
        group.MapPost("/mfa/setup/confirm", async (
            MfaVerifyRequest req,
            HttpContext ctx,
            IJwtTokenService jwtService,
            CancellationToken ct) =>
        {
            var tenantCtx = (ITenantContext)ctx.Items["TenantContext"]!;
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var refreshService = ctx.RequestServices.GetRequiredService<IRefreshTokenService>();
            var totpService = ctx.RequestServices.GetRequiredService<ITotpService>();
            var encryptor = ctx.RequestServices.GetRequiredService<IFieldEncryptor>();
            var recoveryService = ctx.RequestServices.GetRequiredService<ITwoFactorRecoveryService>();

            var userId = Guid.Parse(ctx.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var user = await tenantDb.Users.FindAsync([userId], ct);
            if (user is null || user.TotpSecret is null)
                return Results.Problem(title: "MFA setup not initiated", statusCode: 400);

            if (user.TotpEnabled)
                return Results.Problem(title: "MFA already enabled", statusCode: 400);

            var plaintextSecret = encryptor.Decrypt(user.TotpSecret);
            if (!totpService.VerifyCode(plaintextSecret, req.Code))
                return Results.Problem(title: "Invalid MFA code", statusCode: 401);

            user.TotpEnabled = true;
            await tenantDb.SaveChangesAsync(ct);

            var recoveryCodes = await recoveryService.GenerateAsync(userId, count: 10, ct);

            var accessToken = jwtService.IssueAccessToken(user, tenantCtx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var (rawRefresh, refreshEntity) = await refreshService.IssueAsync(
                user, rememberMe: false, ip, ua, ct);

            return Results.Ok(new
            {
                status = "ok",
                recoveryCodes,
                accessToken,
                refreshToken = rawRefresh,
                accessTokenExpiresAt = DateTimeOffset.UtcNow.Add(jwtService.AccessTokenLifetime),
                refreshTokenExpiresAt = refreshEntity.ExpiresAt,
            });
        })
        .RequireAuthorization(MfaPolicies.RequireMfaSession);

        // ─── MFA: Status abfragen ─────────────────────────────────────────────
        group.MapGet("/mfa/status", async (
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var recoveryService = ctx.RequestServices.GetRequiredService<ITwoFactorRecoveryService>();

            var userId = Guid.Parse(ctx.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var user = await tenantDb.Users.FindAsync([userId], ct);
            if (user is null)
                return Results.Problem(title: "User not found", statusCode: 404);

            var remaining = user.TotpEnabled
                ? await recoveryService.CountRemainingAsync(userId, ct)
                : 0;

            return Results.Ok(new
            {
                enabled = user.TotpEnabled,
                mustRotateRecoveryCodes = user.MustRotateRecoveryCodes,
                recoveryCodesRemaining = remaining,
            });
        })
        .RequireAuthorization(MfaPolicies.RequireFullAuth);

        // ─── MFA: Deaktivieren (nur Patient) ─────────────────────────────────
        group.MapPost("/mfa/disable", async (
            MfaDisableRequest req,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantDb = ctx.RequestServices.GetRequiredService<TenantDbContext>();
            var hasher = ctx.RequestServices.GetRequiredService<IPasswordHasher>();
            var totpService = ctx.RequestServices.GetRequiredService<ITotpService>();
            var encryptor = ctx.RequestServices.GetRequiredService<IFieldEncryptor>();

            var userId = Guid.Parse(ctx.User.FindFirstValue(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!);
            var user = await tenantDb.Users.FindAsync([userId], ct);
            if (user is null)
                return Results.Problem(title: "User not found", statusCode: 404);

            if (MandatoryMfaRoles.Contains(user.Role))
                return Results.Problem(
                    title: "Cannot disable MFA for this role", statusCode: 403);

            if (!hasher.Verify(req.Password, user.PasswordHash))
                return Results.Problem(title: "Invalid credentials", statusCode: 401);

            if (user.TotpEnabled && user.TotpSecret is not null)
            {
                var plaintextSecret = encryptor.Decrypt(user.TotpSecret);
                if (!totpService.VerifyCode(plaintextSecret, req.Code))
                    return Results.Problem(title: "Invalid MFA code", statusCode: 401);
            }

            user.TotpEnabled = false;
            user.TotpSecret = null;

            // Recovery-Codes soft-deleten
            var codes = await tenantDb.TwoFactorRecoveryCodes
                .Where(c => c.UserId == userId)
                .ToListAsync(ct);
            foreach (var c in codes)
                c.IsDeleted = true;

            await tenantDb.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(MfaPolicies.RequireFullAuth);

        return app;
    }
}
