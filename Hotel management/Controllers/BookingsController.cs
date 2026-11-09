using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController(IBookingService bookings, IPaymentService payments, IInvoiceService invoices) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<BookingResponse>> List([FromQuery] BookingQuery query, CancellationToken ct) => bookings.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<BookingResponse> Get(Guid id, CancellationToken ct) => bookings.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var booking = await bookings.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = booking.Id }, booking);
    }

    [HttpPost("{id:guid}/check-in")]
    public Task<BookingResponse> CheckIn(Guid id, CancellationToken ct) => bookings.CheckInAsync(id, ct);

    /// <summary>Checks the guest out. Requires the balance to be fully paid.</summary>
    [HttpPost("{id:guid}/check-out")]
    public Task<BookingResponse> CheckOut(Guid id, CancellationToken ct) => bookings.CheckOutAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<BookingResponse> Cancel(Guid id, CancellationToken ct) => bookings.CancelAsync(id, ct);

    [HttpGet("{id:guid}/payments")]
    public Task<IReadOnlyList<PaymentResponse>> Payments(Guid id, CancellationToken ct) => payments.ListAsync(id, ct);

    /// <summary>Records a payment (or a refund, with "type": "Refund") against the booking.</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<PaymentResponse>> AddPayment(Guid id, CreatePaymentRequest request, CancellationToken ct)
    {
        var payment = await payments.RecordAsync(id, request, ct);
        return CreatedAtAction(nameof(Payments), new { id }, payment);
    }

    [HttpGet("{id:guid}/invoice")]
    public Task<InvoiceResponse> Invoice(Guid id, CancellationToken ct) => invoices.GetForBookingAsync(id, ct);
}
