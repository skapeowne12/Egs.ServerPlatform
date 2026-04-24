namespace Egs.PluginSdk;

public sealed class PluginRequirementDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public List<int> Ports { get; set; } = [];
    public string OperatingSystem { get; set; } = string.Empty;
    public int MinimumFreeDiskGb { get; set; }
    public string Message { get; set; } = string.Empty;
}
