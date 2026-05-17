using System.Collections.Concurrent;
using System.Diagnostics;
using Egs.Agent.Abstractions.Commands;
using Egs.Agent.Abstractions.Console;
using Egs.Agent.Abstractions.Servers;
using Egs.Agent.Abstractions.Status;
using Egs.Agent.Windows.Services.Runtimes;
using Egs.Contracts.Cloud;

namespace Egs.Agent.Windows.Services;

public sealed class ServerCommandExecutor
{
    private readonly ControlPlaneClient _controlPlaneClient;
    private readonly CloudControlClient _cloudControlClient;
    private readonly GameServerRuntimeCatalog _runtimeCatalog;
    private readonly ILogger<ServerCommandExecutor> _logger;
    private readonly ConcurrentDictionary<Guid, Process> _trackedProcesses = new();

    public ServerCommandExecutor(
        ControlPlaneClient controlPlaneClient,
        CloudControlClient cloudControlClient,
        GameServerRuntimeCatalog runtimeCatalog,
        ILogger<ServerCommandExecutor> logger)
    {
        _controlPlaneClient = controlPlaneClient;
        _cloudControlClient = cloudControlClient;
        _runtimeCatalog = runtimeCatalog;
        _logger = logger;
    }

    public async Task<ServerCommandExecutionResult> ExecuteAsync(ServerCommandMessage command, CancellationToken ct)
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
                    return new ServerCommandExecutionResult("Stopped", null, null);

                case ServerCommandType.Uninstall:
                    return await UninstallAsync(command, server, runtime, deleteAfter: false, ct);

                case ServerCommandType.Delete:
                    return await UninstallAsync(command, server, runtime, deleteAfter: true, ct);

                case ServerCommandType.Start:
                    return await StartAsync(command, server, runtime, ct);

                case ServerCommandType.Stop:
                    return await StopAsync(command, server, runtime, ct);

                case ServerCommandType.Restart:
                    await StopAsync(command, server, runtime, ct, updateCommandStatus: false);
                    return await StartAsync(command, server, runtime, ct);

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
            return new ServerCommandExecutionResult(fallbackStatus, fallbackProcessId, ex.Message);
        }
    }

    public async Task<ServerCommandExecutionResult> SendConsoleInputAsync(
        Guid serverId,
        string input,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new ServerCommandExecutionResult(
                "Unknown",
                null,
                "Console command input is required.");
        }

        if (!_trackedProcesses.TryGetValue(serverId, out var process) || process.HasExited)
        {
            _trackedProcesses.TryRemove(serverId, out _);
            return new ServerCommandExecutionResult(
                "Unknown",
                null,
                "Console input is not available for this server process yet.");
        }

        if (!process.StartInfo.RedirectStandardInput)
        {
            return new ServerCommandExecutionResult(
                "Running",
                process.Id,
                "Console input is not available for this server process yet.");
        }

        try
        {
            await process.StandardInput.WriteLineAsync(input.Trim());
            await process.StandardInput.FlushAsync();
            await EmitLineAsync(serverId, "[ConsoleInput] Sent input to server console.", ct);

            return new ServerCommandExecutionResult("Running", process.Id, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or IOException)
        {
            return new ServerCommandExecutionResult(
                "Running",
                process.Id,
                $"Console input is not available for this server process yet. {ex.Message}");
        }
    }

    private async Task<ServerCommandExecutionResult> UninstallAsync(
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
        return new ServerCommandExecutionResult(finalStatus, null, null);
    }

    private async Task<ServerCommandExecutionResult> StartAsync(
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
                return new ServerCommandExecutionResult("Running", existingProcess.Id, null);
            }
        }

        var process = await runtime.StartAsync(server, line => EmitLineAsync(server.Id, line, CancellationToken.None), ct);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _ = OnProcessExitedAsync(server, process);
        _trackedProcesses[server.Id] = process;

        await EmitLineAsync(server.Id, $"[Start] Process started with PID {process.Id}.", ct);
        await PostStatusAsync(command.CommandId, server.Id, "Running", process.Id, null, ct);
        return new ServerCommandExecutionResult("Running", process.Id, null);
    }

    private async Task<ServerCommandExecutionResult> StopAsync(
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

            return new ServerCommandExecutionResult("Stopped", null, null);
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

        return new ServerCommandExecutionResult("Stopped", null, null);
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

    private async Task EmitLineAsync(Guid serverId, string line, CancellationToken ct)
    {
        var recordedUtc = DateTimeOffset.UtcNow;

        await _controlPlaneClient.PostConsoleLineAsync(
            new ConsoleLineMessage(serverId, recordedUtc, line),
            ct);

        try
        {
            await _cloudControlClient.PushConsoleLinesAsync(
                serverId,
                [
                    new PushCloudConsoleLineItem(
                        line,
                        InferConsoleLevel(line),
                        recordedUtc)
                ],
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Cloud console push failed for server {ServerId}; local console path remains active.",
                serverId);
        }
    }

    private static string InferConsoleLevel(string line)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        return "Info";
    }
}

public sealed record ServerCommandExecutionResult(
    string Status,
    int? ProcessId,
    string? ErrorMessage);
