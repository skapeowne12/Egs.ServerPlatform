namespace Egs.Agent.Windows.Services.Runtimes;

public sealed class GameServerRuntimeCatalog
{
    private readonly IReadOnlyList<IGameServerRuntime> _runtimes;

    public GameServerRuntimeCatalog(IEnumerable<IGameServerRuntime> runtimes)
    {
        _runtimes = runtimes.ToList();
    }

    public IGameServerRuntime GetRequired(string gameKey)
    {
        var runtime = _runtimes.FirstOrDefault(x => x.CanHandle(gameKey));
        return runtime ?? throw new NotSupportedException($"No runtime is registered for game key '{gameKey}'.");
    }
}
