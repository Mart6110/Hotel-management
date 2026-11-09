using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Bookings;

public interface IInvoiceService
{
    Task<InvoiceResponse> GetForBookingAsync(Guid bookingId, CancellationToken ct);
}

public class InvoiceService(IBookingRepository bookings) : IInvoiceService
{
    public async Task<InvoiceResponse> GetForBookingAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = (await bookings.GetWithDetailsAsync(bookingId, ct)).OrThrow(bookingId);
        return Build(booking);
    }

    /// <summary>Pure mapping from a fully-loaded booking to its invoice.</summary>
    public static InvoiceResponse Build(Booking booking)
    {
        var room = booking.Room;
        var lines = booking.Status == BookingStatus.Cancelled
            ? []
            : new List<InvoiceLine>
            {
                new($"Room {room.Number} ({room.Type}), {booking.CheckIn:yyyy-MM-dd} to {booking.CheckOut:yyyy-MM-dd}",
                    booking.Nights, booking.PricePerNight, booking.TotalPrice),
            };

        return new InvoiceResponse(
            booking.Id,
            InvoiceNumber(booking),
            booking.Status,
            room.Hotel.Name,
            room.Hotel.FullAddress,
            booking.Guest.FullName,
            booking.Guest.Email,
            room.Number,
            booking.CheckIn,
            booking.CheckOut,
            lines,
            booking.AmountDue,
            booking.TotalPaid,
            booking.TotalRefunded,
            booking.Balance,
            booking.Payments.OrderBy(p => p.PaidAt).Select(PaymentResponse.From).ToList());
    }

    private static string InvoiceNumber(Booking b) =>
        $"INV-{b.CreatedAt:yyyyMMdd}-{b.Id.ToString("N")[^8..].ToUpperInvariant()}";
}
