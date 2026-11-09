using HotelManagement.Application.Auth;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.PublicBooking;
using HotelManagement.Application.Rooms;
using HotelManagement.Application.Users;
using HotelManagement.Infrastructure.Email;
using HotelManagement.Infrastructure.Persistence;
using HotelManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IHotelRepository, HotelRepository>();
        services.AddScoped<IRoomRepository, RoomRepository>();
        services.AddScoped<IGuestRepository, GuestRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();

        services.AddScoped<IUserQueries, UserQueries>();
        services.AddScoped<IHotelQueries, HotelQueries>();
        services.AddScoped<IRoomQueries, RoomQueries>();
        services.AddScoped<IGuestQueries, GuestQueries>();
        services.AddScoped<IBookingQueries, BookingQueries>();
        services.AddScoped<IPublicCatalogQueries, PublicCatalogQueries>();

        services.AddOptions<DatabaseOptions>().BindConfiguration("Database").ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseProductionValidator>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<PersonalDataBackfill>();

        // Security. Every option set is validated at startup, so a misconfigured server fails fast
        // instead of running with weak or public secrets.
        services.AddOptions<JwtOptions>().BindConfiguration("Jwt").ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtProductionValidator>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IBookingCodeGenerator, BookingCodeGenerator>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();

        // Encryption of personal data at rest
        services.AddOptions<EncryptionOptions>().BindConfiguration("Encryption").ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();
        services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();
        services.AddSingleton<IBlindIndex, HmacBlindIndex>();

        // Email: real SMTP when a host is configured, otherwise just log the message.
        services.AddOptions<EmailOptions>().BindConfiguration("Email");
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<LoggingEmailSender>();
        services.AddSingleton<IEmailSender>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<EmailOptions>>().Value.Host)
                ? sp.GetRequiredService<LoggingEmailSender>()
                : sp.GetRequiredService<SmtpEmailSender>());

        return services;
    }
}
