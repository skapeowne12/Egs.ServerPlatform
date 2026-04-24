using System.Net;
using System.Net.Http.Json;
using Egs.Contracts.Servers;

namespace Egs.Web.Services;

public sealed class ServerApiClient
{
    private readonly HttpClient _httpClient;

    public ServerApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ServerSummaryDto>> GetServersAsync(CancellationToken ct = default)
    {
        var result = await _httpClient.GetFromJsonAsync<List<ServerSummaryDto>>("api/servers", ct);
        return result ?? [];
    }

    public async Task<ServerSummaryDto?> GetServerAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"api/servers/{id}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServerSummaryDto>(cancellationToken: ct);
    }

    public async Task<ServerDetailsDto?> GetServerDetailsAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"api/servers/{id}/details", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServerDetailsDto>(cancellationToken: ct);
    }

    public async Task SaveServerSettingsAsync(Guid id, ServerSettingsDto settings, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/servers/{id}/settings", settings, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task CreateServerAsync(CreateServerRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/servers", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task InstallAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"api/servers/{id}/install", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task StartAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"api/servers/{id}/start", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"api/servers/{id}/stop", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RestartAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"api/servers/{id}/restart", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UninstallAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"api/servers/{id}/uninstall", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"api/servers/{id}", ct);
        response.EnsureSuccessStatusCode();
    }
}