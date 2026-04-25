using Egs.Agent.Abstractions.Commands;
using Egs.Contracts.Cloud;
using Egs.Agent.Windows.Cloud;
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

        var localControlPlaneEnabled =
            _configuration.GetValue<bool?>("Agent:LocalControlPlaneEnabled") ?? true;

        var lastHeartbeatUtc = DateTimeOffset.MinValue;

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

                        await _cloudControlClient.SyncServerAsync(
                            new UpsertCloudServerRequest(
                                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                                "Test Cloud Server",
                                "test",
                                "C:\\Egs\\Servers\\Test",
                                "Stopped",
                                "MembersOnly",
                                "{}"),
                            stoppingToken);
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

    private async Task ProcessCommandAsync(ServerCommandMessage command, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing command {CommandId} for server {ServerId}: {CommandType}",
            command.CommandId,
            command.ServerId,
            command.Type);

        await _commandExecutor.ExecuteAsync(command, ct);
    }
}