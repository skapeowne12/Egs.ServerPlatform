using Egs.Agent.Abstractions.Console;
using Egs.Api.Hubs;
using Egs.Application.Servers;
using Microsoft.AspNetCore.SignalR;

namespace Egs.Api.Realtime;

public sealed class SignalRServerConsoleNotifier : IServerConsoleNotifier
{
    private readonly IHubContext<ServerConsoleHub> _hubContext;

    public SignalRServerConsoleNotifier(IHubContext<ServerConsoleHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishAsync(ConsoleLineMessage message, CancellationToken ct = default)
    {
        var group = ServerConsoleHub.GetGroupName(message.ServerId);
        return _hubContext.Clients.Group(group)
            .SendAsync("ConsoleLine", message, ct);
    }
}