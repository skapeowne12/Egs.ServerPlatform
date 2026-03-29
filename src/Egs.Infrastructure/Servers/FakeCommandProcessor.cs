using Egs.Agent.Abstractions.Commands;
using Egs.Agent.Abstractions.Status;
using Egs.Application.Servers;
using Egs.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Egs.Infrastructure.Servers;

public sealed class FakeCommandProcessor : BackgroundService
{
    private readonly IServerCommandQueue _commandQueue;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IServerStatusNotifier _statusNotifier;
    private readonly ILogger<FakeCommandProcessor> _logger;

    public FakeCommandProcessor(
        IServerCommandQueue commandQueue,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IServerStatusNotifier statusNotifier,
        ILogger<FakeCommandProcessor> logger)
    {
        _commandQueue = commandQueue;
        _dbContextFactory = dbContextFactory;
        _statusNotifier = statusNotifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var command in _commandQueue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessCommandAsync(command, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command {CommandId}", command.CommandId);
            }
        }
    }

    private async Task ProcessCommandAsync(ServerCommandMessage command, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == command.ServerId, ct);
        if (server is null)
            return;

        await Task.Delay(TimeSpan.FromSeconds(1), ct);

        switch (command.Type)
        {
            case ServerCommandType.Start:
                server.Status = "Running";
                server.ProcessId = Random.Shared.Next(1000, 99999);
                break;

            case ServerCommandType.Stop:
                server.Status = "Stopped";
                server.ProcessId = null;
                break;

            case ServerCommandType.Restart:
                server.Status = "Running";
                server.ProcessId = Random.Shared.Next(1000, 99999);
                break;
        }

        server.UpdatedUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await _statusNotifier.NotifyStatusChangedAsync(
            new ServerStatusChangedMessage(
                server.Id,
                server.Name,
                server.Status,
                server.ProcessId,
                server.UpdatedUtc),
            ct);
    }
}