using Microsoft.EntityFrameworkCore;
using Npgsql;
using Toamaisutaa.Abstractions;
using LuckyMaze.Domain;

namespace LuckyMaze.Infrastructure.Services
{
    /// <summary>
    /// Bridges Toamaisutaa's local user row (identity: who signed in) to LuckyMaze's own
    /// <see cref="User"/> row (game state: balance, role, ready flag). The two are linked by
    /// <see cref="User.ExternalId"/>, which stores the Toamaisutaa user id as a string - the same
    /// field that used to hold the Pocket ID OIDC subject.
    /// </summary>
    public class UserService(ICurrentUser currentUser, LuckyMazeDbContext dbContext) : IUserService
    {
        public async Task<bool> ExistsAsync(string externalId, CancellationToken cancellationToken = default)
        {
            return await dbContext.Users.AnyAsync(u => u.ExternalId == externalId, cancellationToken);
        }

        public async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        {
            var toaUser = await currentUser.GetOrProvisionAsync(cancellationToken);

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == toaUser.Id.ToString(), cancellationToken)
                ?? throw new UnauthorizedAccessException("User not found");

            return user.Id;
        }

        public async Task SyncCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var toaUser = await currentUser.GetOrProvisionAsync(cancellationToken);
            var externalId = toaUser.Id.ToString();

            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken);

            var email = toaUser.Email ?? string.Empty;
            var displayName = toaUser.DisplayName ?? toaUser.UserName ?? "LuckyMaze User";

            if (user is null)
            {
                user = new User
                {
                    ExternalId = externalId,
                    Email = email,
                    DisplayName = displayName,
                    AvatarUrl = toaUser.PictureUrl,
                    Preferences = new UserPreferences()
                };

                dbContext.Users.Add(user);

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    // Lost a race with another concurrent first-sight request for the same subject
                    // (e.g. the SignalR negotiate and the app's own sync call, right after sign-up).
                    // The row exists now either way - nothing left for this call to do.
                    dbContext.Entry(user).State = EntityState.Detached;
                }

                return;
            }

            user.Email = email;
            user.DisplayName = displayName;
            user.AvatarUrl = toaUser.PictureUrl;

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
