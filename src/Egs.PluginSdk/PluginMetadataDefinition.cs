namespace Egs.PluginSdk;

public sealed class PluginMetadataDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    // "jsonValue" or "static"
    public string Type { get; set; } = "jsonValue";

    // jsonValue fields
    public string? Path { get; set; }
    public string? JsonPath { get; set; }

    // static value or template-rendered value
    public string? Value { get; set; }

    // True = show in the main connection/details card
    public bool Highlight { get; set; }
}