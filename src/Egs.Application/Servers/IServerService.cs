using Egs.Contracts.Servers;

namespace Egs.Application.Servers;

public interface IServerService
{
    Task<IReadOnlyList<ServerSummaryDto>> GetAllAsync(CancellationToken ct = default);
    Task<ServerSummaryDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServerDetailsDto?> GetDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAsync(CreateServerRequest request, CancellationToken ct = default);
    Task UpdateSettingsAsync(Guid id, ServerSettingsDto settings, CancellationToken ct = default);

    Task InstallAsync(Guid id, CancellationToken ct = default);
    Task StartAsync(Guid id, CancellationToken ct = default);
    Task StopAsync(Guid id, CancellationToken ct = default);
    Task RestartAsync(Guid id, CancellationToken ct = default);
    Task UninstallAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}