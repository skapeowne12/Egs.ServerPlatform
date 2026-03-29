using System.Text;
using Egs.Agent.Abstractions.Servers;

namespace Egs.Agent.Windows.Services.Runtimes;

public sealed class ValheimRuntime : SteamCmdGameRuntime
{
    private static readonly string[] Keys = ["valheim", "valheim-dedicated", "steam-valheim"];

    public ValheimRuntime(
        SteamCmdService steamCmdService,
        ILogger<SteamCmdGameRuntime> logger)
        : base(steamCmdService, logger)
    {
    }

    protected override IReadOnlyCollection<string> SupportedGameKeys => Keys;

    protected override string SteamAppId => "896660";

    protected override string ExecutableRelativePath => "valheim_server.exe";

    protected override TimeSpan? StartupReadinessTimeout => TimeSpan.FromMinutes(2);

    protected override string BuildStartArguments(AgentServerDefinitionMessage server)
    {
        var settings = server.Settings.Valheim ?? new();
        var serverName = string.IsNullOrWhiteSpace(settings.ServerName)
            ? server.Name
            : settings.ServerName.Trim();

        var worldName = string.IsNullOrWhiteSpace(settings.WorldName)
            ? MakeSafeWorldName(server.Name)
            : settings.WorldName.Trim();

        var password = settings.Password?.Trim() ?? string.Empty;
        if (password.Length < 5)
        {
            throw new InvalidOperationException(
                "Valheim requires a password with at least 5 characters. Set Settings.Valheim.Password before starting the server.");
        }

        var port = settings.Port <= 0 ? 2456 : settings.Port;

        var args = new StringBuilder();
        args.Append("-nographics -batchmode");
        args.Append(" -name ").Append(Quote(serverName));
        args.Append(" -port ").Append(port);
        args.Append(" -world ").Append(Quote(worldName));
        args.Append(" -password ").Append(Quote(password));
        args.Append(" -public ").Append(settings.Public ? '1' : '0');

        if (!string.IsNullOrWhiteSpace(settings.SaveDirectory))
        {
            args.Append(" -savedir ").Append(Quote(settings.SaveDirectory.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(settings.LogFilePath))
        {
            args.Append(" -logFile ").Append(Quote(settings.LogFilePath.Trim()));
        }

        if (settings.SaveIntervalSeconds is > 0)
        {
            args.Append(" -saveinterval ").Append(settings.SaveIntervalSeconds.Value);
        }

        if (settings.BackupCount is > 0)
        {
            args.Append(" -backups ").Append(settings.BackupCount.Value);
        }

        if (settings.BackupShortSeconds is > 0)
        {
            args.Append(" -backupshort ").Append(settings.BackupShortSeconds.Value);
        }

        if (settings.BackupLongSeconds is > 0)
        {
            args.Append(" -backuplong ").Append(settings.BackupLongSeconds.Value);
        }

        if (settings.Crossplay)
        {
            args.Append(" -crossplay");
        }

        if (!string.IsNullOrWhiteSpace(settings.InstanceId))
        {
            args.Append(" -instanceid ").Append(Quote(settings.InstanceId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(settings.Preset))
        {
            args.Append(" -preset ").Append(Quote(settings.Preset.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(server.Settings.StartupArguments))
        {
            args.Append(' ').Append(server.Settings.StartupArguments.Trim());
        }

        return args.ToString();
    }


    protected override bool IsServerReadyOutput(string line) =>
        line.Contains("Game server connected", StringComparison.OrdinalIgnoreCase);

    protected override async Task ValidateInstallAsync(AgentServerDefinitionMessage server, string executablePath, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        await base.ValidateInstallAsync(server, executablePath, writeLineAsync, ct);
        await writeLineAsync("[Install] Valheim dedicated server runtime is ready. Configure Settings.Valheim before starting.");
    }

    public override async Task StopAsync(AgentServerDefinitionMessage server, System.Diagnostics.Process process, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        if (process.HasExited)
        {
            return;
        }

        await writeLineAsync($"[Stop] Attempting graceful shutdown for Valheim server PID {process.Id}.");

        if (process.CloseMainWindow())
        {
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waitCts.CancelAfter(TimeSpan.FromSeconds(20));

            try
            {
                await process.WaitForExitAsync(waitCts.Token);
                await writeLineAsync("[Stop] Valheim server exited after CloseMainWindow().");
                return;
            }
            catch (OperationCanceledException)
            {
                await writeLineAsync("[Stop] Graceful window-close timed out; falling back to process tree termination.");
            }
        }
        else
        {
            await writeLineAsync("[Stop] No main window handle was available for graceful shutdown; falling back to process tree termination.");
        }

        await base.StopAsync(server, process, writeLineAsync, ct);
    }

    private static string MakeSafeWorldName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "Dedicated" : value.Trim();
        var chars = source.Where(char.IsLetterOrDigit).Take(32).ToArray();
        return chars.Length == 0 ? "Dedicated" : new string(chars);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
