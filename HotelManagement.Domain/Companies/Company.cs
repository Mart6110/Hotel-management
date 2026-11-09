using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HotelManagement.Domain;

/// <summary>A tenant: a hotel company using the system.</summary>
public class Company : Entity
{
    private Company() { }

    public Company(string name, string slug)
    {
        Rename(name);
        ChangeSlug(slug);
    }

    public string Name { get; private set; } = "";

    /// <summary>URL-friendly unique identifier used by the public booking API, e.g. "demo-hotels-group".</summary>
    public string Slug { get; private set; } = "";

    public DateTime CreatedAt { get; private init; } = DateTime.UtcNow;

    public void Rename(string name) => Name = Guard.Required(name, "Company name", 200);

    public void ChangeSlug(string slug) => Slug = CompanySlug.Validate(slug);
}

public static partial class CompanySlug
{
    public const int MinLength = 3;
    public const int MaxLength = 60;
    private const int MaxGeneratedLength = 50; // leaves room for a "-2" style suffix

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();

    /// <summary>Normalizes and validates a slug, or throws a validation <see cref="DomainException"/>.</summary>
    public static string Validate(string? slug)
    {
        var normalized = (slug ?? "").Trim().ToLowerInvariant();
        if (normalized.Length is < MinLength or > MaxLength || !Pattern().IsMatch(normalized))
            throw DomainException.Validation(
                $"Slug must be {MinLength}-{MaxLength} characters of lowercase letters, digits and single hyphens.");
        return normalized;
    }

    /// <summary>Builds a slug from a company name: "Hôtel Du Lac & Spa" becomes "hotel-du-lac-spa".</summary>
    public static string FromName(string name)
    {
        var builder = new StringBuilder();
        var pendingHyphen = false;
        foreach (var ch in (name ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // strip accents
            var c = char.ToLowerInvariant(ch);
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                builder.Append(c);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.ToString();
        if (slug.Length > MaxGeneratedLength)
            slug = slug[..MaxGeneratedLength].TrimEnd('-');
        if (slug.Length < MinLength)
            slug = slug.Length == 0 ? "company" : $"{slug}-company";
        return slug;
    }

    /// <summary>"acme" + 2 = "acme-2". Used to make a generated slug unique.</summary>
    public static string WithSuffix(string slug, int number) => $"{slug}-{number}";
}
