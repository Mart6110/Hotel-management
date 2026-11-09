using System.Text.Json.Serialization;
using HotelManagement.Api.Auth;
using HotelManagement.Api.Errors;
using HotelManagement.Api.OpenApi;
using HotelManagement.Api.RateLimiting;
using HotelManagement.Api.Security;
using HotelManagement.Application;
using HotelManagement.Application.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.PublicBooking;
using HotelManagement.Infrastructure;
using HotelManagement.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

// Composition root: the only place that knows about every layer.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure();

// Application settings
builder.Services.AddOptions<PublicBookingOptions>().BindConfiguration("PublicBooking");
builder.Services.AddOptions<LoginSecurityOptions>().BindConfiguration("LoginSecurity");
builder.Services.AddOptions<RefreshTokenOptions>().BindConfiguration("RefreshTokens");

// Web layer
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddApiDocumentation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddTransportSecurity(builder.Configuration);
builder.Services.AddPublicRateLimiting();
builder.Services.AddJwtAuthentication();

var app = builder.Build();

app.UseTransportSecurity(); // forwarded headers, HTTPS redirect + HSTS, security headers
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseApiDocumentation(); // Swagger UI at /swagger
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

// Fail fast on unsafe or invalid configuration (dev secrets in production, bad keys…) *before*
// touching the database. ValidateOnStart would otherwise only run at app.Run(), after migrations.
app.Services.GetRequiredService<IStartupValidator>().Validate();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
}

app.Run();

// Exposes Program to the integration test project (WebApplicationFactory<Program>).
public partial class Program;
