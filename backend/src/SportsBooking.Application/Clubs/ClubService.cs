using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Clubs;

namespace SportsBooking.Application.Clubs;

/// <summary>A club as its owner sees it in <c>/manage</c>.</summary>
public sealed record OwnerClubResponse(
    Guid Id,
    string Slug,
    string Name,
    string Address,
    string Locality,
    string Phone,
    string ContactEmail,
    int CancellationWindowHours,
    string TimeZone,
    bool NotifyOnNewBooking)
{
    /// <summary>Used both in database projections and for a club already loaded.</summary>
    public static readonly Expression<Func<Club, OwnerClubResponse>> Projection = club => new OwnerClubResponse(
        club.Id,
        club.Slug,
        club.Name,
        club.Address,
        club.Locality,
        club.Phone,
        club.ContactEmail,
        club.CancellationWindowHours,
        club.TimeZoneId,
        club.NotifyOnNewBooking);

    private static readonly Func<Club, OwnerClubResponse> FromClub = Projection.Compile();

    public static OwnerClubResponse From(Club club) => FromClub(club);
}

/// <summary>US06: an owner creates, lists, reads and edits their own clubs. Every read goes through <see cref="OwnedClubs"/> (T02).</summary>
public sealed class ClubService(
    OwnedClubs ownedClubs,
    IClubStore clubStore,
    IAppDbContext dbContext,
    CreateClubRequestValidator createValidator,
    UpdateClubRequestValidator updateValidator,
    IValidator<PageRequest> pageValidator,
    TimeProvider timeProvider)
{
    public async Task<Result<OwnerClubResponse>> CreateAsync(ClubRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await createValidator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var ownerId = ownedClubs.OwnerId;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var club = await clubStore.AddWithUniqueSlugAsync(
            ClubSlug.FromName(normalized.Name),
            slug =>
            {
                var created = Club.Create(
                    ownerId,
                    slug,
                    normalized.Details(),
                    normalized.CancellationWindowHours ?? ClubRules.DefaultCancellationWindowHours,
                    now);
                if (normalized.NotifyOnNewBooking is { } notify)
                {
                    created.ChangeNotifyOnNewBooking(notify, now);
                }

                return created;
            },
            cancellationToken);
        return OwnerClubResponse.From(club);
    }

    /// <summary>
    /// The owner's clubs by name, ignoring case (then id, so equal names keep a stable order), one page at a time.
    /// Lowercasing makes the order the same whatever the database collation does with upper and lower case.
    /// </summary>
    public async Task<Result<PagedResponse<OwnerClubResponse>>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var validation = await pageValidator.ValidateAsync(page, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var clubs = ownedClubs.All().AsNoTracking();
        var total = await clubs.CountAsync(cancellationToken);
#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); no .NET culture is involved.
        var items = await clubs
            .OrderBy(club => club.Name.ToLower())
            .ThenBy(club => club.Name)
            .ThenBy(club => club.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(OwnerClubResponse.Projection)
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
        return new PagedResponse<OwnerClubResponse>(items, page.Page, page.PageSize, total);
    }

    public async Task<Result<OwnerClubResponse>> GetAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var club = await ownedClubs.All().AsNoTracking()
            .Where(candidate => candidate.Id == clubId)
            .Select(OwnerClubResponse.Projection)
            .SingleOrDefaultAsync(cancellationToken);
        return club is null ? ClubErrors.NotFound : club;
    }

    /// <summary>
    /// Replaces the details and the cancellation window. Ownership is checked first, so another owner's club is a
    /// 404 whatever the request holds. The slug never changes.
    /// </summary>
    public async Task<Result<OwnerClubResponse>> UpdateAsync(Guid clubId, ClubRequest request, CancellationToken cancellationToken)
    {
        var found = await ownedClubs.FindAsync(clubId, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var normalized = request.Normalized();
        var validation = await updateValidator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var club = found.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        club.UpdateDetails(normalized.Details(), now);
        club.ChangeCancellationWindow(normalized.CancellationWindowHours!.Value, now);
        // Missing means "leave it as it is": a form that does not know the setting must not switch the emails off.
        if (normalized.NotifyOnNewBooking is { } notify)
        {
            club.ChangeNotifyOnNewBooking(notify, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return OwnerClubResponse.From(club);
    }
}
