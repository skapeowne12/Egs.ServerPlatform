using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Egs.PluginSdk;

namespace Egs.Agent.Windows.Services;

public sealed class PluginSetupActionExecutor
{
    private readonly ILogger<PluginSetupActionExecutor> _logger;

    public PluginSetupActionExecutor(ILogger<PluginSetupActionExecutor> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteStageAsync(
        PluginDescriptor plugin,
        string stage,
        PluginRenderContext context,
        Func<string, Task> writeLineAsync,
        CancellationToken ct)
    {
        var actions = plugin.Setup
            .Where(x => string.Equals(x.Stage, stage, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            await ExecuteActionAsync(plugin, action, context, writeLineAsync, ct);
        }
    }

    private async Task ExecuteActionAsync(
        PluginDescriptor plugin,
        PluginSetupActionDefinition action,
        PluginRenderContext context,
        Func<string, Task> writeLineAsync,
        CancellationToken ct)
    {
        switch (action.Type.Trim().ToLowerInvariant())
        {
            case "ensuredirectory":
                {
                    var path = ResolveFileSystemPath(action.Path, context);
                    Directory.CreateDirectory(path);
                    await writeLineAsync($"[Setup] Ensured directory '{path}'.");
                    return;
                }

            case "writetemplatefile":
                {
                    var templatePath = Path.Combine(plugin.SourceDirectory, NormalizeRelativePath(action.TemplateFile));
                    if (!File.Exists(templatePath))
                    {
                        throw new FileNotFoundException($"Template file '{templatePath}' was not found.", templatePath);
                    }

                    var destinationPath = ResolveFileSystemPath(action.DestinationPath, context);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                    if (!action.Overwrite && File.Exists(destinationPath))
                    {
                        await writeLineAsync($"[Setup] Skipped existing file '{destinationPath}'.");
                        return;
                    }

                    var templateContent = await File.ReadAllTextAsync(templatePath, ct);
                    var renderedContent = PluginTemplateRenderer.Render(templateContent, context);
                    await File.WriteAllTextAsync(destinationPath, renderedContent, Encoding.UTF8, ct);
                    await writeLineAsync($"[Setup] Wrote template file '{destinationPath}'.");
                    return;
                }

            case "writefile":
                {
                    var destinationPath = ResolveFileSystemPath(action.DestinationPath, context);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                    if (!action.Overwrite && File.Exists(destinationPath))
                    {
                        await writeLineAsync($"[Setup] Skipped existing file '{destinationPath}'.");
                        return;
                    }

                    var content = PluginTemplateRenderer.Render(action.Content, context);
                    await File.WriteAllTextAsync(destinationPath, content, Encoding.UTF8, ct);
                    await writeLineAsync($"[Setup] Wrote file '{destinationPath}'.");
                    return;
                }

            case "replacetext":
                {
                    var path = ResolveFileSystemPath(action.Path, context);
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException($"File '{path}' was not found for replaceText.", path);
                    }

                    var search = PluginTemplateRenderer.Render(action.Search, context);
                    var replace = PluginTemplateRenderer.Render(action.Replace, context);
                    var current = await File.ReadAllTextAsync(path, ct);
                    var updated = current.Replace(search, replace, StringComparison.Ordinal);
                    await File.WriteAllTextAsync(path, updated, Encoding.UTF8, ct);
                    await writeLineAsync($"[Setup] Replaced text in '{path}'.");
                    return;
                }

            case "appendline":
                {
                    var path = ResolveFileSystemPath(action.Path, context);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var line = PluginTemplateRenderer.Render(action.Line, context);
                    await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, ct);
                    await writeLineAsync($"[Setup] Appended line to '{path}'.");
                    return;
                }

            case "setinivalue":
                {
                    var path = ResolveFileSystemPath(action.Path, context);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var section = PluginTemplateRenderer.Render(action.Section, context);
                    var key = PluginTemplateRenderer.Render(action.Key, context);
                    var value = PluginTemplateRenderer.Render(action.Value, context);

                    var current = File.Exists(path)
                        ? await File.ReadAllLinesAsync(path, ct)
                        : Array.Empty<string>();

                    var updated = SetIniValue(current, section, key, value);
                    await File.WriteAllLinesAsync(path, updated, Encoding.UTF8, ct);
                    await writeLineAsync($"[Setup] Set INI value [{section}] {key}=... in '{path}'.");
                    return;
                }

            case "setjsonvalue":
                {
                    var path = ResolveFileSystemPath(action.Path, context);
                    var jsonPath = PluginTemplateRenderer.Render(action.JsonPath, context);
                    var renderedValue = PluginTemplateRenderer.Render(action.Value, context);

                    if (!File.Exists(path) && action.IgnoreIfMissing)
                    {
                        await writeLineAsync($"[Setup] Skipped JSON update because '{path}' does not exist yet.");
                        return;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                    JsonObject root;
                    if (File.Exists(path))
                    {
                        var raw = await File.ReadAllTextAsync(path, ct);
                        root = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw)?.AsObject()
                               ?? new JsonObject();
                    }
                    else if (action.CreateIfMissing)
                    {
                        root = new JsonObject();
                    }
                    else
                    {
                        throw new FileNotFoundException($"File '{path}' was not found for setJsonValue.", path);
                    }

                    SetJsonValue(root, jsonPath, ParseJsonNodeValue(renderedValue));
                    await File.WriteAllTextAsync(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    }), Encoding.UTF8, ct);
                    await writeLineAsync($"[Setup] Set JSON value '{jsonPath}' in '{path}'.");
                    return;
                }

