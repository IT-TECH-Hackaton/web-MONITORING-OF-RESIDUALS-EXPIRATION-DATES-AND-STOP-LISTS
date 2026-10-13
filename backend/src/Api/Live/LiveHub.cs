using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Monitoring.Api.Live;

/// <summary>Хаб /hubs/live. Токен в query access_token. Подключившийся попадает в группу live и слушает склад, стоп и смену.</summary>
[Authorize]
public sealed class LiveHub : Hub
{
    public const string GroupName = "live";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
        await base.OnConnectedAsync();
    }
}
