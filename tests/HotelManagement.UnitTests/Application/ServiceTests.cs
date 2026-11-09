using HotelManagement.Application.Auth;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.Rooms;
using HotelManagement.Application.Users;
using HotelManagement.Domain;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static HotelManagement.UnitTests.TestData;

namespace HotelManagement.UnitTests.Application;

public class BookingServiceTests
{
    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IRoomRepository _rooms = Substitute.For<IRoomRepository>();
    private readonly IGuestRepository _guests = Substitute.For<IGuestRepository>();
    private readonly IBookingQueries _queries = Substitute.For<IBookingQueries>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ITransaction _transaction = Substitute.For<ITransaction>();
    private readonly IBookingCodeGenerator _codes = Substitute.For<IBookingCodeGenerator>();
    private readonly BookingService _sut;

    private readonly Room _room = NewRoom(price: 100m);
    private readonly Guest _guest = NewGuest();

    public BookingServiceTests()
    {
        var clock = new FakeTimeProvider(Now);
        _unitOfWork.BeginTransactionAsync(default).ReturnsForAnyArgs(_transaction);
        _rooms.GetByIdAsync(_room.Id, default).ReturnsForAnyArgs(_room);
        _guests.GetByIdAsync(_guest.Id, default).ReturnsForAnyArgs(_guest);
        _queries.GetAsync(default, default).ReturnsForAnyArgs(call => Response(call.Arg<Guid>()));
        _codes.NewReference().Returns("BK-TEST0001");

        // Real reservation logic over mocked repositories: verifies the full lock -> check -> add sequence.
        var reservation = new RoomReservation(_rooms, _bookings, _codes, clock);
        _sut = new BookingService(_bookings, _guests, reservation, _queries, _unitOfWork, clock);
    }

    private CreateBookingRequest Request() => new()
    {
        RoomId = _room.Id, GuestId = _guest.Id, CheckIn = Today.AddDays(1), CheckOut = Today.AddDays(3), NumberOfGuests = 2,
    };

