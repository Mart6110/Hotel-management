using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HotelManagement.Infrastructure.Security;

/// <summary>
/// Keys for encrypting personal data at rest. Each key is 32 random bytes, base64-encoded
/// (generate with: <c>openssl rand -base64 32</c>).
/// </summary>
public class EncryptionOptions
{
    /// <summary>Identifier stored with each ciphertext, so old data stays readable after rotating to a new key.</summary>
    [Required, RegularExpression("^[A-Za-z0-9_-]{1,16}$")]
    public string KeyId { get; set; } = "";

    /// <summary>The active key: used to encrypt new data.</summary>
    [Required] public string Key { get; set; } = "";

    /// <summary>Earlier keys by id, still needed to decrypt data written before a rotation.</summary>
    public Dictionary<string, string> RetiredKeys { get; set; } = [];

    /// <summary>Separate key for the searchable HMAC "blind index" of encrypted emails.</summary>
    [Required] public string BlindIndexKey { get; set; } = "";
}

/// <summary>Encrypts individual database values.</summary>
public interface IFieldEncryptor
{
    string Encrypt(string plaintext);

    /// <summary>Decrypts a value written by <see cref="Encrypt"/>. Values without the prefix are legacy plaintext and returned as-is.</summary>
    string Decrypt(string stored);

    bool IsEncrypted(string? stored);
}

/// <summary>
/// AES-256-GCM (authenticated: tampering is detected) with a fresh random 96-bit nonce per value.
/// Stored format: <c>enc:{keyId}:{base64(nonce | tag | ciphertext)}</c>.
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const string Prefix = "enc:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _activeKeyId;
    private readonly Dictionary<string, byte[]> _keys;

    public AesGcmFieldEncryptor(IOptions<EncryptionOptions> options)
    {
        var o = options.Value;
        _activeKeyId = o.KeyId;
        _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [o.KeyId] = DecodeKey(o.Key, "Encryption:Key") };
        foreach (var (id, key) in o.RetiredKeys)
            _keys.TryAdd(id, DecodeKey(key, $"Encryption:RetiredKeys:{id}"));
    }

    public bool IsEncrypted(string? stored) => stored?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + TagSize + plain.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_keys[_activeKeyId], TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(_activeKeyId));
        return $"{Prefix}{_activeKeyId}:{Convert.ToBase64String(payload)}";
    }

    public string Decrypt(string stored)
    {
        if (!IsEncrypted(stored))
            return stored; // written before encryption was enabled; re-encrypted by the startup backfill

        var separator = stored.IndexOf(':', Prefix.Length);
        var keyId = stored[Prefix.Length..separator];
        if (!_keys.TryGetValue(keyId, out var key))
            throw new CryptographicException($"No decryption key configured for key id '{keyId}'.");

        var payload = Convert.FromBase64String(stored[(separator + 1)..]);
        var plain = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plain, AssociatedData(keyId));
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Binds the key id into the authentication tag, so it can't be swapped undetected.</summary>
    private static byte[] AssociatedData(string keyId) => Encoding.UTF8.GetBytes(keyId);

    internal static byte[] DecodeKey(string base64, string name)
    {
        byte[] key;
        try { key = Convert.FromBase64String(base64); }
        catch (FormatException) { throw new InvalidOperationException($"{name} must be base64."); }
        return key.Length == 32 ? key : throw new InvalidOperationException($"{name} must be 32 bytes (256 bits).");
    }
}

/// <summary>Deterministic keyed hash of a value, so encrypted columns can still be matched exactly.</summary>
public interface IBlindIndex
{
    string Compute(string value);
}

/// <summary>HMAC-SHA256 over the lower-cased, trimmed value. Without the key, the index can't be reversed or brute-forced offline.</summary>
public sealed class HmacBlindIndex(IOptions<EncryptionOptions> options) : IBlindIndex
{
    private readonly byte[] _key = AesGcmFieldEncryptor.DecodeKey(options.Value.BlindIndexKey, "Encryption:BlindIndexKey");

    public string Compute(string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())));
}
