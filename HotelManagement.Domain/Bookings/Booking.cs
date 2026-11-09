namespace HotelManagement.Domain;

public enum BookingStatus
{
    Confirmed,
    CheckedIn,
    CheckedOut,
    Cancelled
}

/// <summary>
/// A guest's reservation of a room. Aggregate root for its payments: all lifecycle
/// and money rules live here. Balance-related members require <see cref="Payments"/> to be loaded.
/// </summary>
public class Booking : TenantEntity
{
    public const int MaxNights = 365;

    private readonly List<Payment> _payments = [];

    private Booking() { }

    public Guid RoomId { get; private set; }
    public Room Room { get; private set; } = null!;
    public Guid GuestId { get; private set; }
    public Guest Guest { get; private set; } = null!;
    public DateOnly CheckIn { get; private set; }
    public DateOnly CheckOut { get; private set; }
    public int NumberOfGuests { get; private set; }

    /// <summary>Room rate captured at booking time, so later price changes don't affect it.</summary>
    public decimal PricePerNight { get; private set; }
    public decimal TotalPrice { get; private set; }
    public BookingStatus Status { get; private set; } = BookingStatus.Confirmed;
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CheckedInAt { get; private set; }
    public DateTime? CheckedOutAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    /// <summary>Optimistic concurrency token.</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<Payment> Payments => _payments;

    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    /// <summary>Whether this booking occupies its room (and so blocks other bookings).</summary>
    public bool IsActive => Status is BookingStatus.Confirmed or BookingStatus.CheckedIn;

    /// <summary>What the guest owes for the stay. Cancelled bookings owe nothing.</summary>
    public decimal AmountDue => Status == BookingStatus.Cancelled ? 0m : TotalPrice;
    public decimal TotalPaid => _payments.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount);
    public decimal TotalRefunded => _payments.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);

    /// <summary>Positive: the guest still owes. Negative: a refund is due to the guest.</summary>
    public decimal Balance => AmountDue - TotalPaid + TotalRefunded;

    public static Booking Create(
        Room room, Guest guest, DateOnly checkIn, DateOnly checkOut, int numberOfGuests, string? notes, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(guest);

        if (checkOut <= checkIn)
            throw DomainException.Validation("CheckOut must be after CheckIn.");
        if (checkIn < DateOnly.FromDateTime(now))
            throw DomainException.Validation("CheckIn cannot be in the past.");
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        if (nights > MaxNights)
            throw DomainException.Validation($"A booking cannot exceed {MaxNights} nights.");
        if (room.CompanyId != guest.CompanyId)
            throw DomainException.Validation("Room and guest must belong to the same company.");
        room.EnsureCanHost(numberOfGuests);

        var booking = new Booking
        {
            RoomId = room.Id,
            Room = room,
            GuestId = guest.Id,
            Guest = guest,
            CheckIn = checkIn,
            CheckOut = checkOut,
            NumberOfGuests = numberOfGuests,
            PricePerNight = room.PricePerNight,
            TotalPrice = room.PricePerNight * nights,
            Notes = Guard.Optional(notes, "Notes", 1000),
            CreatedAt = now,
        };
        booking.InheritCompany(room);
        return booking;
    }

    public void MarkCheckedIn(DateTime now)
    {
        EnsureStatus(BookingStatus.Confirmed, "checked in");
        var today = DateOnly.FromDateTime(now);
        if (today < CheckIn)
            throw DomainException.Conflict($"Check-in is not possible before {CheckIn:yyyy-MM-dd}.");
        if (today >= CheckOut)
            throw DomainException.Conflict("The booking's stay period has already ended.");

        Status = BookingStatus.CheckedIn;
        CheckedInAt = now;
    }

    public void MarkCheckedOut(DateTime now)
    {
        EnsureStatus(BookingStatus.CheckedIn, "checked out");
        if (Balance > 0)
            throw DomainException.Conflict($"The booking has an outstanding balance of {Money.Format(Balance)}. Record a payment first.");

        Status = BookingStatus.CheckedOut;
        CheckedOutAt = now;
    }

    public void Cancel(DateTime now)
    {
        EnsureStatus(BookingStatus.Confirmed, "cancelled");
        Status = BookingStatus.Cancelled;
        CancelledAt = now;
    }

    public Payment RecordPayment(decimal amount, PaymentMethod method, string? reference, DateTime now, Guid? recordedBy)
    {
        Guard.Positive(amount, "Amount");
        if (!IsActive)
            throw DomainException.Conflict($"Payments can't be recorded for a booking with status {Status}.");
        if (amount > Balance)
            throw DomainException.Validation($"Payment of {Money.Format(amount)} exceeds the outstanding balance of {Money.Format(Balance)}.");

        return AddPayment(PaymentType.Payment, amount, method, reference, now, recordedBy);
    }

    public Payment RecordRefund(decimal amount, PaymentMethod method, string? reference, DateTime now, Guid? recordedBy)
    {
        Guard.Positive(amount, "Amount");
        var netPaid = TotalPaid - TotalRefunded;
        if (amount > netPaid)
            throw DomainException.Validation($"Refund of {Money.Format(amount)} exceeds the net amount paid ({Money.Format(netPaid)}).");

        return AddPayment(PaymentType.Refund, amount, method, reference, now, recordedBy);
    }

    private Payment AddPayment(PaymentType type, decimal amount, PaymentMethod method, string? reference, DateTime now, Guid? recordedBy)
    {
        var payment = new Payment(this, type, amount, method, Guard.Optional(reference, "Reference", 200), now, recordedBy);
        _payments.Add(payment);
        return payment;
    }

    private void EnsureStatus(BookingStatus required, string action)
    {
        if (Status != required)
            throw DomainException.Conflict($"Only {Describe(required)} bookings can be {action} (current status: {Status}).");
    }

    private static string Describe(BookingStatus status) => status switch
    {
        BookingStatus.Confirmed => "confirmed",
        BookingStatus.CheckedIn => "checked-in",
        _ => status.ToString(),
    };
}
