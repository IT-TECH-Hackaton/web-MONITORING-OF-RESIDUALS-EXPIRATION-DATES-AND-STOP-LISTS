using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Monitoring.Infrastructure.Auth;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

/// <summary>Проверка логина. Токены не выдаёт — это делает ручка.</summary>
public sealed class AuthService(AppDbContext db)
{
    /// <summary>Вход: логин и пароль. Выход: пользователь или null, если пара неверная.</summary>
    public async Task<AuthenticatedUser?> AuthenticateAsync(string login, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Login == login, cancellationToken);
        if (user is null)
        {
            PasswordHasher.Burn(password);
            return null;
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        if (PasswordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.Hash(password);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!user.Approved)
        {
            return new AuthenticatedUser(user.Id, user.Login, user.Role, false);
        }

        return new AuthenticatedUser(user.Id, user.Login, user.Role);
    }

    /// <summary>Ищет пользователя по id из refresh-токена. Нет записи — сессия мертва.</summary>
    public async Task<AuthenticatedUser?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return user is null || !user.Approved ? null : new AuthenticatedUser(user.Id, user.Login, user.Role);
    }

    public async Task BindRefreshAsync(Guid userId, string refreshToken, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstAsync(row => row.Id == userId, cancellationToken);
        user.RefreshTokenHash = Hash(refreshToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RefreshMatchesAsync(Guid userId, string refreshToken, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(row => row.Id == userId, cancellationToken);
        if (user?.RefreshTokenHash is null)
        {
            return false;
        }

        var actual = Encoding.UTF8.GetBytes(Hash(refreshToken));
        var expected = Encoding.UTF8.GetBytes(user.RefreshTokenHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public async Task ForgetRefreshAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(row => row.Id == userId, cancellationToken);
        if (user is null)
        {
            return;
        }

        user.RefreshTokenHash = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
