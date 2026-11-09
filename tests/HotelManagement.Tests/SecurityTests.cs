using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HotelManagement.Tests;

public class LoginSecurityTests(ApiFactory factory) : ApiTestBase(factory)
{
    private Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password }, Json);

    [Fact]
    public async Task Account_locks_after_five_failed_logins()
    {
        var tenant = await NewCompanyAsync();
        var email = tenant.Auth.User.Email;
        var client = Anonymous();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, "Wrong-Password-1")).StatusCode);

        var withRightPassword = await LoginAsync(client, email, "Sup3rSecret!");
        Assert.Equal(HttpStatusCode.Unauthorized, withRightPassword.StatusCode);

        // The message is the same as for a wrong password or unknown email: nothing to learn from it.
        var unknown = await LoginAsync(client, UniqueEmail("nobody"), "Sup3rSecret!");
        Assert.Equal(await Detail(withRightPassword), await Detail(unknown));
    }

    [Fact]
    public async Task Login_and_signup_are_rate_limited_per_client()
    {
        await using var limited = Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AuthPerMinute"] = "3" })));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await LoginAsync(client, UniqueEmail(), "Whatever-123")).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }

    [Theory]
    [InlineData("password123")]
    [InlineData("1234567890")]
    [InlineData("short")]
    public async Task Weak_passwords_are_rejected_at_signup(string password)
    {
        var res = await Anonymous().PostAsJsonAsync("/api/auth/register", new RegisterCompanyRequest
        {
            CompanyName = "Weak Co", FullName = "Wendy", Email = UniqueEmail(), Password = password,
        }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Signup_with_existing_email_does_not_confirm_the_account_exists()
    {
        var tenant = await NewCompanyAsync();
        var res = await Anonymous().PostAsJsonAsync("/api/auth/register", new RegisterCompanyRequest
        {
            CompanyName = "Other", FullName = "Bob", Email = tenant.Auth.User.Email, Password = "Correct-Horse-Battery",
        }, Json);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.DoesNotContain("already exists", await Detail(res));
    }

    [Fact]
    public async Task Auth_responses_are_never_cached_and_have_security_headers()
    {
        var res = await LoginAsync(Anonymous(), UniqueEmail(), "Whatever-123");
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
    }

    private static async Task<string> Detail(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Json))!.Detail!;
}

public class SessionTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        await Anonymous().PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest { RefreshToken = refreshToken }, Json);

    private HttpClient WithToken(string accessToken)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    [Fact]
    public async Task Refresh_issues_a_new_pair_and_the_old_refresh_token_stops_working()
    {
        var tenant = await NewCompanyAsync();
        var first = tenant.Auth;

        var res = await RefreshAsync(first.RefreshToken);
        await TenantClient.AssertStatus(res, HttpStatusCode.OK);
        var second = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await WithToken(second.AccessToken).GetAsync("/api/auth/me")).StatusCode);

        // Replaying the used token is treated as theft: the whole session dies, including the newest token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var tenant = await NewCompanyAsync();
        var logout = await Anonymous().PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest { RefreshToken = tenant.Auth.RefreshToken }, Json);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tenant.Auth.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Access_tokens_are_short_lived()
    {
        var tenant = await NewCompanyAsync();
        var lifetime = tenant.Auth.ExpiresAt - DateTime.UtcNow;
        Assert.InRange(lifetime.TotalMinutes, 14, 15.1);
        Assert.InRange((tenant.Auth.RefreshTokenExpiresAt - DateTime.UtcNow).TotalDays, 13.9, 14.1);
    }

    [Fact]
    public async Task Deactivating_a_user_revokes_their_access_immediately()
    {
        var admin = await NewCompanyAsync();
        var email = UniqueEmail("staff");
        var staff = await admin.PostAsync<UserResponse>("/api/users", new CreateUserRequest
        {
            FullName = "Temp Staff", Email = email, Password = "Sup3rSecret!", Role = UserRole.Manager,
        });
        var login = await Anonymous().PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Sup3rSecret!" }, Json);
        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        var staffClient = WithToken(session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await staffClient.GetAsync("/api/hotels")).StatusCode);

        var update = await admin.Http.PutAsJsonAsync($"/api/users/{staff.Id}",
            new UpdateUserRequest { FullName = "Temp Staff", Role = UserRole.Manager, IsActive = false }, Json);
        await TenantClient.AssertStatus(update, HttpStatusCode.OK);

        // Same, still unexpired access token: rejected on the very next request. Refresh is dead too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await staffClient.GetAsync("/api/hotels")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Role_change_requires_a_new_token()
    {
        var admin = await NewCompanyAsync();
        var email = UniqueEmail("mgr");
        var user = await admin.PostAsync<UserResponse>("/api/users", new CreateUserRequest
        {
            FullName = "Mia Manager", Email = email, Password = "Sup3rSecret!", Role = UserRole.Manager,
        });
        var login = await Anonymous().PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Sup3rSecret!" }, Json);
        var oldToken = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!.AccessToken;

        await admin.Http.PutAsJsonAsync($"/api/users/{user.Id}",
            new UpdateUserRequest { FullName = "Mia Manager", Role = UserRole.Receptionist, IsActive = true }, Json);

        // The old token still says "Manager"; it must not keep working with the old rights.
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(oldToken).GetAsync("/api/hotels")).StatusCode);
    }
}

