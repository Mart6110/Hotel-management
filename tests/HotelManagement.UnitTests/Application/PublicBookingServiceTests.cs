using HotelManagement.Application.Auth;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.PublicBooking;
using HotelManagement.Application.Rooms;
using HotelManagement.Application.Users;
using HotelManagement.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static HotelManagement.UnitTests.TestData;

namespace HotelManagement.UnitTests.Application;

public class GuestBookingServiceTests
{
    private const string Slug = "acme";
    private const string GoodCode = "GOOD-CODE-0001";

    private readonly IPublicTenantResolver _tenants = Substitute.For<IPublicTenantResolver>();
    private readonly IHotelRepository _hotels = Substitute.For<IHotelRepository>();
    private readonly IRoomRepository _rooms = Substitute.For<IRoomRepository>();
    private readonly IGuestRepository _guests = Substitute.For<IGuestRepository>();
    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IRoomReservation _reservation = Substitute.For<IRoomReservation>();
    private readonly IBookingCodeGenerator _codes = Substitute.For<IBookingCodeGenerator>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ITransaction _transaction = Substitute.For<ITransaction>();
    private readonly IBookingEmailNotifier _notifier = Substitute.For<IBookingEmailNotifier>();
    private readonly GuestBookingService _sut;

    private readonly Company _company = new("Acme", Slug);
    private readonly Hotel _hotel = NewHotel();

    public GuestBookingServiceTests()
    {
        _tenants.EnterAsync(Slug, Arg.Any<CancellationToken>()).Returns(_company);
        _hotels.GetByIdAsync(_hotel.Id, Arg.Any<CancellationToken>()).Returns(_hotel);
        _unitOfWork.BeginTransactionAsync(default).ReturnsForAnyArgs(_transaction);
        _codes.NewAccessCode().Returns(GoodCode);
        _hasher.Hash(GoodCode).Returns("hashed-code");
        _hasher.Verify("hashed-code", GoodCode).Returns(PasswordCheck.Success);

        _sut = new GuestBookingService(_tenants, _hotels, _rooms, _guests, _bookings, _reservation, _codes, _hasher,
            _unitOfWork, _notifier, Options.Create(new PublicBookingOptions()), new FakeTimeProvider(Now));
    }

    private PublicBookingRequest Request() => new()
    {
        HotelId = _hotel.Id, RoomType = RoomType.Double, CheckIn = Today.AddDays(3), CheckOut = Today.AddDays(5),
        NumberOfGuests = 2, FirstName = "Jane", LastName = "Doe", Email = "Jane@Example.com",
    };

