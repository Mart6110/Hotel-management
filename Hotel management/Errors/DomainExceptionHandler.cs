using HotelManagement.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Api.Errors;

/// <summary>Maps exceptions to RFC 7807 problem responses.</summary>
public class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger)
    : IExceptionHandler
{
    private static readonly Dictionary<ErrorKind, (int Status, string Title)> Mapping = new()
    {
        [ErrorKind.Validation] = (StatusCodes.Status400BadRequest, "Bad request"),
        [ErrorKind.Unauthorized] = (StatusCodes.Status401Unauthorized, "Unauthorized"),
        [ErrorKind.NotFound] = (StatusCodes.Status404NotFound, "Not found"),
        [ErrorKind.Conflict] = (StatusCodes.Status409Conflict, "Conflict"),
    };

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception is DomainException domain && Mapping.TryGetValue(domain.Kind, out var m)
            ? (m.Status, m.Title, domain.Message)
            : (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.");

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception");

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
