using HotelManagement.Application.Auth;
using HotelManagement.Application.Bookings;
using HotelManagement.Application.Common;
using HotelManagement.Application.Companies;
using HotelManagement.Application.Guests;
using HotelManagement.Application.Hotels;
using HotelManagement.Application.PublicBooking;
using HotelManagement.Application.Rooms;
using HotelManagement.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HotelManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // One tenant context per request, exposed through two narrow interfaces.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantSelector>(sp => sp.GetRequiredService<TenantContext>());

        // Staff (back-office) use cases
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddScoped<ISessionValidator, SessionValidator>();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddOptions<LoginSecurityOptions>();
        services.AddOptions<RefreshTokenOptions>();
        services.AddScoped<IGuestPrivacyService, GuestPrivacyService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IHotelService, HotelService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IGuestService, GuestService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IRoomReservation, RoomReservation>();

        // Public (guest-facing) use cases
        services.AddScoped<IPublicTenantResolver, PublicTenantResolver>();
        services.AddScoped<IPublicCatalogService, PublicCatalogService>();
        services.AddScoped<IGuestBookingService, GuestBookingService>();
        services.AddScoped<IBookingEmailNotifier, BookingEmailNotifier>();
        services.AddOptions<PublicBookingOptions>();

        return services;
    }
}
