using System.Security.Cryptography;
using HotelManagement.Application.Common;
using HotelManagement.Infrastructure.Email;
using HotelManagement.Infrastructure.Persistence;
using HotelManagement.Infrastructure.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace HotelManagement.UnitTests.Infrastructure;

public class FieldEncryptionTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmFieldEncryptor Encryptor(string keyId, string key, Dictionary<string, string>? retired = null) =>
        new(Options.Create(new EncryptionOptions { KeyId = keyId, Key = key, BlindIndexKey = NewKey(), RetiredKeys = retired ?? [] }));

    [Fact]
    public void Round_trips_and_never_stores_plaintext()
    {
        var encryptor = Encryptor("k1", NewKey());

        var stored = encryptor.Encrypt("Anna Jensen");

        Assert.StartsWith("enc:k1:", stored);
        Assert.DoesNotContain("Anna", stored);
        Assert.Equal("Anna Jensen", encryptor.Decrypt(stored));
    }

    [Fact]
    public void Same_value_encrypts_differently_each_time()
    {
        var encryptor = Encryptor("k1", NewKey());
        Assert.NotEqual(encryptor.Encrypt("same"), encryptor.Encrypt("same")); // random nonce: no pattern leaks
    }

    [Fact]
    public void Tampering_is_detected()
    {
        var encryptor = Encryptor("k1", NewKey());
        var stored = encryptor.Encrypt("Anna");
        var bytes = Convert.FromBase64String(stored["enc:k1:".Length..]);
        bytes[^1] ^= 0x01;
        var tampered = "enc:k1:" + Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(tampered));
    }

    [Fact]
    public void Old_data_stays_readable_after_key_rotation()
    {
        var oldKey = NewKey();
        var written = Encryptor("k1", oldKey).Encrypt("before rotation");

        var rotated = Encryptor("k2", NewKey(), retired: new() { ["k1"] = oldKey });

        Assert.Equal("before rotation", rotated.Decrypt(written));
        Assert.StartsWith("enc:k2:", rotated.Encrypt("after rotation"));
    }

    [Fact]
    public void Legacy_plaintext_passes_through_until_backfilled()
    {
        Assert.Equal("plain old value", Encryptor("k1", NewKey()).Decrypt("plain old value"));
    }

    [Fact]
    public void Unknown_key_id_fails_loudly()
    {
        var written = Encryptor("k1", NewKey()).Encrypt("x");
        Assert.Throws<CryptographicException>(() => Encryptor("k9", NewKey()).Decrypt(written));
    }

    [Fact]
    public void Blind_index_is_deterministic_case_insensitive_and_keyed()
    {
        var key = NewKey();
        var index = new HmacBlindIndex(Options.Create(new EncryptionOptions { BlindIndexKey = key }));
        var otherKey = new HmacBlindIndex(Options.Create(new EncryptionOptions { BlindIndexKey = NewKey() }));

        Assert.Equal(index.Compute("Gina@Example.com"), index.Compute(" gina@example.com "));
        Assert.NotEqual(index.Compute("gina@example.com"), index.Compute("gino@example.com"));
        Assert.NotEqual(index.Compute("gina@example.com"), otherKey.Compute("gina@example.com"));
        Assert.Equal(64, index.Compute("x").Length);
    }
}

public class PasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Hashes_are_salted_and_verifiable()
    {
        var first = _hasher.Hash("Correct-Horse-Battery");
        var second = _hasher.Hash("Correct-Horse-Battery");

        Assert.NotEqual(first, second); // random salt per hash
        Assert.Equal(PasswordCheck.Success, _hasher.Verify(first, "Correct-Horse-Battery"));
        Assert.Equal(PasswordCheck.Failed, _hasher.Verify(first, "correct-horse-battery"));
    }

    [Fact]
    public void Uses_pbkdf2_sha512_with_100k_iterations_and_128_bit_salt()
    {
        var bytes = Convert.FromBase64String(_hasher.Hash("anything-at-all"));
        static uint ReadUInt32(byte[] b, int offset) => (uint)(b[offset] << 24 | b[offset + 1] << 16 | b[offset + 2] << 8 | b[offset + 3]);

        Assert.Equal(0x01, bytes[0]);                    // Identity v3 format
        Assert.Equal(2u, ReadUInt32(bytes, 1));          // HMAC-SHA512
        Assert.Equal(100_000u, ReadUInt32(bytes, 5));    // iterations
        Assert.Equal(16u, ReadUInt32(bytes, 9));         // salt bytes
    }

    [Fact]
    public void Missing_hash_fails_after_doing_the_same_work()
    {
        Assert.Equal(PasswordCheck.Failed, _hasher.Verify(null, "anything"));
    }

    [Fact]
    public void Refresh_tokens_are_random_and_hashed_with_sha256()
    {
        var generator = new SecureTokenGenerator();
        var token = generator.NewToken();

        Assert.NotEqual(token, generator.NewToken());
        Assert.True(token.Length >= 43); // 256 bits
        Assert.Equal(64, generator.Hash(token).Length);
        Assert.Equal(generator.Hash(token), generator.Hash(token));
    }
}

public class ProductionSafetyTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    [Fact]
    public void Dev_jwt_key_is_rejected_outside_development()
    {
        var options = new JwtOptions { Key = "dev-only-signing-key-change-me-0123456789abcdef" };

        Assert.True(new JwtProductionValidator(Env("Production")).Validate(null, options).Failed);
        Assert.True(new JwtProductionValidator(Env("Development")).Validate(null, options).Succeeded);
        Assert.True(new JwtProductionValidator(Env("Production")).Validate(null, new JwtOptions { Key = new string('k', 48) }).Succeeded);
    }

    [Fact]
    public void Demo_seeding_is_rejected_outside_development()
    {
        Assert.True(new DatabaseProductionValidator(Env("Staging")).Validate(null, new DatabaseOptions { SeedSampleData = true }).Failed);
        Assert.True(new DatabaseProductionValidator(Env("Staging")).Validate(null, new DatabaseOptions { SeedSampleData = false }).Succeeded);
    }

    [Fact]
    public void Encryption_keys_must_be_valid_distinct_and_not_public()
    {
        var real = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var other = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var validator = new EncryptionOptionsValidator(Env("Production"));

        Assert.True(validator.Validate(null, new EncryptionOptions { KeyId = "k1", Key = real, BlindIndexKey = other }).Succeeded);
        Assert.True(validator.Validate(null, new EncryptionOptions { KeyId = "k1", Key = "too-short", BlindIndexKey = other }).Failed);
        Assert.True(validator.Validate(null, new EncryptionOptions { KeyId = "k1", Key = real, BlindIndexKey = real }).Failed);
        Assert.True(validator.Validate(null, new EncryptionOptions
        {
            KeyId = "dev", Key = "ZGV2LW9ubHktZW5jcnlwdGlvbi1rZXktMzJieXRlcyE=", BlindIndexKey = other,
        }).Failed);
    }
}

public class EmailLoggingTests
{
    [Theory]
    [InlineData("gina.guest@example.com", "g***@example.com")]
    [InlineData("a@b.c", "a***@b.c")]
    [InlineData("not-an-email", "***")]
    public void Recipient_addresses_are_masked_in_logs(string address, string expected) =>
        Assert.Equal(expected, LoggingEmailSender.MaskAddress(address));
}
