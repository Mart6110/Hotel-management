using System.Net;
using System.Net.Http.Json;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace HotelManagement.Tests;

public class PublicBookingTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string CodeHeader = "X-Booking-Code";

    private async Task<(TenantClient Staff, string Slug, HotelResponse Hotel)> SetupAsync()
    {
        var staff = await NewCompanyAsync();
        var slug = (await staff.GetAsync<CompanyResponse>("/api/company")).Slug;
        var hotel = await staff.CreateHotelAsync("Seaside Hotel");
        return (staff, slug, hotel);
    }

    private static Task<RoomResponse> AddRoomAsync(TenantClient staff, Guid hotelId, string number, RoomType type, int capacity, decimal price) =>
        staff.PostAsync<RoomResponse>("/api/rooms", new RoomRequest
        {
            HotelId = hotelId, Number = number, Type = type, Capacity = capacity, PricePerNight = price, Floor = 1,
        });

    private static PublicBookingRequest Request(Guid hotelId, int inOffset, int outOffset,
        RoomType type = RoomType.Double, int guests = 2, string? email = null) => new()
    {
        HotelId = hotelId, RoomType = type, NumberOfGuests = guests,
        CheckIn = Today.AddDays(inOffset), CheckOut = Today.AddDays(outOffset),
        FirstName = "Gina", LastName = "Guest", Email = email ?? UniqueEmail("guest"), Phone = "+45 1111 2222",
    };

    private Task<HttpResponseMessage> PostBookingAsync(string slug, PublicBookingRequest request) =>
        Anonymous().PostAsJsonAsync($"/api/public/{slug}/bookings", request, Json);

    private async Task<PublicBookingCreatedResponse> BookAsync(string slug, PublicBookingRequest request)
    {
        var res = await PostBookingAsync(slug, request);
        await TenantClient.AssertStatus(res, HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<PublicBookingCreatedResponse>(Json))!;
    }

    private HttpRequestMessage WithCode(HttpMethod method, string url, string? code)
    {
        var message = new HttpRequestMessage(method, url);
        if (code is not null) message.Headers.Add(CodeHeader, code);
        return message;
    }

    [Fact]
    public async Task Anonymous_visitors_can_browse_hotels_and_offers_for_one_company_only()
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);
        await AddRoomAsync(staff, hotel.Id, "102", RoomType.Double, 2, 120m);
        await AddRoomAsync(staff, hotel.Id, "201", RoomType.Suite, 4, 300m);
        await AddRoomAsync(staff, hotel.Id, "001", RoomType.Single, 1, 60m); // too small for 2 guests
        await (await NewCompanyAsync()).CreateHotelAsync("Someone Else's Hotel");

        var hotels = await Anonymous().GetFromJsonAsync<PagedResult<PublicHotelResponse>>($"/api/public/{slug}/hotels", Json);
        Assert.Equal("Seaside Hotel", Assert.Single(hotels!.Items).Name);

        var offers = (await Anonymous().GetFromJsonAsync<List<RoomOfferResponse>>(
            $"/api/public/{slug}/hotels/{hotel.Id}/offers?checkIn={Today.AddDays(10):yyyy-MM-dd}&checkOut={Today.AddDays(12):yyyy-MM-dd}&guests=2", Json))!;

        Assert.Equal([RoomType.Double, RoomType.Suite], offers.Select(o => o.RoomType));
        var doubles = offers[0];
        Assert.Equal(2, doubles.AvailableRooms);
        Assert.Equal(100m, doubles.PricePerNightFrom);
        Assert.Equal(2, doubles.Nights);
        Assert.Equal(200m, doubles.TotalPriceFrom);
    }

    [Fact]
    public async Task Guest_books_online_and_staff_see_it()
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "102", RoomType.Double, 2, 120m);
        var cheapest = await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);
        var email = UniqueEmail("gina");

        var created = await BookAsync(slug, Request(hotel.Id, 10, 13, email: email));

        // Guest-facing response
        Assert.Matches("^BK-[0-9A-HJKMNP-TV-Z]{8}$", created.Booking.Reference);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", created.AccessCode);
        Assert.Equal(BookingStatus.Confirmed, created.Booking.Status);
        Assert.Equal(300m, created.Booking.TotalPrice);
        Assert.Equal("Gina Guest", created.Booking.GuestName);
        Assert.NotNull(created.Booking.CancellableUntil);

        // Staff view: cheapest room assigned, marked as online, guest record created
        var staffView = await staff.GetAsync<PagedResult<BookingResponse>>($"/api/bookings?reference={created.Booking.Reference.ToLowerInvariant()}");
        var booking = Assert.Single(staffView.Items);
        Assert.Equal(cheapest.Id, booking.RoomId);
        Assert.Equal(BookingSource.Online, booking.Source);
        var guests = await staff.GetAsync<PagedResult<GuestResponse>>($"/api/guests?search={Uri.EscapeDataString(email)}");
        Assert.Equal(email, Assert.Single(guests.Items).Email);

        // Confirmation email with the code
        var mail = Assert.Single(Factory.Emails.To(email));
        Assert.Contains(created.Booking.Reference, mail.Subject);
        Assert.Contains(created.AccessCode, mail.TextBody);
    }

    [Fact]
    public async Task Booking_can_be_viewed_only_with_the_right_code()
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);
        var created = await BookAsync(slug, Request(hotel.Id, 5, 6));
        var url = $"/api/public/{slug}/bookings/{created.Booking.Reference}";
        var client = Anonymous();

        var ok = await client.SendAsync(WithCode(HttpMethod.Get, url, created.AccessCode.ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(created.Booking.Reference, (await ok.Content.ReadFromJsonAsync<PublicBookingResponse>(Json))!.Reference);

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(WithCode(HttpMethod.Get, url, "AAAA-BBBB-CCCC"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(WithCode(HttpMethod.Get, url, null))).StatusCode);

        // Same reference and code, but under another company's slug: not found.
        var otherSlug = (await (await NewCompanyAsync()).GetAsync<CompanyResponse>("/api/company")).Slug;
        var crossTenant = await client.SendAsync(WithCode(HttpMethod.Get,
            $"/api/public/{otherSlug}/bookings/{created.Booking.Reference}", created.AccessCode));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
    }

    [Fact]
    public async Task Guest_can_cancel_until_24_hours_before_arrival()
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);
        await AddRoomAsync(staff, hotel.Id, "102", RoomType.Double, 2, 100m);
        var early = await BookAsync(slug, Request(hotel.Id, 5, 7));
        var lastMinute = await BookAsync(slug, Request(hotel.Id, 1, 2)); // deadline was midnight today
        var client = Anonymous();

        var cancel = await client.SendAsync(WithCode(HttpMethod.Post,
            $"/api/public/{slug}/bookings/{early.Booking.Reference}/cancel", early.AccessCode));
        await TenantClient.AssertStatus(cancel, HttpStatusCode.OK);
        Assert.Equal(BookingStatus.Cancelled, (await cancel.Content.ReadFromJsonAsync<PublicBookingResponse>(Json))!.Status);
        Assert.Contains(Factory.Emails.To(early.Booking.GuestEmail!), m => m.Subject.StartsWith("Booking cancelled"));

        var again = await client.SendAsync(WithCode(HttpMethod.Post,
            $"/api/public/{slug}/bookings/{early.Booking.Reference}/cancel", early.AccessCode));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Null(lastMinute.Booking.CancellableUntil);
        var tooLate = await client.SendAsync(WithCode(HttpMethod.Post,
            $"/api/public/{slug}/bookings/{lastMinute.Booking.Reference}/cancel", lastMinute.AccessCode));
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);

        var staffView = await staff.GetAsync<PagedResult<BookingResponse>>("/api/bookings?status=Cancelled");
        Assert.Equal(early.Booking.Reference, Assert.Single(staffView.Items).Reference);
    }

    [Fact]
    public async Task Sold_out_room_type_is_a_conflict()
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);

        await BookAsync(slug, Request(hotel.Id, 20, 22));
        var second = await PostBookingAsync(slug, Request(hotel.Id, 21, 23));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var suite = await PostBookingAsync(slug, Request(hotel.Id, 20, 22, type: RoomType.Suite));
        Assert.Equal(HttpStatusCode.Conflict, suite.StatusCode);
    }

    [Fact]
    public async Task Concurrent_online_bookings_fill_exactly_the_available_rooms()
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);
        await AddRoomAsync(staff, hotel.Id, "102", RoomType.Double, 2, 110m);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostBookingAsync(slug, Request(hotel.Id, 30, 32))));

        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Returning_guest_is_matched_to_the_existing_guest_record()
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);
        var known = await staff.PostAsync<GuestResponse>("/api/guests",
            new GuestRequest { FirstName = "Known", LastName = "Guest", Email = UniqueEmail("known") });

        await BookAsync(slug, Request(hotel.Id, 3, 4, email: known.Email!.ToUpperInvariant()));

        var bookings = await staff.GetAsync<PagedResult<BookingResponse>>($"/api/bookings?guestId={known.Id}");
        Assert.Single(bookings.Items);
        Assert.Equal(1, (await staff.GetAsync<PagedResult<GuestResponse>>($"/api/guests?search={known.Email}")).TotalCount);
    }

    [Theory]
    [InlineData(-1, 1)] // check-in in the past
    [InlineData(3, 3)]  // zero nights
    public async Task Invalid_dates_are_bad_requests(int inOffset, int outOffset)
    {
        var (staff, slug, hotel) = await SetupAsync();
        await AddRoomAsync(staff, hotel.Id, "101", RoomType.Double, 2, 100m);
        var res = await PostBookingAsync(slug, Request(hotel.Id, inOffset, outOffset));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_company_or_hotel_is_not_found()
    {
        var (_, slug, _) = await SetupAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync("/api/public/no-such-company/hotels")).StatusCode);
        var res = await PostBookingAsync(slug, Request(Guid.NewGuid(), 3, 4));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Public_endpoints_are_rate_limited_per_client()
    {
        var (_, slug, _) = await SetupAsync();
        await using var limited = Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:PublicPerMinute"] = "3" })));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.GetAsync($"/api/public/{slug}/hotels")).StatusCode);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}

public class CompanySlugTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Slug_is_generated_from_the_name_and_unique()
    {
        var name = $"Nordic Stays {Guid.NewGuid():N}"[..20];
        var first = await NewCompanyAsync(name);
        var second = await NewCompanyAsync(name);

        var a = await first.GetAsync<CompanyResponse>("/api/company");
        var b = await second.GetAsync<CompanyResponse>("/api/company");
        Assert.StartsWith("nordic-stays-", a.Slug);
        Assert.Equal(a.Slug + "-2", b.Slug);
    }

    [Fact]
    public async Task Admin_can_change_the_slug_and_old_public_urls_stop_working()
    {
        var admin = await NewCompanyAsync();
        var oldSlug = (await admin.GetAsync<CompanyResponse>("/api/company")).Slug;
        var newSlug = $"renamed-{Guid.NewGuid():N}"[..20];

        var update = await admin.Http.PutAsJsonAsync("/api/company", new UpdateCompanyRequest { Name = "Renamed Co", Slug = newSlug }, Json);
        await TenantClient.AssertStatus(update, HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/api/public/{oldSlug}/hotels")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync($"/api/public/{newSlug}/hotels")).StatusCode);
    }

    [Fact]
    public async Task Slug_rules_are_enforced()
    {
        var admin = await NewCompanyAsync();
        var taken = (await (await NewCompanyAsync()).GetAsync<CompanyResponse>("/api/company")).Slug;
        var receptionist = await admin.CreateStaffAsync(UserRole.Receptionist);

        var duplicate = await admin.Http.PutAsJsonAsync("/api/company", new UpdateCompanyRequest { Name = "X", Slug = taken }, Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var invalid = await admin.Http.PutAsJsonAsync("/api/company", new UpdateCompanyRequest { Name = "X", Slug = "Not Valid!" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var forbidden = await receptionist.PutAsJsonAsync("/api/company", new UpdateCompanyRequest { Name = "X", Slug = "whatever" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
