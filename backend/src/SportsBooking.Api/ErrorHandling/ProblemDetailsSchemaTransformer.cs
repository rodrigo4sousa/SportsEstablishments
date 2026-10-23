using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SportsBooking.Api.ErrorHandling;

/// <summary>
/// Documents the <c>code</c> and <c>traceId</c> extensions that every ProblemDetails response carries
/// (ProblemDetailsSetup), so the frontend's generated types include them.
/// </summary>
internal sealed class ProblemDetailsSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type.IsAssignableTo(typeof(ProblemDetails)) || type.IsAssignableTo(typeof(HttpValidationProblemDetails)))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Stable error code (e.g. validation_failed, email_taken). The frontend picks the message by it.",
            };
            schema.Properties["traceId"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Identifies the request in the logs.",
            };
            schema.Required ??= new HashSet<string>();
            schema.Required.Add("code");
            schema.Required.Add("traceId");
        }

        return Task.CompletedTask;
    }
}
