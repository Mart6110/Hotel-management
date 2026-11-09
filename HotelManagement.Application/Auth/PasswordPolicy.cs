using HotelManagement.Domain;

namespace HotelManagement.Application.Auth;

/// <summary>Decides whether a new password is strong enough.</summary>
public interface IPasswordPolicy
{
    /// <summary>Throws a validation <see cref="DomainException"/> listing every problem with the password.</summary>
    void EnsureAcceptable(string password, string email, string? fullName = null);
}

/// <summary>
/// Follows NIST SP 800-63B: favour length over composition rules, and reject passwords that are
/// common, repetitive, sequential, or derived from the user's own details.
/// </summary>
public class PasswordPolicy : IPasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    // A small blocklist of the most common passwords and patterns. Comparison ignores case,
    // and a trailing run of digits/symbols ("password123!") is stripped before checking.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passw0rd", "p@ssword", "p@ssw0rd", "qwerty", "qwertyuiop", "asdfghjkl", "zxcvbnm",
        "letmein", "welcome", "admin", "administrator", "iloveyou", "monkey", "dragon", "sunshine",
        "princess", "football", "baseball", "superman", "batman", "trustno1", "master", "hello",
        "freedom", "whatever", "shadow", "michael", "jennifer", "starwars", "login", "changeme",
        "secret", "default", "guest", "abc", "abcdef", "qazwsx", "1q2w3e4r", "hotel", "booking",
        "summer", "winter", "spring", "autumn", "company", "test", "testing", "user", "root",
    };

    public void EnsureAcceptable(string password, string email, string? fullName = null)
    {
        password ??= "";
        var problems = new List<string>();

        if (password.Length < MinLength)
            problems.Add($"must be at least {MinLength} characters");
        if (password.Length > MaxLength)
            problems.Add($"must be at most {MaxLength} characters");
        if (password.Length > 0 && password.Distinct().Count() <= 2)
            problems.Add("is too repetitive");
        if (IsSequential(password))
            problems.Add("is a simple sequence");
        if (IsCommon(password))
            problems.Add("is too common");
        if (ContainsPersonalInfo(password, email, fullName))
            problems.Add("must not contain your email address or name");

        if (problems.Count > 0)
            throw DomainException.Validation($"Password {string.Join(", ", problems)}.");
    }

    private static bool IsCommon(string password)
    {
        var core = password.TrimEnd("0123456789!@#$%^&*?.-_ ".ToCharArray());
        return Common.Contains(password) || (core.Length > 0 && Common.Contains(core)) || core.Length == 0;
    }

    // Runs people type as "sequences", including the keyboard's 1…9 0 order and wrap-around.
    private static readonly string[] Sequences =
    [
        "0123456789012345678901234567890",
        "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz",
    ];

    /// <summary>"1234567890", "abcdefghij", "9876543210"…</summary>
    private static bool IsSequential(string password)
    {
        if (password.Length < 4) return false;
        var lower = password.ToLowerInvariant();
        var reversed = new string(lower.Reverse().ToArray());
        return Sequences.Any(s => s.Contains(lower, StringComparison.Ordinal) || s.Contains(reversed, StringComparison.Ordinal));
    }

    private static bool ContainsPersonalInfo(string password, string email, string? fullName)
    {
        var parts = new List<string>();
        var localPart = (email ?? "").Split('@')[0];
        parts.Add(localPart);
        parts.AddRange(localPart.Split('.', '_', '-', '+'));
        if (fullName is not null)
            parts.AddRange(fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return parts.Any(p => p.Length >= 4 && password.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Brute-force protection for staff accounts.</summary>
public class LoginSecurityOptions
{
    /// <summary>Failed logins in a row before the account is temporarily locked.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutMinutes);
}
