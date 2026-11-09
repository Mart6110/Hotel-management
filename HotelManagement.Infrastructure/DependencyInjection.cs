using HotelManagement.Application.Auth;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.Rooms;
using HotelManagement.Application.Users;
using HotelManagement.Infrastructure.Persistence;
using HotelManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotelManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Persistence. The connection string is resolved lazily so hosts/tests can override configuration.
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IHotelRepository, HotelRepository>();
        services.AddScoped<IRoomRepository, RoomRepository>();
        services.AddScoped<IGuestRepository, GuestRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();

        services.AddScoped<IUserQueries, UserQueries>();
        services.AddScoped<IHotelQueries, HotelQueries>();
        services.AddScoped<IRoomQueries, RoomQueries>();
        services.AddScoped<IGuestQueries, GuestQueries>();
        services.AddScoped<IBookingQueries, BookingQueries>();

        services.AddOptions<DatabaseOptions>().BindConfiguration("Database");
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<DemoDataSeeder>();

        // Security
        services.AddOptions<JwtOptions>().BindConfiguration("Jwt").ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();

        return services;
    }
}
