using HotelManagement.Application.Auth;
using HotelManagement.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HotelManagement.Api.Auth;

public static class JwtAuthentication
{
    /// <summary>
    /// Bearer-token authentication. Besides signature, issuer, audience and expiry, every request
    /// checks the token's security stamp against the user, so deactivating a user or changing their
    /// role takes effect on their very next request, not when the token expires.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.SigningKey,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // no algorithm downgrade/confusion
                    NameClaimType = AppClaims.Name,
                    RoleClaimType = AppClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = ValidateSessionAsync };
            });

        // Every endpoint requires a valid token unless it opts out with [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirst(AppClaims.Subject)?.Value, out var userId))
        {
            context.Fail("The token has no valid subject.");
            return;
        }

        var stamp = principal.FindFirst(AppClaims.SecurityStamp)?.Value;
        var validator = context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>();
        if (!await validator.IsValidAsync(userId, stamp, context.HttpContext.RequestAborted))
            context.Fail("The session is no longer valid.");
    }
}
