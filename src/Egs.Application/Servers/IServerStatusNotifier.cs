using Egs.Agent.Abstractions.Status;

namespace Egs.Application.Servers;

public interface IServerStatusNotifier
{
    Task NotifyStatusChangedAsync(ServerStatusChangedMessage message, CancellationToken ct = default);
}