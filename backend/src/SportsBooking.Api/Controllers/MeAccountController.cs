using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using SportsBooking.Api.Auth;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Api.Security;
using SportsBooking.Application.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>US30: the signed-in person's rights over their own account, for any profile (no session: 401).</summary>
[ApiController]
[Route("api/me")]
public sealed class MeAccountController(AccountExportService export, AccountDeletionService deletion, AccountPreferencesService preferences) : ControllerBase
{
    private const string ExportFileName = "os-meus-dados.json";

    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The caller's own data as a JSON file to download: profile, bookings and the legal acceptance.</summary>
    [HttpGet("data-export")]
    [EnableRateLimiting(AuthRateLimits.AccountExport)]
    [ProducesResponseType<AccountDataExport>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var result = await export.ExportAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return this.Problem(result.Error!);
        }

        Response.Headers[HeaderNames.CacheControl] = "no-store";
        return File(JsonSerializer.SerializeToUtf8Bytes(result.Value, ExportJson), "application/json", ExportFileName);
    }

    /// <summary>
    /// T09: the language of the caller's emails, <c>pt-PT</c> or <c>en</c>. The site's own language is a cookie and does not change.
    /// 400 <c>validation_failed</c> on <c>preferredLanguage</c> for anything else.
    /// </summary>
    [HttpPut("preferred-language")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ChangePreferredLanguage(ChangePreferredLanguageRequest request, CancellationToken cancellationToken)
    {
        var result = await preferences.ChangePreferredLanguageAsync(request, cancellationToken);
        return result.IsSuccess ? NoContent() : this.Problem(result.Error!);
    }

    /// <summary>
    /// Deletes the caller's account after checking the password: future bookings cancelled, personal data removed, every session ended,
    /// both cookies cleared. 400 <c>validation_failed</c> or <c>invalid_password</c>; 422 <c>owner_has_clubs</c> or <c>admin_cannot_delete_account</c>.
    /// </summary>
    [HttpPost("delete-account")]
    [Consumes("application/json")]
    [EnableRateLimiting(AuthRateLimits.AccountDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await deletion.DeleteAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.Problem(result.Error!);
        }

        AuthCookies.Delete(Response);
        return NoContent();
    }
}
