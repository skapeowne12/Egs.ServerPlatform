namespace Egs.Contracts.Servers;

public sealed class ServerMetadataItemDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Highlight { get; set; }
}