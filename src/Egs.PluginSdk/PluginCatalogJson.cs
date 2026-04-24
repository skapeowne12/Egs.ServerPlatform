using System.Text.Json;
using System.Text.Json.Nodes;

namespace Egs.PluginSdk;

public static class PluginCatalogJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static JsonObject CloneObject(JsonObject? source)
    {
        if (source is null)
        {
            return new JsonObject();
        }

        var json = source.ToJsonString(JsonOptions);
        return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
    }
}
