using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Accounts;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Identity;

internal sealed class AccountStore(AppDbContext dbContext) : IAccountStore
{
    public async Task<AccountResponse?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AccountResponse(
                user.Id,
                user.Name,
                user.Email!,
                dbContext.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(dbContext.Roles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name!)
                    .First()))
            .SingleOrDefaultAsync(cancellationToken);
}
