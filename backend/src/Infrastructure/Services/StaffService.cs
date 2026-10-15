using Microsoft.EntityFrameworkCore;
using Monitoring.Core;
using Monitoring.Core.Live;
using Monitoring.Core.Time;
using Monitoring.Infrastructure.Auth;
using Monitoring.Infrastructure.Persistence;

namespace Monitoring.Infrastructure.Services;

public sealed record StaffApplicationView(Guid Id, string Login, string DisplayName, string Role);

public sealed record ChatMessageView(Guid Id, string Author, string Role, string Text, DateTime At, string Channel, Guid? AuthorId = null, Guid? RecipientId = null, string? RecipientName = null);

/// <summary>Сотрудников заводит менеджер. Чат общий. Склад не трогает.</summary>
public sealed class StaffService(AppDbContext db, IClock clock, ILiveNotifier notifier)
{
    /// <summary>Новая учётка сразу с входом. Пароль в ответе не возвращаем — менеджер отправляет его сам.</summary>
    public async Task<ServiceResult<StaffApplicationView>> InviteAsync(string login, string password, string displayName, string role, CancellationToken cancellationToken)
    {
        login = (login ?? "").Trim();
        displayName = (displayName ?? "").Trim();
        password ??= "";
        if (login.Length is < 3 or > 64 || password.Length is < 6 or > 128 || displayName.Length is < 2 or > 80)
        {
            return ServiceResult<StaffApplicationView>.Bad("Логин от 3 символов, пароль от 6, имя от 2.");
        }

        if (!Enum.TryParse<UserRole>(role, true, out var parsed))
        {
            return ServiceResult<StaffApplicationView>.Bad("Роль: официант, кухня, менеджер или поставка.");
        }

        if (await db.Users.AnyAsync(user => user.Login == login, cancellationToken))
        {
            return ServiceResult<StaffApplicationView>.Bad("Такой логин уже есть.");
        }

        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Login = login,
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(password),
            Role = parsed,
            Approved = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<StaffApplicationView>.Ok(new StaffApplicationView(user.Id, user.Login, user.DisplayName, user.Role.ToString()));
    }

    public async Task<IReadOnlyList<StaffApplicationView>> ListAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(user => user.Approved)
            .OrderBy(user => user.Role)
            .ThenBy(user => user.DisplayName)
            .ThenBy(user => user.Login)
            .Select(user => new StaffApplicationView(user.Id, user.Login, user.DisplayName, user.Role.ToString()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StaffApplicationView>> PendingAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(user => !user.Approved)
            .OrderBy(user => user.Login)
            .Select(user => new StaffApplicationView(user.Id, user.Login, user.DisplayName, user.Role.ToString()))
            .ToListAsync(cancellationToken);

    public async Task<ServiceResult<StaffApplicationView>> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (user is null || user.Approved)
        {
            return ServiceResult<StaffApplicationView>.Bad("Заявка не найдена.");
        }

        user.Approved = true;
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<StaffApplicationView>.Ok(new StaffApplicationView(user.Id, user.Login, user.DisplayName, user.Role.ToString()));
    }

    public async Task<ServiceResult<string>> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(row => row.Id == id && !row.Approved, cancellationToken);
        if (user is null)
        {
            return ServiceResult<string>.Bad("Заявка не найдена.");
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<string>.Ok("Заявка отклонена.");
    }

    public async Task<IReadOnlyList<ChatMessageView>> MessagesAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        if (role == nameof(UserRole.Supply))
        {
            var only = await LatestAsync("Supply", cancellationToken);
            return only.OrderBy(message => message.CreatedAt).Select(Map).ToList();
        }

        var staff = await LatestAsync("Staff", cancellationToken);
        var system = await LatestAsync("System", cancellationToken);
        var direct = await db.StaffMessages.AsNoTracking()
            .Include(message => message.Author)
            .Include(message => message.Recipient)
            .Where(message => message.Channel == "Staff" && message.RecipientId != null && (message.AuthorId == userId || message.RecipientId == userId))
            .OrderByDescending(message => message.CreatedAt)
            .Take(80)
            .ToListAsync(cancellationToken);
        var rows = staff.Concat(system).Concat(direct);
        if (role is nameof(UserRole.Manager) or nameof(UserRole.Kitchen))
        {
            rows = rows.Concat(await LatestAsync("Supply", cancellationToken));
        }

        return rows.OrderBy(message => message.CreatedAt).Select(Map).ToList();
    }

    public async Task<IReadOnlyList<StaffApplicationView>> ColleaguesAsync(Guid except, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(user => user.Approved && user.Id != except && user.Role != UserRole.Supply)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Login)
            .Select(user => new StaffApplicationView(user.Id, user.Login, user.DisplayName, user.Role.ToString()))
            .ToListAsync(cancellationToken);

