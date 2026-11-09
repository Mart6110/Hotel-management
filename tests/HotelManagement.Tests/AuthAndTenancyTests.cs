using System.Net;
using System.Net.Http.Json;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;
using HotelManagement.Application.Common;

namespace HotelManagement.Tests;

public class AuthTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var res = await Anonymous().GetAsync("/api/hotels");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Register_then_login_returns_working_token()
    {
        var email = UniqueEmail();
        var client = Anonymous();
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterCompanyRequest
        {
            CompanyName = "Acme Hotels", FullName = "Alice", Email = email, Password = "Sup3rSecret!",
        }, Json);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email.ToUpperInvariant(), Password = "Sup3rSecret!" }, Json);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.Equal(UserRole.Admin, auth.User.Role);

        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me", Json);
        Assert.Equal(email, me!.Email);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        var tenant = await NewCompanyAsync();
        var res = await Anonymous().PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = tenant.Auth.User.Email, Password = "wrong-password" }, Json);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Register_with_existing_email_conflicts()
    {
        var tenant = await NewCompanyAsync();
        var res = await Anonymous().PostAsJsonAsync("/api/auth/register", new RegisterCompanyRequest
        {
            CompanyName = "Other", FullName = "Bob", Email = tenant.Auth.User.Email, Password = "Sup3rSecret!",
        }, Json);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_request_body_returns_validation_problem()
    {
        var res = await Anonymous().PostAsJsonAsync("/api/auth/register",
            new { companyName = "", fullName = "x", email = "not-an-email", password = "short" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

public class RoleTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Receptionist_can_manage_guests_but_not_hotels_or_users()
    {
        var tenant = await NewCompanyAsync();
        var receptionist = await tenant.CreateStaffAsync(UserRole.Receptionist);

        var hotel = await receptionist.PostAsJsonAsync("/api/hotels", new HotelRequest
        {
            Name = "Nope", Address = "a", City = "b", Country = "c",
        }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, hotel.StatusCode);

        var users = await receptionist.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);

        var guest = await receptionist.PostAsJsonAsync("/api/guests", new GuestRequest { FirstName = "A", LastName = "B" }, Json);
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
    }

    [Fact]
    public async Task Manager_can_create_hotels_but_not_delete_them_or_manage_users()
    {
        var tenant = await NewCompanyAsync();
        var manager = await tenant.CreateStaffAsync(UserRole.Manager);

        var create = await manager.PostAsJsonAsync("/api/hotels", new HotelRequest
        {
            Name = "Managed", Address = "a", City = "b", Country = "c",
        }, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var hotel = (await create.Content.ReadFromJsonAsync<HotelResponse>(Json))!;

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/api/hotels/{hotel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await tenant.Http.DeleteAsync($"/api/hotels/{hotel.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in()
    {
        var tenant = await NewCompanyAsync();
        var email = UniqueEmail("staff");
        var user = await tenant.PostAsync<UserResponse>("/api/users", new CreateUserRequest
        {
            FullName = "Temp", Email = email, Password = "Sup3rSecret!", Role = UserRole.Receptionist,
        });
        var update = await tenant.Http.PutAsJsonAsync($"/api/users/{user.Id}",
            new UpdateUserRequest { FullName = "Temp", Role = UserRole.Receptionist, IsActive = false }, Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var login = await Anonymous().PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Sup3rSecret!" }, Json);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}

public class TenancyTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Companies_cannot_see_or_use_each_others_data()
    {
        var a = await NewCompanyAsync("Company A");
        var b = await NewCompanyAsync("Company B");

        var hotel = await a.CreateHotelAsync("A's Hotel");
        var room = await a.CreateRoomAsync(hotel.Id);
        var guestA = await a.CreateGuestAsync();
        var booking = await a.BookAsync(room.Id, guestA.Id, Today.AddDays(1), Today.AddDays(2));

        // Reads: B sees nothing of A's.
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/hotels/{hotel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/guests/{guestA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/bookings/{booking.Id}/invoice")).StatusCode);
        Assert.Equal(0, (await b.GetAsync<PagedResult<HotelResponse>>("/api/hotels")).TotalCount);
        Assert.Equal(0, (await b.GetAsync<PagedResult<BookingResponse>>("/api/bookings")).TotalCount);
        Assert.Equal(1, (await b.GetAsync<PagedResult<UserResponse>>("/api/users")).TotalCount);

        // Writes: B can't attach data to A's records.
        var roomInA = await b.Http.PostAsJsonAsync("/api/rooms", new RoomRequest
        {
            HotelId = hotel.Id, Number = "999", Type = RoomType.Single, Capacity = 1, PricePerNight = 10, Floor = 1,
        }, Json);
        Assert.Equal(HttpStatusCode.NotFound, roomInA.StatusCode);

        var guestB = await b.CreateGuestAsync();
        var bookInA = await b.BookRawAsync(room.Id, guestB.Id, Today.AddDays(5), Today.AddDays(6));
        Assert.Equal(HttpStatusCode.NotFound, bookInA.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await b.PostRawAsync($"/api/bookings/{booking.Id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.DeleteAsync($"/api/guests/{guestA.Id}")).StatusCode);
    }

    [Fact]
    public async Task Same_guest_email_is_allowed_in_different_companies()
    {
        var a = await NewCompanyAsync();
        var b = await NewCompanyAsync();
        var guest = new GuestRequest { FirstName = "Sam", LastName = "Shared", Email = "sam@shared.example" };

        await a.PostAsync<GuestResponse>("/api/guests", guest);
        await b.PostAsync<GuestResponse>("/api/guests", guest);
        var duplicate = await a.Http.PostAsJsonAsync("/api/guests", guest, Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }
}
