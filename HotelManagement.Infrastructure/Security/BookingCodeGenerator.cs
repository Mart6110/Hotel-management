using System.Security.Cryptography;
using HotelManagement.Application.Common;

namespace HotelManagement.Infrastructure.Security;

/// <summary>
/// Cryptographically random codes over Crockford's Base32 alphabet (no I, L, O, U),
/// so they're easy to read aloud and type: no 0/O or 1/I/L mix-ups.
/// </summary>
internal sealed class BookingCodeGenerator : IBookingCodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>8 symbols = 40 bits; uniqueness is also enforced by a database index.</summary>
    public string NewReference() => "BK-" + RandomString(8);

    /// <summary>12 symbols = 60 bits, grouped for readability: "7K3P-9QXM-2B4D".</summary>
    public string NewAccessCode()
    {
        var raw = RandomString(12);
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..]}";
    }

    private static string RandomString(int length) =>
        string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
}
