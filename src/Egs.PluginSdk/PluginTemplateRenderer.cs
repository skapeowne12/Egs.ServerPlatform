using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Egs.PluginSdk;

public static class PluginTemplateRenderer
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{(?<path>[^}:]+)(:(?<format>[^}]+))?\}", RegexOptions.Compiled);

    private static readonly Regex InvalidPathCharsRegex =
        new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);

    public static string Render(string? template, PluginRenderContext context)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        return PlaceholderRegex.Replace(template, match =>
        {
            var path = match.Groups["path"].Value.Trim();
            var format = match.Groups["format"].Success ? match.Groups["format"].Value.Trim() : string.Empty;

            var rawValue = ResolveValue(path, context);
            return ApplyFormat(rawValue, format);
        });
    }

    public static string MakeSafePathSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var cleaned = InvalidPathCharsRegex.Replace(value.Trim(), "-");
        cleaned = cleaned.Replace(' ', '-');
        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static object? ResolveValue(string path, PluginRenderContext context)
    {
        return path.ToLowerInvariant() switch
        {
            "plugin.key" => context.Plugin.Key,
            "plugin.displayname" => context.Plugin.DisplayName,
            "server.id" => context.ServerId.ToString("D"),
            "server.idshort" => context.ServerId.ToString("N")[..8],
            "server.name" => context.ServerName,
            "server.namesafe" => MakeSafePathSegment(context.ServerName, "server"),
            "server.gamekey" => context.GameKey,
            "server.gamekeysafe" => MakeSafePathSegment(context.GameKey, "game"),
            "server.nodename" => context.NodeName,
            "server.installpath" => context.InstallPath,
            "server.installdirectory" => context.InstallDirectory,
            "settings.startuparguments" => context.Settings.StartupArguments,
            "settings.additionalargs" => ResolveSettingsValue(context.Settings.Plugin ?? new JsonObject(), path) ?? context.Settings.StartupArguments,
            _ when path.StartsWith("settings.", StringComparison.OrdinalIgnoreCase)
                => ResolveSettingsValue(context.Settings.Plugin ?? new JsonObject(), path),
            _ => string.Empty
        };
    }

    private static object? ResolveSettingsValue(JsonObject source, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return string.Empty;
        }

        JsonNode? current = source;
        foreach (var segment in segments.Skip(1))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
            {
                return string.Empty;
            }
        }

        return current switch
        {
            null => string.Empty,
            JsonValue value when value.TryGetValue<bool>(out var booleanValue) => booleanValue,
            JsonValue value when value.TryGetValue<int>(out var intValue) => intValue,
            JsonValue value when value.TryGetValue<long>(out var longValue) => longValue,
            JsonValue value when value.TryGetValue<decimal>(out var decimalValue) => decimalValue,
            JsonValue value when value.TryGetValue<string>(out var stringValue) => stringValue ?? string.Empty,
            _ => current.ToJsonString()
        };
    }

    private static string ApplyFormat(object? rawValue, string format)
    {
        if (rawValue is null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            return rawValue.ToString() ?? string.Empty;
        }

        var formatParts = format.Split(':', 2, StringSplitOptions.TrimEntries);
        var formatName = formatParts[0].ToLowerInvariant();

        return formatName switch
        {
            "quote" => Quote(rawValue.ToString() ?? string.Empty),
            "boolint" => AsBoolean(rawValue) ? "1" : "0",
            "flag" => AsBoolean(rawValue) ? (formatParts.Length > 1 ? formatParts[1] : string.Empty) : string.Empty,
            "safe" => MakeSafePathSegment(rawValue.ToString() ?? string.Empty, string.Empty),
            _ => rawValue.ToString() ?? string.Empty
        };
    }

    private static bool AsBoolean(object value) =>
        value switch
        {
            bool booleanValue => booleanValue,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            string stringValue when int.TryParse(stringValue, out var parsedInt) => parsedInt != 0,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            _ => false
        };

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