    private void CandidateRooms(params Guid[] ids) =>
        _rooms.FindFreeRoomIdsAsync(_hotel.Id, RoomType.Double, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), 2, Arg.Any<CancellationToken>())
            .Returns(ids);

    [Fact]
    public async Task Falls_through_to_the_next_room_when_the_cheapest_was_just_taken()
    {
        Guid taken = Guid.NewGuid(), free = Guid.NewGuid();
        CandidateRooms(taken, free);
        var booking = NewBooking(startOffset: 3);
        _reservation.TryReserveAsync(taken, default!, default!, default, default, default).ReturnsForAnyArgs((Booking?)null, booking);

        var result = await _sut.CreateAsync(Slug, Request(), default);

        await _reservation.Received(1).TryReserveAsync(free, Arg.Any<Guest>(), Arg.Any<ReservationRequest>(),
            BookingSource.Online, "hashed-code", Arg.Any<CancellationToken>());
        await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        Assert.Equal(GoodCode, result.AccessCode);
        Assert.Equal(booking.Reference, result.Booking.Reference);
        await _notifier.Received(1).BookingConfirmedAsync(_company, Arg.Any<PublicBookingResponse>(), GoodCode, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_free_room_of_that_type_is_a_conflict_and_nothing_is_committed()
    {
        CandidateRooms();

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.CreateAsync(Slug, Request(), default));

        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        await _transaction.DidNotReceiveWithAnyArgs().CommitAsync(default);
        await _notifier.DidNotReceiveWithAnyArgs().BookingConfirmedAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task New_guest_is_created_in_the_resolved_company()
    {
        CandidateRooms(Guid.NewGuid());
        _reservation.TryReserveAsync(default, default!, default!, default, default, default).ReturnsForAnyArgs(NewBooking(startOffset: 3));

        await _sut.CreateAsync(Slug, Request(), default);

        _guests.Received(1).Add(Arg.Is<Guest>(g => g.Email == "jane@example.com" && g.CompanyId == _company.Id));
    }

    [Fact]
    public async Task Returning_guest_is_matched_by_email()
    {
        var existing = NewGuest();
        _guests.FindByEmailAsync("jane@example.com", Arg.Any<CancellationToken>()).Returns(existing);
        CandidateRooms(Guid.NewGuid());
        _reservation.TryReserveAsync(default, default!, default!, default, default, default).ReturnsForAnyArgs(NewBooking(startOffset: 3));

        await _sut.CreateAsync(Slug, Request(), default);

        _guests.DidNotReceiveWithAnyArgs().Add(default!);
        await _reservation.Received(1).TryReserveAsync(Arg.Any<Guid>(), existing, Arg.Any<ReservationRequest>(),
            Arg.Any<BookingSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Past_dates_are_rejected_before_touching_the_database()
    {
        var request = Request() with { CheckIn = Today.AddDays(-1) };

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.CreateAsync(Slug, request, default));

        Assert.Equal(ErrorKind.Validation, ex.Kind);
        await _unitOfWork.DidNotReceiveWithAnyArgs().BeginTransactionAsync(default);
    }

    [Theory]
    [InlineData("WRONG-CODE")]
    [InlineData("")]
    public async Task Wrong_access_code_looks_like_an_unknown_booking(string code)
    {
        var booking = Booking.Create(NewRoom(), NewGuest(), Today.AddDays(3), Today.AddDays(4), 1, null, Now,
            "BK-ABCD1234", BookingSource.Online, "hashed-code");
        _bookings.FindByReferenceAsync("BK-ABCD1234", Arg.Any<CancellationToken>()).Returns(booking);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.GetAsync(Slug, "bk-abcd1234", code, default));
        Assert.Equal(ErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Staff_bookings_cannot_be_opened_with_any_code()
    {
        var staffBooking = NewBooking(startOffset: 3); // no access code hash
        _bookings.FindByReferenceAsync(staffBooking.Reference, Arg.Any<CancellationToken>()).Returns(staffBooking);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.GetAsync(Slug, staffBooking.Reference, GoodCode, default));
        Assert.Equal(ErrorKind.NotFound, ex.Kind);
        _hasher.Received(1).Verify(null, GoodCode); // still hash-checks (against nothing), so timing doesn't differ
    }

    [Fact]
    public async Task Cancel_with_valid_code_cancels_saves_and_notifies()
    {
        var booking = Booking.Create(NewRoom(), NewGuest(), Today.AddDays(5), Today.AddDays(6), 1, null, Now,
            "BK-ABCD1234", BookingSource.Online, "hashed-code");
        _bookings.FindByReferenceAsync("BK-ABCD1234", Arg.Any<CancellationToken>()).Returns(booking);

        var result = await _sut.CancelAsync(Slug, "BK-ABCD1234", GoodCode.ToLowerInvariant(), default);

        Assert.Equal(BookingStatus.Cancelled, result.Status);
        Assert.Null(result.CancellableUntil);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.Received(1).BookingCancelledAsync(_company, Arg.Any<PublicBookingResponse>(), Arg.Any<CancellationToken>());
    }
}

public class PublicTenantResolverTests
{
    [Fact]
    public async Task Selects_the_company_for_the_rest_of_the_request()
    {
        var companies = Substitute.For<ICompanyRepository>();
        var company = new Company("Acme", "acme");
        companies.FindBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns(company);
        var tenant = new TenantContext(Substitute.For<ICurrentUser>());

        await new PublicTenantResolver(companies, tenant).EnterAsync(" ACME ", default);

        Assert.Equal(company.Id, tenant.CompanyId);
    }

    [Fact]
    public async Task Unknown_slug_is_not_found()
    {
        var resolver = new PublicTenantResolver(Substitute.For<ICompanyRepository>(), new TenantContext(Substitute.For<ICurrentUser>()));
        var ex = await Assert.ThrowsAsync<DomainException>(() => resolver.EnterAsync("nope", default));
        Assert.Equal(ErrorKind.NotFound, ex.Kind);
    }
}

public class BookingEmailNotifierTests
{
    private static PublicBookingResponse Booking(string? email = "jane@example.com") => new(
        "BK-ABCD1234", BookingStatus.Confirmed, "Harbour View", "1 Quay St, Copenhagen, Denmark", RoomType.Double,
        Today.AddDays(3), Today.AddDays(5), 2, 2, 120m, 240m, "Jane Doe", email, Now.AddDays(1), Now);

    [Fact]
    public async Task Confirmation_contains_reference_code_and_total()
    {
        var sender = Substitute.For<IEmailSender>();
        var notifier = new BookingEmailNotifier(sender, NullLogger<BookingEmailNotifier>.Instance);

        await notifier.BookingConfirmedAsync(new Company("Acme", "acme"), Booking(), "7K3P-9QXM-2B4D", default);

        await sender.Received(1).SendAsync(Arg.Is<EmailMessage>(m =>
            m.To == "jane@example.com" &&
            m.Subject.Contains("BK-ABCD1234") &&
            m.TextBody.Contains("7K3P-9QXM-2B4D") &&
            m.TextBody.Contains("240.00") &&
            m.TextBody.Contains("/api/public/acme/bookings/BK-ABCD1234")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delivery_failure_does_not_throw()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("SMTP down"));
        var notifier = new BookingEmailNotifier(sender, NullLogger<BookingEmailNotifier>.Instance);

        await notifier.BookingConfirmedAsync(new Company("Acme", "acme"), Booking(), "CODE", default); // no exception
    }

    [Fact]
    public async Task Guests_without_email_are_skipped()
    {
        var sender = Substitute.For<IEmailSender>();
        var notifier = new BookingEmailNotifier(sender, NullLogger<BookingEmailNotifier>.Instance);

        await notifier.BookingCancelledAsync(new Company("Acme", "acme"), Booking(email: null), default);

        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }
}

public class CompanySlugRegistrationTests
{
    private readonly ICompanyRepository _companies = Substitute.For<ICompanyRepository>();
    private readonly AuthService _sut;

    public CompanySlugRegistrationTests()
    {
        _sut = new AuthService(_companies, Substitute.For<IUserRepository>(), Substitute.For<IUnitOfWork>(),
            Substitute.For<IPasswordHasher>(), new PasswordPolicy(), Substitute.For<IAuthSessionService>(),
            Substitute.For<ICurrentUser>(), Options.Create(new LoginSecurityOptions()), new FakeTimeProvider(Now));
    }

    private static RegisterCompanyRequest Request(string? slug = null) => new()
    {
        CompanyName = "Demo Hotels", FullName = "Owner", Email = "owner@x.com", Password = "Sup3rSecret!", CompanySlug = slug,
    };

    [Fact]
    public async Task Generated_slug_gets_a_suffix_when_taken()
    {
        _companies.SlugExistsAsync("demo-hotels", Arg.Any<CancellationToken>()).Returns(true);
        _companies.SlugExistsAsync("demo-hotels-2", Arg.Any<CancellationToken>()).Returns(true);

        await _sut.RegisterCompanyAsync(Request(), default);

        _companies.Received(1).Add(Arg.Is<Company>(c => c.Slug == "demo-hotels-3"));
    }

    [Fact]
    public async Task Requested_slug_that_is_taken_is_a_conflict()
    {
        _companies.SlugExistsAsync("taken", Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.RegisterCompanyAsync(Request("Taken"), default));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }
}
