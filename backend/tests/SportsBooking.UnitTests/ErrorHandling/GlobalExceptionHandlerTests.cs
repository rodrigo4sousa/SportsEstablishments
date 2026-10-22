using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Common;

namespace SportsBooking.UnitTests.ErrorHandling;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Unexpected_exception_becomes_generic_500_problem_details_with_code_and_trace_id()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddApiProblemDetails()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("secret internal detail"),
            TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        httpContext.Response.ContentType.ShouldBe("application/problem+json");

        httpContext.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(httpContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var root = body.RootElement;

        root.GetProperty("status").GetInt32().ShouldBe(500);
        root.GetProperty("code").GetString().ShouldBe(ErrorCodes.InternalError);
        root.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetRawText().ShouldNotContain("secret internal detail");
    }
}
