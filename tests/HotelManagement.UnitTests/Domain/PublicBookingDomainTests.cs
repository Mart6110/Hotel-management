using HotelManagement.Domain;
using static HotelManagement.UnitTests.TestData;

namespace HotelManagement.UnitTests.Domain;

public class GuestCancellationTests
{
    private static readonly TimeSpan Cutoff = TimeSpan.FromHours(24);

    private static Booking OnlineBooking(int startOffset) =>
        Booking.Create(NewRoom(), NewGuest(), Today.AddDays(startOffset), Today.AddDays(startOffset + 2), 1, null, Now,
            "BK-ONLINE01", BookingSource.Online, accessCodeHash: "hash");

    [Fact]
    public void Deadline_is_cutoff_before_start_of_arrival_day()
    {
        var booking = OnlineBooking(startOffset: 5);
        var arrivalMidnight = Today.AddDays(5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        Assert.Equal(arrivalMidnight.AddHours(-24), booking.GuestCancellationDeadline(Cutoff));
    }

    [Fact]
    public void Guest_can_cancel_before_the_deadline()
    {
        var booking = OnlineBooking(startOffset: 3);
        booking.CancelByGuest(Now, Cutoff);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Theory]
    [InlineData(0)] // arriving today
    [InlineData(1)] // arriving tomorrow: deadline was midnight today, already passed at noon
    public void Guest_cannot_cancel_after_the_deadline(int startOffset)
    {
        var booking = OnlineBooking(startOffset);
        var ex = Assert.Throws<DomainException>(() => booking.CancelByGuest(Now, Cutoff));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        Assert.Contains("contact the hotel", ex.Message);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void Online_booking_requires_an_access_code()
    {
        Assert.Throws<ArgumentException>(() => Booking.Create(
            NewRoom(), NewGuest(), Today, Today.AddDays(1), 1, null, Now, "BK-X", BookingSource.Online, accessCodeHash: null));
    }

    [Fact]
    public void Booking_records_reference_and_source()
    {
        var booking = OnlineBooking(startOffset: 1);
        Assert.Equal("BK-ONLINE01", booking.Reference);
        Assert.Equal(BookingSource.Online, booking.Source);
    }
}

public class CompanySlugTests
{
    [Theory]
    [InlineData("Demo Hotels Group", "demo-hotels-group")]
    [InlineData("  Hôtel Du Lac & Spa!  ", "hotel-du-lac-spa")]
    [InlineData("Ålesund  Hotels", "alesund-hotels")]
    [InlineData("AB", "ab-company")]
    [InlineData("!!!", "company")]
    public void Generates_slugs_from_names(string name, string expected)
    {
        Assert.Equal(expected, CompanySlug.FromName(name));
    }

    [Fact]
    public void Generated_slugs_are_at_most_50_characters()
    {
        var slug = CompanySlug.FromName(new string('a', 80));
        Assert.Equal(50, slug.Length);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("double--hyphen")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("ümlaut")]
    public void Rejects_invalid_slugs(string slug)
    {
        var ex = Assert.Throws<DomainException>(() => CompanySlug.Validate(slug));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public void Normalizes_case_and_whitespace()
    {
        Assert.Equal("my-hotels", CompanySlug.Validate("  My-Hotels "));
    }
}
