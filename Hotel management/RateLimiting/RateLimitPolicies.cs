using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HotelManagement.Api.RateLimiting;

public class RateLimitOptions
{
    /// <summary>Requests per minute per client IP for all public endpoints.</summary>
    public int PublicPerMinute { get; set; } = 60;

    /// <summary>Bookings and cancellations per minute per client IP (also stops guessing access codes).</summary>
    public int PublicWritePerMinute { get; set; } = 10;

    /// <summary>Login, sign-up, refresh and logout attempts per minute per client IP (stops password guessing).</summary>
    public int AuthPerMinute { get; set; } = 10;
}

/// <summary>Per-IP limits for every anonymous endpoint. Authenticated staff endpoints are not limited.</summary>
public static class RateLimitPolicies
{
    public const string Public = "public";
    public const string PublicWrite = "public-write";
    public const string Auth = "auth";

    public static IServiceCollection AddPublicRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>().BindConfiguration("RateLimiting");
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>((options, limits) =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Public, context => PerIp(context, Public, limits.Value.PublicPerMinute));
            options.AddPolicy(PublicWrite, context => PerIp(context, PublicWrite, limits.Value.PublicWritePerMinute));
            options.AddPolicy(Auth, context => PerIp(context, Auth, limits.Value.AuthPerMinute));
        });
        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext context, string policy, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{policy}:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
}
