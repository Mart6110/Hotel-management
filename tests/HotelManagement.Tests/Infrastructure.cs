using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HotelManagement.Application.Contracts;
using HotelManagement.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace HotelManagement.Tests;

/// <summary>Boots the real API against a throwaway PostgreSQL container (requires Docker).</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _db.GetConnectionString() + ";GSS Encryption Mode=Disable",
            ["Database:SeedSampleData"] = "false",
            ["Jwt:Key"] = "integration-test-signing-key-0123456789abcdef",
        }));
    }

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

[Collection(ApiCollection.Name)]
public abstract class ApiTestBase(ApiFactory factory)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    protected HttpClient Anonymous() => factory.CreateClient();

    protected static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@test.local";

    /// <summary>Registers a brand-new company and returns a client authenticated as its Admin.</summary>
    protected async Task<TenantClient> NewCompanyAsync(string? name = null)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterCompanyRequest
        {
            CompanyName = name ?? $"Company {Guid.NewGuid():N}",
            FullName = "Test Admin",
            Email = UniqueEmail("admin"),
            Password = "Sup3rSecret!",
        }, Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new TenantClient(factory, client, auth);
    }
}

/// <summary>An authenticated client for one company, with helpers to create test data.</summary>
public class TenantClient(ApiFactory factory, HttpClient http, AuthResponse auth)
{
    public HttpClient Http { get; } = http;
    public AuthResponse Auth { get; } = auth;

    public async Task<T> PostAsync<T>(string url, object body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var res = await Http.PostAsJsonAsync(url, body, ApiTestBase.Json);
        await AssertStatus(res, expected);
        return (await res.Content.ReadFromJsonAsync<T>(ApiTestBase.Json))!;
    }

    public async Task<T> GetAsync<T>(string url)
    {
        var res = await Http.GetAsync(url);
        await AssertStatus(res, HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<T>(ApiTestBase.Json))!;
    }

    public Task<HttpResponseMessage> PostRawAsync(string url, object? body = null) =>
        Http.PostAsJsonAsync(url, body ?? new { }, ApiTestBase.Json);

    public Task<HotelResponse> CreateHotelAsync(string name = "Test Hotel") =>
        PostAsync<HotelResponse>("/api/hotels", new HotelRequest
        {
            Name = name, Address = "1 Test Street", City = "Testville", Country = "Testland",
        });

    public Task<RoomResponse> CreateRoomAsync(Guid hotelId, string number = "101", int capacity = 2, decimal price = 100m) =>
        PostAsync<RoomResponse>("/api/rooms", new RoomRequest
        {
            HotelId = hotelId, Number = number, Type = RoomType.Double, Capacity = capacity, PricePerNight = price, Floor = 1,
        });

    public Task<GuestResponse> CreateGuestAsync(string first = "Jane", string last = "Doe") =>
        PostAsync<GuestResponse>("/api/guests", new GuestRequest { FirstName = first, LastName = last });

    public Task<HttpResponseMessage> BookRawAsync(Guid roomId, Guid guestId, DateOnly checkIn, DateOnly checkOut, int guests = 1) =>
        PostRawAsync("/api/bookings", new CreateBookingRequest
        {
            RoomId = roomId, GuestId = guestId, CheckIn = checkIn, CheckOut = checkOut, NumberOfGuests = guests,
        });

    public async Task<BookingResponse> BookAsync(Guid roomId, Guid guestId, DateOnly checkIn, DateOnly checkOut, int guests = 1)
    {
        var res = await BookRawAsync(roomId, guestId, checkIn, checkOut, guests);
        await AssertStatus(res, HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<BookingResponse>(ApiTestBase.Json))!;
    }

    /// <summary>Creates a staff user in this company and returns a client logged in as them.</summary>
    public async Task<HttpClient> CreateStaffAsync(UserRole role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        await PostAsync<UserResponse>("/api/users", new CreateUserRequest
        {
            FullName = $"Test {role}", Email = email, Password = "Sup3rSecret!", Role = role,
        });
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Sup3rSecret!" }, ApiTestBase.Json);
        await AssertStatus(login, HttpStatusCode.OK);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiTestBase.Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    public static async Task AssertStatus(HttpResponseMessage res, HttpStatusCode expected)
    {
        if (res.StatusCode != expected)
        {
            var body = await res.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)res.StatusCode} {res.StatusCode}: {body}");
        }
    }
}
