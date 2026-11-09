using System.ComponentModel.DataAnnotations;
using HotelManagement.Domain;
using HotelManagement.Application.Common;

namespace HotelManagement.Application.Contracts;

// ---------- Auth & users ----------

public record RegisterCompanyRequest
{
    [Required, MaxLength(200)] public string CompanyName { get; init; } = "";
    [Required, MaxLength(200)] public string FullName { get; init; } = "";
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = "";
    [Required, MinLength(8), MaxLength(128)] public string Password { get; init; } = "";
}

public record LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
}

public record UserResponse(Guid Id, Guid CompanyId, string Email, string FullName, UserRole Role, bool IsActive, DateTime CreatedAt)
{
    public static UserResponse From(User u) => new(u.Id, u.CompanyId, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt);
}

public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserResponse User);

public record CreateUserRequest
{
    [Required, MaxLength(200)] public string FullName { get; init; } = "";
    [Required, EmailAddress, MaxLength(256)] public string Email { get; init; } = "";
    [Required, MinLength(8), MaxLength(128)] public string Password { get; init; } = "";
    [Required, EnumDataType(typeof(UserRole))] public UserRole Role { get; init; }
}

public record UpdateUserRequest
{
    [Required, MaxLength(200)] public string FullName { get; init; } = "";
    [Required, EnumDataType(typeof(UserRole))] public UserRole Role { get; init; }
    public bool IsActive { get; init; } = true;
}

public class UserQuery : PageQuery
{
    public string? Search { get; set; }
    public UserRole? Role { get; set; }
}

// ---------- Hotels ----------

public record HotelRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = "";
    [Required, MaxLength(300)] public string Address { get; init; } = "";
    [Required, MaxLength(100)] public string City { get; init; } = "";
    [Required, MaxLength(100)] public string Country { get; init; } = "";
    [MaxLength(50)] public string? Phone { get; init; }
    [EmailAddress, MaxLength(256)] public string? Email { get; init; }
}

public record HotelResponse(Guid Id, string Name, string Address, string City, string Country, string? Phone, string? Email, int RoomCount, DateTime CreatedAt);

public class HotelQuery : PageQuery
{
    /// <summary>Matches name or city (case-insensitive).</summary>
    public string? Search { get; set; }
    public string? Country { get; set; }
}

// ---------- Rooms ----------

public record RoomRequest
{
    [Required] public Guid HotelId { get; init; }
    [Required, MaxLength(20)] public string Number { get; init; } = "";
    [Required, EnumDataType(typeof(RoomType))] public RoomType Type { get; init; }
    [Range(1, 20)] public int Capacity { get; init; }
    [Range(0.01, 1_000_000)] public decimal PricePerNight { get; init; }
    [Range(-5, 200)] public int Floor { get; init; }
    public bool IsActive { get; init; } = true;
}

public record RoomResponse(Guid Id, Guid HotelId, string HotelName, string Number, RoomType Type, int Capacity, decimal PricePerNight, int Floor, bool IsActive);

public class RoomQuery : PageQuery
{
    public Guid? HotelId { get; set; }
    public RoomType? Type { get; set; }
    public int? MinCapacity { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool? IsActive { get; set; }
}

public class AvailabilityQuery : PageQuery
{
    [Required] public Guid? HotelId { get; set; }
    [Required] public DateOnly? CheckIn { get; set; }
    [Required] public DateOnly? CheckOut { get; set; }
    [Range(1, 20)] public int Guests { get; set; } = 1;
    public RoomType? Type { get; set; }
}

// ---------- Guests ----------

public record GuestRequest
{
    [Required, MaxLength(100)] public string FirstName { get; init; } = "";
    [Required, MaxLength(100)] public string LastName { get; init; } = "";
    [EmailAddress, MaxLength(256)] public string? Email { get; init; }
    [MaxLength(50)] public string? Phone { get; init; }
}

public record GuestResponse(Guid Id, string FirstName, string LastName, string? Email, string? Phone, DateTime CreatedAt)
{
    public static GuestResponse From(Guest g) => new(g.Id, g.FirstName, g.LastName, g.Email, g.Phone, g.CreatedAt);
}

public class GuestQuery : PageQuery
{
    /// <summary>Matches first name, last name, email or phone.</summary>
    public string? Search { get; set; }
}

// ---------- Bookings ----------

public record CreateBookingRequest
{
    [Required] public Guid RoomId { get; init; }
    [Required] public Guid GuestId { get; init; }
    [Required] public DateOnly CheckIn { get; init; }
    [Required] public DateOnly CheckOut { get; init; }
    [Range(1, 20)] public int NumberOfGuests { get; init; } = 1;
    [MaxLength(1000)] public string? Notes { get; init; }
}

public record BookingResponse(
    Guid Id,
    Guid HotelId,
    string HotelName,
    Guid RoomId,
    string RoomNumber,
    Guid GuestId,
    string GuestName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int NumberOfGuests,
    decimal PricePerNight,
    decimal TotalPrice,
    BookingStatus Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime? CheckedInAt,
    DateTime? CheckedOutAt,
    DateTime? CancelledAt)
{
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;
}

public class BookingQuery : PageQuery
{
    public Guid? HotelId { get; set; }
    public Guid? RoomId { get; set; }
    public Guid? GuestId { get; set; }
    public BookingStatus? Status { get; set; }

    /// <summary>Only bookings whose stay overlaps [From, To).</summary>
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

// ---------- Payments & invoices ----------

public record CreatePaymentRequest
{
    [Range(0.01, 10_000_000)] public decimal Amount { get; init; }
    [Required, EnumDataType(typeof(PaymentMethod))] public PaymentMethod Method { get; init; }
    [EnumDataType(typeof(PaymentType))] public PaymentType Type { get; init; } = PaymentType.Payment;
    [MaxLength(200)] public string? Reference { get; init; }
}

public record PaymentResponse(Guid Id, Guid BookingId, PaymentType Type, decimal Amount, PaymentMethod Method, string? Reference, DateTime PaidAt)
{
    public static PaymentResponse From(Payment p) => new(p.Id, p.BookingId, p.Type, p.Amount, p.Method, p.Reference, p.PaidAt);
}

public record InvoiceLine(string Description, int Quantity, decimal UnitPrice, decimal Amount);

public record InvoiceResponse(
    Guid BookingId,
    string InvoiceNumber,
    BookingStatus BookingStatus,
    string HotelName,
    string HotelAddress,
    string GuestName,
    string? GuestEmail,
    string RoomNumber,
    DateOnly CheckIn,
    DateOnly CheckOut,
    IReadOnlyList<InvoiceLine> Lines,
    decimal AmountDue,
    decimal TotalPaid,
    decimal TotalRefunded,
    decimal Balance, // Positive: guest still owes. Negative: a refund is due to the guest.
    IReadOnlyList<PaymentResponse> Payments);