    [Fact]
    public async Task Creates_booking_inside_a_locked_transaction()
    {
        var result = await _sut.CreateAsync(Request(), default);

        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _rooms.LockForBookingAsync(_room.Id, Arg.Any<CancellationToken>());
            _bookings.Add(Arg.Is<Booking>(b =>
                b.RoomId == _room.Id && b.TotalPrice == 200m &&
                b.Reference == "BK-TEST0001" && b.Source == BookingSource.Staff && b.AccessCodeHash == null));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _transaction.CommitAsync(Arg.Any<CancellationToken>());
        });
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Overlapping_booking_is_a_conflict_and_nothing_is_saved()
    {
        _bookings.HasOverlapAsync(_room.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.CreateAsync(Request(), default));

        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        _bookings.DidNotReceiveWithAnyArgs().Add(default!);
        await _transaction.DidNotReceiveWithAnyArgs().CommitAsync(default);
    }

    [Fact]
    public async Task Unknown_guest_is_not_found()
    {
        _guests.GetByIdAsync(default, default).ReturnsForAnyArgs((Guest?)null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.CreateAsync(Request(), default));
        Assert.Equal(ErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Check_in_saves_the_state_change()
    {
        var booking = NewBooking();
        _bookings.GetByIdAsync(booking.Id, default).ReturnsForAnyArgs(booking);

        await _sut.CheckInAsync(booking.Id, default);

        Assert.Equal(BookingStatus.CheckedIn, booking.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static BookingResponse Response(Guid id) => new(
        id, Guid.Empty, "", Guid.Empty, "", Guid.Empty, "", Today, Today.AddDays(1), 1, 0, 0,
        BookingStatus.Confirmed, null, Now, null, null, null, "BK-TEST0001", BookingSource.Staff);
}

public class PaymentServiceTests
{
    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _unitOfWork.BeginTransactionAsync(default).ReturnsForAnyArgs(Substitute.For<ITransaction>());
        _sut = new PaymentService(_bookings, _unitOfWork, _currentUser, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Locks_the_booking_before_checking_the_balance_and_records_who_paid()
    {
        var booking = NewBooking(price: 100m);
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _bookings.GetByIdAsync(booking.Id, default).ReturnsForAnyArgs(booking);

        var payment = await _sut.RecordAsync(booking.Id, new CreatePaymentRequest { Amount = 50m, Method = PaymentMethod.Card }, default);

        Received.InOrder(() =>
        {
            _bookings.LockAsync(booking.Id, Arg.Any<CancellationToken>());
            _bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>());
        });
        Assert.Equal(50m, payment.Amount);
        Assert.Equal(150m, booking.Balance);
        Assert.Equal(userId, booking.Payments.Single().RecordedByUserId);
    }

    [Fact]
    public async Task Refund_type_records_a_refund()
    {
        var booking = NewBooking(price: 100m);
        booking.RecordPayment(100m, PaymentMethod.Card, null, Now, null);
        _bookings.GetByIdAsync(booking.Id, default).ReturnsForAnyArgs(booking);

        var refund = await _sut.RecordAsync(booking.Id,
            new CreatePaymentRequest { Amount = 30m, Method = PaymentMethod.Card, Type = PaymentType.Refund }, default);

        Assert.Equal(PaymentType.Refund, refund.Type);
        Assert.Equal(130m, booking.Balance);
    }
}

public class InvoiceServiceTests
{
    [Fact]
    public void Builds_lines_and_totals_from_the_booking()
    {
        var booking = NewBooking(price: 150m, nights: 2);
        booking.RecordPayment(100m, PaymentMethod.Card, null, Now, null);

        var invoice = InvoiceService.Build(booking);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(150m, line.UnitPrice);
        Assert.Equal(300m, invoice.AmountDue);
        Assert.Equal(200m, invoice.Balance);
        Assert.Equal("Jane Doe", invoice.GuestName);
        Assert.StartsWith("INV-20260601-", invoice.InvoiceNumber);
    }

    [Fact]
    public void Cancelled_booking_has_no_lines()
    {
        var booking = NewBooking();
        booking.Cancel(Now);
        Assert.Empty(InvoiceService.Build(booking).Lines);
    }
}

public class DeletionGuardTests
{
    [Fact]
    public async Task Hotel_with_rooms_cannot_be_deleted()
    {
        var hotels = Substitute.For<IHotelRepository>();
        var hotel = NewHotel();
        hotels.GetByIdAsync(hotel.Id, default).ReturnsForAnyArgs(hotel);
        hotels.HasRoomsAsync(hotel.Id, default).ReturnsForAnyArgs(true);
        var sut = new HotelService(hotels, Substitute.For<IHotelQueries>(), Substitute.For<IUnitOfWork>());

        var ex = await Assert.ThrowsAsync<DomainException>(() => sut.DeleteAsync(hotel.Id, default));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        hotels.DidNotReceiveWithAnyArgs().Remove(default!);
    }

    [Fact]
    public async Task Guest_with_bookings_cannot_be_deleted()
    {
        var guests = Substitute.For<IGuestRepository>();
        var guest = NewGuest();
        guests.GetByIdAsync(guest.Id, default).ReturnsForAnyArgs(guest);
        guests.HasBookingsAsync(guest.Id, default).ReturnsForAnyArgs(true);
        var sut = new GuestService(guests, Substitute.For<IGuestQueries>(), Substitute.For<IUnitOfWork>());

        await Assert.ThrowsAsync<DomainException>(() => sut.DeleteAsync(guest.Id, default));
        guests.DidNotReceiveWithAnyArgs().Remove(default!);
    }

    [Fact]
    public async Task Room_with_bookings_cannot_be_deleted()
    {
        var rooms = Substitute.For<IRoomRepository>();
        var room = NewRoom();
        rooms.GetByIdAsync(room.Id, default).ReturnsForAnyArgs(room);
        rooms.HasBookingsAsync(room.Id, default).ReturnsForAnyArgs(true);
        var sut = new RoomService(rooms, Substitute.For<IHotelRepository>(), Substitute.For<IRoomQueries>(), Substitute.For<IUnitOfWork>());

        await Assert.ThrowsAsync<DomainException>(() => sut.DeleteAsync(room.Id, default));
        rooms.DidNotReceiveWithAnyArgs().Remove(default!);
    }
}
