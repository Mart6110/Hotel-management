using Microsoft.AspNetCore.HttpOverrides;

namespace HotelManagement.Api.Security;

public class TransportSecurityOptions
{
    /// <summary>
    /// The API runs behind a TLS-terminating reverse proxy (e.g. Caddy in docker-compose.prod.yml).
    /// Trusts its X-Forwarded-For/-Proto headers so the app sees the real client IP (rate limiting)
    /// and knows the original request was HTTPS. Only enable it when the API port is not publicly reachable.
    /// </summary>
    public bool BehindProxy { get; set; }

    /// <summary>Outside Development: redirect HTTP to HTTPS and send HSTS.</summary>
    public bool RequireHttps { get; set; } = true;

    public int HstsMaxAgeDays { get; set; } = 365;
}

public static class TransportSecurity
{
    public static IServiceCollection AddTransportSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Https").Get<TransportSecurityOptions>() ?? new TransportSecurityOptions();
        services.AddSingleton(options);

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // The proxy's address on the Docker network isn't fixed; the API port isn't exposed, so only the proxy can reach it.
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            o.ForwardLimit = 1;
        });
        services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(options.HstsMaxAgeDays);
            o.IncludeSubDomains = true;
        });
        return services;
    }

    /// <summary>Must run first in the pipeline, so everything after it sees the real scheme and client IP.</summary>
    public static WebApplication UseTransportSecurity(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<TransportSecurityOptions>();
        if (options.BehindProxy)
            app.UseForwardedHeaders();

        if (!app.Environment.IsDevelopment() && options.RequireHttps)
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        // Conservative headers for a JSON API. Added in OnStarting (just before headers are sent) because
        // the exception handler clears headers when it turns an error into a problem response.
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                if (context.Request.Path.StartsWithSegments("/api/auth"))
                    headers.CacheControl = "no-store"; // never cache responses containing tokens
                return Task.CompletedTask;
            });
            await next();
        });
        return app;
    }
}
