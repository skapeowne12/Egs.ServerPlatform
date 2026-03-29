using System.Threading.Channels;
using Egs.Agent.Abstractions.Commands;
using Egs.Application.Servers;

namespace Egs.Infrastructure.Servers;

public sealed class ChannelServerCommandQueue : IServerCommandQueue
{
    private readonly Channel<ServerCommandMessage> _channel =
        Channel.CreateUnbounded<ServerCommandMessage>();

    public ValueTask QueueAsync(ServerCommandMessage command, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(command, ct);

    public IAsyncEnumerable<ServerCommandMessage> DequeueAllAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct);
}