public class PersonalDataProtectionTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<Dictionary<string, object?>> RawGuestRowAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "FirstName", "LastName", "Email", "Phone", "EmailIndex" FROM "Guests" WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i));
    }

    [Fact]
    public async Task Guest_personal_data_is_encrypted_at_rest_but_readable_through_the_api()
    {
        var tenant = await NewCompanyAsync();
        var email = UniqueEmail("anna");
        var guest = await tenant.PostAsync<GuestResponse>("/api/guests",
            new GuestRequest { FirstName = "Anna", LastName = "Jensen", Email = email, Phone = "+45 2000 0001" });

        var row = await RawGuestRowAsync(guest.Id);
        foreach (var column in new[] { "FirstName", "LastName", "Email", "Phone" })
        {
            var stored = (string)row[column]!;
            Assert.StartsWith("enc:", stored);
            Assert.DoesNotContain("Anna", stored);
            Assert.DoesNotContain("Jensen", stored);
        }
        Assert.Matches("^[0-9a-f]{64}$", (string)row["EmailIndex"]!);

        var viaApi = await tenant.GetAsync<GuestResponse>($"/api/guests/{guest.Id}");
        Assert.Equal(("Anna", "Jensen", email), (viaApi.FirstName, viaApi.LastName, viaApi.Email));

        // Search still works on decrypted data, and duplicate emails are still caught via the blind index.
        Assert.Equal(1, (await tenant.GetAsync<PagedResult<GuestResponse>>("/api/guests?search=jens")).TotalCount);
        var duplicate = await tenant.Http.PostAsJsonAsync("/api/guests",
            new GuestRequest { FirstName = "Other", LastName = "Person", Email = email.ToUpperInvariant() }, Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Booking_notes_are_encrypted_at_rest()
    {
        var tenant = await NewCompanyAsync();
        var hotel = await tenant.CreateHotelAsync();
        var room = await tenant.CreateRoomAsync(hotel.Id);
        var guest = await tenant.CreateGuestAsync();
        var booking = await tenant.PostAsync<BookingResponse>("/api/bookings", new CreateBookingRequest
        {
            RoomId = room.Id, GuestId = guest.Id, CheckIn = Today.AddDays(1), CheckOut = Today.AddDays(2), Notes = "Allergic to nuts",
        });

        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "Notes" FROM "Bookings" WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", booking.Id);
        var stored = (string)(await command.ExecuteScalarAsync())!;

        Assert.StartsWith("enc:", stored);
        Assert.Equal("Allergic to nuts", (await tenant.GetAsync<BookingResponse>($"/api/bookings/{booking.Id}")).Notes);
    }

    [Fact]
    public async Task Guest_data_can_be_exported()
    {
        var tenant = await NewCompanyAsync();
        var hotel = await tenant.CreateHotelAsync("Export Hotel");
        var room = await tenant.CreateRoomAsync(hotel.Id, price: 90m);
        var guest = await tenant.CreateGuestAsync("Erik", "Export");
        var booking = await tenant.BookAsync(room.Id, guest.Id, Today.AddDays(2), Today.AddDays(4));
        await tenant.PostAsync<PaymentResponse>($"/api/bookings/{booking.Id}/payments",
            new CreatePaymentRequest { Amount = 50m, Method = PaymentMethod.Card });

        var export = await tenant.GetAsync<GuestDataExport>($"/api/guests/{guest.Id}/export");

        Assert.Equal(("Erik", "Export"), (export.Guest.FirstName, export.Guest.LastName));
        var stay = Assert.Single(export.Bookings);
        Assert.Equal("Export Hotel", stay.Hotel);
        Assert.Equal(180m, stay.TotalPrice);
        Assert.Equal(50m, Assert.Single(stay.Payments).Amount);

        var receptionist = await tenant.CreateStaffAsync(UserRole.Receptionist);
        Assert.Equal(HttpStatusCode.Forbidden, (await receptionist.GetAsync($"/api/guests/{guest.Id}/export")).StatusCode);
    }

    [Fact]
    public async Task Guest_can_be_anonymized_once_no_stay_is_pending()
    {
        var tenant = await NewCompanyAsync();
        var hotel = await tenant.CreateHotelAsync();
        var room = await tenant.CreateRoomAsync(hotel.Id);
        var guest = await tenant.PostAsync<GuestResponse>("/api/guests",
            new GuestRequest { FirstName = "Fiona", LastName = "Forget", Email = UniqueEmail("fiona"), Phone = "+45 1234" });
        var booking = await tenant.BookAsync(room.Id, guest.Id, Today.AddDays(3), Today.AddDays(5));

        // Upcoming stay: not yet.
        var blocked = await tenant.PostRawAsync($"/api/guests/{guest.Id}/anonymize");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        await tenant.PostAsync<BookingResponse>($"/api/bookings/{booking.Id}/cancel", new { }, HttpStatusCode.OK);
        var anonymized = await tenant.PostAsync<GuestResponse>($"/api/guests/{guest.Id}/anonymize", new { }, HttpStatusCode.OK);

        Assert.True(anonymized.IsAnonymized);
        Assert.Equal(("Anonymized", "Guest"), (anonymized.FirstName, anonymized.LastName));
        Assert.Null(anonymized.Email);
        Assert.Null(anonymized.Phone);

        var row = await RawGuestRowAsync(guest.Id);
        Assert.Null(row["Email"]);
        Assert.Null(row["EmailIndex"]);

        // The booking is kept for the books, but no longer points to a person.
        var kept = await tenant.GetAsync<BookingResponse>($"/api/bookings/{booking.Id}");
        Assert.Equal("Anonymized Guest", kept.GuestName);

        var edit = await tenant.Http.PutAsJsonAsync($"/api/guests/{guest.Id}", new GuestRequest { FirstName = "Back", LastName = "Again" }, Json);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }
}

public class ProductionSafetyTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private Exception? StartInProduction(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Encryption:KeyId"] = "prod1",
            ["Encryption:Key"] = NewKey(),
            ["Encryption:BlindIndexKey"] = NewKey(),
            ["Database:SeedSampleData"] = "false",
        };
        foreach (var (key, value) in overrides) settings[key] = value;

        using var app = Factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings));
        });
        try
        {
            using var client = app.CreateClient();
            var health = client.GetAsync("/health").GetAwaiter().GetResult();
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            return null;
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            return e;
        }
    }

    [Fact]
    public void Starts_in_production_with_real_secrets() => Assert.Null(StartInProduction([]));

    [Theory]
    [InlineData("Jwt:Key", "dev-only-signing-key-change-me-0123456789abcdef", "Jwt:Key")]
    [InlineData("Encryption:Key", "ZGV2LW9ubHktZW5jcnlwdGlvbi1rZXktMzJieXRlcyE=", "Encryption:Key")]
    [InlineData("Encryption:Key", "", "Encryption")]
    [InlineData("Database:SeedSampleData", "true", "SeedSampleData")]
    public void Refuses_to_start_in_production_with_unsafe_settings(string setting, string value, string expectedInMessage)
    {
        var error = StartInProduction(new() { [setting] = value });

        Assert.NotNull(error);
        Assert.Contains(expectedInMessage, error.ToString());
    }
}
