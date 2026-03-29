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

    public Task<ServerSummaryDto?> GetServerAsync(Guid id, CancellationToken ct = default)
        => _httpClient.GetFromJsonAsync<ServerSummaryDto>($"api/servers/{id}", ct);

    public Task<ServerDetailsDto?> GetServerDetailsAsync(Guid id, CancellationToken ct = default)
        => _httpClient.GetFromJsonAsync<ServerDetailsDto>($"api/servers/{id}/details", ct);

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
}