using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Common;

namespace SportsBooking.Api.Security;

/// <summary>
/// CSRF defence for cookie sessions (docs/stack.md): POST, PUT, PATCH and DELETE are rejected with 403
/// unless their <c>Origin</c> header is one of the site's own origins. A missing <c>Origin</c> is rejected too;
/// browsers always send it on these methods.
/// </summary>
internal sealed class OriginCheckMiddleware(RequestDelegate next, IOptions<OriginCheckOptions> options, IProblemDetailsService problemDetails)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMutating(context.Request.Method) || IsAllowed(context.Request.Headers.Origin.ToString()))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Origem não permitida",
                Detail = "Este pedido só pode ser feito a partir do próprio site.",
                Extensions = { ["code"] = ErrorCodes.OriginNotAllowed },
            },
        });
    }

    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private bool IsAllowed(string origin) =>
        origin.Length > 0
        && options.Value.AllowedOrigins.Any(allowed => string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase));
}
