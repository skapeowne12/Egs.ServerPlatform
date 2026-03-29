using Egs.Agent.Abstractions.Status;
using Egs.Api.Hubs;
using Egs.Application.Servers;
using Microsoft.AspNetCore.SignalR;

namespace Egs.Api.Realtime;

public sealed class SignalRServerStatusNotifier : IServerStatusNotifier
{
    private readonly IHubContext<ServerStatusHub> _hubContext;

    public SignalRServerStatusNotifier(IHubContext<ServerStatusHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyStatusChangedAsync(ServerStatusChangedMessage message, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("ServerStatusChanged", message, ct);
}