    /// <summary>Кладёт строку «Системы» в текущий контекст. Сохраняет вызывающий вместе со своей записью.</summary>
    public StaffMessage StageSystem(string text)
    {
        text = text.Trim();
        if (text.Length > 500)
        {
            text = text[..500];
        }

        var message = new StaffMessage
        {
            Id = Guid.NewGuid(),
            Channel = "System",
            Text = text,
            CreatedAt = clock.UtcNow,
        };
        db.StaffMessages.Add(message);
        return message;
    }

    public async Task AnnounceAsync(StaffMessage message, CancellationToken cancellationToken)
    {
        var view = Map(message);
        await notifier.ChatPosted(view.Id, view.Author, view.Role, view.Text, view.At, view.Channel, null, null, cancellationToken);
    }

    /// <summary>Лента «Система»: порча и вышедший срок. В чат людей не пишется.</summary>
    public async Task PostSystemAsync(string text, CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (text.Length > 500)
        {
            text = text[..500];
        }

        var message = new StaffMessage
        {
            Id = Guid.NewGuid(),
            Channel = "System",
            Text = text,
            CreatedAt = clock.UtcNow,
        };
        db.StaffMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        var view = Map(message);
        await notifier.ChatPosted(view.Id, view.Author, view.Role, view.Text, view.At, view.Channel, null, null, cancellationToken);
    }

    public async Task<ServiceResult<ChatMessageView>> PostAsync(Guid authorId, string text, Guid? recipientId, bool toSupplier, CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length is < 1 or > 500)
        {
            return ServiceResult<ChatMessageView>.Bad("Сообщение от 1 до 500 символов.");
        }

        var author = await db.Users.FirstOrDefaultAsync(user => user.Id == authorId && user.Approved, cancellationToken);
        if (author is null)
        {
            return ServiceResult<ChatMessageView>.Bad("Сотрудник не найден.");
        }

        UserAccount? recipient = null;
        if (recipientId is Guid target)
        {
            if (target == authorId)
            {
                return ServiceResult<ChatMessageView>.Bad("Себе написать нельзя. Это общая лента.");
            }

            recipient = await db.Users.FirstOrDefaultAsync(user => user.Id == target && user.Approved && user.Role != UserRole.Supply, cancellationToken);
            if (recipient is null)
            {
                return ServiceResult<ChatMessageView>.Bad("Такого сотрудника нет.");
            }
        }

        var toSupplyRoom = toSupplier || author.Role == UserRole.Supply;
        if (toSupplyRoom && author.Role is not (UserRole.Manager or UserRole.Kitchen or UserRole.Supply))
        {
            return ServiceResult<ChatMessageView>.Bad("Поставщику пишут менеджер и кухня.");
        }

        var message = new StaffMessage
        {
            Id = Guid.NewGuid(),
            AuthorId = author.Id,
            RecipientId = toSupplyRoom ? null : recipient?.Id,
            Channel = toSupplyRoom ? "Supply" : "Staff",
            Text = text,
            CreatedAt = clock.UtcNow,
            Author = author,
            Recipient = toSupplyRoom ? null : recipient,
        };
        db.StaffMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        var view = Map(message);
        if (toSupplyRoom)
        {
            var audience = await db.Users.AsNoTracking()
                .Where(user => user.Approved && (user.Role == UserRole.Manager || user.Role == UserRole.Kitchen || user.Role == UserRole.Supply))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);
            await notifier.ChatAddressed(audience, view.Id, view.Author, view.Role, view.Text, view.At, view.Channel, view.AuthorId, cancellationToken);
        }
        else
        {
            await notifier.ChatPosted(view.Id, view.Author, view.Role, view.Text, view.At, view.Channel, view.AuthorId, view.RecipientId, cancellationToken);
        }

        return ServiceResult<ChatMessageView>.Ok(view);
    }

    private async Task<List<StaffMessage>> LatestAsync(string channel, CancellationToken cancellationToken) =>
        await db.StaffMessages.AsNoTracking()
            .Include(message => message.Author)
            .Where(message => message.Channel == channel && message.RecipientId == null)
            .OrderByDescending(message => message.CreatedAt)
            .Take(60)
            .ToListAsync(cancellationToken);

    private static ChatMessageView Map(StaffMessage message)
    {
        var author = message.Author;
        var name = author is null
            ? "Система"
            : string.IsNullOrWhiteSpace(author.DisplayName) ? author.Login : author.DisplayName;
        var recipientName = message.Recipient is null
            ? null
            : string.IsNullOrWhiteSpace(message.Recipient.DisplayName) ? message.Recipient.Login : message.Recipient.DisplayName;
        return new ChatMessageView(message.Id, name, author?.Role.ToString() ?? "System", message.Text, message.CreatedAt, message.Channel, message.AuthorId, message.RecipientId, recipientName);
    }
}
