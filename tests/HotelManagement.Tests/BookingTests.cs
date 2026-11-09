using System.Net;
using System.Net.Http.Json;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;
using HotelManagement.Application.Common;

namespace HotelManagement.Tests;

public class BookingTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(TenantClient Tenant, RoomResponse Room, GuestResponse Guest)> SetupAsync(decimal price = 100m)
    {
        var tenant = await NewCompanyAsync();
        var hotel = await tenant.CreateHotelAsync();
        var room = await tenant.CreateRoomAsync(hotel.Id, price: price);
        var guest = await tenant.CreateGuestAsync();
        return (tenant, room, guest);
    }

    [Fact]
    public async Task Booking_captures_price_and_nights()
    {
        var (t, room, guest) = await SetupAsync(price: 120m);
        var booking = await t.BookAsync(room.Id, guest.Id, Today.AddDays(3), Today.AddDays(6), guests: 2);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(3, booking.Nights);
        Assert.Equal(120m, booking.PricePerNight);
        Assert.Equal(360m, booking.TotalPrice);
    }

    [Fact]
    public async Task Overlapping_booking_is_rejected_but_back_to_back_is_allowed()
    {
        var (t, room, guest) = await SetupAsync();
        await t.BookAsync(room.Id, guest.Id, Today.AddDays(10), Today.AddDays(13));

        var overlap = await t.BookRawAsync(room.Id, guest.Id, Today.AddDays(12), Today.AddDays(14));
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        var inside = await t.BookRawAsync(room.Id, guest.Id, Today.AddDays(11), Today.AddDays(12));
        Assert.Equal(HttpStatusCode.Conflict, inside.StatusCode);

        // Checkout day of one booking is the check-in day of the next.
        await t.BookAsync(room.Id, guest.Id, Today.AddDays(13), Today.AddDays(15));
        await t.BookAsync(room.Id, guest.Id, Today.AddDays(8), Today.AddDays(10));
    }

    [Fact]
    public async Task Concurrent_bookings_for_same_room_only_one_succeeds()
    {
        var (t, room, guest) = await SetupAsync();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            t.BookRawAsync(room.Id, guest.Id, Today.AddDays(20), Today.AddDays(22))));

        Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(attempts.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Theory]
    [InlineData(-1, 1, 1)]  // check-in in the past
    [InlineData(2, 2, 1)]   // zero nights
    [InlineData(4, 2, 1)]   // check-out before check-in
    [InlineData(1, 2, 5)]   // too many guests for capacity 2
    public async Task Invalid_bookings_are_bad_requests(int inOffset, int outOffset, int guests)
    {
        var (t, room, guest) = await SetupAsync();
        var res = await t.BookRawAsync(room.Id, guest.Id, Today.AddDays(inOffset), Today.AddDays(outOffset), guests);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Inactive_room_cannot_be_booked()
    {
        var (t, room, guest) = await SetupAsync();
        var update = await t.Http.PutAsJsonAsync($"/api/rooms/{room.Id}", new RoomRequest
        {
            HotelId = room.HotelId, Number = room.Number, Type = room.Type, Capacity = room.Capacity,
            PricePerNight = room.PricePerNight, Floor = room.Floor, IsActive = false,
        }, Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var res = await t.BookRawAsync(room.Id, guest.Id, Today.AddDays(1), Today.AddDays(2));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Cancelling_frees_the_room_and_availability_reflects_bookings()
    {
        var (t, room, guest) = await SetupAsync();
        var otherRoom = await t.CreateRoomAsync(room.HotelId, number: "102");
        var checkIn = Today.AddDays(30);
        var checkOut = Today.AddDays(32);
        string AvailableUrl() => $"/api/rooms/available?hotelId={room.HotelId}&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}&guests=2";

        var booking = await t.BookAsync(room.Id, guest.Id, checkIn, checkOut);
        var available = await t.GetAsync<PagedResult<RoomResponse>>(AvailableUrl());
        Assert.Equal([otherRoom.Id], available.Items.Select(r => r.Id));

        var cancelled = await t.PostAsync<BookingResponse>($"/api/bookings/{booking.Id}/cancel", new { }, HttpStatusCode.OK);
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);

        available = await t.GetAsync<PagedResult<RoomResponse>>(AvailableUrl());
        Assert.Equal(2, available.TotalCount);
        await t.BookAsync(room.Id, guest.Id, checkIn, checkOut);
    }

    [Fact]
    public async Task Status_transitions_are_enforced()
    {
        var (t, room, guest) = await SetupAsync();
        var future = await t.BookAsync(room.Id, guest.Id, Today.AddDays(5), Today.AddDays(6));
        Assert.Equal(HttpStatusCode.Conflict, (await t.PostRawAsync($"/api/bookings/{future.Id}/check-in")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.PostRawAsync($"/api/bookings/{future.Id}/check-out")).StatusCode);

        var now = await t.BookAsync(room.Id, guest.Id, Today, Today.AddDays(2));
        var checkedIn = await t.PostAsync<BookingResponse>($"/api/bookings/{now.Id}/check-in", new { }, HttpStatusCode.OK);
        Assert.Equal(BookingStatus.CheckedIn, checkedIn.Status);
        Assert.NotNull(checkedIn.CheckedInAt);

        Assert.Equal(HttpStatusCode.Conflict, (await t.PostRawAsync($"/api/bookings/{now.Id}/check-in")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.PostRawAsync($"/api/bookings/{now.Id}/cancel")).StatusCode);
    }

    [Fact]
    public async Task Rooms_and_guests_with_bookings_cannot_be_deleted()
    {
        var (t, room, guest) = await SetupAsync();
        await t.BookAsync(room.Id, guest.Id, Today.AddDays(1), Today.AddDays(2));

        Assert.Equal(HttpStatusCode.Conflict, (await t.Http.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.Http.DeleteAsync($"/api/guests/{guest.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.Http.DeleteAsync($"/api/hotels/{room.HotelId}")).StatusCode);
    }

    [Fact]
    public async Task Bookings_can_be_filtered()
    {
        var (t, room, guest) = await SetupAsync();
        var other = await t.CreateGuestAsync("Other", "Guest");
        await t.BookAsync(room.Id, guest.Id, Today.AddDays(1), Today.AddDays(3));
        var b2 = await t.BookAsync(room.Id, other.Id, Today.AddDays(10), Today.AddDays(12));
        await t.PostAsync<BookingResponse>($"/api/bookings/{b2.Id}/cancel", new { }, HttpStatusCode.OK);

        Assert.Equal(1, (await t.GetAsync<PagedResult<BookingResponse>>($"/api/bookings?guestId={other.Id}")).TotalCount);
        Assert.Equal(1, (await t.GetAsync<PagedResult<BookingResponse>>("/api/bookings?status=Cancelled")).TotalCount);
        var window = await t.GetAsync<PagedResult<BookingResponse>>(
            $"/api/bookings?from={Today.AddDays(2):yyyy-MM-dd}&to={Today.AddDays(5):yyyy-MM-dd}");
        Assert.Equal(guest.Id, Assert.Single(window.Items).GuestId);
    }
}

public class PaymentTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Full_stay_payment_invoice_and_checkout_flow()
    {
        var t = await NewCompanyAsync();
        var hotel = await t.CreateHotelAsync();
        var room = await t.CreateRoomAsync(hotel.Id, price: 150m);
        var guest = await t.CreateGuestAsync();
        var booking = await t.BookAsync(room.Id, guest.Id, Today, Today.AddDays(2)); // 300 total
        var bookingUrl = $"/api/bookings/{booking.Id}";

        await t.PostAsync<BookingResponse>($"{bookingUrl}/check-in", new { }, HttpStatusCode.OK);
        await t.PostAsync<PaymentResponse>($"{bookingUrl}/payments",
            new CreatePaymentRequest { Amount = 100m, Method = PaymentMethod.Card, Reference = "Deposit" });

        // Can't overpay, can't check out with a balance.
        var overpay = await t.PostRawAsync($"{bookingUrl}/payments", new CreatePaymentRequest { Amount = 250m, Method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.BadRequest, overpay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.PostRawAsync($"{bookingUrl}/check-out")).StatusCode);

        var invoice = await t.GetAsync<InvoiceResponse>($"{bookingUrl}/invoice");
        Assert.Equal(300m, invoice.AmountDue);
        Assert.Equal(100m, invoice.TotalPaid);
        Assert.Equal(200m, invoice.Balance);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(150m, line.UnitPrice);
        Assert.StartsWith("INV-", invoice.InvoiceNumber);

        await t.PostAsync<PaymentResponse>($"{bookingUrl}/payments", new CreatePaymentRequest { Amount = 200m, Method = PaymentMethod.Cash });
        var checkedOut = await t.PostAsync<BookingResponse>($"{bookingUrl}/check-out", new { }, HttpStatusCode.OK);
        Assert.Equal(BookingStatus.CheckedOut, checkedOut.Status);

        invoice = await t.GetAsync<InvoiceResponse>($"{bookingUrl}/invoice");
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(2, invoice.Payments.Count);
        Assert.Equal(2, (await t.GetAsync<List<PaymentResponse>>($"{bookingUrl}/payments")).Count);

        // No more payments on a finished stay.
        var late = await t.PostRawAsync($"{bookingUrl}/payments", new CreatePaymentRequest { Amount = 1m, Method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    [Fact]
    public async Task Cancelled_booking_with_payment_is_refundable()
    {
        var t = await NewCompanyAsync();
        var hotel = await t.CreateHotelAsync();
        var room = await t.CreateRoomAsync(hotel.Id, price: 80m);
        var guest = await t.CreateGuestAsync();
        var booking = await t.BookAsync(room.Id, guest.Id, Today.AddDays(5), Today.AddDays(7));
        var bookingUrl = $"/api/bookings/{booking.Id}";

        await t.PostAsync<PaymentResponse>($"{bookingUrl}/payments", new CreatePaymentRequest { Amount = 50m, Method = PaymentMethod.Card });
        await t.PostAsync<BookingResponse>($"{bookingUrl}/cancel", new { }, HttpStatusCode.OK);

        var invoice = await t.GetAsync<InvoiceResponse>($"{bookingUrl}/invoice");
        Assert.Equal(0m, invoice.AmountDue);
        Assert.Equal(-50m, invoice.Balance);
        Assert.Empty(invoice.Lines);

        var tooMuch = await t.PostRawAsync($"{bookingUrl}/payments",
            new CreatePaymentRequest { Amount = 60m, Method = PaymentMethod.Card, Type = PaymentType.Refund });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);

        await t.PostAsync<PaymentResponse>($"{bookingUrl}/payments",
            new CreatePaymentRequest { Amount = 50m, Method = PaymentMethod.Card, Type = PaymentType.Refund });
        invoice = await t.GetAsync<InvoiceResponse>($"{bookingUrl}/invoice");
        Assert.Equal(50m, invoice.TotalRefunded);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public async Task Concurrent_payments_cannot_exceed_balance()
    {
        var t = await NewCompanyAsync();
        var hotel = await t.CreateHotelAsync();
        var room = await t.CreateRoomAsync(hotel.Id, price: 100m);
        var guest = await t.CreateGuestAsync();
        var booking = await t.BookAsync(room.Id, guest.Id, Today.AddDays(1), Today.AddDays(2)); // 100 total

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            t.PostRawAsync($"/api/bookings/{booking.Id}/payments", new CreatePaymentRequest { Amount = 60m, Method = PaymentMethod.Card })));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        var invoice = await t.GetAsync<InvoiceResponse>($"/api/bookings/{booking.Id}/invoice");
        Assert.Equal(60m, invoice.TotalPaid);
    }
}

public class PagingTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task List_endpoints_page_and_search()
    {
        var t = await NewCompanyAsync();
        foreach (var name in new[] { "Adams", "Baker", "Clark", "Davis", "Evans" })
            await t.CreateGuestAsync("Pat", name);

        var page1 = await t.GetAsync<PagedResult<GuestResponse>>("/api/guests?page=1&pageSize=2");
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(["Adams", "Baker"], page1.Items.Select(g => g.LastName));

        var page3 = await t.GetAsync<PagedResult<GuestResponse>>("/api/guests?page=3&pageSize=2");
        Assert.Equal("Evans", Assert.Single(page3.Items).LastName);

        var search = await t.GetAsync<PagedResult<GuestResponse>>("/api/guests?search=pat%20cla");
        Assert.Equal("Clark", Assert.Single(search.Items).LastName);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Invalid_paging_is_rejected(string query)
    {
        var t = await NewCompanyAsync();
        var res = await t.Http.GetAsync($"/api/guests?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Rooms_can_be_filtered()
    {
        var t = await NewCompanyAsync();
        var hotel = await t.CreateHotelAsync();
        await t.CreateRoomAsync(hotel.Id, "101", capacity: 1, price: 50m);
        await t.CreateRoomAsync(hotel.Id, "102", capacity: 2, price: 100m);
        await t.CreateRoomAsync(hotel.Id, "103", capacity: 4, price: 250m);

        var big = await t.GetAsync<PagedResult<RoomResponse>>($"/api/rooms?hotelId={hotel.Id}&minCapacity=2");
        Assert.Equal(["102", "103"], big.Items.Select(r => r.Number));
        var cheap = await t.GetAsync<PagedResult<RoomResponse>>($"/api/rooms?hotelId={hotel.Id}&maxPrice=100");
        Assert.Equal(2, cheap.TotalCount);
    }
}
