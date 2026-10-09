using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Monitoring.Api.Live;

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
