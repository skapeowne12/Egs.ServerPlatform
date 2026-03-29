using Egs.Agent.Abstractions.Commands;

namespace Egs.Agent.Windows.Services;

public sealed class AgentWorker : BackgroundService
{
    private readonly ControlPlaneClient _controlPlaneClient;
    private readonly ServerCommandExecutor _commandExecutor;
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(
        ControlPlaneClient controlPlaneClient,
        ServerCommandExecutor commandExecutor,
        ILogger<AgentWorker> logger)
    {
        _controlPlaneClient = controlPlaneClient;
        _commandExecutor = commandExecutor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var commands = await _controlPlaneClient.PollCommandsAsync(stoppingToken);

                foreach (var command in commands)
                {
                    await ProcessCommandAsync(command, stoppingToken);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("API not ready yet: {Message}", ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent polling loop failed.");
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
