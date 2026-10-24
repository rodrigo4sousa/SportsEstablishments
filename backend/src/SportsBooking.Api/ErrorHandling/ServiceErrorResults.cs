using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SportsBooking.Application.Common;

namespace SportsBooking.Api.ErrorHandling;

internal static class ServiceErrorResults
{
    /// <summary>
    /// Turns an expected service failure into a ProblemDetails response with its stable <c>code</c>
    /// (and per-field <c>errors</c> for validation failures). The <c>traceId</c> is added by ProblemDetailsSetup.
    /// </summary>
    public static ObjectResult Problem(this ControllerBase controller, ServiceError error)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
        };

        var factory = controller.ProblemDetailsFactory;
        ProblemDetails problem;
        if (error.FieldErrors is { Count: > 0 } fieldErrors)
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in fieldErrors)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            problem = factory.CreateValidationProblemDetails(controller.HttpContext, modelState, status, error.Title, detail: error.Detail);
        }
        else
        {
            problem = factory.CreateProblemDetails(controller.HttpContext, status, error.Title, detail: error.Detail);
        }

        problem.Extensions["code"] = error.Code;
        foreach (var (name, value) in error.Extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[name] = value;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