            case "runprocessuntilfileexists":
                {
                    var executablePath = ResolveExecutablePath(action.ExecutablePath, context);
                    var arguments = PluginTemplateRenderer.Render(action.ArgumentsTemplate, context).Trim();
                    var workingDirectory = ResolveWorkingDirectory(action.WorkingDirectory, executablePath, context);
                    var waitForPath = ResolveFileSystemPath(action.WaitForPath, context);
                    var timeoutSeconds = action.TimeoutSeconds <= 0 ? 60 : action.TimeoutSeconds;

                    if (!File.Exists(executablePath))
                    {
                        throw new FileNotFoundException($"Executable '{executablePath}' was not found for runProcessUntilFileExists.", executablePath);
                    }

                    if (File.Exists(waitForPath))
                    {
                        await writeLineAsync($"[Setup] File '{waitForPath}' already exists. Skipping process bootstrap.");
                        return;
                    }

                    await writeLineAsync($"[Setup] Starting bootstrap process '{Path.GetFileName(executablePath)}' until '{waitForPath}' exists.");

                    using var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = executablePath,
                            Arguments = arguments,
                            WorkingDirectory = workingDirectory,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        },
                        EnableRaisingEvents = true
                    };

                    process.OutputDataReceived += (_, args) =>
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                        {
                            _ = writeLineAsync($"[Setup] {args.Data}");
                        }
                    };

                    process.ErrorDataReceived += (_, args) =>
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                        {
                            _ = writeLineAsync($"[Setup] {args.Data}");
                        }
                    };

                    if (!process.Start())
                    {
                        throw new InvalidOperationException($"Failed to start bootstrap process '{executablePath}'.");
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
                    while (!File.Exists(waitForPath))
                    {
                        ct.ThrowIfCancellationRequested();

                        if (process.HasExited)
                        {
                            throw new InvalidOperationException(
                                $"Bootstrap process '{Path.GetFileName(executablePath)}' exited with code {process.ExitCode} before '{waitForPath}' was created.");
                        }

                        if (DateTimeOffset.UtcNow >= deadline)
                        {
                            throw new TimeoutException(
                                $"Timed out waiting for '{waitForPath}' after {timeoutSeconds} seconds.");
                        }

                        await Task.Delay(1000, ct);
                    }

                    await writeLineAsync($"[Setup] Bootstrap file '{waitForPath}' detected. Stopping bootstrap process.");

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        try
                        {
                            await process.WaitForExitAsync(ct);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }

                    await writeLineAsync("[Setup] Bootstrap process finished.");
                    return;
                }

            default:
                _logger.LogWarning("Unsupported setup action type '{SetupActionType}' in plugin '{PluginKey}'.", action.Type, plugin.Key);
                await writeLineAsync($"[Setup] WARNING: Unsupported setup action '{action.Type}' was ignored.");
                return;
        }
    }

    private static string ResolveFileSystemPath(string template, PluginRenderContext context)
    {
        var rendered = PluginTemplateRenderer.Render(template, context);
        rendered = NormalizeRelativePath(rendered);

        if (Path.IsPathRooted(rendered))
        {
            return Path.GetFullPath(rendered);
        }

        return Path.GetFullPath(Path.Combine(context.InstallDirectory, rendered));
    }

    private static string ResolveExecutablePath(string template, PluginRenderContext context)
    {
        var rendered = PluginTemplateRenderer.Render(template, context);
        rendered = NormalizeRelativePath(rendered);

        if (Path.IsPathRooted(rendered))
        {
            return Path.GetFullPath(rendered);
        }

        return Path.GetFullPath(Path.Combine(context.InstallDirectory, rendered));
    }

    private static string ResolveWorkingDirectory(string template, string executablePath, PluginRenderContext context)
    {
        var rendered = PluginTemplateRenderer.Render(template, context).Trim();
        if (string.IsNullOrWhiteSpace(rendered))
        {
            return Path.GetDirectoryName(executablePath) ?? context.InstallDirectory;
        }

        rendered = NormalizeRelativePath(rendered);
        if (Path.IsPathRooted(rendered))
        {
            return Path.GetFullPath(rendered);
        }

        return Path.GetFullPath(Path.Combine(context.InstallDirectory, rendered));
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private static string[] SetIniValue(IReadOnlyList<string> lines, string section, string key, string value)
    {
        var result = lines.ToList();
        var sectionHeader = $"[{section}]";

        var sectionIndex = result.FindIndex(x => string.Equals(x.Trim(), sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
            {
                result.Add(string.Empty);
            }

            result.Add(sectionHeader);
            result.Add($"{key}={value}");
            return result.ToArray();
        }

        var insertIndex = result.Count;
        for (var i = sectionIndex + 1; i < result.Count; i++)
        {
            if (result[i].TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                insertIndex = i;
                break;
            }

            var equalsIndex = result[i].IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var existingKey = result[i][..equalsIndex].Trim();
            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                result[i] = $"{key}={value}";
                return result.ToArray();
            }
        }

        result.Insert(insertIndex, $"{key}={value}");
        return result.ToArray();
    }

    private static void SetJsonValue(JsonObject root, string jsonPath, JsonNode? value)
    {
        var segments = NormalizeJsonPath(jsonPath);
        if (segments.Count == 0)
        {
            throw new InvalidOperationException($"JSON path '{jsonPath}' is invalid.");
        }

        JsonObject current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var segment = segments[i];
            if (current[segment] is not JsonObject child)
            {
                child = new JsonObject();
                current[segment] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }

    private static List<string> NormalizeJsonPath(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            return [];
        }

        var trimmed = jsonPath.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed == "$")
        {
            return [];
        }

        return trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static JsonNode? ParseJsonNodeValue(string renderedValue)
    {
        if (string.IsNullOrWhiteSpace(renderedValue))
        {
            return string.Empty;
        }

        var value = renderedValue.Trim();

        if (bool.TryParse(value, out var boolValue))
        {
            return JsonValue.Create(boolValue);
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return JsonValue.Create(intValue);
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return JsonValue.Create(longValue);
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return JsonValue.Create(decimalValue);
        }

        if ((value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal))
            || (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
            || (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)))
        {
            try
            {
                return JsonNode.Parse(value);
            }
            catch
            {
            }
        }

        return JsonValue.Create(renderedValue);
    }
}
