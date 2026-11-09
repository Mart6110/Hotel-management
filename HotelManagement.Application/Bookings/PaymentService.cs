using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;

namespace HotelManagement.Application.Bookings;

/// <summary>Payments and refunds recorded against bookings.</summary>
public interface IPaymentService
{
    Task<IReadOnlyList<PaymentResponse>> ListAsync(Guid bookingId, CancellationToken ct);
    Task<PaymentResponse> RecordAsync(Guid bookingId, CreatePaymentRequest request, CancellationToken ct);
}

public class PaymentService(
    IBookingRepository bookings,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock) : IPaymentService
{
    public async Task<IReadOnlyList<PaymentResponse>> ListAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = (await bookings.GetByIdAsync(bookingId, ct)).OrThrow(bookingId);
        return booking.Payments.OrderBy(p => p.PaidAt).Select(PaymentResponse.From).ToList();
    }

    public async Task<PaymentResponse> RecordAsync(Guid bookingId, CreatePaymentRequest r, CancellationToken ct)
    {
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);

        // Lock first so two concurrent payments can't both pass the balance check.
        await bookings.LockAsync(bookingId, ct);
        var booking = (await bookings.GetByIdAsync(bookingId, ct)).OrThrow(bookingId);

        var now = clock.GetUtcNow().UtcDateTime;
        var payment = r.Type == PaymentType.Refund
            ? booking.RecordRefund(r.Amount, r.Method, r.Reference, now, currentUser.UserId)
            : booking.RecordPayment(r.Amount, r.Method, r.Reference, now, currentUser.UserId);

        await unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return PaymentResponse.From(payment);
    }
}
