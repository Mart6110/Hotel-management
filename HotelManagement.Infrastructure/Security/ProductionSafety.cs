using HotelManagement.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HotelManagement.Infrastructure.Security;

/// <summary>
/// Development-only secrets that ship in this repository (appsettings.Development.json,
/// docker-compose.yml defaults, .env.example). Anyone can read them, so they must never protect real data.
/// </summary>
public static class KnownDevelopmentSecrets
{
    public static readonly HashSet<string> JwtKeys =
    [
        "dev-only-signing-key-change-me-0123456789abcdef",
        "replace-with-a-long-random-secret-at-least-32-chars",
        "integration-test-signing-key-0123456789abcdef",
    ];

    public static readonly HashSet<string> EncryptionKeys =
    [
        "ZGV2LW9ubHktZW5jcnlwdGlvbi1rZXktMzJieXRlcyE=", // "dev-only-encryption-key-32bytes!"
        "ZGV2LW9ubHktYmxpbmQtaW5kZXgta2V5LTMyYnl0ZXM=", // "dev-only-blind-index-key-32bytes"
    ];
}

/// <summary>Outside Development, refuse to start with a JWT signing key that's published in the repository.</summary>
internal sealed class JwtProductionValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options) =>
        !environment.IsDevelopment() && KnownDevelopmentSecrets.JwtKeys.Contains(options.Key)
            ? ValidateOptionsResult.Fail(
                $"Jwt:Key is a public development key, which is not allowed in the '{environment.EnvironmentName}' environment. " +
                "Set JWT_KEY (or Jwt__Key) to a long random secret.")
            : ValidateOptionsResult.Success;
}

/// <summary>Outside Development, refuse to seed demo accounts with a published password.</summary>
internal sealed class DatabaseProductionValidator(IHostEnvironment environment) : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options) =>
        !environment.IsDevelopment() && options.SeedSampleData
            ? ValidateOptionsResult.Fail(
                $"Database:SeedSampleData would create demo accounts with a public password in the '{environment.EnvironmentName}' environment. " +
                "Set SEED_SAMPLE_DATA=false.")
            : ValidateOptionsResult.Success;
}

/// <summary>Validates encryption keys: well-formed everywhere, and not the published dev keys outside Development.</summary>
internal sealed class EncryptionOptionsValidator(IHostEnvironment environment) : IValidateOptions<EncryptionOptions>
{
    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        var failures = new List<string>();
        void Check(string key, string setting)
        {
            try
            {
                AesGcmFieldEncryptor.DecodeKey(key, setting);
            }
            catch (InvalidOperationException e)
            {
                failures.Add(e.Message);
                return;
            }
            if (!environment.IsDevelopment() && KnownDevelopmentSecrets.EncryptionKeys.Contains(key))
                failures.Add($"{setting} is a public development key, which is not allowed in the '{environment.EnvironmentName}' environment.");
        }

        Check(options.Key, "Encryption:Key");
        Check(options.BlindIndexKey, "Encryption:BlindIndexKey");
        foreach (var (id, key) in options.RetiredKeys)
            Check(key, $"Encryption:RetiredKeys:{id}");
        if (options.Key == options.BlindIndexKey)
            failures.Add("Encryption:Key and Encryption:BlindIndexKey must be different keys.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
