namespace HotelManagement.Domain;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized
}

/// <summary>An expected failure: a broken business rule, a missing record, bad input.</summary>
public class DomainException(ErrorKind kind, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;

    public static DomainException Validation(string message) => new(ErrorKind.Validation, message);
    public static DomainException Conflict(string message) => new(ErrorKind.Conflict, message);
    public static DomainException Unauthorized(string message) => new(ErrorKind.Unauthorized, message);
    public static DomainException NotFound(string entity, object? id) =>
        new(ErrorKind.NotFound, $"{entity} '{id}' was not found.");
}

public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}

/// <summary>Marks an entity owned by exactly one company (tenant).</summary>
public interface ITenantEntity
{
    Guid CompanyId { get; }

    /// <summary>Sets the owning company once. Reassigning to a different company is not allowed.</summary>
    void AssignCompany(Guid companyId);
}

public abstract class TenantEntity : Entity, ITenantEntity
{
    public Guid CompanyId { get; private set; }

    public void AssignCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company id cannot be empty.", nameof(companyId));
        if (CompanyId != Guid.Empty && CompanyId != companyId)
            throw new InvalidOperationException("An entity cannot be moved to another company.");
        CompanyId = companyId;
    }

    /// <summary>Inherit the tenant of a parent entity, if it has one yet.</summary>
    protected void InheritCompany(ITenantEntity parent)
    {
        if (parent.CompanyId != Guid.Empty)
            AssignCompany(parent.CompanyId);
    }
}

internal static class Money
{
    /// <summary>Culture-independent money text for messages ("1234.50"), regardless of server locale.</summary>
    public static string Format(decimal amount) => amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class Guard
{
    public static string Required(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw DomainException.Validation($"{name} is required.");
        if (trimmed.Length > maxLength)
            throw DomainException.Validation($"{name} must be at most {maxLength} characters.");
        return trimmed;
    }

    public static string? Optional(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > maxLength)
            throw DomainException.Validation($"{name} must be at most {maxLength} characters.");
        return trimmed;
    }

    public static string? OptionalEmail(string? value, string name) =>
        Optional(value, name, 256)?.ToLowerInvariant();

    public static int InRange(int value, string name, int min, int max) =>
        value < min || value > max
            ? throw DomainException.Validation($"{name} must be between {min} and {max}.")
            : value;

    public static decimal Positive(decimal value, string name) =>
        value <= 0 ? throw DomainException.Validation($"{name} must be greater than zero.") : value;
}
