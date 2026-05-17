using Egs.Agent.Abstractions.Commands;
using Egs.Agent.Abstractions.Servers;
using Egs.Contracts.Cloud;
using System.Text.Json;

namespace Egs.Agent.Windows.Services;

public sealed class AgentWorker : BackgroundService
{
    private readonly ControlPlaneClient _controlPlaneClient;
    private readonly CloudControlClient _cloudControlClient;
    private readonly ServerCommandExecutor _commandExecutor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(
        ControlPlaneClient controlPlaneClient,
        CloudControlClient cloudControlClient,
        ServerCommandExecutor commandExecutor,
        IConfiguration configuration,
        ILogger<AgentWorker> logger)
    {
        _controlPlaneClient = controlPlaneClient;
        _cloudControlClient = cloudControlClient;
        _commandExecutor = commandExecutor;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent worker started.");

        var cloudEnabled = _configuration.GetValue<bool>("CloudControl:Enabled");
        var heartbeatSeconds = _configuration.GetValue<int?>("CloudControl:HeartbeatSeconds") ?? 15;
        var serverSyncSeconds = _configuration.GetValue<int?>("CloudControl:ServerSyncSeconds") ?? 30;
        var commandPollSeconds = _configuration.GetValue<int?>("CloudControl:CommandPollSeconds") ?? 5;

        var localControlPlaneEnabled =
            _configuration.GetValue<bool?>("Agent:LocalControlPlaneEnabled") ?? true;

        var lastHeartbeatUtc = DateTimeOffset.MinValue;
        var lastServerSyncUtc = DateTimeOffset.MinValue;
        var lastCloudCommandPollUtc = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (cloudEnabled &&
                DateTimeOffset.UtcNow - lastHeartbeatUtc >= TimeSpan.FromSeconds(heartbeatSeconds))
            {
                try
                {
                    var node = await _cloudControlClient.SendHeartbeatAsync(stoppingToken);
                    lastHeartbeatUtc = DateTimeOffset.UtcNow;

                    if (node is not null)
                    {
                        _logger.LogInformation(
                            "Cloud heartbeat OK. NodeId={NodeId}, NodeKey={NodeKey}, Status={Status}",
                            node.Id,
                            node.NodeKey,
                            node.Status);

                        if (DateTimeOffset.UtcNow - lastServerSyncUtc >= TimeSpan.FromSeconds(serverSyncSeconds))
                        {
                            await SyncLocalServersToCloudAsync(stoppingToken);
                            lastServerSyncUtc = DateTimeOffset.UtcNow;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Cloud control plane not available: {Message}",
                        ex.Message);
                }
            }

            if (cloudEnabled &&
                DateTimeOffset.UtcNow - lastCloudCommandPollUtc >= TimeSpan.FromSeconds(commandPollSeconds))
            {
                try
                {
                    var cloudCommands = await _cloudControlClient.PollCommandsAsync(stoppingToken);
                    lastCloudCommandPollUtc = DateTimeOffset.UtcNow;

                    foreach (var command in cloudCommands)
                    {
                        await ProcessCloudCommandAsync(command, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Cloud command polling failed: {Message}",
                        ex.Message);
                }
            }

            if (localControlPlaneEnabled)
            {
                try
                {
                    var commands = await _controlPlaneClient.PollCommandsAsync(stoppingToken);

                    foreach (var command in commands)
                    {
                        await ProcessCommandAsync(command, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Local control plane not available: {Message}",
                        ex.Message);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessCloudCommandAsync(
        CloudServerCommandDto command,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Cloud command received. CommandId={CommandId}, ServerId={ServerId}, Type={CommandType}",
            command.Id,
            command.ServerId,
            command.CommandType);

        await _cloudControlClient.UpdateCommandStatusAsync(
            command.Id,
            "Running",
            null,
            null,
            ct);

        try
        {
            if (string.Equals(command.CommandType, "ConsoleCommand", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessCloudConsoleCommandAsync(command, ct);
                return;
            }

            var localCommand = new ServerCommandMessage(
                command.Id,
                command.ServerId,
                MapCloudCommandType(command.CommandType),
                command.CreatedUtc);

            var result = await _commandExecutor.ExecuteAsync(localCommand, ct);
            var cloudStatus = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Completed" : "Failed";

            await _cloudControlClient.UpdateCommandStatusAsync(
                command.Id,
                cloudStatus,
                JsonSerializer.Serialize(new
                {
                    message = "Cloud command executed by Windows agent.",
                    status = result.Status,
                    processId = result.ProcessId
                }),
                result.ErrorMessage,
                ct);

            await _cloudControlClient.PushServerStatusAsync(
                command.ServerId,
                new PushCloudServerStatusRequest(
                    result.Status,
                    result.ProcessId,
                    null,
                    null,
                    "{}"),
                ct);

            _logger.LogInformation(
                "Cloud command completed. CommandId={CommandId}, Type={CommandType}",
                command.Id,
                command.CommandType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _cloudControlClient.UpdateCommandStatusAsync(
                command.Id,
                "Failed",
                null,
                ex.Message,
                ct);

            _logger.LogError(
                ex,
                "Cloud command failed. CommandId={CommandId}, Type={CommandType}",
                command.Id,
                command.CommandType);
        }
    }

    private async Task ProcessCloudConsoleCommandAsync(
        CloudServerCommandDto command,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ConsoleCommandPayload>(
            string.IsNullOrWhiteSpace(command.PayloadJson) ? "{}" : command.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (payload is null || string.IsNullOrWhiteSpace(payload.Input))
        {
            await _cloudControlClient.UpdateCommandStatusAsync(
                command.Id,
                "Failed",
                null,
                "Console command input is required.",
                ct);
            return;
        }

        var result = await _commandExecutor.SendConsoleInputAsync(command.ServerId, payload.Input, ct);
        var cloudStatus = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Completed" : "Failed";

        await _cloudControlClient.UpdateCommandStatusAsync(
            command.Id,
            cloudStatus,
            JsonSerializer.Serialize(new
            {
                message = "Console input sent to server process.",
                status = result.Status,
                processId = result.ProcessId
            }),
            result.ErrorMessage,
            ct);

        await _cloudControlClient.PushServerStatusAsync(
            command.ServerId,
            new PushCloudServerStatusRequest(
                result.Status,
                result.ProcessId,
                null,
                null,
                "{}"),
            ct);
    }

    private async Task ProcessCommandAsync(ServerCommandMessage command, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing local command {CommandId} for server {ServerId}: {CommandType}",
            command.CommandId,
            command.ServerId,
            command.Type);

        await _commandExecutor.ExecuteAsync(command, ct);
    }

    private async Task SyncLocalServersToCloudAsync(CancellationToken ct)
    {
        IReadOnlyList<AgentServerDefinitionMessage> servers;

        try
        {
            var summaries = await _controlPlaneClient.GetServersAsync(ct);
            var definitions = new List<AgentServerDefinitionMessage>(summaries.Count);

            foreach (var summary in summaries)
            {
                definitions.Add(await _controlPlaneClient.GetServerDefinitionAsync(summary.Id, ct));
            }

            servers = definitions;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Local server enumeration failed during cloud sync: {Message}", ex.Message);
            return;
        }

        foreach (var server in servers)
        {
            try
            {
                await _cloudControlClient.SyncServerAsync(
                    new UpsertCloudServerRequest(
                        server.Id,
                        server.Name,
                        server.GameKey,
                        server.InstallPath,
                        string.IsNullOrWhiteSpace(server.Status) ? "Unknown" : server.Status,
                        _configuration["CloudControl:DefaultPublicStatus"] ?? "MembersOnly",
                        JsonSerializer.Serialize(server.Settings)),
                    ct);

                await _cloudControlClient.PushServerStatusAsync(
                    server.Id,
                    new PushCloudServerStatusRequest(
                        string.IsNullOrWhiteSpace(server.Status) ? "Unknown" : server.Status,
                        server.ProcessId,
                        null,
                        null,
                        "{}"),
                    ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Cloud sync failed for local server {ServerId}: {Message}",
                    server.Id,
                    ex.Message);
            }
        }
    }

    private static ServerCommandType MapCloudCommandType(string commandType)
    {
        if (Enum.TryParse<ServerCommandType>(commandType, ignoreCase: true, out var parsed))
            return parsed;

        throw new NotSupportedException($"Unsupported cloud command type '{commandType}'.");
    }
}
