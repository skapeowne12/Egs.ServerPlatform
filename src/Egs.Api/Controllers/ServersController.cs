using Egs.Application.Servers;
using Egs.Contracts.Servers;
using Microsoft.AspNetCore.Mvc;

namespace Egs.Api.Controllers;

[ApiController]
[Route("api/servers")]
public sealed class ServersController : ControllerBase
{
    private readonly IServerService _serverService;

    public ServersController(IServerService serverService)
    {
        _serverService = serverService;
    }

    [HttpGet]
    public Task<IReadOnlyList<ServerSummaryDto>> GetAll(CancellationToken ct)
        => _serverService.GetAllAsync(ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var server = await _serverService.GetAsync(id, ct);
        return server is null ? NotFound() : Ok(server);
    }

    [HttpGet("{id:guid}/details")]
    public async Task<IActionResult> GetDetails(Guid id, CancellationToken ct)
    {
        var server = await _serverService.GetDetailsAsync(id, ct);
        return server is null ? NotFound() : Ok(server);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServerRequest request, CancellationToken ct)
    {
        var id = await _serverService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPut("{id:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] ServerSettingsDto settings, CancellationToken ct)
    {
        await _serverService.UpdateSettingsAsync(id, settings, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/install")]
    public async Task<IActionResult> Install(Guid id, CancellationToken ct)
    {
        await _serverService.InstallAsync(id, ct);
        return Accepted();
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        await _serverService.StartAsync(id, ct);
        return Accepted();
    }

    [HttpPost("{id:guid}/stop")]
    public async Task<IActionResult> Stop(Guid id, CancellationToken ct)
    {
        await _serverService.StopAsync(id, ct);
        return Accepted();
    }

    [HttpPost("{id:guid}/restart")]
    public async Task<IActionResult> Restart(Guid id, CancellationToken ct)
    {
        await _serverService.RestartAsync(id, ct);
        return Accepted();
    }

    [HttpPost("{id:guid}/uninstall")]
    public async Task<IActionResult> Uninstall(Guid id, CancellationToken ct)
    {
        await _serverService.UninstallAsync(id, ct);
        return Accepted();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _serverService.DeleteAsync(id, ct);
        return Accepted();
    }
}