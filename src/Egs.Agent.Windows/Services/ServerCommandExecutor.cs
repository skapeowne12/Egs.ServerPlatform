using System.Collections.Concurrent;
using System.Diagnostics;
using Egs.Agent.Abstractions.Commands;
using Egs.Agent.Abstractions.Console;
using Egs.Agent.Abstractions.Servers;
using Egs.Agent.Abstractions.Status;
using Egs.Agent.Windows.Services.Runtimes;

namespace Egs.Agent.Windows.Services;

public sealed class ServerCommandExecutor
{
    private readonly ControlPlaneClient _controlPlaneClient;
    private readonly GameServerRuntimeCatalog _runtimeCatalog;
    private readonly ILogger<ServerCommandExecutor> _logger;
    private readonly ConcurrentDictionary<Guid, Process> _trackedProcesses = new();

    public ServerCommandExecutor(
        ControlPlaneClient controlPlaneClient,
        GameServerRuntimeCatalog runtimeCatalog,
        ILogger<ServerCommandExecutor> logger)
    {
        _controlPlaneClient = controlPlaneClient;
        _runtimeCatalog = runtimeCatalog;
        _logger = logger;
    }

    public async Task ExecuteAsync(ServerCommandMessage command, CancellationToken ct)
    {
        var server = await _controlPlaneClient.GetServerDefinitionAsync(command.ServerId, ct);
        var runtime = _runtimeCatalog.GetRequired(server.GameKey);

        try
        {
            await EmitLineAsync(server.Id, $"[{command.Type}] Loaded server '{server.Name}' ({server.GameKey}) at '{server.InstallPath}'.", ct);

            switch (command.Type)
            {
                case ServerCommandType.Install:
                    await runtime.InstallAsync(server, line => EmitLineAsync(server.Id, line, ct), ct);
                    await PostStatusAsync(command.CommandId, server.Id, "Stopped", null, null, ct);
                    break;

                case ServerCommandType.Uninstall:
                    await UninstallAsync(command, server, runtime, deleteAfter: false, ct);
                    break;

                case ServerCommandType.Delete:
                    await UninstallAsync(command, server, runtime, deleteAfter: true, ct);
                    break;

                case ServerCommandType.Start:
                    await StartAsync(command, server, runtime, ct);
                    break;

                case ServerCommandType.Stop:
                    await StopAsync(command, server, runtime, ct);
                    break;

                case ServerCommandType.Restart:
                    await StopAsync(command, server, runtime, ct, updateCommandStatus: false);
                    await StartAsync(command, server, runtime, ct);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported command type '{command.Type}'.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {CommandId} failed for server {ServerId}.", command.CommandId, server.Id);
            await EmitLineAsync(server.Id, $"[{command.Type}] ERROR: {ex.Message}", ct);

            var fallbackStatus = command.Type switch
            {
                ServerCommandType.Install => "InstallFailed",
                ServerCommandType.Uninstall => "Stopped",
                ServerCommandType.Delete => "Stopped",
                ServerCommandType.Start => "Stopped",
                ServerCommandType.Stop => "Running",
                ServerCommandType.Restart => "Stopped",
                _ => server.Status
            };

            var fallbackProcessId = command.Type switch
            {
                ServerCommandType.Stop => server.ProcessId,
                _ => null
            };

            await PostStatusAsync(command.CommandId, server.Id, fallbackStatus, fallbackProcessId, ex.Message, ct);
        }
    }

    private async Task UninstallAsync(
        ServerCommandMessage command,
        AgentServerDefinitionMessage server,
        IGameServerRuntime runtime,
        bool deleteAfter,
        CancellationToken ct)
    {
        var process = ResolveTrackedOrExistingProcess(server);
        if (process is not null && !process.HasExited)
        {
            await EmitLineAsync(server.Id, $"[{command.Type}] Server is running; stopping before uninstall.", ct);
            await runtime.StopAsync(server, process, line => EmitLineAsync(server.Id, line, CancellationToken.None), ct);

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (InvalidOperationException)
            {
            }

            _trackedProcesses.TryRemove(server.Id, out _);
        }

        await runtime.UninstallAsync(server, line => EmitLineAsync(server.Id, line, ct), ct);

        var finalStatus = deleteAfter ? "Deleted" : "Stopped";
        await PostStatusAsync(command.CommandId, server.Id, finalStatus, null, null, ct);
    }

    private async Task StartAsync(
        ServerCommandMessage command,
        AgentServerDefinitionMessage server,
        IGameServerRuntime runtime,
        CancellationToken ct)
    {
        var existingProcess = ResolveTrackedOrExistingProcess(server);
        if (existingProcess is not null)
        {
            if (existingProcess.HasExited)
            {
                _trackedProcesses.TryRemove(server.Id, out _);
            }
            else
            {
                await EmitLineAsync(server.Id, $"[Start] Server already appears to be running with PID {existingProcess.Id}.", ct);
                await PostStatusAsync(command.CommandId, server.Id, "Running", existingProcess.Id, null, ct);
                return;
            }
        }

        var process = await runtime.StartAsync(server, line => EmitLineAsync(server.Id, line, CancellationToken.None), ct);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _ = OnProcessExitedAsync(server, process);
        _trackedProcesses[server.Id] = process;

        await EmitLineAsync(server.Id, $"[Start] Process started with PID {process.Id}.", ct);
        await PostStatusAsync(command.CommandId, server.Id, "Running", process.Id, null, ct);
    }

    private async Task StopAsync(
        ServerCommandMessage command,
        AgentServerDefinitionMessage server,
        IGameServerRuntime runtime,
        CancellationToken ct,
        bool updateCommandStatus = true)
    {
        var process = ResolveTrackedOrExistingProcess(server);
        if (process is null || process.HasExited)
        {
            _trackedProcesses.TryRemove(server.Id, out _);
            await EmitLineAsync(server.Id, "[Stop] Server is not running.", ct);

            if (updateCommandStatus)
                await PostStatusAsync(command.CommandId, server.Id, "Stopped", null, null, ct);

            return;
        }

        await runtime.StopAsync(server, process, line => EmitLineAsync(server.Id, line, CancellationToken.None), ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (InvalidOperationException)
        {
        }

        _trackedProcesses.TryRemove(server.Id, out _);

        if (updateCommandStatus)
            await PostStatusAsync(command.CommandId, server.Id, "Stopped", null, null, ct);
    }

    private async Task OnProcessExitedAsync(AgentServerDefinitionMessage server, Process process)
    {
        _trackedProcesses.TryRemove(server.Id, out _);

        try
        {
            var exitCode = process.ExitCode;
            await EmitLineAsync(server.Id, $"[Process] Server process exited with code {exitCode}.", CancellationToken.None);
            await PostStatusAsync(null, server.Id, "Stopped", null, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish exit state for server {ServerId}.", server.Id);
        }
        finally
        {
            process.Dispose();
        }
    }

    private Process? ResolveTrackedOrExistingProcess(AgentServerDefinitionMessage server)
    {
        if (_trackedProcesses.TryGetValue(server.Id, out var trackedProcess))
            return trackedProcess;

        if (server.ProcessId is not int processId)
            return null;

        try
        {
            return Process.GetProcessById(processId);
        }
        catch
        {
            return null;
        }
    }

    private Task PostStatusAsync(
        Guid? commandId,
        Guid serverId,
        string status,
        int? processId,
        string? error,
        CancellationToken ct) =>
        _controlPlaneClient.PostStatusAsync(
            new AgentServerUpdateMessage(
                commandId,
                serverId,
                status,
                processId,
                error,
                DateTimeOffset.UtcNow),
            ct);

    private Task EmitLineAsync(Guid serverId, string line, CancellationToken ct) =>
        _controlPlaneClient.PostConsoleLineAsync(
            new ConsoleLineMessage(serverId, DateTimeOffset.UtcNow, line),
            ct);
}