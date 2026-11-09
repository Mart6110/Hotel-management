using HotelManagement.Domain;
using static HotelManagement.UnitTests.TestData;

namespace HotelManagement.UnitTests.Domain;

public class BookingCreationTests
{
    [Fact]
    public void Captures_room_rate_and_computes_total()
    {
        var booking = NewBooking(price: 120m, startOffset: 3, nights: 3);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(3, booking.Nights);
        Assert.Equal(120m, booking.PricePerNight);
        Assert.Equal(360m, booking.TotalPrice);
        Assert.Equal(CompanyId, booking.CompanyId);
        Assert.Equal(360m, booking.Balance);
    }

    [Theory]
    [InlineData(-1, 1, 1)]   // check-in in the past
    [InlineData(2, 2, 1)]    // zero nights
    [InlineData(4, 2, 1)]    // check-out before check-in
    [InlineData(0, 366, 1)]  // longer than max stay
    [InlineData(0, 1, 3)]    // over capacity (2)
    [InlineData(0, 1, 0)]    // no guests
    public void Rejects_invalid_requests(int inOffset, int outOffset, int guests)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Booking.Create(NewRoom(), NewGuest(), Today.AddDays(inOffset), Today.AddDays(outOffset), guests, null, Now));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public void Inactive_room_cannot_be_booked()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Booking.Create(NewRoom(isActive: false), NewGuest(), Today, Today.AddDays(1), 1, null, Now));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Room_and_guest_must_share_a_company()
    {
        var foreignGuest = new Guest("Other", "Tenant", null, null);
        foreignGuest.AssignCompany(Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(() =>
            Booking.Create(NewRoom(), foreignGuest, Today, Today.AddDays(1), 1, null, Now));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }
}

public class BookingLifecycleTests
{
    [Fact]
    public void Confirmed_booking_can_check_in_on_arrival_day()
    {
        var booking = NewBooking();
        booking.MarkCheckedIn(Now);

        Assert.Equal(BookingStatus.CheckedIn, booking.Status);
        Assert.Equal(Now, booking.CheckedInAt);
    }

    [Fact]
    public void Cannot_check_in_before_arrival_day()
    {
        var booking = NewBooking(startOffset: 2);
        var ex = Assert.Throws<DomainException>(() => booking.MarkCheckedIn(Now));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Cannot_check_in_after_stay_has_ended()
    {
        var booking = NewBooking(nights: 2);
        Assert.Throws<DomainException>(() => booking.MarkCheckedIn(Now.AddDays(2)));
    }

    [Fact]
    public void Cannot_check_out_with_outstanding_balance()
    {
        var booking = NewBooking(price: 100m, nights: 2);
        booking.MarkCheckedIn(Now);
        booking.RecordPayment(150m, PaymentMethod.Card, null, Now, null);

        var ex = Assert.Throws<DomainException>(() => booking.MarkCheckedOut(Now));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        Assert.Contains("50.00", ex.Message);
    }

    [Fact]
    public void Fully_paid_booking_can_check_out()
    {
        var booking = NewBooking(price: 100m, nights: 2);
        booking.MarkCheckedIn(Now);
        booking.RecordPayment(200m, PaymentMethod.Cash, null, Now, null);
        booking.MarkCheckedOut(Now.AddDays(2));

        Assert.Equal(BookingStatus.CheckedOut, booking.Status);
        Assert.Equal(0m, booking.Balance);
    }

    [Fact]
    public void Only_confirmed_bookings_can_be_cancelled()
    {
        var booking = NewBooking();
        booking.MarkCheckedIn(Now);
        Assert.Throws<DomainException>(() => booking.Cancel(Now));
    }

    [Fact]
    public void Cannot_check_out_a_booking_that_was_never_checked_in()
    {
        Assert.Throws<DomainException>(() => NewBooking().MarkCheckedOut(Now));
    }
}

public class BookingPaymentTests
{
    [Fact]
    public void Payments_reduce_the_balance_and_inherit_the_tenant()
    {
        var booking = NewBooking(price: 100m, nights: 3);
        var payment = booking.RecordPayment(120m, PaymentMethod.Card, " Deposit ", Now, recordedBy: Guid.Empty);

        Assert.Equal(180m, booking.Balance);
        Assert.Equal("Deposit", payment.Reference);
        Assert.Equal(booking.Id, payment.BookingId);
        Assert.Equal(CompanyId, payment.CompanyId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Payment_amount_must_be_positive(decimal amount)
    {
        Assert.Throws<DomainException>(() => NewBooking().RecordPayment(amount, PaymentMethod.Cash, null, Now, null));
    }

    [Fact]
    public void Cannot_overpay()
    {
        var booking = NewBooking(price: 100m, nights: 1);
        var ex = Assert.Throws<DomainException>(() => booking.RecordPayment(100.01m, PaymentMethod.Cash, null, Now, null));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public void Cancelling_after_payment_creates_a_refundable_credit()
    {
        var booking = NewBooking(price: 80m, startOffset: 5, nights: 2);
        booking.RecordPayment(50m, PaymentMethod.Card, null, Now, null);
        booking.Cancel(Now);

        Assert.Equal(0m, booking.AmountDue);
        Assert.Equal(-50m, booking.Balance);

        Assert.Throws<DomainException>(() => booking.RecordRefund(60m, PaymentMethod.Card, null, Now, null));
        booking.RecordRefund(50m, PaymentMethod.Card, null, Now, null);
        Assert.Equal(0m, booking.Balance);
        Assert.Equal(50m, booking.TotalRefunded);
    }

    [Fact]
    public void Cannot_take_payments_on_a_cancelled_booking()
    {
        var booking = NewBooking();
        booking.Cancel(Now);
        var ex = Assert.Throws<DomainException>(() => booking.RecordPayment(10m, PaymentMethod.Cash, null, Now, null));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }
}
