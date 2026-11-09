using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>Test host only (added by <see cref="ApiFactory"/>, never part of the API): what the request looks like after the forwarded headers were applied.</summary>
[ApiController]
[Route("test-only/connection")]
public sealed class ConnectionInfoTestController : ControllerBase
{
    public const string Route = "/test-only/connection";

    public sealed record ConnectionInfoResponse(string? Address, string Scheme);

    [HttpGet]
    [AllowAnonymous]
    public ConnectionInfoResponse Get() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Scheme);
}
