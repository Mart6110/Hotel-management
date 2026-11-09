using System.Globalization;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;
using Microsoft.Extensions.Logging;

namespace HotelManagement.Application.PublicBooking;

/// <summary>Tells guests about their online bookings.</summary>
public interface IBookingEmailNotifier
{
    Task BookingConfirmedAsync(Company company, PublicBookingResponse booking, string accessCode, CancellationToken ct);
    Task BookingCancelledAsync(Company company, PublicBookingResponse booking, CancellationToken ct);
}

/// <summary>
/// Composes the emails and hands them to <see cref="IEmailSender"/>. Delivery failures are logged, not thrown:
/// the booking is already committed and the response carries the reference and code anyway.
/// </summary>
public class BookingEmailNotifier(IEmailSender sender, ILogger<BookingEmailNotifier> logger) : IBookingEmailNotifier
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public Task BookingConfirmedAsync(Company company, PublicBookingResponse b, string accessCode, CancellationToken ct)
    {
        var body = $"""
            Dear {b.GuestName},

            Thank you for booking with {b.HotelName}. Your booking is confirmed.

              Booking reference: {b.Reference}
              Access code:       {accessCode}

              Hotel:     {b.HotelName}, {b.HotelAddress}
              Room type: {b.RoomType}
              Check-in:  {Date(b.CheckIn)}
              Check-out: {Date(b.CheckOut)} ({b.Nights} night{(b.Nights == 1 ? "" : "s")}, {b.NumberOfGuests} guest{(b.NumberOfGuests == 1 ? "" : "s")})
              Total:     {Money(b.TotalPrice)} (payable at the hotel)

            {CancellationText(b)}

            Keep your reference and access code: you need both to view or cancel your booking
            (GET or POST /api/public/{company.Slug}/bookings/{b.Reference}, header X-Booking-Code).

            {company.Name}
            """;
        return SendAsync(b, $"Booking confirmed: {b.HotelName}, {Date(b.CheckIn)} ({b.Reference})", body, ct);
    }

    public Task BookingCancelledAsync(Company company, PublicBookingResponse b, CancellationToken ct)
    {
        var body = $"""
            Dear {b.GuestName},

            Your booking {b.Reference} at {b.HotelName} for {Date(b.CheckIn)} to {Date(b.CheckOut)} has been cancelled.
            If you paid anything in advance, the hotel will arrange a refund.

            {company.Name}
            """;
        return SendAsync(b, $"Booking cancelled: {b.Reference}", body, ct);
    }

    private async Task SendAsync(PublicBookingResponse b, string subject, string body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(b.GuestEmail))
            return;
        try
        {
            await sender.SendAsync(new EmailMessage(b.GuestEmail, b.GuestName, subject, body), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send '{Subject}' to the guest of booking {Reference}", subject, b.Reference);
        }
    }

    private static string CancellationText(PublicBookingResponse b) => b.CancellableUntil is { } until
        ? $"Free online cancellation until {until.ToString("yyyy-MM-dd HH:mm", Invariant)} UTC."
        : "Online cancellation is no longer available for this booking; please contact the hotel.";

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Invariant);
    private static string Money(decimal amount) => amount.ToString("0.00", Invariant);
}
