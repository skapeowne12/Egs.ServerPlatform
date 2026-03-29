using Microsoft.AspNetCore.SignalR;

namespace Egs.Api.Hubs;

public sealed class ServerConsoleHub : Hub
{
    public Task SubscribeServer(Guid serverId)
        => Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(serverId));

    public Task UnsubscribeServer(Guid serverId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(serverId));

    public static string GetGroupName(Guid serverId) => $"server-console:{serverId}";
}