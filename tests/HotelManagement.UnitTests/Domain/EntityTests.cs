using HotelManagement.Domain;
using static HotelManagement.UnitTests.TestData;

namespace HotelManagement.UnitTests.Domain;

public class RoomTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(21, 100)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public void Rejects_invalid_capacity_or_price(int capacity, decimal price)
    {
        var ex = Assert.Throws<DomainException>(() => new Room(NewHotel(), "101", RoomType.Double, capacity, price, 1));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public void Inherits_company_from_hotel()
    {
        var room = NewRoom();
        Assert.Equal(CompanyId, room.CompanyId);
    }

    [Fact]
    public void Cannot_move_to_another_companys_hotel()
    {
        var room = NewRoom();
        var foreignHotel = new Hotel("Other", "a", "b", "c", null, null);
        foreignHotel.AssignCompany(Guid.NewGuid());

        Assert.Throws<DomainException>(() =>
            room.Update(foreignHotel, "101", RoomType.Double, 2, 100m, 1, true));
    }
}

public class TenantEntityTests
{
    [Fact]
    public void Company_can_be_assigned_once_but_not_changed()
    {
        var guest = new Guest("A", "B", null, null);
        var company = Guid.NewGuid();
        guest.AssignCompany(company);
        guest.AssignCompany(company); // idempotent

        Assert.Throws<InvalidOperationException>(() => guest.AssignCompany(Guid.NewGuid()));
    }
}

public class GuestTests
{
    [Fact]
    public void Normalizes_input()
    {
        var guest = NewGuest();
        Assert.Equal("jane@example.com", guest.Email);
        Assert.Equal("Jane Doe", guest.FullName);
    }

    [Fact]
    public void Blank_optional_fields_become_null()
    {
        var guest = new Guest("A", "B", "  ", "");
        Assert.Null(guest.Email);
        Assert.Null(guest.Phone);
    }

    [Fact]
    public void Requires_names()
    {
        Assert.Throws<DomainException>(() => new Guest(" ", "B", null, null));
    }
}

public class UserTests
{
    [Fact]
    public void Email_is_normalized()
    {
        var user = new User(CompanyId, "  Admin@Example.COM ", "Admin", UserRole.Admin, "hash");
        Assert.Equal("admin@example.com", user.Email);
    }

    [Theory]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Admin, false)]
    public void Admin_cannot_demote_or_deactivate_themselves(UserRole newRole, bool isActive)
    {
        var admin = new User(CompanyId, "a@x.com", "Admin", UserRole.Admin, "hash");
        var ex = Assert.Throws<DomainException>(() => admin.Update("Admin", newRole, isActive, changedBy: admin.Id));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Admin_can_demote_another_user()
    {
        var other = new User(CompanyId, "b@x.com", "Other", UserRole.Admin, "hash");
        other.Update("Other", UserRole.Receptionist, false, changedBy: Guid.NewGuid());
        Assert.Equal(UserRole.Receptionist, other.Role);
        Assert.False(other.IsActive);
    }
}
