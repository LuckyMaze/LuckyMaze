using Microsoft.EntityFrameworkCore;
using Toamaisutaa.Abstractions;
using LuckyMaze.Domain.Enums;

namespace LuckyMaze.Infrastructure.Services
{
    /// <summary>
    /// Roles for a locally issued access token. OIDC tokens carry roles straight from the identity
    /// provider's claim; a local sign-in has no such source, so Toamaisutaa asks this instead. The
    /// answer comes from <see cref="Domain.User.Role"/>, keyed by the same external id that links
    /// the two user rows.
    /// </summary>
    public class LuckyMazeUserRoleProvider(LuckyMazeDbContext dbContext) : IUserRoleProvider
    {
        public async Task<IReadOnlyList<string>> GetRolesAsync(ToamaisutaaUser user, CancellationToken cancellationToken = default)
        {
            var externalId = user.Id.ToString();

            var role = await dbContext.Users
                .Where(u => u.ExternalId == externalId)
                .Select(u => (UserRole?)u.Role)
                .FirstOrDefaultAsync(cancellationToken);

            return role == UserRole.Admin ? ["admin"] : [];
        }
    }
}
