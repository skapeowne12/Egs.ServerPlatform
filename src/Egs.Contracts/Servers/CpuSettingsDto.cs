namespace Egs.Contracts.Servers;

public sealed class CpuSettingsDto
{
    public bool UseAffinityMask { get; set; }
    public long? AffinityMask { get; set; }
    public string PriorityClass { get; set; } = "Normal";
}