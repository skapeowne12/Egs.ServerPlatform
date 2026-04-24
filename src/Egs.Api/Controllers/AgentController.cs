using Egs.Agent.Abstractions.Commands;
using Egs.Agent.Abstractions.Console;
using Egs.Agent.Abstractions.Servers;
using Egs.Agent.Abstractions.Status;
using Egs.Application.Servers;
using Egs.Contracts.Servers;
using Egs.Domain.Servers;
using Egs.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Egs.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    [HttpGet("servers/{serverId:guid}")]
    public async Task<ActionResult<AgentServerDefinitionMessage>> GetServer(Guid serverId, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == serverId, ct);
        if (server is null)
            return NotFound();

        return Ok(new AgentServerDefinitionMessage
        {
            Id = server.Id,
            Name = server.Name,
            GameKey = server.GameKey,
            NodeName = server.NodeName,
            InstallPath = server.InstallPath,
            Status = server.Status,
            ProcessId = server.ProcessId,
            Settings = DeserializeSettings(server.SettingsJson)
        });
    }

    [HttpPost("status")]
    public async Task<IActionResult> UpdateStatus([FromBody] AgentServerUpdateMessage message, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == message.ServerId, ct);
        if (server is null)
            return NotFound();

        ServerCommand? command = null;
        if (message.CommandId is Guid commandId)
        {
            command = await db.ServerCommands.SingleOrDefaultAsync(x => x.Id == commandId, ct);
            if (command is not null)
            {
                command.Status = string.IsNullOrWhiteSpace(message.Error) ? "Completed" : "Failed";
                command.Error = message.Error;
                command.CompletedUtc = DateTime.UtcNow;
            }
        }

        server.Status = message.Status;
        server.ProcessId = message.ProcessId;
        server.UpdatedUtc = message.UpdatedUtc;

        if (string.Equals(message.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            server.StartedUtc ??= message.UpdatedUtc;
        }
        else if (string.Equals(message.Status, "Stopped", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Status, "Deleted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(message.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            server.StartedUtc = null;
        }

        var deleteServer = string.IsNullOrWhiteSpace(message.Error)
            && command is not null
            && string.Equals(command.Type, "Delete", StringComparison.OrdinalIgnoreCase);

        var serverId = server.Id;
        var serverName = server.Name;
        var updatedUtc = server.UpdatedUtc;

        if (deleteServer)
        {
            db.Servers.Remove(server);
        }

        await db.SaveChangesAsync(ct);

        await _statusNotifier.NotifyStatusChangedAsync(
            new ServerStatusChangedMessage(
                serverId,
                serverName,
                deleteServer ? "Deleted" : message.Status,
                deleteServer ? null : message.ProcessId,
                updatedUtc),
            ct);

        return Accepted();
    }

    [HttpPost("console")]
    public async Task<IActionResult> PostConsoleLine([FromBody] ConsoleLineMessage message, CancellationToken ct)
    {
        await _consoleNotifier.PublishAsync(message, ct);
        return Accepted();
    }

    private static ServerSettingsDto DeserializeSettings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ServerSettingsDto();

        return JsonSerializer.Deserialize<ServerSettingsDto>(json, JsonOptions)
               ?? new ServerSettingsDto();
    }
}