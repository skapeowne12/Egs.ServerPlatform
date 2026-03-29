using System.Diagnostics;
using Egs.Agent.Abstractions.Servers;

namespace Egs.Agent.Windows.Services.Runtimes;

public interface IGameServerRuntime
{
    bool CanHandle(string gameKey);
    Task InstallAsync(AgentServerDefinitionMessage server, Func<string, Task> writeLineAsync, CancellationToken ct);
    Task<Process> StartAsync(AgentServerDefinitionMessage server, Func<string, Task> writeLineAsync, CancellationToken ct);
    Task StopAsync(AgentServerDefinitionMessage server, Process process, Func<string, Task> writeLineAsync, CancellationToken ct);
}
