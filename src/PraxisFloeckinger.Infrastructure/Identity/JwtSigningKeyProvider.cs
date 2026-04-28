using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace PraxisFloeckinger.Infrastructure.Identity;

/// <summary>
/// Lädt oder generiert (nur Dev) das RSA-2048-Keypair für JWT RS256.
/// Singleton — Key wird einmalig beim ersten Zugriff geladen.
/// </summary>
public sealed class JwtSigningKeyProvider
{
    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _validationKey;

    public JwtSigningKeyProvider(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<JwtSigningKeyProvider> logger)
    {
        var keyPath = configuration["Jwt:PrivateKeyPath"]
            ?? throw new InvalidOperationException("Jwt:PrivateKeyPath ist nicht konfiguriert.");

        if (!File.Exists(keyPath))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    $"JWT-Private-Key nicht gefunden: '{keyPath}'. " +
                    "In Production muss der Key manuell bereitgestellt werden.");

            logger.LogInformation("Dev-JWT-Keypair wird generiert: {KeyPath}", keyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath) ?? ".");
            using var genRsa = RSA.Create(2048);
            File.WriteAllText(keyPath, genRsa.ExportRSAPrivateKeyPem());
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(keyPath));
        _signingKey = new RsaSecurityKey(rsa);

        // Für Validierung nur Public Key
        var rsaPub = RSA.Create();
        rsaPub.ImportSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo(), out _);
        _validationKey = new RsaSecurityKey(rsaPub);
    }

    /// <summary>Privater Key zum Signieren von Access-Tokens.</summary>
    public RsaSecurityKey SigningKey => _signingKey;

    /// <summary>Öffentlicher Key zur Token-Validierung (kein Private Key).</summary>
    public RsaSecurityKey ValidationKey => _validationKey;
}
