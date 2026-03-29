using Egs.Agent.Abstractions.Commands;

namespace Egs.Application.Servers;

public interface IServerCommandQueue
{
    ValueTask QueueAsync(ServerCommandMessage command, CancellationToken ct = default);
    IAsyncEnumerable<ServerCommandMessage> DequeueAllAsync(CancellationToken ct = default);
}