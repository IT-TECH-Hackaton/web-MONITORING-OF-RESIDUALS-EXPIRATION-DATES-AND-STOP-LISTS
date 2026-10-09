using Microsoft.EntityFrameworkCore;
using Monitoring.Infrastructure.Auth;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

public sealed class AuthService(AppDbContext db)
{
    public async Task<AuthenticatedUser?> AuthenticateAsync(string login, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Login == login, cancellationToken);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return new AuthenticatedUser(user.Id, user.Login, user.Role);
    }

    public async Task<AuthenticatedUser?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return user is null ? null : new AuthenticatedUser(user.Id, user.Login, user.Role);
    }
}
