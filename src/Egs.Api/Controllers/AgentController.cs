using Egs.Agent.Abstractions.Commands;
using Egs.Agent.Abstractions.Console;
using Egs.Agent.Abstractions.Status;
using Egs.Application.Servers;
using Egs.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Egs.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IServerStatusNotifier _statusNotifier;
    private readonly IServerConsoleNotifier _consoleNotifier;

    public AgentController(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IServerStatusNotifier statusNotifier,
        IServerConsoleNotifier consoleNotifier)
    {
        _dbContextFactory = dbContextFactory;
        _statusNotifier = statusNotifier;
        _consoleNotifier = consoleNotifier;
    }

    [HttpGet("commands/{nodeName}")]
    public async Task<ActionResult<AgentPollResponse>> Poll(string nodeName, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var pending = (await db.ServerCommands
            .Where(x => x.NodeName == nodeName && x.Status == "Pending")
            .ToListAsync(ct))
            .OrderBy(x => x.CreatedUtc)
            .Take(20)
            .ToList();

        foreach (var command in pending)
        {
            command.Status = "Claimed";
            command.ClaimedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        var result = pending
            .Select(x => new ServerCommandMessage(
                x.Id,
                x.ServerId,
                Enum.Parse<ServerCommandType>(x.Type),
                x.CreatedUtc))
            .ToList();

        return Ok(new AgentPollResponse(result));
    }

    [HttpPost("status")]
    public async Task<IActionResult> UpdateStatus([FromBody] AgentServerUpdateMessage message, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == message.ServerId, ct);
        if (server is null)
            return NotFound();

        server.Status = message.Status;
        server.ProcessId = message.ProcessId;
        server.UpdatedUtc = message.UpdatedUtc;

        var command = (await db.ServerCommands
            .Where(x => x.ServerId == message.ServerId && x.Status == "Claimed")
            .ToListAsync(ct))
            .OrderByDescending(x => x.ClaimedUtc)
            .FirstOrDefault();

        if (command is not null)
        {
            command.Status = string.IsNullOrWhiteSpace(message.Error) ? "Completed" : "Failed";
            command.Error = message.Error;
            command.CompletedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await _statusNotifier.NotifyStatusChangedAsync(
            new ServerStatusChangedMessage(
                server.Id,
                server.Name,
                server.Status,
                server.ProcessId,
                server.UpdatedUtc),
            ct);

        return Accepted();
    }

    [HttpPost("console")]
    public async Task<IActionResult> PostConsoleLine([FromBody] ConsoleLineMessage message, CancellationToken ct)
    {
        await _consoleNotifier.PublishAsync(message, ct);
        return Accepted();
    }
}