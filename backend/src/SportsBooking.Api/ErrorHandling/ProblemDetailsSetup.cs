using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using SportsBooking.Application.Common;

namespace SportsBooking.Api.ErrorHandling;

internal static class ProblemDetailsSetup
{
    /// <summary>
    /// Registers ProblemDetails (RFC 9457) for every error response, always carrying a stable
    /// <c>code</c> and the <c>traceId</c>.
    /// </summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;

            problem.Extensions.TryAdd("code", DefaultCode(problem.Status));
            problem.Extensions["traceId"] =
                Activity.Current?.Id
                ?? context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity.Id
                ?? context.HttpContext.TraceIdentifier;
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    private static string DefaultCode(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.BadRequest,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status429TooManyRequests => ErrorCodes.TooManyRequests,
        _ => ErrorCodes.InternalError,
    };
}
