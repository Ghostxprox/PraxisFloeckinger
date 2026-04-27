using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PraxisFloeckinger.Core.Tenancy;

namespace PraxisFloeckinger.Api.Tenancy;

/// <summary>
/// Liest die Subdomain aus dem eingehenden Request, löst den Tenant auf und
/// speichert einen <see cref="TenantContext"/> in <c>HttpContext.Items["TenantContext"]</c>.
/// Requests ohne Subdomain passieren ohne Tenant (z.B. Marketing-Seiten, /healthz).
/// Requests mit unbekannter Subdomain erhalten 404 Problem Details.
/// </summary>
public sealed class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolverMiddleware> _logger;

    public TenantResolverMiddleware(RequestDelegate next, ILogger<TenantResolverMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolver resolver,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        var subdomain = ExtractSubdomain(context, configuration, env);

        if (subdomain is null)
        {
            await _next(context);
            return;
        }

        var tenantInfo = await resolver.ResolveAsync(subdomain, context.RequestAborted);

        if (tenantInfo is null)
        {
            _logger.LogInformation(
                "Kein aktiver Tenant für Subdomain '{Subdomain}' — 404", subdomain);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Tenant not found",
                Detail = $"No active tenant exists for subdomain '{subdomain}'.",
            });
            return;
        }

        context.Items["TenantContext"] = new TenantContext(
            tenantInfo.TenantId,
            tenantInfo.Subdomain,
            tenantInfo.ConnectionString);

        await _next(context);
    }

    private string? ExtractSubdomain(
        HttpContext context,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        // Dev-Override: X-Tenant-Subdomain Header (nur in Development)
        if (env.IsDevelopment()
            && context.Request.Headers.TryGetValue("X-Tenant-Subdomain", out var headerValue))
        {
            var header = headerValue.ToString().Trim();
            if (!string.IsNullOrEmpty(header))
                return header.ToLowerInvariant();
        }

        var host = context.Request.Host.Host.ToLowerInvariant();

        // Loopback-Adressen: niemals Subdomain-Extraktion
        if (host is "localhost" or "127.0.0.1" or "0.0.0.0" or "::1")
            return null;

        var rootDomains = configuration
            .GetSection("Tenancy:RootDomains")
            .Get<string[]>() ?? [];

        foreach (var rootDomain in rootDomains)
        {
            var normalizedRoot = rootDomain.TrimStart('.').ToLowerInvariant();

            // Root-Domain selbst oder www. davon → kein Tenant
            if (host == normalizedRoot || host == $"www.{normalizedRoot}")
                return null;

            var suffix = $".{normalizedRoot}";
            if (host.EndsWith(suffix, StringComparison.Ordinal))
            {
                // Erstes Segment vor dem Root-Domain-Suffix extrahieren
                var withoutSuffix = host[..^suffix.Length];
                return withoutSuffix.Split('.')[0];
            }
        }

        return null;
    }
}
