namespace HotelManagement.Domain;

public enum PaymentMethod
{
    Cash,
    Card,
    BankTransfer,
    Other
}

public enum PaymentType
{
    Payment,
    Refund
}

/// <summary>Money received for, or returned on, a booking. Created only through <see cref="Booking"/>.</summary>
public class Payment : TenantEntity
{
    private Payment() { }

    internal Payment(Booking booking, PaymentType type, decimal amount, PaymentMethod method, string? reference, DateTime paidAt, Guid? recordedBy)
    {
        InheritCompany(booking);
        BookingId = booking.Id;
        Type = type;
        Amount = amount;
        Method = method;
        Reference = reference;
        PaidAt = paidAt;
        RecordedByUserId = recordedBy;
    }

    public Guid BookingId { get; private set; }
    public PaymentType Type { get; private set; }

    /// <summary>Always positive; <see cref="Type"/> decides the direction.</summary>
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public DateTime PaidAt { get; private set; }
    public Guid? RecordedByUserId { get; private set; }
}
