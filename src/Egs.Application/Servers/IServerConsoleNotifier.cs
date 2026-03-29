using Egs.Agent.Abstractions.Console;

namespace Egs.Application.Servers;

public interface IServerConsoleNotifier
{
    Task PublishAsync(ConsoleLineMessage message, CancellationToken ct = default);
}