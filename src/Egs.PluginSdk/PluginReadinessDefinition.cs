namespace Egs.PluginSdk;

public sealed class PluginReadinessDefinition
{
    public List<string> StdoutContains { get; set; } = [];
    public int TimeoutSeconds { get; set; }
}
