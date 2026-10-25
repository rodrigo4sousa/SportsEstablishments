using FluentValidation;
using SportsBooking.Application.Common;

namespace SportsBooking.Application.Admin;

public enum OwnerStatusAction
{
    Deactivated,
    Reactivated,
}

public static class OwnerErrors
{
    /// <summary>An unknown id and an id of a player or the admin give exactly this answer: this endpoint speaks only of owners.</summary>
    public static ServiceError NotFound { get; } = new(
        ErrorKind.NotFound,
        ErrorCodes.OwnerNotFound,
        "Dono não encontrado",
        "Não encontrámos este dono.");

    public static ServiceError AlreadyDeactivated { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.OwnerAlreadyDeactivated,
        "Dono já desativado",
        "Este dono já está desativado.");

    public static ServiceError NotDeactivated { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.OwnerNotDeactivated,
        "Dono não desativado",
        "Este dono não está desativado.");
}

/// <summary>One deactivation or reactivation of an owner, as recorded (US29): who, when and why. Never edited or removed.</summary>
public sealed record OwnerStatusChangeResponse(Guid Id, string Action, string Reason, Guid AdminId, string AdminName, DateTime At);

/// <summary>
/// The change of an owner's status, in Infrastructure because it is Identity's account and the session tokens it touches. One
/// transaction does everything or nothing: the status (decided by a conditional update, so two admins acting at once give one
/// change and one "already"), the audit row and, for a deactivation, the revocation of every refresh token of the owner.
/// </summary>
public interface IOwnerStatusStore
{
    /// <summary>True when the status changed; false when the owner was already in that state (nothing is written then).</summary>
    Task<bool> TryChangeAsync(Guid ownerId, OwnerStatusAction action, string reason, Guid adminId, DateTime now, CancellationToken cancellationToken);

    /// <summary>The changes of one owner, newest first.</summary>
    Task<IReadOnlyList<OwnerStatusChangeResponse>> HistoryAsync(Guid ownerId, CancellationToken cancellationToken);
}

/// <summary>The body of the deactivate and reactivate requests: why (required, at most 500 characters once trimmed; provisional).</summary>
public sealed record OwnerStatusRequest(string Reason)
{
    public const int ReasonMaximumLength = 500;
}

public sealed class OwnerStatusRequestValidator : AbstractValidator<OwnerStatusRequest>
{
    public OwnerStatusRequestValidator()
    {
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason)).WithMessage("Indica o motivo.")
            .Must(reason => (reason ?? string.Empty).Trim().Length <= OwnerStatusRequest.ReasonMaximumLength)
            .WithMessage($"O motivo pode ter no máximo {OwnerStatusRequest.ReasonMaximumLength} caracteres.");
    }
}

/// <summary>
/// US29: deactivating and reactivating an owner, and the history of it. The owner is looked up first (404, whatever the body),
/// then the reason is validated (400, trimmed), then the store changes the status (422 when it already is that way). The
/// admin comes from the session, never from the request. A deactivation cancels no booking: the answer carries the owner as
/// <c>GET</c> gives it, with the number of future bookings that stay confirmed.
/// </summary>
public sealed class AdminOwnerStatusService(
    AdminOwnersService owners,
    IOwnerStatusStore store,
    ICurrentUser currentUser,
    OwnerStatusRequestValidator validator,
    TimeProvider timeProvider)
{
    public Task<Result<AdminOwnerDetailResponse>> DeactivateAsync(Guid ownerId, OwnerStatusRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, request, OwnerStatusAction.Deactivated, cancellationToken);

    public Task<Result<AdminOwnerDetailResponse>> ReactivateAsync(Guid ownerId, OwnerStatusRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(ownerId, request, OwnerStatusAction.Reactivated, cancellationToken);

    public async Task<Result<List<OwnerStatusChangeResponse>>> HistoryAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var owner = await owners.GetAsync(ownerId, cancellationToken);
        if (!owner.IsSuccess)
        {
            return owner.Error!;
        }

        return (await store.HistoryAsync(ownerId, cancellationToken)).ToList();
    }

    private async Task<Result<AdminOwnerDetailResponse>> ChangeAsync(Guid ownerId, OwnerStatusRequest request, OwnerStatusAction action, CancellationToken cancellationToken)
    {
        var owner = await owners.GetAsync(ownerId, cancellationToken);
        if (!owner.IsSuccess)
        {
            return owner.Error!;
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var adminId = currentUser.UserId ?? throw new InvalidOperationException("The admin area requires a session.");
        var changed = await store.TryChangeAsync(ownerId, action, request.Reason.Trim(), adminId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (!changed)
        {
            return action == OwnerStatusAction.Deactivated ? OwnerErrors.AlreadyDeactivated : OwnerErrors.NotDeactivated;
        }

        return await owners.GetAsync(ownerId, cancellationToken);
    }
}
