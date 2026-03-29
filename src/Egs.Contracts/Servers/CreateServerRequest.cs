namespace Egs.Contracts.Servers;

public sealed class CreateServerRequest
{
    public string Name { get; set; } = string.Empty;
    public string GameKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = "Local";
}