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
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return new AuthenticatedUser(user.Id, user.Login, user.Role);
    }

    /// <summary>Ищет пользователя по id из refresh-токена. Нет записи — сессия мертва.</summary>
    public async Task<AuthenticatedUser?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return user is null ? null : new AuthenticatedUser(user.Id, user.Login, user.Role);
    }
}
